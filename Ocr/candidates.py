"""Candidate detector experiments, separate from immutable Phase 1."""
import sys,math
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import benchmark as phase1
cv2,np=phase1.cv2,phase1.np

def dedup(cs):
 out=[]
 for c in cs:
  if not any(math.hypot(c[0]-v[0],c[1]-v[1])<min(c[2],v[2])*.65 for v in out):out.append(c)
 return out

def candidates(image,mode='hough'):
 scale=min(1,2800/max(image.size)); size=(round(image.width*scale),round(image.height*scale))
 rgb=np.asarray(image.convert('RGB').resize(size));gray=cv2.cvtColor(rgb,cv2.COLOR_RGB2GRAY)
 gray=cv2.GaussianBlur(gray,(5,5),1)
 low=max(6,round(size[0]*.002));high=max(low+2,round(size[0]*.013))
 out=[]
 for y in range(0,size[1],700):
  for x in range(0,size[0],700):
   pad=high+8;l=max(0,x-pad);t=max(0,y-pad);r=min(size[0],x+700+pad);b=min(size[1],y+700+pad)
   tile=gray[t:b,l:r]
   if mode=='hough':
    found=cv2.HoughCircles(tile,cv2.HOUGH_GRADIENT,1,low*1.6,param1=90,param2=20,minRadius=low,maxRadius=high)
    cs=[] if found is None else found[0]
   else:
    if mode=='adaptive':binary=cv2.adaptiveThreshold(tile,255,cv2.ADAPTIVE_THRESH_GAUSSIAN_C,cv2.THRESH_BINARY,61,7)
    elif mode=='otsu':binary=cv2.threshold(tile,0,255,cv2.THRESH_BINARY+cv2.THRESH_OTSU)[1]
    else:
     hsv=cv2.cvtColor(rgb[t:b,l:r],cv2.COLOR_RGB2HSV);binary=cv2.inRange(hsv,(0,70,30),(179,255,245));binary=cv2.morphologyEx(binary,cv2.MORPH_OPEN,np.ones((3,3),np.uint8))
    contours,_=cv2.findContours(binary,cv2.RETR_LIST,cv2.CHAIN_APPROX_SIMPLE);cs=[]
    for contour in contours:
     area=cv2.contourArea(contour)
     if len(contour)<8 or area<math.pi*low*low*.65 or area>math.pi*high*high*1.3:continue
     (cx,cy),(w,h),a=cv2.fitEllipse(contour);rad=(w+h)/4
     if not low<=rad<=high or min(w,h)/max(w,h)<.8 or area/(math.pi*w*h/4)<.85:continue
     points=contour[:,0,:];dist=np.sqrt((points[:,0]-cx)**2+(points[:,1]-cy)**2)
     if np.std(dist)/rad>.12:continue
     cs.append((cx,cy,rad))
   for cx,cy,rad in cs:
    cx=float(cx)+l;cy=float(cy)+t
    if x<=cx<min(x+700,size[0]) and y<=cy<min(y+700,size[1]):out.append((cx/scale,cy/scale,float(rad)/scale))
 return dedup(out)

def quality(image,cs):
 scale=min(1,2800/max(image.size));gray=cv2.GaussianBlur(np.asarray(image.convert('L').resize((round(image.width*scale),round(image.height*scale)))),(5,5),1)
 result=[];angles=np.arange(96)*2*np.pi/96
 for cx,cy,rad in cs:
  cx*=scale;cy*=scale;rad*=scale
  radii=np.arange(max(1,rad-3),rad+3.1,.5)
  xx=np.rint(cx+np.cos(angles)[:,None]*radii).astype(int);yy=np.rint(cy+np.sin(angles)[:,None]*radii).astype(int)
  if xx.min()<0 or yy.min()<0 or xx.max()>=gray.shape[1] or yy.max()>=gray.shape[0]:result.append((0,0));continue
  vals=gray[yy,xx].astype(float);edge=np.max(vals,axis=1)-np.min(vals,axis=1);support=float(np.mean(edge>35))
  x0=max(0,int(cx-rad*.65));y0=max(0,int(cy-rad*.65));x1=min(gray.shape[1],int(cx+rad*.65));y1=min(gray.shape[0],int(cy+rad*.65));inside=gray[y0:y1,x0:x1]
  contrast=float(np.percentile(inside,95)-np.percentile(inside,5)) if inside.size else 0
  result.append((support,contrast))
 return result

def select_candidates(image,variants=None):
 variants=variants or {m:candidates(image,m) for m in ['hough','adaptive','color']}
 hough=variants['hough'];qh=quality(image,hough)
 radii=[c[2] for c,q in zip(hough,qh) if q[0]>=.9 and q[1]>60]
 if not radii:return []
 # True binary scans have no halftone texture; area weighting can favor large mechanical rings.
 binary=sum(v>0 for v in image.convert('L').histogram())<=2
 power=1 if binary else 2
 tolerance=.4 if binary else .28
 mode=max(radii,key=lambda r:sum(abs(v-r)<r*.12 for v in radii)*r**power)
 mode=float(np.median([r for r in radii if abs(r-mode)<mode*.12]))
 base=variants.get('baseline');base=phase1.circle_candidates(image) if base is None else base
 groups=[(base,.55),(hough,.95 if binary else .9),(variants['adaptive'],.7),(variants['color'],.7)]
 selected=[]
 for cs,minimum in groups:
  qs=qh if cs is hough else quality(image,cs)
  for c,q in zip(cs,qs):
   if abs(c[2]-mode)<mode*tolerance and q[0]>=minimum and q[1]>60:selected.append(c)
 return dedup(selected)
