"""Integrity and coordinate contract checks; no OCR-derived truth creation."""
import json,hashlib,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parent
import sys
sys.path.insert(0,str(ROOT/'.deps'))
from PIL import Image

def main():
 manifest=json.loads((ROOT/'inputs/manifest.json').read_text(encoding='utf-8'));gt=json.loads((ROOT/'ground_truth.json').read_text())
 assert len(gt['annotations'])==38
 for item in manifest:
  p=ROOT/item['image'];assert hashlib.sha256(p.read_bytes()).hexdigest()==item['sha256']
  z=Path(item['zip']);assert hashlib.sha256(z.read_bytes()).hexdigest()==item['zip_sha256']
  with zipfile.ZipFile(z) as archive:assert hashlib.sha256(archive.read(item['member'])).hexdigest()==item['sha256']
 count=0
 for p in (ROOT/'results').glob('*/*dpi-r*.json'):
  d=json.loads(p.read_text());w=d['image']['width'];h=d['image']['height'];ds=d['detections']
  assert len(ds)==d['detection_count'];assert len(set(v['id'] for v in ds))==len(ds)
  assert len(set(v['number'] for v in ds))==d['unique_number_count']
  for v in ds:
   x,y,bw,bh=v['bbox'];assert 0<=x<x+bw<=w and 0<=y<y+bh<=h,(p,v)
   assert v['number'].isdigit();assert 0<=v['confidence']<=100
  count+=1
 assert count==48,count
 for s in json.loads((ROOT/'results/summary.json').read_text()):
  assert s['correct']+s['wrong_number']+s['miss']==38
  assert s['repeats']==3 and s['repeat_detections_identical']
 for dpi in (300,600):
  d=json.loads((ROOT/f'results/{dpi}dpi.json').read_text());assert Image.open(ROOT/f'results/{dpi}dpi-detections.jpg').size==(d['image']['width'],d['image']['height'])
 # Both separately located 49 and 118 on the feet must remain independent.
 d=json.loads((ROOT/'results/600dpi.json').read_text());r=next(r for r in gt['regions'] if r['id']=='B_feet')['bbox_600']
 from evaluate import inside
 foot=[v for v in d['detections'] if inside(v['bbox'],r)]
 assert sum(v['number']=='49' for v in foot)==2
 assert sum(v['number']=='118' for v in foot)==2
 print('PASS: original hashes, 48 runs, coordinate bounds, repeated-number separation, metric totals, overlays and repeat reproducibility.')

if __name__=='__main__':main()
