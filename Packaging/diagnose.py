"""Read-only portable runtime diagnostics. Does not touch application caches."""
import sys, pathlib, json, platform, os
import cv2, numpy, PIL, psutil, tesserocr
root = pathlib.Path(sys.executable).resolve().parents[2]
paths = [str(pathlib.Path(p).resolve()) for p in sys.path]
assert all(pathlib.Path(p).is_relative_to(root) for p in paths), paths
os.chdir(root/'Ocr')
with tesserocr.PyTessBaseAPI(path='tessdata', lang='eng', psm=7) as api:
    assert 'eng' in api.GetAvailableLanguages()
modules = sorted({m.path for m in psutil.Process().memory_maps()})
print(json.dumps(dict(python=sys.version, executable=sys.executable, platform=platform.platform(),
    paths=paths, versions=dict(numpy=numpy.__version__, opencv=cv2.__version__, pillow=PIL.__version__,
    psutil=psutil.__version__, tesserocr=tesserocr.__version__), engine=tesserocr.tesseract_version(),
    loaded_modules=modules), ensure_ascii=True, indent=2))
