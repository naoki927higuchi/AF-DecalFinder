"""Local subprocess bridge. Never writes to the input image or Phase 1 data."""
import sys,json,time,threading,os
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
# Resolve the user argument before changing cwd. Tesseract's Windows narrow-path
# API can load relative ASCII tessdata even when the package path is Japanese.
image_path=Path(sys.argv[1]).resolve()
os.chdir(Path(__file__).resolve().parent)
import recognition
from PIL import Image
start=time.perf_counter();process=recognition.phase1.psutil.Process();peak=[process.memory_info().rss];stop=threading.Event()
def sample():
 while not stop.wait(.01):peak.append(process.memory_info().rss)
thread=threading.Thread(target=sample,daemon=True);thread.start()
try:
 with Image.open(image_path) as image:
  image.load();detections,circles=recognition.recognize(image,baseline='--baseline' in sys.argv)
  result=dict(schema_version=2,image=dict(width=image.width,height=image.height),detections=detections,candidate_count=len(circles),candidates=circles,seconds=time.perf_counter()-start,peak_rss_mb=max(peak)/1048576,engine=recognition.phase1.tesserocr.tesseract_version())
finally:stop.set();thread.join()
print(json.dumps(result,ensure_ascii=True))
