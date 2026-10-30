import sys,json,time,math
from pathlib import Path
sys.path.insert(0,str(Path.cwd()/'Ocr'))
import candidates as detector
from PIL import Image
truth=json.loads(Path('Evaluation180/ground-truth.json').read_text(encoding='utf8'))
results=[]
for item in truth['images']:
 im=Image.open(Path('inputs')/item['file']);im.load()
 for mode in ['baseline','hough','adaptive','otsu','color']:
  start=time.perf_counter();cs=detector.phase1.circle_candidates(im) if mode=='baseline' else detector.candidates(im,mode);seconds=time.perf_counter()-start
  x,y,w,h=item['roi'];region=[c for c in cs if x<=c[0]<x+w and y<=c[1]<y+h];matched=set();tp=0
  for c in region:
   near=[(math.hypot(c[0]-g['circle'][0],c[1]-g['circle'][1]),i) for i,g in enumerate(item['labels']) if i not in matched and .55*g['circle'][2]<=c[2]<=1.5*g['circle'][2]]
   if near:
    dist,i=min(near)
    if dist<item['labels'][i]['circle'][2]*.65:matched.add(i);tp+=1
  result=dict(file=item['file'],mode=mode,total=len(cs),tp=tp,fn=len(item['labels'])-tp,fp=len(region)-tp,seconds=seconds,candidates=cs)
  results.append(result);print(item['file'],mode,len(cs),tp,len(item['labels'])-tp,len(region)-tp,round(seconds,2),flush=True)
 Path('Evaluation180/experiments.json').write_text(json.dumps(results,ensure_ascii=False),encoding='utf8')
