"""Read-only candidate-stage diagnostics and local visual evidence."""
import json, math, sys
from pathlib import Path
from PIL import Image, ImageDraw
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'bin/AF-DecalFinder-1.9.2-win-x64/Ocr'))
import candidates as detector
HERE=Path(__file__).parent;OUT=ROOT/'viewer-checks/comparison-rg'
gt=json.loads((HERE/'ground-truth.json').read_text(encoding='utf-8'))
diagnostics={}
for name,g in gt.items():
    im=Image.open(g['path']).convert('RGB')
    variants={m:detector.candidates(im,m) for m in ['hough','adaptive','color']}
    hough=variants['hough'];quality=detector.quality(im,hough)
    radii=[c[2] for c,q in zip(hough,quality) if q[0]>=.9 and q[1]>60]
    binary=sum(v>0 for v in im.convert('L').histogram())<=2
    mode=max(radii,key=lambda r:sum(abs(v-r)<r*.12 for v in radii)*r**(1 if binary else 2))
    mode=float(detector.np.median([r for r in radii if abs(r-mode)<mode*.12]))
    diagnostics[name]=dict(estimated_mode_radius=mode,true_label_radius=g['records'][0]['radius'],strict_hough_count=len(radii),raw_hough_count=len(hough),size_gate=[mode*.72,mode*1.28])
    diagnostics[name]['hough_matches_before_size_gate']=sum(any(math.dist(r['center'],c[:2])<=r['radius'] for c in hough) for r in g['records'])
    result=json.loads((OUT/f'{name}-run1.json').read_text())
    draw=ImageDraw.Draw(im)
    for c in result['candidates']:
        x,y,r=c;draw.ellipse((x-r,y-r,x+r,y+r),outline='red',width=max(1,round(im.width/700)))
    for r in g['records']:
        x,y=r['center'];rad=r['radius']
        draw.rectangle((x-rad,y-rad,x+rad,y+rad),outline='#00ffff',width=max(1,round(im.width/1000)))
    xs,ys=zip(*g['roi']);im=im.crop((int(min(xs)),int(min(ys)),int(max(xs))+1,int(max(ys))+1))
    if name=='phone_raw':im=im.transpose(Image.Transpose.ROTATE_270)
    im.thumbnail((1000,1300));im.save(OUT/f'{name}-overlay.png')
    print(name,diagnostics[name],flush=True)
(HERE/'diagnostics.json').write_text(json.dumps(diagnostics,indent=2),encoding='utf-8')
