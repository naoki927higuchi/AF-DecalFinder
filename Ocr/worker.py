"""Local subprocess bridge. Never writes to the input image or Phase 1 data."""
import sys,json,time,threading,os
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
# Resolve the user argument before changing cwd. Tesseract's Windows narrow-path
# API can load relative ASCII tessdata even when the package path is Japanese.
image_path=Path(sys.argv[1]).resolve()
# EXIF orientation is decided by the viewer (1..8, default 1 = raw pixel matrix) and applied here
# losslessly, so returned coordinates refer to the same upright image the viewer displays.
orientation=int(sys.argv[sys.argv.index('--orientation')+1]) if '--orientation' in sys.argv else 1
if orientation not in range(1,9):orientation=1
os.chdir(Path(__file__).resolve().parent)
import recognition
from PIL import Image
start=time.perf_counter();process=recognition.phase1.psutil.Process();peak=[process.memory_info().rss];stop=threading.Event()
def sample():
 while not stop.wait(.01):peak.append(process.memory_info().rss)
thread=threading.Thread(target=sample,daemon=True);thread.start()
try:
 with Image.open(image_path) as image:
  image.load()
  if orientation!=1:
   image=image.transpose({2:Image.Transpose.FLIP_LEFT_RIGHT,3:Image.Transpose.ROTATE_180,4:Image.Transpose.FLIP_TOP_BOTTOM,
    5:Image.Transpose.TRANSPOSE,6:Image.Transpose.ROTATE_270,7:Image.Transpose.TRANSVERSE,8:Image.Transpose.ROTATE_90}[orientation])
  detections,circles=recognition.recognize(image,baseline='--baseline' in sys.argv)
  result=dict(schema_version=2,image=dict(width=image.width,height=image.height),detections=detections,candidate_count=len(circles),candidates=circles,seconds=time.perf_counter()-start,peak_rss_mb=max(peak)/1048576,engine=recognition.phase1.tesserocr.tesseract_version())
finally:stop.set();thread.join()
print(json.dumps(result,ensure_ascii=True))
