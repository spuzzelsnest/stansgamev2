"""Normandy 1944 paint for the vehicle textures (Stan's Game, 23 Nov 2026).
German: Dunkelgelb (RAL 7028) base with Olivgruen (RAL 6003) and Rotbraun (RAL 8017) sprayed bands, as the factories and
units applied it from 1944. Allied: US Olive Drab No. 9 / British SCC 15, plus Normandy dust and mud.
The model's own detail (panel lines, grime, shading) is kept from the original texture's luminance; very dark parts
(tracks, tyres, openings) are left alone. Usage: python3 normandy_paint.py <in> <out> <scheme> [seed] [scale]"""
import sys, numpy as np
from PIL import Image
from numpy.fft import fft2, ifft2, fftfreq

def noise(h, w, scale, seed):
    r = np.random.default_rng(seed).standard_normal((h, w))
    k = np.sqrt((fftfreq(h)[:, None] * h / 1024) ** 2 + (fftfreq(w)[None, :] * w / 1024) ** 2)
    n = np.real(ifft2(fft2(r) * np.exp(-(k * scale) ** 2)))
    return (n - n.mean()) / (n.std() + 1e-9)

def blur(a, scale):
    h, w = a.shape
    k = np.sqrt((fftfreq(h)[:, None] * h / 1024) ** 2 + (fftfreq(w)[None, :] * w / 1024) ** 2)
    return np.real(ifft2(fft2(a) * np.exp(-(k * scale) ** 2)))

def ss(e0, e1, x): t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)

DG = np.array([0.71, 0.62, 0.42]); OG = np.array([0.30, 0.34, 0.22]); RB = np.array([0.38, 0.23, 0.16])
OD = np.array([0.33, 0.32, 0.21]); SCC = np.array([0.30, 0.31, 0.21]); DUST = np.array([0.62, 0.55, 0.42])

def paint(src, dst, scheme, seed=1, scale=60.0):
    img = Image.open(src).convert('RGB'); a = np.asarray(img, float) / 255; h, w, _ = a.shape
    L = a @ np.array([0.3, 0.59, 0.11])
    detail = np.clip(L / (blur(L, 6) + 0.03), 0.55, 1.6)                 # panel lines, rivets, grime
    shade = np.clip(blur(L, 40) / (L.mean() + 1e-3), 0.55, 1.35)          # broad shading and ambient occlusion
    keep = 1 - ss(0.05, 0.14, blur(L, 3))                                 # very dark: tracks, tyres, holes
    sat = a.max(2) - a.min(2)
    keep = np.maximum(keep, ss(0.52, 0.62, L) * (1 - ss(0.18, 0.3, sat)))   # white markings: stars, numbers, crosses
    keep = np.maximum(keep, ss(0.25, 0.35, sat) * ss(0.3, 0.4, L) * (np.abs(a[..., 0] - a[..., 2]) > 0.25))  # red / yellow signs
    if scheme == 'german':
        n1 = noise(h, w, scale, seed); n2 = noise(h, w, scale * 0.9, seed + 7)
        edge = 0.18                                                       # soft sprayed edges
        olive = ss(0.45 - edge, 0.45 + edge, n1); brown = ss(0.55 - edge, 0.55 + edge, n2) * (1 - olive)
        col = DG[None, None] * (1 - olive - brown)[..., None] + OG * olive[..., None] + RB * brown[..., None]
    elif scheme == 'dunkelgelb':
        n1 = noise(h, w, scale, seed); olive = ss(0.9, 1.3, n1) * 0.85
        col = DG[None, None] * (1 - olive)[..., None] + OG * olive[..., None]
    else:
        col = np.broadcast_to((OD if scheme == 'us' else SCC)[None, None], (h, w, 3)).copy()
    out = col * detail[..., None] * (0.5 + 0.5 * shade)[..., None]
    if scheme in ('us', 'british', 'dust'):
        if scheme == 'dust': out = a.copy()
        d = ss(0.7, 2.0, noise(h, w, scale * 0.8, seed + 3)) * 0.3
        out = out * (1 - d[..., None]) + (DUST * L[..., None] / max(L.mean(), 0.05) * 0.55 + DUST * 0.45) * d[..., None] * np.clip(detail, 0.8, 1.2)[..., None]
    out = out * (1 - keep[..., None]) + a * keep[..., None]
    Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8)).save(dst, quality=90)

if __name__ == '__main__':
    paint(sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 1, float(sys.argv[5]) if len(sys.argv) > 5 else 60.0)
