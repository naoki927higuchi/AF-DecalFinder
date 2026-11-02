"""Validate preserved inputs, repeatability and accounting without re-running OCR."""
import hashlib,json,os,platform
from pathlib import Path
HERE=Path(__file__).parent;ROOT=HERE.parent;RAW=ROOT/'viewer-checks/comparison-rg'
g=json.loads((HERE/'ground-truth.json').read_text(encoding='utf-8'))
m=json.loads((HERE/'measurements.json').read_text())
checks=[]
def check(name,condition):
    assert condition,name
    checks.append(name)
for name,v in m.items():
    p=Path(g[name]['path']);s=p.stat()
    check(name+' input sha256',hashlib.sha256(p.read_bytes()).hexdigest()==v['input']['sha256'])
    check(name+' input mtime and size',s.st_mtime_ns==v['input']['mtime_ns'] and s.st_size==v['input']['bytes'])
    runs=[json.loads((RAW/f'{name}-run{i}.json').read_text()) for i in range(1,4)]
    def stable(r):return (r['candidates'],[{k:v for k,v in d.items() if k!='id'} for d in r['detections']])
    check(name+' three repeat result equality',all(stable(r)==stable(runs[0]) for r in runs))
    e=v['evaluation']
    check(name+' 24 label accounting',e['correct']+e['wrong']+e['unknown']+e['miss']==24 and e['true_candidates']+e['miss']==24)
    check(name+' distinct ground truth IDs',len({r['id'] for r in g[name]['records']})==24)
    (HERE/f'ocr-{name}.json').write_text(json.dumps(runs[0],indent=2),encoding='utf-8')
for file in ['worker.py','recognition.py','candidates.py']:
    check(file+' source matches shipped code',(ROOT/'Ocr'/file).read_bytes()==(ROOT/'bin/AF-DecalFinder-1.9.2-win-x64/Ocr'/file).read_bytes())
(HERE/'verification.json').write_text(json.dumps(dict(check_count=len(checks),checks=checks,python=platform.python_version(),platform=platform.platform(),processor=os.environ.get('PROCESSOR_IDENTIFIER'),logical_processors=os.cpu_count()),indent=2),encoding='utf-8')
print(len(checks),'checks passed')
