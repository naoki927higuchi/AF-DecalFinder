"""Spatial, one-to-one evaluation against frozen manually read annotations."""
import sys, json, csv, statistics, math, shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'.deps'))
from PIL import Image, ImageDraw
from benchmark import iou

def inside(box,region):
 x,y,w,h=box; l,t,rw,rh=region
 return l<=x+w/2<l+rw and t<=y+h/2<t+rh

def evaluate(data,truth):
 dpi=data['dpi']; key=f'bbox_{dpi}'; rows=[]; pairs=[]
 for region in truth['regions']:
  gt=[g for g in truth['annotations'] if g['region']==region['id']]
  ds=[d for d in data['detections'] if inside(d['bbox'],region[key])]
  # Geometry only: no preference for matching numbers. Highest IoU first.
  candidates=sorted([(iou(g[key],d['bbox']),i,j) for i,g in enumerate(gt) for j,d in enumerate(ds) if iou(g[key],d['bbox'])>=.1],reverse=True)
  usedg=set();usedd=set(); matches={}
  for overlap,i,j in candidates:
   if i not in usedg and j not in usedd:matches[i]=(j,overlap);usedg.add(i);usedd.add(j)
  for i,g in enumerate(gt):
   d=None; overlap=0; center=''
   if i in matches:
    j,overlap=matches[i]; d=ds[j]; b=d['bbox'];a=g[key]
    center=math.hypot(b[0]+b[2]/2-a[0]-a[2]/2,b[1]+b[3]/2-a[1]-a[3]/2)
   status='miss' if d is None else ('correct' if d['number']==g['number'] else 'wrong_number')
   rows.append(dict(config=data['config']['name'],dpi=dpi,region=region['id'],gt_id=g['id'],expected=g['number'],recognized=d['number'] if d else '',status=status,iou=round(overlap,4),center_error_px=round(center,3) if center!='' else '',confidence=d['confidence'] if d else '',gt_bbox=json.dumps(g[key]),det_bbox=json.dumps(d['bbox']) if d else '',detection_id=d['id'] if d else '',tags=','.join(g['tags'])))
  for j,d in enumerate(ds):
   if j not in usedd:rows.append(dict(config=data['config']['name'],dpi=dpi,region=region['id'],gt_id='',expected='',recognized=d['number'],status='false_positive',iou=0,center_error_px='',confidence=d['confidence'],gt_bbox='',det_bbox=json.dumps(d['bbox']),detection_id=d['id'],tags='negative_control' if region['negative_only'] else ''))
 counts={k:sum(r['status']==k for r in rows) for k in ['correct','wrong_number','miss','false_positive']}
 correct=[r for r in rows if r['status']=='correct']; n=len(truth['annotations']); pred=counts['correct']+counts['wrong_number']+counts['false_positive']
 summary=dict(config=data['config']['name'],dpi=dpi,total_detections=data['detection_count'],unique_numbers=data['unique_number_count'],**counts,ground_truth_count=n,exact_recall=counts['correct']/n,exact_precision=counts['correct']/pred if pred else 0,mean_iou_correct=statistics.mean(r['iou'] for r in correct) if correct else 0,median_center_error_px=statistics.median(r['center_error_px'] for r in correct) if correct else None,correct_iou_ge_05=sum(r['iou']>=.5 for r in correct),negative_region_fp=sum(r['status']=='false_positive' and r['region']=='F_paint' for r in rows))
 return summary,rows

def csv_write(path,rows):
 with path.open('w',encoding='utf-8-sig',newline='') as f:
  w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)

def main():
 truth=json.loads((ROOT/'ground_truth.json').read_text(encoding='utf-8')); summaries=[];all_rows=[]
 for path in sorted((ROOT/'results').glob('*/*dpi-r1.json')):
  data=json.loads(path.read_text(encoding='utf-8')); summary,rows=evaluate(data,truth)
  repeats=[json.loads(p.read_text(encoding='utf-8')) for p in path.parent.glob(f'{data["dpi"]}dpi-r*.json')]
  times=[d['timing']['total_seconds'] for d in repeats]
  summary.update(repeats=len(repeats),seconds_median=statistics.median(times),seconds_min=min(times),seconds_max=max(times),peak_rss_mb_median=statistics.median(d['memory']['sampled_peak_rss_mb'] for d in repeats),peak_wset_mb_max=max(d['memory']['windows_peak_wset_mb'] for d in repeats),repeat_detections_identical=all([(d['number'],d['bbox']) for d in r['detections']]==[(d['number'],d['bbox']) for d in data['detections']] for r in repeats))
  csv_write(path.parent/f'{data["dpi"]}dpi-comparison.csv',rows); summaries.append(summary);all_rows+=rows
 csv_write(ROOT/'results/summary.csv',summaries);csv_write(ROOT/'results/comparison-all.csv',all_rows)
 (ROOT/'results/summary.json').write_text(json.dumps(summaries,ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps(summaries,indent=2))

if __name__=='__main__':main()
