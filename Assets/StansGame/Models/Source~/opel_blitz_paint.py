# Paint schemes for the Opel Blitz (Models/Props/SME/Textures/OpelBlitzPaint): run in a folder holding x_OpelBlitz_BaseColor.jpg.png
# (the base colour extracted from OpelBlitzCovered.fbx). Dunkelgelb + Olivgruen/Rotbraun camouflage, plain Dunkelgelb, Panzergrau, olive.
import numpy as np, colorsys
from PIL import Image
from scipy.ndimage import gaussian_filter, zoom
src=np.array(Image.open('x_OpelBlitz_BaseColor.jpg.png').convert('RGB')).astype(np.float32)/255
H,W,_=src.shape
mx=src.max(-1); mn=src.min(-1); v=mx; s=np.where(mx>1e-4,(mx-mn)/np.maximum(mx,1e-4),0)
r,g,b=src[...,0],src[...,1],src[...,2]
# hue
d=np.maximum(mx-mn,1e-5)
h=np.where(mx==r,((g-b)/d)%6,np.where(mx==g,(b-r)/d+2,(r-g)/d+4))*60
tan=(h>18)&(h<62)&(s>0.10)&(v>0.14)
canvas=np.zeros_like(tan); canvas[0:int(H*0.42),int(W*0.70):]=True   # the canvas tilt island (top right)
wood=np.zeros_like(tan); wood[int(H*0.42):int(H*0.90),int(W*0.77):]=True   # the wooden bed
paint=tan&~canvas&~wood
pm=gaussian_filter(paint.astype(np.float32),1.2)
pm=np.maximum(pm,wood.astype(np.float32)*0.8)   # the wooden bed was painted too
def noise(seed,scale):
    rng=np.random.default_rng(seed); n=rng.random((H//scale+2,W//scale+2)).astype(np.float32)
    n=zoom(n,scale,order=3)[:H,:W]; return gaussian_filter(n,scale*0.35)
def variant(name,base,camo,canvas_col,seed):
    out=src.copy()
    lum=(0.3*r+0.59*g+0.11*b)
    # the paint: shading from the original luminance, new colour
    shade=np.clip(lum/0.27,0.2,1.12)[...,None]
    col=np.array(base,np.float32)[None,None,:]*np.ones((H,W,3),np.float32)
    for i,(c,thr) in enumerate(camo):
        n=noise(seed+i,110)
        m=np.clip((n-thr)*7,0,1)[...,None]
        col=col*(1-m)+np.array(c,np.float32)*m
    painted=np.clip(col*shade,0,1)
    out=out*(1-pm[...,None])+painted*pm[...,None]
    # canvas tilt and the rest: a little brighter; the tilt tinted
    lift=np.clip(src*1.35,0,1)
    out=np.where((pm[...,None]<0.5)&~canvas[...,None],lift*(1-pm[...,None])+out*pm[...,None],out)
    cshade=np.clip(lum/0.26,0.2,1.5)[...,None]
    cv=np.clip(np.array(canvas_col,np.float32)*cshade,0,1)
    out=np.where(canvas[...,None],cv,out)
    Image.fromarray((np.clip(out,0,1)*255).astype(np.uint8)).save(f'OpelBlitz_Paint_{name}.jpg',quality=92)
    Image.fromarray((np.clip(out,0,1)*255).astype(np.uint8)).resize((512,512)).save(f'p_{name}.jpg')
# 1944 Dunkelgelb with olive green and red-brown camouflage; plain dunkelgelb; the older Panzergrau; a Luftwaffe blue-grey
variant('Camo',(0.70,0.60,0.40),[((0.33,0.37,0.22),0.56),((0.44,0.27,0.17),0.60)],(0.46,0.47,0.33),11)
variant('Dunkelgelb',(0.72,0.62,0.42),[],(0.52,0.49,0.36),3)
variant('Grau',(0.33,0.35,0.35),[],(0.38,0.40,0.31),5)
variant('Olive',(0.38,0.41,0.25),[((0.64,0.55,0.36),0.58)],(0.44,0.45,0.31),21)
