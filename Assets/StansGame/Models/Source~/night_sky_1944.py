# The night sky over Normandy on 5-6 June 1944, for the skybox (Scripts/Environment/NightSky.shader):
#   NightSky_Pegasus.png  Bénouville 49.2424 N 0.2747 W, 22:16 UTC on 5 June (00:16 British Double Summer Time:
#                         Howard's glider lands): moon 99% full at az 164, alt 22 in Scorpius; sun 14.5 deg down in the
#                         north (a last twilight glow), Jupiter and Mars low in the west.
#   NightSky_SME.png      Sainte-Mère-Église 49.4085 N 1.3167 W, 23:30 UTC (01:30 BDST, the 505th's drop).
# Equirectangular: u = azimuth / 360 (0 = north, 90 = east), v = altitude (bottom -90, top +90).
# RGB = sky colour (twilight glow; the moon's aureole is added by the shader), A = stars and planets.
# Also: Moon_1944.png (the full moon, its seas roughly where they are) and Clouds_Broken.png (tileable cloud field).
# Positions from PyEphem (its 115 brightest named stars: with a full moon up only the bright stars show anyway).
# Run: python3 night_sky_1944.py <out dir>
import sys, os, math, random
import numpy as np, ephem, ephem.stars
from PIL import Image, ImageFilter
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
W, H = 4096, 2048
def sky(name, lat, lon, utc):
    o = ephem.Observer(); o.lat, o.lon, o.date, o.pressure = lat, lon, utc, 0
    sun = ephem.Sun(o); moon = ephem.Moon(o)
    saz, salt = math.degrees(sun.az), math.degrees(sun.alt)
    # ---- background: dark zenith, paler towards the horizon, a faint glow where the sun is below the horizon
    v = (np.arange(H)[:, None] + 0.5) / H; alt = (v - 0.5) * 180.0                 # row 0 = bottom
    u = (np.arange(W)[None, :] + 0.5) / W; az = u * 360.0
    a = np.clip(alt, 0, 90)
    t = np.exp(-a / 22.0)                                                            # 1 at the horizon
    zen = np.array([0.020, 0.028, 0.055]); hor = np.array([0.060, 0.070, 0.100])     # horizon = the levels' fog colour
    rgb = zen[None, None, :] * (1 - t[..., None]) + hor[None, None, :] * t[..., None]
    rgb = np.broadcast_to(rgb, (H, W, 3)).copy()
    daz = np.abs(((az - saz + 180) % 360) - 180)
    glow = np.exp(-a / 7.0) * np.exp(-(daz / 55.0) ** 2) * max(0.0, (18 + salt) / 18.0)
    rgb += glow[..., None] * np.array([0.07, 0.085, 0.09])[None, None, :]            # pale greenish-grey twilight
    rgb[(alt < 0)[:, 0]] = hor
    # ---- stars and planets in the alpha channel
    A = np.zeros((H, W), np.float32)
    def dot(az_d, alt_d, mag):
        if alt_d < -1: return
        b = 10 ** (-0.4 * (mag - 1.0))                                               # mag 1 -> 1
        b *= np.clip((alt_d - 1) / 12.0, 0.15, 1.0)                                  # extinction low down
        x = az_d / 360.0 * W; y = (alt_d + 90) / 180.0 * H
        r = 1.0 + 0.6 * max(0.0, 2.0 - mag)
        sx = r / max(0.15, math.cos(math.radians(alt_d)))                            # equirect stretch near the zenith
        for yy in range(int(y - r * 2), int(y + r * 2) + 2):
            for xx in range(int(x - sx * 2), int(x + sx * 2) + 2):
                if 0 <= yy < H:
                    d2 = ((xx + 0.5 - x) / sx) ** 2 + ((yy + 0.5 - y) / r) ** 2
                    A[yy, xx % W] += min(1.0, b) * math.exp(-d2 * 1.6)
    n = 0
    for nm in ephem.stars.stars:
        s = ephem.star(nm); s.compute(o)
        if s.mag > 3.6: continue
        dot(math.degrees(s.az), math.degrees(s.alt), s.mag); n += 1
    for p in (ephem.Jupiter(o), ephem.Mars(o), ephem.Saturn(o), ephem.Venus(o)):
        dot(math.degrees(p.az), math.degrees(p.alt), p.mag)
    A = np.clip(A, 0, 1)
    img = np.dstack([np.clip(rgb, 0, 1), A])
    Image.fromarray((img[::-1] * 255 + 0.5).astype(np.uint8), 'RGBA').save(os.path.join(OUT, f'NightSky_{name}.png'))
    print(name, 'moon az %.1f alt %.1f  sun az %.1f alt %.1f  stars %d' % (math.degrees(moon.az), math.degrees(moon.alt), saz, salt, n))
sky('Pegasus', '49.2424', '-0.2747', '1944/6/5 22:16')
sky('SME', '49.4085', '-1.3167', '1944/6/5 23:30')

# ---- the full moon: bright highlands, the dark seas roughly where they are (as seen from the north, upright)
N = 512; yy, xx = np.mgrid[0:N, 0:N]; x = (xx + 0.5) / N * 2 - 1; y = 1 - (yy + 0.5) / N * 2
r2 = x * x + y * y; inside = r2 <= 1.0
rng = np.random.default_rng(1944)
def blob(cx, cy, rx, ry, k=1.0):
    return k * np.exp(-(((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2) ** 1.4)
maria = (blob(-0.25, 0.42, 0.33, 0.22) + blob(0.18, 0.40, 0.17, 0.16) + blob(0.33, 0.12, 0.2, 0.2) + blob(0.68, 0.30, 0.12, 0.13)
         + blob(0.52, -0.22, 0.13, 0.22) + blob(-0.55, 0.05, 0.25, 0.45) + blob(-0.18, -0.38, 0.2, 0.17) + blob(-0.5, -0.38, 0.17, 0.14, 0.7)
         + blob(0.05, 0.05, 0.12, 0.12, 0.6))
maria = np.clip(maria, 0, 1)
noise = np.array(Image.fromarray((rng.random((64, 64)) * 255).astype(np.uint8)).resize((N, N), Image.BICUBIC)) / 255.0
fine = rng.random((N, N)) * 0.06
bright = 0.92 - 0.42 * maria + 0.08 * (noise - 0.5) - fine
bright += blob(-0.12, -0.62, 0.05, 0.05, 0.25)                                       # Tycho, bright young crater
bright *= (1 - r2.clip(0, 1)) ** 0.18 * 0.25 + 0.75                                  # slight limb darkening
col = np.dstack([bright * 1.0, bright * 0.98, bright * 0.93])
alpha = np.clip((1 - np.sqrt(r2)) * N / 2, 0, 1)
Image.fromarray((np.dstack([np.clip(col, 0, 1), alpha]) * 255).astype(np.uint8), 'RGBA').save(os.path.join(OUT, 'Moon_1944.png'))

# ---- broken stratocumulus: tileable fbm (sums of periodic value noise)
def periodic_noise(size, cells, seed):
    g = np.random.default_rng(seed).random((cells, cells))
    t = np.linspace(0, cells, size, endpoint=False); i = t.astype(int); f = t - i; f = f * f * (3 - 2 * f)
    i1 = (i + 1) % cells
    a = g[np.ix_(i, i)]; b = g[np.ix_(i, i1)]; c = g[np.ix_(i1, i)]; d = g[np.ix_(i1, i1)]
    fx = f[None, :]; fy = f[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy
S = 1024; fbm = np.zeros((S, S))
for k, (cells, w) in enumerate(((4, 0.5), (8, 0.25), (16, 0.14), (32, 0.07), (64, 0.04))):
    fbm += w * periodic_noise(S, cells, 6 + k)
fbm = (fbm - fbm.min()) / (fbm.max() - fbm.min())
Image.fromarray((fbm * 255).astype(np.uint8), 'L').save(os.path.join(OUT, 'Clouds_Broken.png'))
print('moon and clouds done')
