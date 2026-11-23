# Leaf canopy (tileable, opaque, with normal map) and bark (oak, poplar) for the tree crowns, hedges and trunks.
import numpy as np, math, random
from PIL import Image, ImageDraw, ImageFilter
N = 1024
rng = random.Random(1944); np.random.seed(1944)

def normal_from_height(h, strength):
    dx = np.roll(h, -1, 1) - np.roll(h, 1, 1); dy = np.roll(h, -1, 0) - np.roll(h, 1, 0)
    nx, ny, nz = -dx * strength, dy * strength, np.ones_like(h)
    l = np.sqrt(nx*nx + ny*ny + nz*nz)
    return Image.fromarray((np.stack([nx/l, ny/l, nz/l], 2) * 127.5 + 127.5).astype(np.uint8))

# ---- canopy: layers of leaves, deeper layers darker; height = layer
S = 2  # supersample
W = N * S
col = Image.new('RGB', (W, W), (12, 20, 9))
hgt = Image.new('L', (W, W), 0)
dc, dh = ImageDraw.Draw(col), ImageDraw.Draw(hgt)
def leaf_poly(cx, cy, L, Wd, ang, lobed):
    pts = []
    for i in range(24):
        t = i / 23.0
        x = (t - 0.5) * L
        w = math.sin(math.pi * t) ** 0.8 * Wd / 2
        if lobed: w *= 0.75 + 0.25 * abs(math.sin(t * math.pi * 5))
        pts.append((x, w))
    pts += [(x, -w) for x, w in reversed(pts)]
    c, s = math.cos(ang), math.sin(ang)
    return [(cx + x*c - y*s, cy + x*s + y*c) for x, y in pts]
layers = 6
for layer in range(layers):
    k = layer / (layers - 1)
    count = int(2600 * (0.6 + 0.4 * (1 - k)))
    for _ in range(count):
        cx, cy = rng.random() * W, rng.random() * W
        L = rng.uniform(26, 46) * S; Wd = L * rng.uniform(0.45, 0.62)
        ang = rng.random() * math.tau; lobed = rng.random() < 0.45
        g = rng.uniform(0.75, 1.15)
        base = (int((28 + 40*k) * g), int((48 + 62*k) * g), int((18 + 26*k) * g))
        if rng.random() < 0.12: base = (int(base[0]*1.25), int(base[1]*1.15), int(base[2]*0.8))  # sunlit, yellower
        hv = int(40 + 210 * k * rng.uniform(0.85, 1.0))
        for ox in (-W, 0, W):
            for oy in (-W, 0, W):
                x, y = cx + ox, cy + oy
                if -L < x < W + L and -L < y < W + L:
                    p = leaf_poly(x, y, L, Wd, ang, lobed)
                    dc.polygon(p, fill=base); dh.polygon(p, fill=hv)
                    # the midrib
                    c_, s_ = math.cos(ang), math.sin(ang)
                    dc.line([(x - c_*L*0.45, y - s_*L*0.45), (x + c_*L*0.45, y + s_*L*0.45)], fill=tuple(min(255, int(v*1.2)) for v in base), width=S)
col = col.resize((N, N), Image.LANCZOS); hgt = hgt.resize((N, N), Image.LANCZOS).filter(ImageFilter.GaussianBlur(1.2))
col.save('Leaves_Canopy.jpg', quality=88)
normal_from_height(np.asarray(hgt, np.float32) / 255.0, 6.0).save('Leaves_Canopy_N.png')

# ---- bark: periodic noise
def pnoise(n, freqs, seed):
    r = np.random.RandomState(seed); y, x = np.mgrid[0:n, 0:n] / n
    out = np.zeros((n, n), np.float32)
    for fx, fy, a in freqs:
        for _ in range(3):
            ph = r.rand() * math.tau; kx = r.randint(-fx, fx + 1); ky = r.randint(1, fy + 1)
            out += a * np.sin(math.tau * (kx * x + ky * y) + ph)
    return out / (np.abs(out).max() + 1e-6)
def bark(name, base, dark, furrow_count, furrow_depth, seed):
    y, x = np.mgrid[0:N, 0:N] / N
    warp = pnoise(N, [(1, 6, 1.0), (2, 12, 0.5)], seed) * 0.018
    u = furrow_count * (x + warp) % furrow_count
    rid = np.abs(np.sin(math.pi * u)) ** 0.35                     # broad plates, narrow dark furrows
    col_ = np.floor(u).astype(int) % furrow_count
    ph = np.random.RandomState(seed + 3).rand(furrow_count) * 10; rep = np.random.RandomState(seed + 4).randint(3, 7, furrow_count)
    crack = np.exp(-np.sin(math.pi * (y * rep[col_] + ph[col_] + 0.05 * warp * 50)) ** 2 / 0.006)
    rid = rid * (1 - 0.75 * crack)
    plates = pnoise(N, [(furrow_count * 2, 30, 0.5), (furrow_count * 4, 60, 0.3)], seed + 1)
    fine = np.random.RandomState(seed + 2).rand(N, N).astype(np.float32)
    fine = np.asarray(Image.fromarray((fine * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.0)), np.float32) / 255
    h = rid * furrow_depth + 0.25 * (plates - plates.min()) / (np.ptp(plates) + 1e-6) + 0.15 * fine
    h = (h - h.min()) / np.ptp(h)
    c = np.array(dark)[None, None, :] * (1 - h[..., None]) + np.array(base)[None, None, :] * h[..., None]
    c *= (0.9 + 0.2 * fine[..., None])
    # lichen patches
    lich = pnoise(N, [(3, 4, 1.0), (6, 8, 0.5)], seed + 5)
    m = np.clip((lich - 0.55) * 1.2, 0, 0.35)[..., None]
    c = c * (1 - m) + np.array([92, 100, 78])[None, None, :] * m
    Image.fromarray(np.clip(c, 0, 255).astype(np.uint8)).save(f'{name}.jpg', quality=88)
    normal_from_height(h, 5.0).save(f'{name}_N.png')
bark('Bark_Oak', (96, 84, 70), (28, 23, 19), 9, 1.0, 7)
bark('Bark_Poplar', (128, 124, 112), (52, 49, 44), 5, 0.6, 11)
