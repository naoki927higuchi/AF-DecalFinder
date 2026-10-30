"""Extra 1-bit scan check using the same manually annotated RX-78 ROI."""
import hashlib
import json
import math
from pathlib import Path
import subprocess
import sys
import time
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Evaluation180'
truth = json.loads((OUT / 'ground-truth.json').read_text(encoding='utf8'))
item = next(row for row in truth['images'] if row['file'] == 'RX-78 ver 3.0.png')
source = ROOT / 'inputs' / item['file']
assert hashlib.sha256(source.read_bytes()).hexdigest() == item['sha256']
with Image.open(source) as image:
    image.convert('L').point(lambda value: 255 if value > 180 else 0, mode='1').save(OUT / 'mono-source.png')
rows = []
for version in ['1.7.0', '1.8.0']:
    start = time.perf_counter()
    command = [sys.executable, '-B', str(ROOT / 'bin/Release-1.8.0/Ocr/worker.py'), str(OUT / 'mono-source.png')]
    if version == '1.7.0':
        command.append('--baseline')
    result = json.loads(subprocess.run(command, capture_output=True, text=True, encoding='utf8', check=True).stdout)
    elapsed = time.perf_counter() - start
    x, y, w, h = item['roi']
    candidates = [c for c in result['candidates'] if x <= c[0] < x + w and y <= c[1] < y + h]
    used = set()
    for label in item['labels']:
        gx, gy, radius = label['circle']
        pairs = [(math.hypot(c[0]-gx, c[1]-gy), index) for index, c in enumerate(candidates)
                 if index not in used and .55*radius <= c[2] <= 1.5*radius]
        if pairs:
            distance, index = min(pairs)
            if distance < .65*radius:
                used.add(index)
    rows.append(dict(version=version, tp=len(used), fn=len(item['labels'])-len(used),
                     fp=len(candidates)-len(used), seconds=elapsed, peak_rss_mb=result['peak_rss_mb']))
(OUT / 'monochrome-summary.json').write_text(json.dumps(rows, indent=2), encoding='utf8')
print(json.dumps(rows))
assert hashlib.sha256(source.read_bytes()).hexdigest() == item['sha256']
