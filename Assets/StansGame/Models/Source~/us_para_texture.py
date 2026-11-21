# Repaints the British para texture (Denison smock camouflage, battledress) as the US M1942 jump suit.
# Keeps skin, boots/gloves (dark) and web gear; cloth takes one khaki-olive colour with its folds and seams kept.
# Run: python3 us_para_texture.py <British_Para_BaseColor.jpg> <US_Para_BaseColor.jpg>
import sys, numpy as np
from PIL import Image, ImageFilter
im = np.asarray(Image.open(sys.argv[1]).convert('RGB')).astype(np.float32) / 255
r, g, b = im[..., 0], im[..., 1], im[..., 2]
mx = im.max(-1); mn = im.min(-1); v = mx; s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
d = np.maximum(mx - mn, 1e-6)
h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
skin = (r > g) & (g > b) & (r - b > 0.12) & (v > 0.35) & (h < 40) & (s > 0.12) & (s < 0.75) & (r > 0.45) & ((r - g) > 0.06)
black = ((v < 0.2) & (s < 0.4)) | (v < 0.07)
webbing = (h > 25) & (h < 60) & (v > 0.5) & (s > 0.2) & (s < 0.7) & ~skin
cloth = ~(skin | black | webbing)
lum = 0.30 * r + 0.59 * g + 0.11 * b
local = np.asarray(Image.fromarray((lum * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(24))).astype(np.float32) / 255
shade = np.clip(lum / np.maximum(local, 0.03), 0.6, 1.4) ** 0.6
out = im.copy()
out[cloth] = np.clip(np.array([0.36, 0.35, 0.25]) * shade[cloth, None], 0, 1)
wl = lum / np.maximum(local, 0.03)
out[webbing] = np.clip(np.array([0.50, 0.47, 0.34]) * np.clip(wl[webbing], 0.7, 1.3)[:, None], 0, 1)
Image.fromarray((out * 255).astype(np.uint8)).save(sys.argv[2], quality=92)
