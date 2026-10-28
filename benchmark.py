"""Native Tesseract benchmark. Runs each configuration in a fresh process."""
import os
os.environ['OMP_THREAD_LIMIT']='1'
import sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'.deps'))
import argparse, json, time, hashlib, subprocess, threading, re, platform, math
import numpy as np
import cv2, psutil, tesserocr
from PIL import Image, ImageDraw
cv2.setNumThreads(1)

CONFIGS={
 'full_psm3_digits':dict(psm=3,whitelist='0123456789',tile=False,prep='none'),
 'full_psm6_digits':dict(psm=6,whitelist='0123456789',tile=False,prep='none'),
 'full_psm11_digits':dict(psm=11,whitelist='0123456789',tile=False,prep='none'),
 'full_psm11_separators':dict(psm=11,whitelist='0123456789()+',tile=False,prep='none'),
 'tile_psm11_separators':dict(psm=11,whitelist='0123456789()+',tile=True,prep='none'),
 'tile_psm11_otsu':dict(psm=11,whitelist='0123456789()+',tile=True,prep='otsu'),
 'circle_psm7':dict(psm=7,whitelist='0123456789',tile=True,prep='circle',stretch=1),
 'circle_psm7_wide':dict(psm=7,whitelist='0123456789',tile=True,prep='circle',stretch=1.5),
}

def configuration_metadata(cfg,width):
 if cfg['prep']=='circle':
  return dict(overlap_fraction=None,candidate_tile_padding_px=round(25*width/3549),circle_detector=dict(method='HOUGH_GRADIENT',dp=1,reference_width=3549,min_radius_reference_px=12,max_radius_reference_px=18,min_distance_reference_px=24,param1=120,param2_reference=22,mask_inset_reference_px=1.3),resize_x=2*cfg['stretch'],resize_y=2,white_padding_px=10,opencv_threads=1)
 return dict(overlap_fraction=.1 if cfg['tile'] else 0,opencv_threads=1)

def circle_candidates(image):
 # Radius range follows the image scale, with no ground-truth positions/numbers.
 scale=image.width/3549; gray=np.asarray(image.convert('L')); result=[]
 for row in range(4):
  for col in range(4):
   l=round(col*image.width/4);t=round(row*image.height/4);r=round((col+1)*image.width/4);b=round((row+1)*image.height/4)
   pad=round(25*scale);x=max(0,l-pad);y=max(0,t-pad)
   tile=gray[y:min(image.height,b+pad),x:min(image.width,r+pad)]
   cs=cv2.HoughCircles(tile,cv2.HOUGH_GRADIENT,dp=1,minDist=24*scale,param1=120,param2=22*scale,minRadius=round(12*scale),maxRadius=round(18*scale))
   if cs is not None:
    for cx,cy,rad in cs[0]:
     cx=float(cx)+x;cy=float(cy)+y
     if l<=cx<r and t<=cy<b:result.append((cx,cy,float(rad)))
 return result

def recognize_circles(api,image,cfg):
 start=time.perf_counter(); candidates=circle_candidates(image); prep=time.perf_counter()-start; recog=0; ds=[]
 for index,(cx,cy,rad) in enumerate(candidates):
  start=time.perf_counter();l=max(0,math.floor(cx-rad));t=max(0,math.floor(cy-rad));r=min(image.width,math.ceil(cx+rad));b=min(image.height,math.ceil(cy+rad))
  gray=np.array(image.crop((l,t,r,b)).convert('L'));yy,xx=np.ogrid[t:b,l:r]
  gray[(xx-cx)**2+(yy-cy)**2>(rad-1.3*image.width/3549)**2]=255
  crop=Image.fromarray(gray);sx=2*cfg['stretch'];sy=2
  resized=crop.resize((round(crop.width*sx),round(crop.height*sy)),Image.Resampling.LANCZOS);sx=resized.width/crop.width;sy=resized.height/crop.height
  padded=Image.new('L',(resized.width+20,resized.height+20),255);padded.paste(resized,(10,10));prep+=time.perf_counter()-start
  start=time.perf_counter();api.SetImage(padded);api.SetSourceResolution(300);api.Recognize();found=extract(api,(0,0),f'circle:{index}');recog+=time.perf_counter()-start
  for d in found:
   def mapped(bb):
    x,y,w,h=bb;left=max(0,round(l+(x-10)/sx));top=max(0,round(t+(y-10)/sy));right=min(image.width,round(l+(x+w-10)/sx));bottom=min(image.height,round(t+(y+h-10)/sy));return [left,top,right-left,bottom-top]
   d['bbox']=mapped(d['bbox'])
   for symbol in d['symbols']:symbol['bbox']=mapped(symbol['bbox'])
   d['candidate_circle']=[cx,cy,rad];ds.append(d)
 return ds,prep,recog,len(candidates)

def union(boxes):
 l=min(b[0] for b in boxes); t=min(b[1] for b in boxes)
 r=max(b[0]+b[2] for b in boxes); b=max(b[1]+b[3] for b in boxes)
 return [l,t,r-l,b-t]

def iou(a,b):
 l=max(a[0],b[0]); t=max(a[1],b[1]); r=min(a[0]+a[2],b[0]+b[2]); bot=min(a[1]+a[3],b[1]+b[3]); inter=max(0,r-l)*max(0,bot-t)
 return inter/(a[2]*a[3]+b[2]*b[3]-inter) if inter else 0

def extract(api,offset,tile_id):
 """Split numeric runs at word boundaries and recognized non-digit symbols.
 Bounding boxes are unions of the actual digit symbol boxes, never equal splits.
 """
 it=api.GetIterator(); output=[]; run=[]; raw_word=''; word_conf=0
 def flush():
  if not run:return
  output.append(dict(number=''.join(v['text'] for v in run),bbox=union([v['bbox'] for v in run]),confidence=word_conf,confidence_scope='Tesseract parent word',symbol_confidence_mean=sum(v['confidence'] for v in run)/len(run),raw_word=raw_word,symbols=run.copy(),tile_id=tile_id))
  run.clear()
 if it is None or it.Empty(tesserocr.RIL.BLOCK):return output
 while True:
  if it.IsAtBeginningOf(tesserocr.RIL.WORD):
   flush(); raw_word=it.GetUTF8Text(tesserocr.RIL.WORD) or ''; word_conf=float(it.Confidence(tesserocr.RIL.WORD))
  ch='' if it.Empty(tesserocr.RIL.SYMBOL) else (it.GetUTF8Text(tesserocr.RIL.SYMBOL) or '')
  box=it.BoundingBox(tesserocr.RIL.SYMBOL)
  if ch and re.fullmatch('[0-9]+',ch) and box:
   l,t,r,b=box; run.append(dict(text=ch,bbox=[l+offset[0],t+offset[1],r-l,b-t],confidence=float(it.Confidence(tesserocr.RIL.SYMBOL))))
  else:flush()
  if not it.Next(tesserocr.RIL.SYMBOL):break
 flush(); return output

def run_one(dpi,name,repeat):
 cfg=CONFIGS[name]; dst=ROOT/'results'/name; dst.mkdir(parents=True,exist_ok=True)
 process=psutil.Process(); stop=threading.Event(); peaks=[process.memory_info().rss]
 def sample():
  while not stop.wait(.01):peaks.append(process.memory_info().rss)
 th=threading.Thread(target=sample,daemon=True);th.start()
 begin=time.perf_counter(); src=ROOT/'inputs'/f'{dpi}dpi.jpg'
 image=Image.open(src); image.load(); width,height=image.size
 loaded=time.perf_counter()
 api=tesserocr.PyTessBaseAPI(path=str(ROOT/'tessdata'),lang='eng',oem=tesserocr.OEM.LSTM_ONLY,psm=cfg['psm'],variables={'load_system_dawg':'0','load_freq_dawg':'0'})
 api.SetVariable('tessedit_char_whitelist',cfg['whitelist']); initialized=time.perf_counter()
 # Fixed 4x4 grid at both resolutions with 10% overlap around each cell.
 # Keep detections whose centers are within the non-overlapping owner cell.
 n=4 if cfg['tile'] else 1; detections=[]; recog_seconds=0; prep_seconds=0
 candidate_count=None
 if cfg['prep']=='circle':
  detections,prep_seconds,recog_seconds,candidate_count=recognize_circles(api,image,cfg)
 for row in range(n if cfg['prep']!='circle' else 0):
  for col in range(n):
   core=(round(col*width/n),round(row*height/n),round((col+1)*width/n),round((row+1)*height/n))
   px=round(width/n*.10) if n>1 else 0; py=round(height/n*.10) if n>1 else 0
   box=(max(0,core[0]-px),max(0,core[1]-py),min(width,core[2]+px),min(height,core[3]+py))
   t=time.perf_counter(); crop=image.crop(box)
   if cfg['prep']=='otsu':
    gray=np.asarray(crop.convert('L')); _,binary=cv2.threshold(gray,0,255,cv2.THRESH_BINARY|cv2.THRESH_OTSU); crop=Image.fromarray(binary)
   prep_seconds+=time.perf_counter()-t
   t=time.perf_counter(); api.SetImage(crop); api.SetSourceResolution(dpi);api.Recognize()
   found=extract(api,box[:2],f'{row}:{col}'); recog_seconds+=time.perf_counter()-t
   for d in found:
    x,y,w,h=d['bbox'];cx=x+w/2;cy=y+h/2
    if core[0]<=cx<core[2] and core[1]<=cy<core[3]:detections.append(d)
 api.End();elapsed=time.perf_counter()-begin
 stop.set();th.join(); peaks.append(process.memory_info().rss)
 # Cross-boundary boxes can move centers slightly; only suppress same-number,
 # overlapping detections from DIFFERENT tiles (never merge distinct locations).
 kept=[];suppressed=[]
 for d in sorted(detections,key=lambda v:v['confidence'],reverse=True):
  if any(d['tile_id']!=k['tile_id'] and d['number']==k['number'] and iou(d['bbox'],k['bbox'])>.5 for k in kept):suppressed.append(d)
  else:kept.append(d)
 kept.sort(key=lambda d:(d['bbox'][1],d['bbox'][0]))
 for i,d in enumerate(kept):d['id']=f'{dpi}-{i+1:04d}'; d['x'],d['y'],d['width'],d['height']=d['bbox']
 out=dict(schema_version=1,dpi=dpi,image=dict(path=str(src.relative_to(ROOT)),width=width,height=height,sha256=hashlib.sha256(src.read_bytes()).hexdigest()),engine=tesserocr.tesseract_version(),config=dict(name=name,**cfg,oem=1,lang='eng',dictionaries=False,omp_thread_limit=1,grid=n,overlap_fraction=.1 if n>1 else 0),timing=dict(total_seconds=elapsed,load_seconds=loaded-begin,initialize_seconds=initialized-loaded,preprocessing_seconds=prep_seconds,recognition_and_extraction_seconds=recog_seconds),memory=dict(sampled_peak_rss_mb=max(peaks)/2**20,windows_peak_wset_mb=getattr(process.memory_info(),'peak_wset',0)/2**20,sampling_ms=10,scope='fresh Python process; imports+image+Tesseract; excludes overlay/JSON output'),detection_count=len(kept),unique_number_count=len(set(d['number'] for d in kept)),detections=kept,suppressed_tile_duplicates=suppressed)
 out['candidate_count']=candidate_count
 out['config'].update(configuration_metadata(cfg,width))
 path=dst/f'{dpi}dpi-r{repeat}.json';path.write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps(dict(config=name,dpi=dpi,repeat=repeat,seconds=round(elapsed,3),count=len(kept),unique=out['unique_number_count'],rss_mb=round(max(peaks)/2**20,1))),flush=True)

def main():
 p=argparse.ArgumentParser();p.add_argument('--worker',action='store_true');p.add_argument('--dpi',type=int,choices=[300,600]);p.add_argument('--config',choices=list(CONFIGS));p.add_argument('--repeat',type=int,default=1);p.add_argument('--repeats',type=int,default=1);a=p.parse_args()
 if a.worker:run_one(a.dpi,a.config,a.repeat);return
 for repeat in range(1,a.repeats+1):
  for name in ([a.config] if a.config else CONFIGS):
   for dpi in ([a.dpi] if a.dpi else [300,600]):
    subprocess.run([sys.executable,__file__,'--worker','--dpi',str(dpi),'--config',name,'--repeat',str(repeat)],check=True)

if __name__=='__main__':main()
