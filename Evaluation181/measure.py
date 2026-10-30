"""Compare UI-size thresholds and an optional second circle-search pass.
Uses the unchanged manually annotated 1.8.0 regions, never OCR-derived truth.
Run from the repository root with its Python 3.12 runtime.
"""
import hashlib, json, math, subprocess, sys, time, statistics
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT/'Ocr'))
import candidates as detector
from PIL import Image
cv2, np = detector.cv2, detector.np
out = ROOT/'Evaluation181'
truth = json.loads((ROOT/'Evaluation180/ground-truth.json').read_text(encoding='utf8'))

def score(circles, item):
    x,y,w,h = item['roi']
    region = [c for c in circles if x <= c[0] < x+w and y <= c[1] < y+h]
    used = set()
    for label in item['labels']:
        gx,gy,r = label['circle']
        options = [(math.hypot(c[0]-gx,c[1]-gy),i) for i,c in enumerate(region)
                   if i not in used and .55*r <= c[2] <= 1.5*r]
        if options:
            distance,index = min(options)
            if distance < .65*r: used.add(index)
    return dict(tp=len(used),fn=len(item['labels'])-len(used),fp=len(region)-len(used))

rows=[]
for item in truth['images']:
    path=ROOT/'inputs'/item['file']
    assert hashlib.sha256(path.read_bytes()).hexdigest() == item['sha256']
    start=time.perf_counter()
    result=json.loads(subprocess.run([sys.executable,'-B',str(ROOT/'bin/Release-1.8.1/Ocr/worker.py'),str(path)],
        capture_output=True,text=True,encoding='utf8',check=True).stdout)
    ocr_seconds=time.perf_counter()-start
    (out/(item['file']+'.json')).write_text(json.dumps(result),encoding='utf8')
    circles=result['candidates'];diameters=[2*c[2] for c in circles];median=statistics.median(diameters)
    thresholds=[]
    for tolerance in [.2,.3,.4]:
        kept=[d for d in diameters if abs(d-median)<=median*tolerance]
        mean=statistics.mean(kept)
        thresholds.append(dict(tolerance=tolerance,inliers=len(kept),width=mean,height=mean,cv=statistics.pstdev(kept)/mean))
    size=thresholds[1]['width']
    with Image.open(path) as image:
        start=time.perf_counter()
        scale=min(1,2800/max(image.size));wh=(round(image.width*scale),round(image.height*scale))
        gray=cv2.GaussianBlur(np.asarray(image.convert('L').resize(wh)),(5,5),1)
        radius=size*scale/2;low=max(3,round(radius*.7));high=max(low+1,round(radius*1.3));extra=[]
        for y in range(0,wh[1],700):
            for x in range(0,wh[0],700):
                pad=high+8;l=max(0,x-pad);t=max(0,y-pad)
                tile=gray[t:min(wh[1],y+700+pad),l:min(wh[0],x+700+pad)]
                found=cv2.HoughCircles(tile,cv2.HOUGH_GRADIENT,1,max(3,low*1.6),param1=90,param2=17,minRadius=low,maxRadius=high)
                for cx,cy,r in ([] if found is None else found[0]):
                    cx=float(cx)+l;cy=float(cy)+t
                    if x<=cx<min(x+700,wh[0]) and y<=cy<min(y+700,wh[1]):extra.append((cx/scale,cy/scale,float(r)/scale))
        quality=detector.quality(image,extra)
        extra=[c for c,q in zip(extra,quality) if q[0]>=.9 and q[1]>60]
        combined=detector.dedup(circles+extra) # Never remove existing candidates by size.
        pass2_seconds=time.perf_counter()-start
    row=dict(file=item['file'],records=len(result['detections']),candidates=len(circles),median=median,thresholds=thresholds,
        old=score(circles,item),two_pass=score(combined,item),two_pass_count=len(combined),ocr_seconds=ocr_seconds,extra_pass_seconds=pass2_seconds)
    rows.append(row);print(json.dumps(row,ensure_ascii=True),flush=True)
    assert hashlib.sha256(path.read_bytes()).hexdigest() == item['sha256']
    (out/'measurements.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf8')
