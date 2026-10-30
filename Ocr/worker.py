"""App bridge: reuse immutable Phase 1 recognition; emit a single JSON document."""
import sys, json, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import benchmark as phase1
from PIL import Image

start = time.perf_counter()
with Image.open(sys.argv[1]) as image:
    image.load()
    cfg = phase1.CONFIGS['circle_psm7_wide']
    with phase1.tesserocr.PyTessBaseAPI(path=str(phase1.ROOT/'tessdata'), lang='eng',
            oem=phase1.tesserocr.OEM.LSTM_ONLY, psm=7,
            variables={'load_system_dawg':'0','load_freq_dawg':'0'}) as api:
        api.SetVariable('tessedit_char_whitelist', '0123456789')
        detections, prep, recog, candidates = phase1.recognize_circles(api, image, cfg)
    kept = []
    for d in sorted(detections, key=lambda v:v['confidence'], reverse=True):
        if not any(d['tile_id'] != k['tile_id'] and d['number'] == k['number'] and phase1.iou(d['bbox'], k['bbox']) > .5 for k in kept):
            kept.append(d)
    kept.sort(key=lambda d:(d['bbox'][1], d['bbox'][0]))
    for i,d in enumerate(kept): d['id'] = f'ocr-{i+1:04d}'
    print(json.dumps(dict(schema_version=1, image=dict(width=image.width,height=image.height),
        detections=kept, candidate_count=candidates, seconds=time.perf_counter()-start,
        engine=phase1.tesserocr.tesseract_version()), ensure_ascii=True))
