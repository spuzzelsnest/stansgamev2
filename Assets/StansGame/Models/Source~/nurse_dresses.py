# Dress colours for the nurse model (Models/Civilian/Nurse): recolours the blue of nurse_body_anya_vmtr_c.png.
# Nurse_Blue (as made), Nurse_White (Red Cross whites), Vion (Madame Vion: a dark navy dress, the apron dyed to match),
# and three civilian dresses (green, brown, burgundy) with the Red Cross badge painted out. 2048 px.
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter
T = '/mnt/user-data/uploads/stansGameV2/_downloads/_Characters/nurse/textures/'
src = Image.open(T + 'nurse_body_anya_vmtr_c.png').convert('RGBA').resize((2048, 2048), Image.LANCZOS)
a = np.array(src).astype(np.float32) / 255; rgb = a[..., :3]; alpha = a[..., 3:]
mx = rgb.max(-1); mn = rgb.min(-1); sat = (mx - mn) / np.maximum(mx, 1e-4)
r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
blue = gaussian_filter(((b > r + 0.025) & (b > g - 0.03) & (sat > 0.055)).astype(np.float32), 1.5)
cream = gaussian_filter(((sat < 0.16) & (mx > 0.45)).astype(np.float32), 1.0)
lum = (0.3 * r + 0.59 * g + 0.11 * b)[..., None]
H = 2048; cy, cx, rad = int(297 / 512 * H), int(190 / 512 * H), int(19 / 512 * H)
yy, xx = np.mgrid[0:H, 0:H]; badge = ((yy - cy) ** 2 + (xx - cx) ** 2 < rad ** 2).astype(np.float32)
def make(name, dress, apron=None, no_badge=False):
    out = rgb.copy()
    shade = np.clip(lum / 0.55, 0.25, 1.35)
    out = out * (1 - blue[..., None]) + np.clip(np.array(dress) * shade, 0, 1) * blue[..., None]
    if apron is not None:
        out = out * (1 - cream[..., None]) + np.clip(np.array(apron) * np.clip(lum / 0.75, 0.3, 1.2), 0, 1) * cream[..., None]
    if no_badge:
        fill = np.array(apron if apron is not None else (0.78, 0.76, 0.70)) * 0.95
        out = out * (1 - badge[..., None]) + fill * badge[..., None]
    im = Image.fromarray((np.concatenate([np.clip(out, 0, 1), alpha], -1) * 255).astype(np.uint8))
    im.save(f'Nurse_Dress_{name}.png')
src.save('Nurse_Dress_Blue.png')   # as made
make('White', (0.86, 0.86, 0.84))
make('Vion', (0.12, 0.13, 0.20), apron=(0.16, 0.17, 0.24), no_badge=True)
make('Green', (0.25, 0.33, 0.22), no_badge=True)
make('Brown', (0.40, 0.29, 0.20), no_badge=True)
make('Burgundy', (0.42, 0.13, 0.15), no_badge=True)
