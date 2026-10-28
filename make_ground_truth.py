"""Manual transcription, recorded before OCR. No OCR inputs are read here."""
import json, math
from pathlib import Path

ROOT = Path(__file__).resolve().parent
# Digit-only bounding rectangles in the original 300 dpi crops (x,y,w,h).
# Visually read from original crops at both resolutions, not OCR output.
MANUAL = {
 'A_head': [(81,35,29,20,21),(77,100,29,22,21),(14,308,95,22,21),(1,380,95,11,21),(2,439,95,13,21),(41,479,95,22,21),(78,687,29,22,21),(82,753,29,22,21)],
 'B_feet': [(49,22,51,22,21),(118,80,51,25,21),(120,80,90,25,21),(49,358,51,22,21),(118,416,51,25,21),(120,355,90,25,21)],
 'C_side': [(99,79,44,21,21),(106,121,44,25,21),(102,76,149,25,21),(107,121,149,25,21),(103,76,303,25,21),(109,121,303,25,21),(112,25,403,25,21),(114,68,403,25,21),(119,25,499,25,21),(51,81,499,21,21),(132,147,499,25,21),(121,25,534,25,21),(50,81,534,21,21),(127,147,534,25,21)],
 'D_rear': [(9,230,38,12,21),(77,35,198,22,21),(78,476,198,22,21),(12,144,284,22,21),(13,365,284,22,21)],
 'E_bottom': [(149,41,52,25,21),(148,82,52,25,21),(147,158,52,25,21),(145,255,52,25,21),(146,298,52,25,21)],
 'F_paint': [],
}

def main():
 regions=json.loads((ROOT/'evaluation_sources/regions.json').read_text())
 registration=json.loads((ROOT/'evaluation_sources/registration.json').read_text())
 m=registration['matrix']
 def transform(box):
  x,y,w,h=box
  pts=[(m[0][0]*a+m[0][1]*b+m[0][2],m[1][0]*a+m[1][1]*b+m[1][2]) for a,b in [(x,y),(x+w,y),(x,y+h),(x+w,y+h)]]
  l=math.floor(min(p[0] for p in pts)); t=math.floor(min(p[1] for p in pts)); r=math.ceil(max(p[0] for p in pts)); b=math.ceil(max(p[1] for p in pts))
  return [l,t,r-l,b-t]
 out={'provenance':'Manually transcribed from original-image crops before running OCR. Both resolutions visually checked. 600 dpi boxes transferred by image feature registration, not OCR.', 'bbox_definition':'Digit ink rectangle; excludes circle, parentheses, plus and leader. Manual tolerance approx +/-2 pixels at 300 dpi, scaled at 600 dpi.', 'registration':registration,'regions':[],'annotations':[]}
 for name,box in regions.items():
  x,y,x2,y2=box; bb=[x,y,x2-x,y2-y]
  out['regions'].append({'id':name,'bbox_300':bb,'bbox_600':transform(bb),'negative_only':not MANUAL[name]})
  for i,(num,l,t,w,h) in enumerate(MANUAL[name]):
   bb=[x+l,y+t,w,h]
   tags=[]
   if num>=100:tags+=['three_digits','small_print']
   elif num<10:tags+=['one_digit']
   else:tags+=['two_digits']
   if name in ('B_feet','C_side'):tags+=['leader_nearby','dense']
   if name=='C_side' and i in (0,2,4,6,8,9,10):tags+=['parentheses']
   if name=='E_bottom' and i in (0,4):tags+=['parentheses']
   if (name=='B_feet' and i in (0,1,3,4)) or (name=='C_side' and i in (9,10,12,13)):tags+=['compound']
   out['annotations'].append({'id':f'{name}_{i+1:02d}','region':name,'number':str(num),'bbox_300':bb,'bbox_600':transform(bb),'tags':tags})
 (ROOT/'ground_truth.json').write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8')
 print('Frozen manual annotations:',len(out['annotations']))

if __name__=='__main__':main()
