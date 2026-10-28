"""Extract original JPEG bytes without re-encoding; never modify source ZIPs."""
from pathlib import Path
import sys
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'.deps'))
import argparse,zipfile,hashlib,json
from PIL import Image

def main():
 p=argparse.ArgumentParser();p.add_argument('--source-dir',type=Path,default=Path('D:/Picture'));a=p.parse_args()
 (ROOT/'inputs').mkdir(exist_ok=True);manifest=[]
 for dpi in (300,600):
  src=a.source_dir/f'テムジンデカール{dpi}dpi.zip'
  with zipfile.ZipFile(src) as z:
   members=[i for i in z.infolist() if not i.is_dir() and Path(i.filename).suffix.lower() in ('.jpg','.jpeg')]
   if len(members)!=1:raise ValueError(f'Expected exactly one original JPEG: {src}')
   member=members[0];data=z.read(member);dest=ROOT/'inputs'/f'{dpi}dpi.jpg'
   digest=hashlib.sha256(data).hexdigest()
   if dest.exists() and hashlib.sha256(dest.read_bytes()).hexdigest()!=digest:raise ValueError(f'Existing input differs: {dest}')
   if not dest.exists():dest.write_bytes(data)
   with Image.open(dest) as im:size=list(im.size)
   manifest.append(dict(dpi=dpi,zip=str(src),member=member.filename,image=str(dest.relative_to(ROOT)),size=size,sha256=digest,zip_sha256=hashlib.sha256(src.read_bytes()).hexdigest()))
 (ROOT/'inputs/manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
 print('Original ZIP bytes and extracted JPEGs verified.')

if __name__=='__main__':main()
