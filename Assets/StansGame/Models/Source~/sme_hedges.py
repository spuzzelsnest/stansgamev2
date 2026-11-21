# Sainte-Mere-Eglise bocage: the hedgerow banks (talus) found in the LiDAR ground (Y.npy from sme_terrain.py, 1 m
# grid, game frame) -> Terrain/SainteMereHedges.json: points every ~7.5 m along each bank, with its direction.
# A bank = ground standing 0.12 m or more above its 12 m surroundings (or a ditch 0.18 m below), in a line at least 30 m long. Outside the town,
# off the roads, and not where the bypass earthworks were smoothed away.
import numpy as np, json
from scipy.ndimage import gaussian_filter, label, binary_closing
from skimage.morphology import skeletonize
Y = np.load('Y.npy'); W = np.load('mask.npy')
n = Y.shape[0]; g = np.linspace(-512, 512, n); GX, GZ = np.meshgrid(g, g)
D = gaussian_filter(Y, 0.6) - gaussian_filter(Y, 6)
ridge = ((D > 0.12) | (D < -0.18)) & (W < 0.005)  # a bank, or the ditch along a hedge
from scipy.ndimage import uniform_filter, binary_dilation as bd
ridge &= ~bd(W > 0.005, iterations=4)                  # not along the edge of the smoothed bypass
rough = uniform_filter(ridge.astype(float), 41) > 0.35  # woodland and rough ground: everything is a 'ridge' there
ridge &= ~rough
town = (GX > -160) & (GX < 140) & (GZ > -285) & (GZ < 285)
ridge &= ~town
ROADS = [((-92, -512), (-92, 512), 9), ((-96, 46), (-512, 46), 6), ((33, 24), (110, 40), 6), ((110, 40), (330, 95), 6), ((330, 95), (512, 140), 6),
         ((33, 24), (78, 70), 5.5), ((78, 70), (86, 150), 5.5), ((86, 150), (100, 300), 5.5), ((100, 300), (128, 512), 5.5), ((-235, 46), (-235, -5), 4)]
def sd(a, b):
    a = np.array(a, float); b = np.array(b, float); ab = b - a
    t = np.clip(((GX - a[0]) * ab[0] + (GZ - a[1]) * ab[1]) / (ab @ ab), 0, 1)
    return np.hypot(GX - (a[0] + t * ab[0]), GZ - (a[1] + t * ab[1]))
road = np.zeros_like(ridge)
for a, b, w in ROADS: road |= sd(a, b) < w / 2 + 4
ridge &= ~road
ridge &= np.hypot(GX + 235, GZ + 15) > 45            # the 3/505 orchard
ridge = binary_closing(ridge, iterations=1)
sk = skeletonize(ridge)
lab, k = label(sk, structure=np.ones((3, 3)))
ok = np.zeros(k + 1, bool)
izl, ixl = np.nonzero(lab)
from scipy.ndimage import find_objects
for c, sl in enumerate(find_objects(lab), start=1):
    if sl is None: continue
    zz, xx = np.nonzero(lab[sl] == c)
    if len(zz) < 30: continue
    Q = np.c_[xx, zz].astype(float); Q -= Q.mean(0)
    w = np.linalg.eigvalsh(Q.T @ Q)
    # a field boundary: a long straight run, or a long line with a corner or two (not a blob of rough ground)
    ok[c] = (len(zz) >= 40 and w[1] > 15 * max(w[0], 1e-3)) or (len(zz) >= 110 and w[1] > 3 * w[0])
sk &= ok[lab]
iz, ix = np.nonzero(sk)
P = np.c_[g[ix], g[iz]]
rng = np.random.default_rng(1944); order = rng.permutation(len(P))
from scipy.spatial import cKDTree
tree = cKDTree(P); taken = []; tk = None
acc = np.zeros(len(P), bool)
pts = []
for i in order:
    p = P[i]
    if pts:
        if min(np.hypot(*(np.array(pts)[-200:, :2] - p).T)) < 7.0 if len(pts) < 200 else False: continue
    pts.append((p[0], p[1]))
# proper thinning with a grid hash
pts = []; cell = {}
for i in order:
    p = P[i]; c = (int(p[0] // 7.5), int(p[1] // 7.5)); clash = False
    for dx in (-1, 0, 1):
        for dz in (-1, 0, 1):
            for q in cell.get((c[0] + dx, c[1] + dz), []):
                if (q[0] - p[0]) ** 2 + (q[1] - p[1]) ** 2 < 7.0 ** 2: clash = True
    if clash: continue
    cell.setdefault(c, []).append(p); pts.append(p)
out = []
for p in pts:
    idx = tree.query_ball_point(p, 6.0)
    Q = P[idx] - P[idx].mean(0)
    if len(Q) < 4: continue
    w, v = np.linalg.eigh(Q.T @ Q); d = v[:, 1]
    out.append([round(float(p[0]), 1), round(float(p[1]), 1), round(float(np.degrees(np.arctan2(d[0], d[1]))), 0)])
print('bank pieces', len(out), 'skeleton px', len(P))
json.dump(dict(source='hedgerow banks found in the IGN LiDAR (sme_hedges.py)', pieces=[dict(x=a, z=b, yaw=c) for a, b, c in out]), open('SainteMereHedges.json', 'w'))
from PIL import Image
im = np.zeros((n, n, 3), np.uint8); im[...] = 40; im[ridge] = (90, 90, 90); im[sk] = (255, 255, 0)
for a, b, c in out:
    i, j = int(b + 512), int(a + 512); im[max(i-1,0):i+2, max(j-1,0):j+2] = (255, 0, 0)
Image.fromarray(im[::-1]).save('hedges.png')
