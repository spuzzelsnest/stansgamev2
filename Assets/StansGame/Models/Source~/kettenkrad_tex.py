"""Kettenkrad textures: UDIM tiles 1001-1004 -> 2048 jpgs, the baked AO multiplied in, and the Normandy camouflage
(Olivgruen and Rotbraun bands over the Dunkelgelb) sprayed only on the painted metal - tracks, rubber and rust stay."""
import sys, os, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import normandy_paint as P
from PIL import Image
T = '/mnt/user-data/uploads/stansGameV2/_downloads/_Vehicles/sdkfz-2-kettenkrad/textures/'
os.makedirs('tex', exist_ok=True)
for i in range(1, 5):
    a = np.asarray(Image.open(T + f'22-11-29Kettenkrad2_Body_BaseColor.100{i}.png').convert('RGB'), float) / 255
    ao = np.asarray(Image.open(T + f'bake_ao_100{i}.png').convert('L').resize(a.shape[1::-1]), float) / 255
    h, w, _ = a.shape
    L = a @ np.array([0.3, 0.59, 0.11])
    # the painted metal: near the dominant sand colour, smooth (not the busy track and bolt areas)
    dom = np.median(a.reshape(-1, 3), 0)
    near = 1 - P.ss(0.10, 0.22, np.linalg.norm(a - dom, axis=2))
    busy = P.ss(0.05, 0.12, np.sqrt(np.abs(P.blur(L * L, 4) - P.blur(L, 4) ** 2)))
    paint = P.blur(near * (1 - busy), 3).clip(0, 1)
    n1 = P.noise(h, w, 55, 20 + i); n2 = P.noise(h, w, 50, 40 + i)
    olive = P.ss(0.75, 1.1, n1); brown = P.ss(0.95, 1.3, n2) * (1 - olive)
    detail = np.clip(L / (P.blur(L, 6) + 0.03), 0.6, 1.5)[..., None]
    camo = (P.DG * (1 - olive - brown)[..., None] + P.OG * olive[..., None] + P.RB * brown[..., None]) * detail
    out = a * (1 - paint[..., None] * 0.85) + camo * paint[..., None] * 0.85
    out *= (0.55 + 0.45 * ao)[..., None]
    Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8)).save(f'tex/Kettenkrad_{i}.jpg', quality=88)
    nm = Image.open(T + f'22-11-29Kettenkrad2_Body_Normal.100{i}.png').convert('RGB').resize((1024, 1024), Image.LANCZOS)
    nm.save(f'tex/Kettenkrad_{i}_N.jpg', quality=92)
print('ok')
