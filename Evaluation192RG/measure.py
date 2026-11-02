"""Run shipped worker without app caches; evaluate manually verified common labels."""
import csv, hashlib, json, math, os, statistics, subprocess, time
from pathlib import Path
import cv2
import numpy as np

HERE=Path(__file__).resolve().parent
ROOT=HERE.parent
PACKAGE=ROOT/'bin/AF-DecalFinder-1.9.2-win-x64/Ocr'
GT=json.loads((HERE/'ground-truth.json').read_text(encoding='utf-8'))
RAW=ROOT/'viewer-checks/comparison-rg'
def fingerprint(path):
    p=Path(path);s=p.stat()
    return dict(sha256=hashlib.sha256(p.read_bytes()).hexdigest(),bytes=s.st_size,mtime_ns=s.st_mtime_ns)

summaries={}; rows=[]
for name,g in GT.items():
    before=fingerprint(g['path']);runs=[]
    for repeat in range(3):
        start=time.perf_counter()
        proc=subprocess.run([str(PACKAGE/'python/python.exe'),'-B',str(PACKAGE/'worker.py'),g['path']],capture_output=True,env=dict(os.environ,OMP_THREAD_LIMIT='1',PYTHONUTF8='1'),check=True)
        wall=time.perf_counter()-start
        result=json.loads(proc.stdout)
        (RAW/f'{name}-run{repeat+1}.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
        runs.append(dict(seconds=result['seconds'],wall_seconds=wall,peak_rss_mib=result['peak_rss_mb'],candidates=result['candidate_count'],records=len(result['detections']),unknown=sum(d['is_unrecognized'] for d in result['detections'])))
        if repeat==0:
            first=result
            (HERE/f'ocr-{name}.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
        else:
            def stable(r):
                return (r['candidates'],[{k:v for k,v in d.items() if k!='id'} for d in r['detections']])
            assert stable(result)==stable(first), 'OCR result changed across repeats'
    assert fingerprint(g['path'])==before, 'Input changed during evaluation'
    circles=first['candidates'];detections=first['detections']
    # Center within one independently estimated true circle radius; unique pairing.
    edges=[]
    for i,label in enumerate(g['records']):
        for j,c in enumerate(circles):
            distance=math.dist(label['center'],c[:2])
            if distance<=label['radius']:edges.append((distance,i,j))
    matched={};used=set()
    for distance,i,j in sorted(edges):
        if i not in matched and j not in used:matched[i]=(j,distance);used.add(j)
    counts=dict(correct=0,wrong=0,unknown=0,miss=0)
    for i,label in enumerate(g['records']):
        found=matched.get(i);ds=[]
        if found:
            c=circles[found[0]]
            ds=[d for d in detections if math.dist(d['candidate_circle'],c)<.01]
        actual=[d['number'] for d in ds if not d['is_unrecognized']]
        state='miss' if found is None else 'unknown' if not actual else 'correct' if actual==[label['expected']] else 'wrong'
        counts[state]+=1
        rows.append(dict(image=name,id=label['id'],expected=label['expected'],actual='|'.join(actual),state=state,x=label['center'][0],y=label['center'][1],center_error_px=found[1] if found else '',candidate_index=found[0] if found else ''))
    polygon=np.float32(g['roi'])
    inside={j for j,c in enumerate(circles) if cv2.pointPolygonTest(polygon,tuple(c[:2]),False)>=0}
    fp=sorted(inside-used)
    summaries[name]=dict(input=before,size=g['size'],runs=runs,mean_seconds=statistics.mean(r['seconds'] for r in runs),mean_wall_seconds=statistics.mean(r['wall_seconds'] for r in runs),peak_rss_mib=max(r['peak_rss_mib'] for r in runs),total_candidates=len(circles),total_records=len(detections),total_unknown=sum(d['is_unrecognized'] for d in detections),evaluation=dict(ground_truth=len(g['records']),true_candidates=len(matched),false_candidates=len(fp),false_candidate_indices=fp,**counts))
    print(name,json.dumps(summaries[name]['evaluation']),flush=True)
with (HERE/'comparison.csv').open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.DictWriter(f,fieldnames=rows[0]);writer.writeheader();writer.writerows(rows)
(HERE/'measurements.json').write_text(json.dumps(summaries,indent=2),encoding='utf-8')
