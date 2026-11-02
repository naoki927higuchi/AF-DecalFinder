"""Prepare non-OCR registration and visual ground-truth verification crops."""
import json
from pathlib import Path
import cv2
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'viewer-checks/comparison-rg'
OUT.mkdir(parents=True, exist_ok=True)
web = ROOT / 'viewer-checks/web-manual-1585/marking-web.png'
scan = ROOT / 'inputs/RG RX-78-2 ver 2.0 600dpi.jpg'
phone = ROOT / 'inputs/RG RX-78-2 ver 2.0 スマホ撮影.jpg'
with Image.open(phone) as im:
    print('phone EXIF orientation', im.getexif().get(274), 'size', im.size)
    assert im.getexif().get(274)==6, 'Recheck orientation for changed input'
    upright=im.transpose(Image.Transpose.ROTATE_270)
    upright.info.pop('exif',None)
    upright.save(OUT / 'phone-upright.png')
paths = {'web': web, 'scan': scan, 'phone_upright': OUT / 'phone-upright.png'}
gt = [{k: g[k] for k in ('x', 'y', 'expected')} for g in json.loads((web.parent / 'spot-check.json').read_text())]
ref = np.array(Image.open(web).convert('L'))
sift = cv2.SIFT_create(nfeatures=12000)
k0,d0 = sift.detectAndCompute(ref, None)
allgt = {}
for name,path in paths.items():
    im = Image.open(path).convert('RGB')
    scale = min(1, 2200/max(im.size))
    gray = np.array(im.resize((round(im.width*scale),round(im.height*scale))).convert('L'))
    k,d = sift.detectAndCompute(gray, None)
    matches = cv2.BFMatcher().knnMatch(d0,d,k=2)
    good = [a for a,b in matches if a.distance < .65*b.distance]
    H,mask = cv2.findHomography(np.float32([k0[m.queryIdx].pt for m in good]),np.float32([k[m.trainIdx].pt for m in good]),cv2.RANSAC,3)
    H = np.diag([1/scale,1/scale,1]) @ H
    if name == 'web': H = np.eye(3)
    def transform(points):
        return cv2.perspectiveTransform(np.float32([points]),H)[0].tolist()
    points = transform([[g['x'],g['y']] for g in gt])
    radius = np.linalg.norm(np.array(transform([[142,346],[151,346]]))[1]-np.array(transform([[142,346],[151,346]]))[0])
    records = [dict(id=i+1, expected=g['expected'], center=p, radius=float(radius)) for i,(g,p) in enumerate(zip(gt,points))]
    roi = transform([[100,320],[520,320],[520,880],[100,880]])
    allgt[name] = dict(path=str(path),size=im.size, records=records, roi=roi, registration_inliers=int(mask.sum()),registration_matches=len(good))
    sheet = Image.new('RGB',(6*160,4*180),'#dddddd')
    draw = ImageDraw.Draw(sheet)
    for i,r in enumerate(records):
        x,y=r['center']; s=radius*1.9
        crop=im.crop((round(x-s),round(y-s),round(x+s),round(y+s))).resize((150,150))
        ox=(i%6)*160;oy=(i//6)*180
        sheet.paste(crop,(ox,oy+25));draw.text((ox+2,oy+3),f"{i+1}: {r['expected']}",fill='black')
    sheet.save(OUT / f'{name}-gt-check.png')
    # Large common evaluation ROI, without OCR annotations.
    xs,ys=zip(*roi); crop=im.crop((int(min(xs)),int(min(ys)),int(max(xs))+1,int(max(ys))+1))
    crop.thumbnail((1000,1300));crop.save(OUT / f'{name}-roi.png')
    print(name, 'inliers',int(mask.sum()),'radius',radius)
raw = dict(allgt['phone_upright'])
raw['path']=str(phone);raw['size']=[4000,3000]
raw['records']=[dict(r,center=[r['center'][1],2999-r['center'][0]]) for r in raw['records']]
raw['roi']=[[y,2999-x] for x,y in raw['roi']]
allgt['phone_raw']=raw
(Path(__file__).parent/'ground-truth.json').write_text(json.dumps(allgt,indent=2,ensure_ascii=False),encoding='utf-8')
