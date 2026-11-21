# Procedural, tiling textures for the Sainte-Mere-Eglise church: rubble stone, dressed stone, slate, oak.
# Writes <name>_d.png (albedo) and <name>_n.png (normal, OpenGL/Unity +Y) into OUT.
import numpy as np, sys, os
from PIL import Image, ImageFilter
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(1944)

def noise(n, scale, seed):
    """periodic (tiling) noise: random spectrum filtered to a band, normalised 0..1"""
    r = np.random.default_rng(seed)
    f = np.fft.fftfreq(n)[:, None] ** 2 + np.fft.fftfreq(n)[None, :] ** 2
    amp = np.exp(-f * (scale ** 2) * 40.0) * (f > 0)
    z = np.fft.ifft2(amp * np.exp(2j * np.pi * r.random((n, n)))).real
    z -= z.min(); return z / max(z.max(), 1e-9)

def normal_from_height(h, strength):
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    nz = np.ones_like(h); l = np.sqrt(gx ** 2 + gy ** 2 + 1)
    n = np.stack([-gx / l, gy / l, nz / l], -1)            # image rows go down: +Y (OpenGL) is up the image
    return Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8))

def save(name, rgb, h, strength):
    Image.fromarray(np.clip(rgb * 255, 0, 255).astype(np.uint8)).save(f'{OUT}/{name}_d.png')
    normal_from_height(h, strength).save(f'{OUT}/{name}_n.png')

def masonry(n, course_px, len_px, mortar, palette, jitter, seed, rough=1.0):
    """courses of stones with random lengths, wrapping horizontally and vertically"""
    r = np.random.default_rng(seed)
    sid = np.zeros((n, n), np.int32); edge = np.ones((n, n), np.float32)
    rows = []; y = 0
    while y < n:
        hgt = int(r.uniform(*course_px)); 
        if n - y - hgt < course_px[0]: hgt = n - y
        rows.append((y, hgt)); y += hgt
    k = 1
    yy, xx = np.mgrid[0:n, 0:n]
    for (y0, hgt) in rows:
        x = int(r.integers(0, n)); start = x; total = 0
        while total < n:
            L = int(r.uniform(*len_px))
            if n - total - L < len_px[0]: L = n - total
            cols = (np.arange(x, x + L) % n)
            sid[y0:y0 + hgt, cols] = k
            # distance to the stone edge, for the mortar and the rounded edges
            dx = np.minimum(np.arange(L) + 1, L - np.arange(L))[None, :]
            dy = np.minimum(np.arange(hgt) + 1, hgt - np.arange(hgt))[:, None]
            # wobbly edges: rubble is not sawn
            d = np.minimum(dx + r.normal(0, jitter, (1, L)), dy + r.normal(0, jitter, (hgt, 1)))
            edge[y0:y0 + hgt, cols] = d
            x += L; total += L; k += 1
    nst = k
    tone = r.random(nst)
    pal = np.array(palette, np.float32)
    base = pal[r.integers(0, len(pal), nst)] * (0.85 + 0.3 * tone[:, None])
    rgb = base[sid]
    grain = noise(n, 0.4, seed + 1)[..., None]; big = noise(n, 6, seed + 2)[..., None]; pits = noise(n, 0.15, seed + 3)
    rgb = rgb * (0.8 + 0.35 * grain) * (0.9 + 0.2 * big)
    m = np.clip((edge - mortar) / 3.0, 0, 1)            # 0 in the joints
    mortar_col = np.array([0.62, 0.60, 0.55], np.float32) * (0.85 + 0.25 * grain)
    rgb = rgb * m[..., None] + mortar_col * (1 - m[..., None])
    # dark in the deepest joint, lichen/soot stains
    rgb *= (0.75 + 0.25 * np.clip(edge / (mortar + 0.5), 0, 1))[..., None]
    h = np.clip(edge / 9.0, 0, 1) ** 0.5 * 0.8 + 0.2 * grain[..., 0] * rough + 0.1 * pits
    h = np.where(edge < mortar, 0.0, h)
    rgb = rgb * 0.72 + rgb.mean(-1, keepdims=True) * 0.28     # weathered: less colour, more grey
    return rgb, h

N = 2048
# rubble walls: Cotentin limestone and grey schist, rough courses (texture = 4 m x 4 m)
rgb, h = masonry(N, (55, 110), (90, 260), 6.0, [(0.60, 0.57, 0.50), (0.55, 0.53, 0.48), (0.66, 0.62, 0.54), (0.47, 0.46, 0.44), (0.58, 0.52, 0.42)], 2.5, 11)
save('SME_Rubble', rgb * 0.92, h, 6.0)
# dressed limestone for quoins, buttresses and window surrounds (texture = 2 m x 2 m)
rgb, h = masonry(1024, (150, 190), (220, 420), 2.5, [(0.72, 0.69, 0.61), (0.69, 0.66, 0.58), (0.75, 0.71, 0.62)], 0.6, 12, 0.5)
save('SME_Ashlar', rgb, h, 4.0)
# slate (texture = 2 m x 2 m): staggered rows, each slate darker at its lower edge where the next row overlaps
n = 1024; r = np.random.default_rng(13)
rgb = np.zeros((n, n, 3), np.float32); h = np.zeros((n, n), np.float32)
row = 64; w = 110
for i in range(n // row):
    off = (i % 2) * w // 2 + int(r.integers(-8, 8))
    for j in range(n // w + 2):
        x0 = off + j * w + int(r.integers(-4, 4)); x1 = x0 + w - 3
        c = np.array([0.20, 0.22, 0.25]) * (0.8 + 0.45 * r.random()) + np.array([0.0, 0.0, 0.02]) * r.random()
        cols = np.arange(x0, x1) % n
        ys = np.arange(i * row, (i + 1) * row)
        grad = np.linspace(1.15, 0.75, row)[:, None]            # lit top edge, shadowed overlap
        rgb[np.ix_(ys, cols)] = c * grad[..., None] * np.ones((1, len(cols), 1))
        h[np.ix_(ys, cols)] = np.linspace(0.2, 1.0, row)[:, None]
g = noise(n, 0.3, 14)
rgb *= (0.85 + 0.3 * g)[..., None]; rgb += (noise(n, 4, 15) > 0.78)[..., None] * np.array([0.05, 0.06, 0.02])   # a little moss
save('SME_Slate', rgb, h, 3.0)
# oak planks with iron studs (texture = 2 m x 2 m)
n = 512; x = np.arange(n)
plank = (x // 64) % 8; r = np.random.default_rng(16)
tone = r.random(8)[plank][None, :]
grain = noise(n, 0.2, 17); streak = np.abs(np.sin((np.arange(n)[None, :] * 0.35) + noise(n, 3, 18) * 12))
rgb = np.array([0.30, 0.20, 0.12])[None, None, :] * (0.7 + 0.4 * tone[..., None]) * (0.75 + 0.35 * streak[..., None]) * (0.85 + 0.2 * grain[..., None])
gap = (x % 64 < 2)[None, :] * np.ones((n, 1)); rgb[gap > 0] *= 0.3
h = 1 - gap * 1.0 + 0.1 * streak
yy, xx = np.mgrid[0:n, 0:n]
for cy in range(32, n, 128):
    for cx in range(32, n, 64):
        d = (yy - cy) ** 2 + (xx - cx) ** 2 < 25; rgb[d] = (0.08, 0.08, 0.08); h[d] = 1.6
save('SME_Oak', rgb, h, 2.0)
print('textures in', OUT)
