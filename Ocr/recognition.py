"""Numeric recognition with retained circle candidates; Phase 1 is read-only."""
import math,uuid
import candidates as detector
phase1=detector.phase1
np=phase1.np
from PIL import Image

def recognize(image,baseline=False):
 cfg=phase1.CONFIGS['circle_psm7_wide']
 with phase1.tesserocr.PyTessBaseAPI(path=str(phase1.ROOT/'tessdata'),lang='eng',oem=phase1.tesserocr.OEM.LSTM_ONLY,psm=7,variables={'load_system_dawg':'0','load_freq_dawg':'0'}) as api:
  api.SetVariable('tessedit_char_whitelist','0123456789')
  if baseline:
   original=phase1.circle_candidates
   circles=original(image)
   phase1.circle_candidates=lambda _:circles
   try:found,_,_,_=phase1.recognize_circles(api,image,cfg)
   finally:phase1.circle_candidates=original
  else:
   circles=detector.select_candidates(image);found=[]
   for i,(cx,cy,rad) in enumerate(circles):
    l=max(0,math.floor(cx-rad));t=max(0,math.floor(cy-rad));r=min(image.width,math.ceil(cx+rad));b=min(image.height,math.ceil(cy+rad))
    gray=np.array(image.crop((l,t,r,b)).convert('L'));yy,xx=np.ogrid[t:b,l:r];distance=(xx-cx)**2+(yy-cy)**2
    ring=gray[(distance>(rad*.7)**2)&(distance<(rad*.85)**2)]
    if ring.size and np.median(ring)<128:gray=255-gray
    gray[distance>(rad-1.3*image.width/3549)**2]=255
    crop=Image.fromarray(gray);resized=crop.resize((round(crop.width*3),round(crop.height*2)),Image.Resampling.LANCZOS)
    sx=resized.width/crop.width;sy=resized.height/crop.height
    padded=Image.new('L',(resized.width+20,resized.height+20),255);padded.paste(resized,(10,10))
    api.SetImage(padded);api.SetSourceResolution(300);api.Recognize()
    ds=phase1.extract(api,(0,0),f'circle:{i}')
    if ds:
     for d in ds:
      x,y,w,h=d['bbox'];left=max(0,round(l+(x-10)/sx));top=max(0,round(t+(y-10)/sy));right=min(image.width,round(l+(x+w-10)/sx));bottom=min(image.height,round(t+(y+h-10)/sy))
      d['bbox']=[left,top,right-left,bottom-top];d['candidate_circle']=[cx,cy,rad];d['is_unrecognized']=False
      found.append(d)
    else:found.append(dict(number='',bbox=[l,t,r-l,b-t],confidence=0,tile_id=f'circle:{i}',candidate_circle=[cx,cy,rad],is_unrecognized=True))
 kept=[]
 for d in sorted(found,key=lambda v:v['confidence'],reverse=True):
  if not any(d['tile_id']!=k['tile_id'] and d['number']==k['number'] and phase1.iou(d['bbox'],k['bbox'])>.5 for k in kept):kept.append(d)
 kept.sort(key=lambda d:(d['bbox'][1],d['bbox'][0]))
 generation=uuid.uuid4().hex
 for i,d in enumerate(kept):d['id']=f'ocr-{generation}-{i+1:04d}'
 return kept,circles
