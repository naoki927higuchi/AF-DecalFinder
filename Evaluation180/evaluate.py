"""Reproducible same-ROI evaluation; ground truth was manually recorded first."""
import json,sys,subprocess,time,math,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
truth=json.loads((ROOT/'Evaluation180/ground-truth.json').read_text(encoding='utf8'))
worker=ROOT/'bin/Release-1.8.0/Ocr/worker.py'
rows=[];details=[]
for item in truth['images']:
 path=ROOT/'inputs'/item['file']
 assert hashlib.sha256(path.read_bytes()).hexdigest()==item['sha256']
 for mode in ['1.7.0','1.8.0']:
  start=time.perf_counter();run=subprocess.run([sys.executable,'-B',str(worker),str(path)]+(['--baseline'] if mode=='1.7.0' else []),capture_output=True,text=True,encoding='utf8',check=True);elapsed=time.perf_counter()-start
  result=json.loads(run.stdout);(ROOT/'Evaluation180'/(item['file']+'.'+mode+'.json')).write_text(json.dumps(result,ensure_ascii=False),encoding='utf8')
  x,y,w,h=item['roi'];cs=[c for c in result['candidates'] if x<=c[0]<x+w and y<=c[1]<y+h];matched={};used=set()
  for i,g in enumerate(item['labels']):
   pairs=[(math.hypot(c[0]-g['circle'][0],c[1]-g['circle'][1]),j) for j,c in enumerate(cs) if j not in used and .55*g['circle'][2]<=c[2]<=1.5*g['circle'][2]]
   if pairs:
    distance,j=min(pairs)
    if distance<g['circle'][2]*.65:matched[i]=cs[j];used.add(j)
  correct=wrong=unread=alphabet=0
  for i,g in enumerate(item['labels']):
   c=matched.get(i);ds=[] if c is None else [d for d in result['detections'] if all(abs(a-b)<.01 for a,b in zip(d['candidate_circle'],c))]
   values=[d['number'] for d in ds if not d.get('is_unrecognized',False)]
   if c is None:state='candidate_miss'
   elif not g['number'].isdigit():state='alphabet_not_scored';alphabet+=1
   elif not values:state='unrecognized';unread+=1
   elif values==[g['number']]:state='correct';correct+=1
   else:state='wrong';wrong+=1
   details.append(dict(file=item['file'],version=mode,truth=g,state=state,recognized=values))
  unknown=sum(d.get('is_unrecognized',False) for d in result['detections'])
  row=dict(file=item['file'],version=mode,truth=len(item['labels']),tp=len(matched),fn=len(item['labels'])-len(matched),fp=len(cs)-len(matched),ocr_correct=correct,ocr_wrong=wrong,ocr_unread=unread,alphabet=alphabet,full_candidates=len(result['candidates']),full_records=len(result['detections']),full_unknown_retained=unknown,seconds=elapsed,peak_rss_mb=result['peak_rss_mb'])
  rows.append(row);print(json.dumps(row,ensure_ascii=True),flush=True)
 assert hashlib.sha256(path.read_bytes()).hexdigest()==item['sha256']
 (ROOT/'Evaluation180/measurements.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf8')
 (ROOT/'Evaluation180/comparison.json').write_text(json.dumps(details,ensure_ascii=False,indent=2),encoding='utf8')
