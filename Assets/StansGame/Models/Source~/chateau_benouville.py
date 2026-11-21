# Château de Bénouville (Claude-Nicolas Ledoux, 1770s) for Stan's Game, from Joan's photos (chateau de benoville01-06):
# a big four-storey block of pale Caen stone, eleven bays on the long fronts and seven on the ends, rusticated corner
# pilasters and string courses; on the entrance front a giant tetrastyle Ionic portico (four columns in front, a
# second pair at the back corners) rising through the two lower floors, with an entablature and the sculpted arms
# above; a low hipped slate roof with chimney stacks; on the garden (canal) side a terrace behind a balustraded
# retaining wall; the service wing (communs) beside the forecourt and the small medieval chapel.
#
# Blender: X across the entrance front, +Y = the entrance (portico) front, Z up; 1 unit = 1 m; the forecourt level
# is z = 0. Exported nose-to-+Y like the other props (the Unity builder turns it with a 180 degree yaw).
# Run: python3 -I chateau_benouville.py OUT.fbx   (pip bpy)
import bpy, bmesh, sys, math, os
import numpy as np
from mathutils import Vector, Matrix

OUT = sys.argv[-1]
TEX = os.path.join(os.path.dirname(OUT) or '.', 'chateau_tex')
os.makedirs(TEX, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
rng = np.random.default_rng(1770)

# ---------------------------------------------------------------- textures (generated, UVs in metres)

def save_img(name, arr):
    h, w = arr.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=False)
    rgba = np.ones((h, w, 4), np.float32); rgba[..., :3] = arr
    img.pixels = rgba[::-1].ravel().tolist()
    path = os.path.join(TEX, name + '.png'); img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    return img

def noise(h, w, scale, octaves=4):
    acc = np.zeros((h, w), np.float32); amp = 1.0; tot = 0
    for o in range(octaves):
        s = max(1, int(scale / (2 ** o)))
        small = rng.random((h // s + 2, w // s + 2)).astype(np.float32)
        yy = np.linspace(0, small.shape[0] - 1.001, h); xx = np.linspace(0, small.shape[1] - 1.001, w)
        y0 = yy.astype(int); x0 = xx.astype(int); fy = (yy - y0)[:, None]; fx = (xx - x0)[None, :]
        a = small[y0][:, x0]; b = small[y0][:, x0 + 1]; c = small[y0 + 1][:, x0]; d = small[y0 + 1][:, x0 + 1]
        acc += amp * ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy); tot += amp; amp *= 0.5
    return acc / tot

def normal_from_height(hgt, strength):
    gy, gx = np.gradient(hgt)
    n = np.stack([-gx * strength, gy * strength, np.ones_like(hgt)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5

N = 1024
# Caen stone ashlar: 4 m square tile, courses 0.5 m, blocks 0.8-1.4 m, fine joints
def ashlar(base, course=0.5, tile=4.0, rustic=False):
    px = N / tile
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
    row = (yy / (course * px)).astype(int)
    off = (row % 2) * 0.55
    blk = 1.1
    xb = (xx / px + off * blk) / blk
    jx = np.abs(xb - np.round(xb)) * blk * px
    jy = np.abs(yy / (course * px) - np.round(yy / (course * px))) * course * px
    jw = 3.5 if rustic else 1.6
    joint = np.clip(1 - np.minimum(jx, jy) / jw, 0, 1)
    tone = 0.92 + 0.08 * noise(N, N, 48) + 0.05 * (rng.random((N, N)) - 0.5)
    per_block = (np.sin(row * 12.9898 + np.floor(xb) * 78.233) * 43758.5453) % 1.0
    tone *= 0.94 + 0.08 * per_block
    col = np.array(base, np.float32)[None, None, :] * tone[..., None]
    col = col * (1 - 0.35 * joint[..., None]) + np.array([0.55, 0.52, 0.45]) * 0.35 * joint[..., None] * 0.0
    hgt = -joint * (2.5 if rustic else 1.0) + 0.15 * noise(N, N, 16)
    return np.clip(col, 0, 1), normal_from_height(hgt, 3.0)

stone_c, stone_n = ashlar((0.93, 0.88, 0.75))
rust_c, rust_n = ashlar((0.91, 0.86, 0.73), course=0.55, rustic=True)
# slate: 2 m tile, courses 0.22 m
yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
px = N / 2.0
row = (yy / (0.22 * px)).astype(int)
xs = (xx / px + (row % 2) * 0.15) / 0.3
sj = np.minimum(np.abs(xs - np.round(xs)) * 0.3 * px, np.abs(yy / (0.22 * px) - np.round(yy / (0.22 * px))) * 0.22 * px)
sjoint = np.clip(1 - sj / 2.0, 0, 1)
stone_rand = (np.sin(row * 7.1 + np.floor(xs) * 3.3) * 999.9) % 1.0
slate_c = np.stack([0.22 + 0.04 * stone_rand, 0.24 + 0.04 * stone_rand, 0.28 + 0.04 * stone_rand], -1) * (1 - 0.4 * sjoint[..., None])
slate_n = normal_from_height(-sjoint + 0.1 * noise(N, N, 8), 3.0)

IMGS = {}
for name, arr in (('Chateau_Stone_d', stone_c), ('Chateau_Stone_n', stone_n), ('Chateau_Rustic_d', rust_c), ('Chateau_Rustic_n', rust_n),
                  ('Chateau_Slate_d', slate_c), ('Chateau_Slate_n', slate_n)):
    IMGS[name] = save_img(name, arr.astype(np.float32))

def material(name, color=None, img=None, nrm=None, rough=0.8, metal=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes['Principled BSDF']
    b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    if img:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = IMGS[img]; nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    elif color:
        b.inputs['Base Color'].default_value = (*color, 1)
    if nrm:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = IMGS[nrm]; t.image.colorspace_settings.name = 'Non-Color'
        nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(t.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], b.inputs['Normal'])
    return m

M_STONE = material('Chateau_Stone', img='Chateau_Stone_d', nrm='Chateau_Stone_n', rough=0.85)
M_RUST = material('Chateau_Rustic', img='Chateau_Rustic_d', nrm='Chateau_Rustic_n', rough=0.9)
M_SLATE = material('Chateau_Slate', img='Chateau_Slate_d', nrm='Chateau_Slate_n', rough=0.6)
M_GLASS = material('Chateau_Glass', color=(0.04, 0.05, 0.065), rough=0.08)
M_FRAME = material('Chateau_WindowFrame', color=(0.80, 0.79, 0.74), rough=0.6)
M_IRON = material('Chateau_Iron', color=(0.04, 0.04, 0.045), rough=0.5, metal=0.6)
M_DOOR = material('Chateau_Door', color=(0.20, 0.15, 0.10), rough=0.7)
MATS = [M_STONE, M_RUST, M_SLATE, M_GLASS, M_FRAME, M_IRON, M_DOOR]
MI = {m.name: i for i, m in enumerate(MATS)}
TILE = {'Chateau_Stone': 4.0, 'Chateau_Rustic': 4.0, 'Chateau_Slate': 2.0}

# ---------------------------------------------------------------- one big bmesh per object, box-projected UVs in metres

class Builder:
    def __init__(self, name):
        self.name = name; self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new('UVMap')

    def face(self, pts, mat):
        vs = [self.bm.verts.new(p) for p in pts]
        try: f = self.bm.faces.new(vs)
        except ValueError: return None
        f.material_index = MI[mat.name]
        n = (Vector(pts[1]) - Vector(pts[0])).cross(Vector(pts[2]) - Vector(pts[0]))
        ax = max(range(3), key=lambda i: abs(n[i]))
        t = TILE.get(mat.name, 1.0)
        for l in f.loops:
            c = l.vert.co
            u, v = ((c.y, c.z) if ax == 0 else (c.x, c.z) if ax == 1 else (c.x, c.y))
            l[self.uv].uv = (u / t, v / t)
        return f

    def box(self, c, s, mat, top=True, bottom=False):
        cx, cy, cz = c; sx, sy, sz = s[0] / 2, s[1] / 2, s[2] / 2
        p = [(cx + dx * sx, cy + dy * sy, cz + dz * sz) for dz in (-1, 1) for dy in (-1, 1) for dx in (-1, 1)]
        # p index: x fastest
        quads = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        for i, q in enumerate(quads):
            if i == 0 and not bottom: continue
            if i == 1 and not top: continue
            self.face([p[k] for k in q], mat)

    def cylinder(self, c, r0, r1, h, mat, seg=16, flutes=0, caps=True):
        cx, cy, cz = c
        ring = []
        for k in range(2):
            r = r0 if k == 0 else r1; z = cz + h * k; pts = []
            for i in range(seg):
                a = 2 * math.pi * i / seg
                rr = r * (1 - 0.06 * (i % 2)) if flutes else r
                pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr, z))
            ring.append(pts)
        for i in range(seg):
            j = (i + 1) % seg
            self.face([ring[0][i], ring[0][j], ring[1][j], ring[1][i]], mat)
        if caps:
            self.face(list(reversed(ring[0])), mat); self.face(ring[1], mat)

    def hcyl(self, c, r, length, axis, mat, seg=10):
        # horizontal cylinder along X (axis='x') or Y
        cx, cy, cz = c
        for i in range(seg):
            a0 = 2 * math.pi * i / seg; a1 = 2 * math.pi * (i + 1) / seg
            def P(a, s):
                if axis == 'x': return (cx + s * length / 2, cy + math.cos(a) * r, cz + math.sin(a) * r)
                return (cx + math.cos(a) * r, cy + s * length / 2, cz + math.sin(a) * r)
            self.face([P(a0, -1), P(a1, -1), P(a1, 1), P(a0, 1)], mat)
        for s in (-1, 1):
            pts = [((cx + s * length / 2, cy + math.cos(2 * math.pi * i / seg) * r, cz + math.sin(2 * math.pi * i / seg) * r) if axis == 'x'
                    else (cx + math.cos(2 * math.pi * i / seg) * r, cy + s * length / 2, cz + math.sin(2 * math.pi * i / seg) * r)) for i in range(seg)]
            self.face(pts if s > 0 else list(reversed(pts)), mat)

    def finish(self):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=0.001)
        # Unity draws one side of a face only, and the faces here are wound both ways (walls, reveals, roofs):
        # give every face a back face, so walls never show through from either side (Joan's printscreens 05/06)
        faces = list(self.bm.faces)
        dup = bmesh.ops.duplicate(self.bm, geom=faces)
        bmesh.ops.reverse_faces(self.bm, faces=[g for g in dup['geom'] if isinstance(g, bmesh.types.BMFace)])
        me = bpy.data.meshes.new(self.name); self.bm.to_mesh(me); self.bm.free()
        for m in MATS: me.materials.append(m)
        ob = bpy.data.objects.new(self.name, me); bpy.context.collection.objects.link(ob)
        return ob

# ---------------------------------------------------------------- a façade with windows (wall split round the openings)

def facade(B, origin, right, normal, width, height, openings, mat, depth=0.35):
    """origin = bottom-left corner of the wall plane (seen from outside), right = unit vector along it, normal = outward.
    openings: list of (u0, u1, v0, v1, kind) in metres along the wall. Builds the wall round them, the reveals, the glass,
    glazing bars and (kind 'balcony') a wrought-iron balconette."""
    O = Vector(origin); R = Vector(right); U = Vector((0, 0, 1)); Nn = Vector(normal)
    us = sorted({0.0, width, *[o[0] for o in openings], *[o[1] for o in openings]})
    vs = sorted({0.0, height, *[o[2] for o in openings], *[o[3] for o in openings]})
    def inside(u, v): return any(o[0] <= u <= o[1] and o[2] <= v <= o[3] for o in openings)
    P = lambda u, v, d=0.0: tuple(O + R * u + U * v - Nn * d)
    for i in range(len(us) - 1):
        for j in range(len(vs) - 1):
            uc, vc = (us[i] + us[i + 1]) / 2, (vs[j] + vs[j + 1]) / 2
            if inside(uc, vc): continue
            B.face([P(us[i], vs[j]), P(us[i + 1], vs[j]), P(us[i + 1], vs[j + 1]), P(us[i], vs[j + 1])], mat)
    for (u0, u1, v0, v1, kind) in openings:
        # reveals
        B.face([P(u0, v0), P(u0, v1), P(u0, v1, depth), P(u0, v0, depth)], mat)
        B.face([P(u1, v1), P(u1, v0), P(u1, v0, depth), P(u1, v1, depth)], mat)
        B.face([P(u1, v1), P(u1, v1, depth), P(u0, v1, depth), P(u0, v1)], mat)
        B.face([P(u0, v0), P(u0, v0, depth), P(u1, v0, depth), P(u1, v0)], mat)
        if kind == 'door':
            B.face([P(u0, v0, depth), P(u1, v0, depth), P(u1, v1, depth), P(u0, v1, depth)], M_DOOR)
            continue
        B.face([P(u0, v0, depth), P(u1, v0, depth), P(u1, v1, depth), P(u0, v1, depth)], M_GLASS)
        # frame and glazing bars, just proud of the glass
        d = depth - 0.03; bw = 0.06
        def bar(a0, a1, b0, b1):
            B.face([P(a0, b0, d), P(a1, b0, d), P(a1, b1, d), P(a0, b1, d)], M_FRAME)
        um = (u0 + u1) / 2
        bar(um - bw / 2, um + bw / 2, v0, v1)
        nb = max(1, int((v1 - v0) / 0.75))
        for k in range(1, nb):
            vv = v0 + (v1 - v0) * k / nb; bar(u0, u1, vv - bw / 2, vv + bw / 2)
        bar(u0, u1, v0, v0 + 0.1); bar(u0, u1, v1 - 0.1, v1); bar(u0, u0 + 0.09, v0, v1); bar(u1 - 0.09, u1, v0, v1)
        if kind == 'balcony':
            # a low wrought-iron rail across the opening, just outside the wall
            h = 0.85; out = 0.18
            rail = lambda a0, a1, b0, b1: B.face([P(a0, b0, -out), P(a1, b0, -out), P(a1, b1, -out), P(a0, b1, -out)], M_IRON)
            rail(u0 - 0.05, u1 + 0.05, v0 + h - 0.05, v0 + h)
            rail(u0 - 0.05, u1 + 0.05, v0 + 0.1, v0 + 0.15)
            for k in range(9):
                uu = u0 + (u1 - u0) * (k + 0.5) / 9; rail(uu - 0.015, uu + 0.015, v0 + 0.1, v0 + h)

# ---------------------------------------------------------------- the main block

W, D = 42.0, 26.0              # entrance front (x) and ends (y)
H = 19.0                       # to the top of the main cornice of the attic storey
FLOORS = [  # (sill, top, width, kind)
    (0.9, 4.9, 1.55, 'win'),       # ground floor: tall French windows
    (6.4, 9.8, 1.45, 'balcony'),   # first floor (piano nobile), with balconettes
    (11.7, 14.2, 1.30, 'win'),     # second floor
    (16.1, 17.6, 1.10, 'win'),     # attic storey
]
B = Builder('Chateau_Main')

def bays(width, n, pil=2.4):
    inner = width - 2 * pil
    return [pil + inner * (k + 0.5) / n for k in range(n)]

def front_openings(width, n, door_bays=(), skip=()):
    ops = []
    for k, uc in enumerate(bays(width, n)):
        for fi, (s, t, w, kind) in enumerate(FLOORS):
            if (k, fi) in skip: continue
            if fi == 0 and k in door_bays: ops.append((uc - w / 2, uc + w / 2, 0.6, t, 'door')); continue
            ops.append((uc - w / 2, uc + w / 2, s, t, kind))
    return ops

# entrance front (+Y): door in the centre bay behind the portico
facade(B, (-W / 2, D / 2, 0), (1, 0, 0), (0, 1, 0), W, H, front_openings(W, 11, door_bays=(5,)), M_STONE)
# garden front (-Y), seen from outside: runs from +x to -x
facade(B, (W / 2, -D / 2, 0), (-1, 0, 0), (0, -1, 0), W, H, front_openings(W, 11, door_bays=(5,)), M_STONE)
# the ends (+X and -X)
facade(B, (W / 2, D / 2, 0), (0, -1, 0), (1, 0, 0), D, H, front_openings(D, 7), M_STONE)
facade(B, (-W / 2, -D / 2, 0), (0, 1, 0), (-1, 0, 0), D, H, front_openings(D, 7), M_STONE)

# rusticated corner pilasters and the pilasters either side of the centre (portico) bays, plinth, string courses, cornices
def ring_band(z0, z1, out, mat):
    for (cx, cy, sx, sy) in ((0, D / 2 + out / 2, W + 2 * out, out), (0, -D / 2 - out / 2, W + 2 * out, out),
                             (W / 2 + out / 2, 0, out, D), (-W / 2 - out / 2, 0, out, D)):
        B.box((cx, cy, (z0 + z1) / 2), (sx, sy, z1 - z0), mat, top=True, bottom=True)

for (x, y) in ((W / 2, D / 2), (-W / 2, D / 2), (W / 2, -D / 2), (-W / 2, -D / 2)):
    B.box((x - math.copysign(0.95, x), y + math.copysign(0.08, y), H / 2), (1.9, 0.16, H), M_RUST)
    B.box((x + math.copysign(0.08, x), y - math.copysign(0.95, y), H / 2), (0.16, 1.9, H), M_RUST)
ring_band(0.0, 0.7, 0.18, M_RUST)                 # plinth
ring_band(5.55, 5.95, 0.16, M_STONE)              # first-floor string course
ring_band(10.35, 10.75, 0.22, M_STONE)            # main cornice below the second floor (as on photo 05)
ring_band(10.75, 11.15, 0.42, M_STONE)
ring_band(15.35, 15.7, 0.16, M_STONE)
ring_band(H - 0.6, H - 0.2, 0.3, M_STONE)         # top cornice
ring_band(H - 0.2, H + 0.25, 0.5, M_STONE)
# roof: low hipped slate roof rising from the cornice to a flat top, chimney stacks
rz0, rz1, inset = H + 0.25, H + 2.7, 7.5
c0 = [(-W / 2 - 0.3, -D / 2 - 0.3), (W / 2 + 0.3, -D / 2 - 0.3), (W / 2 + 0.3, D / 2 + 0.3), (-W / 2 - 0.3, D / 2 + 0.3)]
c1 = [(-W / 2 + inset, -D / 2 + inset), (W / 2 - inset, -D / 2 + inset), (W / 2 - inset, D / 2 - inset), (-W / 2 + inset, D / 2 - inset)]
for i in range(4):
    j = (i + 1) % 4
    B.face([(*c0[i], rz0), (*c0[j], rz0), (*c1[j], rz1), (*c1[i], rz1)], M_SLATE)
B.face([(*c1[0], rz1), (*c1[1], rz1), (*c1[2], rz1), (*c1[3], rz1)], M_SLATE)
for (x, y) in ((-12, 6), (12, 6), (-12, -6), (12, -6)):
    B.box((x, y, rz1 + 1.0), (2.2, 0.9, 3.0), M_STONE)
    B.box((x, y, rz1 + 2.6), (2.5, 1.1, 0.25), M_STONE)
B.box((0, 0, H / 2), (W - 0.2, D - 0.2, 0.2), M_STONE, top=False)   # (unseen floor plate, keeps the block closed in shadows)
main = B.finish()

# ---------------------------------------------------------------- the portico: four giant Ionic columns, a second pair behind

P = Builder('Chateau_Portico')
pw, pd = 15.6, 5.2                 # width across the front, depth out from the wall
y0 = D / 2
P.box((0, y0 + pd / 2 + 0.6, 0.3), (pw + 1.6, pd + 1.8, 0.6), M_STONE)            # stylobate
for k in range(3):                                                                  # steps down to the forecourt
    P.box((0, y0 + pd + 1.6 + k * 0.45, 0.3 - 0.15 * (k + 1) + 0.075), (pw + 1.0, 0.45, 0.6 - 0.15 * (k + 1)), M_STONE)
col_x = [-pw / 2 + 1.2 + (pw - 2.4) * k / 3 for k in range(4)]
def ionic(x, y):
    P.cylinder((x, y, 0.6), 0.78, 0.78, 0.18, M_STONE, seg=16)                     # plinth disc
    P.cylinder((x, y, 0.78), 0.72, 0.66, 0.22, M_STONE, seg=16)                    # torus (approx.)
    P.cylinder((x, y, 1.0), 0.62, 0.62, 0.12, M_STONE, seg=16)
    P.cylinder((x, y, 1.12), 0.56, 0.47, 8.0, M_STONE, seg=32, flutes=1, caps=False)  # fluted shaft
    P.cylinder((x, y, 9.12), 0.52, 0.58, 0.28, M_STONE, seg=16)                    # echinus
    P.box((x, y, 9.5), (1.45, 1.45, 0.2), M_STONE)                                  # abacus
    for s in (-1, 1):                                                               # volutes, scrolls seen from the front
        P.hcyl((x + s * 0.6, y, 9.33), 0.24, 1.2, 'y', M_STONE)
for x in col_x: ionic(x, y0 + pd)
for x in (col_x[0], col_x[3]): ionic(x, y0 + 1.5)
for x in (col_x[1], col_x[2]):                                                      # pilasters on the wall behind
    P.box((x, y0 + 0.12, 5.3), (1.1, 0.24, 9.4), M_STONE)
# entablature (architrave, frieze, cornice) over the columns and back to the wall
P.box((0, y0 + pd / 2 + 0.2, 10.0), (pw + 0.6, pd + 1.2, 0.7), M_STONE)
P.box((0, y0 + pd / 2 + 0.25, 10.65), (pw + 0.9, pd + 1.4, 0.6), M_STONE)
P.box((0, y0 + pd / 2 + 0.3, 11.15), (pw + 1.5, pd + 1.9, 0.4), M_STONE)
# the arms of the Sanguin de Livry over the portico: a cartouche between trophies (blocked out)
P.box((0, y0 + pd + 0.4, 12.4), (3.2, 0.9, 2.1), M_STONE)
P.cylinder((0, y0 + pd + 0.55, 13.4), 1.1, 0.4, 1.0, M_STONE, seg=12)
for s in (-1, 1):
    P.box((s * 2.4, y0 + pd + 0.4, 11.95), (1.6, 0.7, 1.2), M_STONE)
    P.cylinder((s * 2.9, y0 + pd + 0.4, 12.5), 0.35, 0.15, 1.3, M_STONE, seg=8)
portico = P.finish()

# ---------------------------------------------------------------- the garden side: terrace, retaining wall and balustrade

T = Builder('Chateau_Terrace')
ty = -D / 2 - 16.0          # the terrace edge, 16 m out from the garden front
tw = W + 26.0
drop = 3.6                  # the ground falls away to the lower garden by the canal
T.box((0, (ty + -D / 2) / 2, -0.15), (tw, abs(ty + D / 2), 0.3), M_STONE)           # terrace paving
T.box((0, ty - 0.35, -drop / 2), (tw + 0.7, 0.7, drop), M_STONE, top=False)       # retaining wall
T.box((0, ty - 0.35, 0.08), (tw + 0.9, 0.85, 0.16), M_STONE)                      # coping
for side in (-1, 1):                                                               # the side walls back to the house
    T.box((side * (tw / 2 + 0.35), (ty - D / 2) / 2, -drop / 2), (0.7, abs(ty + D / 2), drop), M_STONE, top=False)
# balustrade: plinth, balusters, handrail; dies (pedestals) every 6 m and a gap in the middle for the stair
def balustrade(x0, x1, y):
    T.box(((x0 + x1) / 2, y, 0.28), (x1 - x0, 0.42, 0.24), M_STONE)
    T.box(((x0 + x1) / 2, y, 1.0), (x1 - x0, 0.5, 0.16), M_STONE)
    n = int((x1 - x0) / 0.32)
    for k in range(n):
        x = x0 + (k + 0.5) * (x1 - x0) / n
        if abs((x - x0) % 6.0) < 0.35: continue
        T.cylinder((x, y, 0.4), 0.07, 0.11, 0.25, M_STONE, seg=6, caps=False)
        T.cylinder((x, y, 0.65), 0.11, 0.06, 0.27, M_STONE, seg=6, caps=False)
    x = x0
    while x <= x1 + 0.01:
        T.box((x, y, 0.62), (0.5, 0.55, 0.92), M_STONE); x += 6.0
balustrade(-tw / 2, -3.0, ty - 0.35)
balustrade(3.0, tw / 2, ty - 0.35)
# the stair down to the lower garden, through the gap
for k in range(12):
    T.box((0, ty - 0.7 - k * 0.38, -0.15 - k * 0.3), (5.0, 0.38, 0.3), M_STONE)
terrace = T.finish()

# ---------------------------------------------------------------- the communs (service wing) and the chapel

S = Builder('Chateau_Communs')
sx, sy0, sy1, sh = -W / 2 - 16.0, -34.0, 8.0, 8.6
sw = 10.0
def plain_block(Bd, x0, x1, y0b, y1b, h, roof_h, nwin_x, nwin_y):
    for (o, r, n, w, nw) in (((x0, y1b, 0), (1, 0, 0), (0, 1, 0), x1 - x0, nwin_x), ((x1, y0b, 0), (-1, 0, 0), (0, -1, 0), x1 - x0, nwin_x),
                              ((x1, y1b, 0), (0, -1, 0), (1, 0, 0), y1b - y0b, nwin_y), ((x0, y0b, 0), (0, 1, 0), (-1, 0, 0), y1b - y0b, nwin_y)):
        ops = []
        for uc in bays(w, nw, pil=1.6):
            ops.append((uc - 0.6, uc + 0.6, 1.0, 3.2, 'win'))
            ops.append((uc - 0.55, uc + 0.55, 5.0, 7.0, 'win'))
        facade(Bd, o, r, n, w, h, ops, M_STONE, depth=0.25)
    # hipped slate roof
    cx, cy = (x0 + x1) / 2, (y0b + y1b) / 2
    hw, hd = (x1 - x0) / 2 + 0.4, (y1b - y0b) / 2 + 0.4
    ridge = max(0.0, max(hw, hd) - min(hw, hd))
    if hd >= hw: r0, r1 = (cx, cy - ridge, h + roof_h), (cx, cy + ridge, h + roof_h)
    else: r0, r1 = (cx - ridge, cy, h + roof_h), (cx + ridge, cy, h + roof_h)
    a, b, c, d = (cx - hw, cy - hd, h), (cx + hw, cy - hd, h), (cx + hw, cy + hd, h), (cx - hw, cy + hd, h)
    if hd >= hw:
        Bd.face([a, b, r0], M_SLATE); Bd.face([c, d, r1], M_SLATE); Bd.face([b, c, r1, r0], M_SLATE); Bd.face([d, a, r0, r1], M_SLATE)
    else:
        Bd.face([d, a, r0], M_SLATE); Bd.face([b, c, r1], M_SLATE); Bd.face([a, b, r1, r0], M_SLATE); Bd.face([c, d, r0, r1], M_SLATE)
plain_block(S, sx - sw / 2, sx + sw / 2, sy0, sy1, sh, 4.2, 3, 9)
communs = S.finish()

C = Builder('Chateau_Chapel')
chx, chy = W / 2 + 9.0, D / 2 - 2.0
plain_block(C, chx - 4.0, chx + 4.0, chy - 6.5, chy + 6.5, 6.2, 5.0, 1, 3)
C.cylinder((chx, chy + 6.0, 11.2), 0.05, 0.05, 1.2, M_IRON, seg=4)                  # the cross on the ridge
chapel = C.finish()

# ---------------------------------------------------------------- markers for the game

def marker(name, loc):
    me = bpy.data.meshes.new(name); bm = bmesh.new(); bmesh.ops.create_cube(bm, size=0.1); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); ob.location = loc; bpy.context.collection.objects.link(ob); return ob
marker('RoofMarker', (0, 0, H + 3.7))                 # where Hoeller stood to look at the bridge
marker('PorticoMarker', (0, D / 2 + pd + 2.5, 0))     # in front of the steps
marker('TerraceMarker', (0, ty + 1.5, 0))            # on the terrace by the balustrade
marker('RedCrossMarker', (0, D / 2 + 0.2, H + 0.3))   # the Red Cross flag over the entrance front

for o in bpy.data.objects: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH'}, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', path_mode='COPY', embed_textures=True, mesh_smooth_type='FACE')
print('chateau exported', OUT, sum(len(o.data.vertices) for o in bpy.data.objects if o.type == 'MESH'), 'verts')
