import sys,json,math
from pathlib import Path
sys.path.insert(0,str(Path.cwd()/'Ocr'));import candidates as d
from PIL import Image
truth=json.loads(Path('Evaluation180/ground-truth.json').read_text(encoding='utf8'));ex=json.loads(Path('Evaluation180/experiments.json').read_text(encoding='utf8'));out=[]
for item in truth['images']:
 im=Image.open(Path('inputs')/item['file']);cs=next(e['candidates'] for e in ex if e['file']==item['file'] and e['mode']=='hough');qs=d.quality(im,cs)
 for threshold in [.7,.8,.9,.95]:
  keep=[c for c,q in zip(cs,qs) if q[0]>=threshold and q[1]>60];x,y,w,h=item['roi'];region=[c for c in keep if x<=c[0]<x+w and y<=c[1]<y+h];match=set()
  for c in region:
   for i,g in enumerate(item['labels']):
    if i not in match and .55*g['circle'][2]<=c[2]<=1.5*g['circle'][2] and math.hypot(c[0]-g['circle'][0],c[1]-g['circle'][1])<g['circle'][2]*.65:match.add(i);break
  print(item['file'],threshold,len(keep),len(match),len(item['labels'])-len(match),len(region)-len(match),flush=True)
 out.append(dict(file=item['file'],candidates=cs,quality=qs))
Path('Evaluation180/quality.json').write_text(json.dumps(out,ensure_ascii=False),encoding='utf8')
