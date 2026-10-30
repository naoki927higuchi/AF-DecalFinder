import sys,json,math,time
from pathlib import Path
sys.path.insert(0,str(Path.cwd()/'Ocr'));import candidates as d
from PIL import Image
truth=json.loads(Path('Evaluation180/ground-truth.json').read_text(encoding='utf8'));ex=json.loads(Path('Evaluation180/experiments.json').read_text(encoding='utf8'));out=[]
for g in truth['images']:
 im=Image.open(Path('inputs')/g['file']);cs=d.select_candidates(im,{e['mode']:e['candidates'] for e in ex if e['file']==g['file']});x,y,w,h=g['roi'];region=[c for c in cs if x<=c[0]<x+w and y<=c[1]<y+h];matched=set()
 for c in region:
  for i,label in enumerate(g['labels']):
   if i not in matched and .55*label['circle'][2]<=c[2]<=1.5*label['circle'][2] and math.hypot(c[0]-label['circle'][0],c[1]-label['circle'][1])<label['circle'][2]*.65:matched.add(i);break
 print(g['file'],len(cs),len(matched),len(g['labels'])-len(matched),len(region)-len(matched),flush=True);out.append(dict(file=g['file'],candidates=cs))
Path('Evaluation180/selected.json').write_text(json.dumps(out,ensure_ascii=False),encoding='utf8')
