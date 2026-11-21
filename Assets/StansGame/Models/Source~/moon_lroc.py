# The full moon for the night sky, from NASA's LRO colour map (lroc_color_poles_1k.jpg, NASA SVS CGI Moon Kit,
# public domain): the near side projected onto a disc (orthographic, centred on 0 deg longitude / 0 deg latitude),
# lunar north up, with a little limb darkening. Run: python3 moon_lroc.py <lroc jpg> <out png>
import sys, numpy as np
from PIL import Image
src, out = sys.argv[1:3]
M = np.asarray(Image.open(src).convert('RGB')).astype(np.float32) / 255.0
h, w, _ = M.shape
N = 512
yy, xx = np.mgrid[0:N, 0:N]
x = (xx + 0.5) / N * 2 - 1; y = 1 - (yy + 0.5) / N * 2
r2 = x * x + y * y; inside = r2 <= 1.0
z = np.sqrt(np.clip(1 - r2, 0, 1))
lat = np.arcsin(np.clip(y, -1, 1)); lon = np.arctan2(x, z)
u = (np.degrees(lon) + 180.0) / 360.0 * w; v = (90.0 - np.degrees(lat)) / 180.0 * h
ui = np.clip(u.astype(int), 0, w - 1); vi = np.clip(v.astype(int), 0, h - 1)
col = M[vi, ui]
col = col / np.percentile(col[inside], 99) * 0.95                      # full range: bright highlands, dark seas
col = col * (0.72 + 0.28 * z[..., None] ** 0.35)                      # limb darkening
grey = col.mean(axis=2, keepdims=True); col = grey + (col - grey) * 0.35   # the eye sees the moon nearly colourless
alpha = np.clip((1 - np.sqrt(r2)) * N / 2, 0, 1)
img = np.dstack([np.clip(col, 0, 1), alpha])
Image.fromarray((img * 255).astype(np.uint8), 'RGBA').save(out)
print('moon', out)
