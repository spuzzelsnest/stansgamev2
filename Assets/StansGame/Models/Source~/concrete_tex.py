# Seamless concrete (from _downloads/concrete-wall) + a normal map from its relief. Run: python3 -I concrete_tex.py <ConcreteWall.png> <out dir>
import sys, numpy as np
from PIL import Image
src, out = sys.argv[1], sys.argv[2]
a = np.asarray(Image.open(src).convert('RGB'), dtype=np.float32) / 255.0
h, w, _ = a.shape
def ramp(n):
    t = np.abs(np.arange(n) - (n - 1) / 2) / ((n - 1) / 2)       # 0 at centre, 1 at the edges
    return np.clip((1 - t) / 0.35, 0, 1)                           # full original in the middle 30%... fades at edges
def smooth(m): return m * m * (3 - 2 * m)
mx = smooth(ramp(w))[None, :, None]; my = smooth(ramp(h))[:, None, None]
c = a * mx + np.roll(a, w // 2, 1) * (1 - mx)          # seamless across x (the rolled copy's seam sits where it has no weight)
c = c * my + np.roll(c, h // 2, 0) * (1 - my)          # then across y
# even out the big dark streaks a little so the repeat is less obvious
lum = c.mean(2, keepdims=True)
from PIL import ImageFilter
blur = np.asarray(Image.fromarray((lum[..., 0] * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(60)), dtype=np.float32)[..., None] / 255.0
c = np.clip(c - (blur - blur.mean()) * 0.55, 0, 1)
c = c * np.array([1.02, 1.0, 0.96])                                 # warmer, like the 1930s quay in pb01
c = np.clip(c * 1.15, 0, 1)
Image.fromarray((c * 255).astype(np.uint8)).save(f'{out}/Concrete_d.png')
g = c.mean(2)
dx = (np.roll(g, -1, 1) - np.roll(g, 1, 1)) * 3.0
dy = (np.roll(g, -1, 0) - np.roll(g, 1, 0)) * 3.0
n = np.dstack([-dx, dy, np.ones_like(g)]); n /= np.linalg.norm(n, axis=2, keepdims=True)
Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8)).save(f'{out}/Concrete_n.png')
print('ok', c.shape, abs(c[:, 0] - c[:, -1]).mean(), abs(c[0] - c[-1]).mean())
