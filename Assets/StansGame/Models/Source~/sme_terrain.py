# Sainte-Mere-Eglise terrain. Step 1: mosaic the four LHD tiles (as _downloads/terrain/tools/mosaic.py, tiles 0386_6932/6933, 0387_6932/6933) to mosaic.npy; step 2: this script.
# Sainte-Mere-Eglise: IGN LiDAR HD MNT (0.5 m, Lambert-93 / IGN69) -> Unity heightmap in the game frame, as in June 1944.
# Game frame: origin = centre of the church of Notre-Dame-de-l'Assomption (from the LiDAR: the cut-out footprint in
# its churchyard), +X along the church axis (bearing 70 deg, towards the choir), +Z = bearing 340 deg (along the
# rue General de Gaulle, the old N13, towards Cherbourg). y = altitude - the ground at the church (so y 0 = the
# churchyard). 1025 x 1025 samples over 1024 m.
# 1944: the post-war earthworks are taken out - the N13 dual-carriageway bypass on the west of the town (embankments,
# cuttings, the interchange with the road to La Fiere) - by replacing ground that stands
# more than 1.3 m off the surrounding fields with that surrounding surface. Hedgerow banks (under ~1 m) stay.
import numpy as np, json
from scipy.ndimage import map_coordinates, gaussian_filter, median_filter, binary_dilation, label, zoom
M = np.load('mosaic.npy').astype(np.float64); X0, YT = 386000.0, 6933000.0
O = np.array([386808.5, 6931821.0]); BEAR = np.radians(340.0)
zax = np.array([np.sin(BEAR), np.cos(BEAR)]); xax = np.array([zax[1], -zax[0]])    # +X = bearing 70
RES = 1025; SIZE = 1024.0
g = np.linspace(-SIZE / 2, SIZE / 2, RES); GX, GZ = np.meshgrid(g, g)              # [iz, ix]
E = O[0] + GX * xax[0] + GZ * zax[0]; N = O[1] + GX * xax[1] + GZ * zax[1]
Ms = gaussian_filter(M, 0.7)
H = map_coordinates(Ms, [(YT - N) / 0.5, (E - X0) / 0.5], order=1, mode='nearest')
zref = float(map_coordinates(Ms, [[(YT - O[1]) / 0.5], [(O[0] - X0) / 0.5]], order=1)[0])
print('church ground', zref, 'range', H.min(), H.max())
# --- the fields' own surface: a robust (median) 90 m surface on a 4 m grid
small = H[::4, ::4]
B = median_filter(small, size=23, mode='nearest')
B = gaussian_filter(B, 3)
B = zoom(B, (RES / B.shape[0], RES / B.shape[1]), order=1)[:RES, :RES]
D = H - B
mask = np.abs(D) > 1.3
lab, n = label(mask)
sizes = np.bincount(lab.ravel())
keep = sizes >= 250; keep[0] = False                   # ignore small things (a pond, a pit, a mound)
mask = keep[lab]
R = np.hypot(GX, GZ)
mask &= R > 170                                        # the old town centre is left as measured
mask = binary_dilation(mask, iterations=6)
# the N13 bypass itself (both carriageways, the slip roads and the interchange with the road to La Fiere), traced
# from the LiDAR in game coordinates: everything within 30 m of it goes back to the fields' surface
BYPASS = [[(-280, 512), (-332, 357), (-368, 203), (-378, 48), (-368, -65), (-332, -209), (-275, -333), (-213, -456), (-182, -513)],
          [(-275, -333), (-203, -343), (-131, -508)], [(-262, 512), (-285, 400), (-300, 300)]]
def seg_dist(px, pz, a, b):
    a = np.array(a, float); b = np.array(b, float); ab = b - a
    t = np.clip(((px - a[0]) * ab[0] + (pz - a[1]) * ab[1]) / (ab @ ab), 0, 1)
    return np.hypot(px - (a[0] + t * ab[0]), pz - (a[1] + t * ab[1]))
dist = np.full(GX.shape, 1e9)
for line in BYPASS:
    for a, b in zip(line[:-1], line[1:]): dist = np.minimum(dist, seg_dist(GX, GZ, a, b))
mask |= dist < 30
mask |= (np.hypot(GX + 378, GZ - 48) < 55)   # the interchange
w = gaussian_filter(mask.astype(float), 3)
H44 = H * (1 - w) + B * w
print('changed %.1f%% of the map' % (100 * (w > 0.5).mean()))
Y = H44 - zref
lo, hi = float(Y.min()), float(Y.max())
BASE = np.floor(lo - 1.0); HEIGHT = float(np.ceil(hi - BASE + 1.0))
U = np.round(np.clip((Y - BASE) / HEIGHT, 0, 1) * 65535).astype('<u2')
U.tofile('SainteMereHeights.r16')
meta = dict(source='IGN LiDAR HD MNT 0.5 m, tiles 0386_6932 0386_6933 0387_6932 0387_6933 (Lambert-93 / IGN69, acquired 2022), Etalab Open Licence 2.0; '
                   'post-war earthworks (the N13 bypass and its interchange) smoothed out',
            resolution=RES, size=SIZE, baseY=BASE, height=HEIGHT, zref=round(zref, 2), coverage=1.0,
            rowOrder='row 0 = z -512, column 0 = x -512', origin_L93=[O[0], O[1]], gridBearingDeg=340.0)
json.dump(meta, open('SainteMereHeights.json', 'w'), indent=1)
np.save('Y.npy', Y); np.save('Yraw.npy', H - zref); np.save('mask.npy', w)
print(meta['baseY'], meta['height'])
