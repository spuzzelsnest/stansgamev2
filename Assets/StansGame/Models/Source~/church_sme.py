# Notre-Dame-de-l'Assomption, Sainte-Mere-Eglise, as it stood on 6 June 1944 (after the US Signal Corps photos of
# that week: stMereEglise07 / 13 in _downloads/Photos/Sainte-Mere-Eglise).
#   Romanesque nave with a steep slate roof and a lean-to south aisle; the square crossing tower with a belfry of paired
#   louvred lancets, corner pinnacles and a saddleback slate roof with a clock in each gable; a Gothic choir with a flat
#   east end and a three-light window; buttresses; the classical south chapel; the rubble churchyard wall with gate piers and pasted bills (no graves).
# Blender: X = east, Y = north, Z = up, metres, ground at z = 0. Origin = centre of the tower on the ground.
# Markers: TowerMarker (top of the saddleback ridge), SteeleMarker (tip of the north-west pinnacle).
# Materials (textures from church_tex.py, UVs in metres / tile): SME_Rubble 4 m, SME_Ashlar 2 m, SME_Slate 2 m, SME_Oak 2 m,
#   SME_Glass, SME_Iron, SME_ClockFace, SME_Louvre (oak 2 m).
# Usage: python3 -I church_sme.py -- out.fbx [preview.png]
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index('--') + 1:]
OUT = args[0]; PREVIEW = args[1] if len(args) > 1 else None
bpy.ops.wm.read_factory_settings(use_empty=True)

TILE = {'rubble': 4.0, 'ashlar': 2.0, 'slate': 2.0, 'oak': 2.0, 'louvre': 2.0, 'glass': 2.0, 'iron': 1.0, 'clock': 1.0}
COL = {'rubble': (0.55, 0.53, 0.49), 'ashlar': (0.70, 0.67, 0.59), 'slate': (0.21, 0.23, 0.26), 'oak': (0.28, 0.19, 0.11),
       'louvre': (0.30, 0.27, 0.24), 'glass': (0.05, 0.06, 0.08), 'iron': (0.08, 0.08, 0.08), 'clock': (0.85, 0.84, 0.80)}
NAME = {'rubble': 'SME_Rubble', 'ashlar': 'SME_Ashlar', 'slate': 'SME_Slate', 'oak': 'SME_Oak', 'louvre': 'SME_Louvre',
        'glass': 'SME_Glass', 'iron': 'SME_Iron', 'clock': 'SME_ClockFace'}
MATS = {}
for k, n in NAME.items():
    m = bpy.data.materials.new(n); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value = (*COL[k], 1)
    b.inputs['Roughness'].default_value = 0.15 if k == 'glass' else 0.85
    MATS[k] = m

bm = bmesh.new(); MI = {}
def mi(m):
    if m not in MI: MI[m] = len(MI)
    return MI[m]

def faces(vs, fs, m):
    v = [bm.verts.new(p) for p in vs]
    for f in fs:
        try: bm.faces.new([v[i] for i in f]).material_index = mi(m)
        except ValueError: pass

def box(x0, x1, y0, y1, z0, z1, m='rubble'):
    x0, x1 = sorted((x0, x1)); y0, y1 = sorted((y0, y1)); z0, z1 = sorted((z0, z1))
    v = [(x0,y0,z0),(x1,y0,z0),(x1,y1,z0),(x0,y1,z0),(x0,y0,z1),(x1,y0,z1),(x1,y1,z1),(x0,y1,z1)]
    faces(v, [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)], m)

def prism(pts, a, b, m='rubble'):
    """extrude a convex polygon of 3D points along the vector a->b (pts given at offset a)"""
    a = Vector(a); b = Vector(b); n = len(pts)
    v = [tuple(Vector(p) + a) for p in pts] + [tuple(Vector(p) + b) for p in pts]
    f = [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))]
    for i in range(n):
        j = (i + 1) % n; f.append((j, i, n + i, n + j))
    faces(v, f, m)

def prism_yz(prof, x0, x1, m='rubble'):   # profile (y, z), extruded along +X
    prism([(x0, y, z) for y, z in prof], (0, 0, 0), (x1 - x0, 0, 0), m)

def prism_xz(prof, y0, y1, m='rubble'):   # profile (x, z), extruded along +Y
    prism([(x, y0, z) for x, z in prof], (0, 0, 0), (0, y1 - y0, 0), m)

def bar(p, q, w, h, m='ashlar', up=(0, 0, 1)):
    p = Vector(p); q = Vector(q); d = q - p; L = d.length
    if L < 1e-4: return
    a = d / L; u = Vector(up)
    if abs(a.dot(u.normalized())) > 0.95: u = Vector((1, 0, 0)) if abs(a.x) < 0.9 else Vector((0, 1, 0))
    side = a.cross(u).normalized(); up2 = side.cross(a).normalized()
    c = [tuple(t + side * (sx * w / 2) + up2 * (sz * h / 2)) for t in (p, q) for sx, sz in ((-1,-1),(1,-1),(1,1),(-1,1))]
    faces(c, [(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)], m)

def slab(e0, e1, r0, r1, t, m='slate'):
    """roof plane: eave edge e0-e1, ridge edge r0-r1, thickness t on the outer side"""
    e0, e1, r0, r1 = map(Vector, (e0, e1, r0, r1))
    n = (e1 - e0).cross(r0 - e0).normalized()
    if n.z < 0: n = -n
    lo = [e0, e1, r1, r0]; hi = [p + n * t for p in lo]
    faces([tuple(p) for p in lo + hi], [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)], m)

def pyramid(cx, cy, s, z0, z1, m='ashlar'):
    h = s / 2
    faces([(cx-h,cy-h,z0),(cx+h,cy-h,z0),(cx+h,cy+h,z0),(cx-h,cy+h,z0),(cx,cy,z1)], [(0,3,2,1),(0,1,4),(1,2,4),(2,3,4),(3,0,4)], m)

# ---- walls: a frame (origin on the wall face, u along it, outward normal n) to place openings
class Wall:
    def __init__(s, origin, u, n): s.o = Vector(origin); s.u = Vector(u).normalized(); s.n = Vector(n).normalized()
    def p(s, u, z, d=0.0): return tuple(s.o + s.u * u + Vector((0, 0, z)) + s.n * d)

def arch(u0, w, zb, h, steps=7):
    """outline of a pointed (equilateral) lancet: width w, sill zb, total height h; returns [(u, z)] anticlockwise"""
    r = w; spring = zb + h - w * math.sqrt(3) / 2
    pts = [(u0 - w / 2, zb), (u0 + w / 2, zb), (u0 + w / 2, spring)]
    for k in range(1, steps + 1):   # right side: centre at the left springer
        a = math.radians(60 * k / steps); pts.append((u0 - w / 2 + r * math.cos(a), spring + r * math.sin(a)))
    for k in range(steps - 1, -1, -1):   # left side: centre at the right springer
        a = math.radians(60 * k / steps); pts.append((u0 + w / 2 - r * math.cos(a), spring + r * math.sin(a)))
    return pts

def round_arch(u0, w, zb, h, steps=9):
    """outline of a round-headed (semicircular) window: width w, sill zb, total height h"""
    r = w / 2; spring = zb + h - r
    return [(u0 - r, zb), (u0 + r, zb)] + [(u0 + r * math.cos(math.pi * k / steps), spring + r * math.sin(math.pi * k / steps)) for k in range(steps + 1)]

def opening(wall, u0, w, zb, h, fill='glass', frame=0.24, depth=0.16, lights=1, louvres=False, sill=True, round_head=False):
    global arch
    pointed = arch
    if round_head: arch = round_arch
    try: _opening(wall, u0, w, zb, h, fill, frame, depth, lights, louvres, sill)
    finally: arch = pointed

def _opening(wall, u0, w, zb, h, fill='glass', frame=0.24, depth=0.16, lights=1, louvres=False, sill=True):
    out = arch(u0, w, zb, h)
    # the dark opening, a hair proud of the wall
    prism([wall.p(u, z, 0.0) for u, z in out], (0, 0, 0), tuple(wall.n * 0.03), fill)
    # dressed-stone surround following the arch
    ring = arch(u0, w + frame, zb - frame / 2, h + frame * 0.75)
    for i in range(1, len(ring)):
        a, b = ring[i - 1], ring[i]
        if abs(a[1] - zb + frame / 2) < 1e-3 and abs(b[1] - zb + frame / 2) < 1e-3: continue   # no bar across the sill
        bar(wall.p(*a, depth / 2), wall.p(*b, depth / 2), frame, depth, 'ashlar', up=tuple(wall.n))
    if sill: bar(wall.p(u0 - w / 2 - frame, zb - frame / 2, 0.12), wall.p(u0 + w / 2 + frame, zb - frame / 2, 0.12), 0.18, 0.26, 'ashlar', up=(0, 0, 1))
    spring = zb + h - w * math.sqrt(3) / 2
    for k in range(1, lights):   # stone mullions
        uu = u0 - w / 2 + w * k / lights
        bar(wall.p(uu, zb, 0.06), wall.p(uu, spring + 0.3, 0.06), 0.14, 0.12, 'ashlar', up=tuple(wall.n))
    if fill == 'glass':   # lead cames: a few horizontal bars
        z = zb + 0.6
        while z < spring:
            bar(wall.p(u0 - w / 2, z, 0.045), wall.p(u0 + w / 2, z, 0.045), 0.03, 0.02, 'iron', up=tuple(wall.n)); z += 0.6
    if louvres:   # slanted boards that keep the rain out of the bell chamber
        z = zb + 0.35
        while z < spring + 0.2:
            bar(wall.p(u0 - w / 2 + 0.02, z, 0.12), wall.p(u0 + w / 2 - 0.02, z, 0.12), 0.05, 0.36, 'louvre',
                up=tuple((Vector((0, 0, 1)) + wall.n * 0.9).normalized())); z += 0.42

def buttress(wall, u0, w, d, h, cap=1.0, m='ashlar'):
    """a buttress against the wall: width w along it, projecting d, top sloping back over 'cap'"""
    pts = [(0, 0), (d, 0), (d, h), (0, h + cap)]
    prism([wall.p(u0 - w / 2, z, x) for x, z in pts], (0, 0, 0), tuple(wall.u * w), m)

def gable_block(x0, x1, hw, eave, ridge, m='rubble', yc=0.0):
    prism_yz([(yc - hw, 0), (yc + hw, 0), (yc + hw, eave), (yc, ridge), (yc - hw, eave)], x0, x1, m)

def gable_roof(x0, x1, hw, eave, ridge, over=0.45, t=0.2, yc=0.0):
    slope = (ridge - eave) / hw
    ez = eave - over * slope
    for s in (-1, 1):
        slab((x0, yc + s * (hw + over), ez), (x1, yc + s * (hw + over), ez), (x0, yc, ridge), (x1, yc, ridge), t)
    bar((x0, yc, ridge + t + 0.05), (x1, yc, ridge + t + 0.05), 0.3, 0.22, 'iron')    # lead ridge

def coping(x, hw, eave, ridge, yc=0.0, out=0.08):
    for s in (-1, 1):
        bar((x, yc + s * (hw + 0.15), eave + 0.1), (x, yc, ridge + 0.3), 0.55, 0.32, 'ashlar', up=(0, 0, 1))

PITCH = math.tan(math.radians(55))

# ================================================================== nave (west of the tower)
NX0, NX1, NHW, NE = -26.0, -4.0, 5.0, 8.5
NR = NE + NHW * PITCH
gable_block(NX0, NX1, NHW, NE, NR)
gable_roof(NX0 - 0.3, NX1, NHW, NE, NR)
coping(NX0 - 0.1, NHW, NE, NR)
box(NX0 - 0.15, NX1, -NHW - 0.15, NHW + 0.15, 0, 0.6, 'ashlar')                                   # plinth
north = Wall((0, NHW, 0), (1, 0, 0), (0, 1, 0))
for x in (-22.5, -17.5, -12.5, -7.8): opening(north, x, 1.1, 3.4, 3.8)
for x in (-24.6, -20.0, -15.0, -10.1): buttress(north, x, 0.9, 1.0, 5.2)
west = Wall((NX0, 0, 0), (0, -1, 0), (-1, 0, 0))
opening(west, 0.0, 2.3, 0.0, 4.4, fill='oak', frame=0.45, depth=0.35, sill=False)                    # west door
opening(west, 0.0, 2.9, 0.0, 5.0, fill='oak', frame=0.25, depth=0.5, sill=False)                     # second order of the arch
opening(west, 0.0, 1.3, 6.4, 4.4)                                                                   # lancet over the door
opening(west, 0.0, 0.6, 11.6, 1.4)                                                                  # small light in the gable
for s in (-1, 1):   # angle buttresses at the west corners
    buttress(west, s * (NHW - 0.45), 0.9, 1.2, 6.2, 1.2)
    side = Wall((NX0, s * NHW, 0), (1, 0, 0), (0, s, 0))
    buttress(side, 0.45 if s > 0 else 0.45, 0.9, 1.2, 6.2, 1.2)
box(NX0 - 1.6, NX0, -1.9, 1.9, 0, 0.18, 'ashlar'); box(NX0 - 0.9, NX0, -1.6, 1.6, 0.18, 0.36, 'ashlar')   # steps
bar((NX0 - 0.25, 0, NR + 0.4), (NX0 - 0.25, 0, NR + 2.0), 0.1, 0.1, 'iron')                         # gable cross
bar((NX0 - 0.25, -0.45, NR + 1.45), (NX0 - 0.25, 0.45, NR + 1.45), 0.1, 0.1, 'iron')

# south aisle: lean-to along the nave
AX0, AX1, AW, AE, AT = -24.0, -4.0, 3.6, 4.4, 7.0
prism_yz([(-NHW - AW, 0), (-NHW, 0), (-NHW, AT), (-NHW - AW, AE)], AX0, AX1)
slope = (AT - AE) / AW
slab((AX0 - 0.3, -NHW - AW - 0.4, AE - 0.4 * slope), (AX1, -NHW - AW - 0.4, AE - 0.4 * slope), (AX0 - 0.3, -NHW, AT + 0.05), (AX1, -NHW, AT + 0.05), 0.18)
box(AX0 - 0.15, AX1, -NHW - AW - 0.15, -NHW, 0, 0.6, 'ashlar')
south = Wall((0, -NHW - AW, 0), (-1, 0, 0), (0, -1, 0))
for x in (-21.0, -15.5, -10.0): opening(south, -x, 0.9, 1.5, 2.4)
opening(south, 6.6, 1.6, 0.0, 3.1, fill='oak', frame=0.3, depth=0.25, sill=False)                  # south door (x = -6.6)
for x in (-23.4, -18.2, -12.7, -8.3): buttress(south, -x, 0.8, 0.8, 3.4, 0.8)
aw = Wall((AX0, -NHW - AW / 2, 0), (0, -1, 0), (-1, 0, 0)); opening(aw, 0, 0.8, 1.6, 2.2)

# ================================================================== the tower over the crossing
T, TE = 4.7, 24.0                  # half side, eaves of the saddleback
TR = TE + 8.2                      # ridge of the saddleback
for sx in (-1, 1):                 # dressed quoins up the corners
    for sy in (-1, 1):
        x0, x1 = sorted((sx * (T - 0.45), sx * (T + 0.08))); y0, y1 = sorted((sy * (T - 0.45), sy * (T + 0.08)))
        box(x0, x1, y0, y1, NR - 1, TE, 'ashlar')
for z0, z1, o in ((16.2, 16.55, 0.22), (TE - 0.45, TE + 0.1, 0.3)):   # string course under the belfry, cornice
    box(-T - o, T + o, -T - o, T + o, z0, z1, 'ashlar')
faces_dirs = [((T, 0, 0), (0, 1, 0), (1, 0, 0)), ((-T, 0, 0), (0, -1, 0), (-1, 0, 0)),
              ((0, T, 0), (-1, 0, 0), (0, 1, 0)), ((0, -T, 0), (1, 0, 0), (0, -1, 0))]
for o, u, n in faces_dirs:
    w = Wall(o, u, n)
    for du in (-1.45, 1.45): opening(w, du, 1.25, 17.1, 5.3, fill='glass', louvres=True)
    bar(w.p(0, 17.0, 0.1), w.p(0, 21.0, 0.1), 0.35, 0.2, 'ashlar', up=tuple(w.n))   # central shaft between the lights
    if abs(n[1]) > 0.5: opening(w, 0.0, 0.7, 12.0, 2.2)          # small light above the aisle roof
# saddleback: gables east and west, slate slopes north and south
gable_block(-T, T, T, TE, TR)          # the whole tower shaft, ground to ridge
gable_roof(-T - 0.25, T + 0.25, T, TE, TR, over=0.4, t=0.2)
for x in (-T - 0.1, T + 0.1): coping(x, T, TE, TR)
for x, s in ((-T, -1), (T, 1)):    # the clock in each gable
    zc = TE + 2.6
    pts = [(x + s * 0.06, 0.95 * math.cos(2 * math.pi * k / 24), zc + 0.95 * math.sin(2 * math.pi * k / 24)) for k in range(24)]
    prism(pts, (0, 0, 0), (s * 0.08, 0, 0), 'clock')
    ring = [(x + s * 0.06, 1.12 * math.cos(2 * math.pi * k / 24), zc + 1.12 * math.sin(2 * math.pi * k / 24)) for k in range(25)]
    for i in range(24): bar(ring[i], ring[i + 1], 0.2, 0.2, 'ashlar', up=(s, 0, 0))
    bar((x + s * 0.17, 0, zc), (x + s * 0.17, 0.0, zc + 0.7), 0.07, 0.03, 'iron', up=(s, 0, 0))      # it was about ten past four...
    bar((x + s * 0.17, 0, zc), (x + s * 0.17, 0.45, zc - 0.12), 0.09, 0.03, 'iron', up=(s, 0, 0))
    for k in range(12):
        a = 2 * math.pi * k / 12; p = Vector((x + s * 0.15, 0.8 * math.cos(a), zc + 0.8 * math.sin(a)))
        bar(p, p + Vector((0, 0.1 * math.cos(a), 0.1 * math.sin(a))), 0.05, 0.02, 'iron', up=(s, 0, 0))
for sx in (-1, 1):                 # corner pinnacles
    for sy in (-1, 1):
        cx, cy = sx * (T - 0.1), sy * (T - 0.1)
        box(cx - 0.36, cx + 0.36, cy - 0.36, cy + 0.36, TE + 0.1, TE + 2.4, 'ashlar')
        box(cx - 0.44, cx + 0.44, cy - 0.44, cy + 0.44, TE + 2.4, TE + 2.6, 'ashlar')
        pyramid(cx, cy, 0.8, TE + 2.6, TE + 4.3)
bar((-T - 0.2, 0, TR + 0.4), (-T - 0.2, 0, TR + 2.2), 0.1, 0.1, 'iron'); bar((-T - 0.2, -0.5, TR + 1.6), (-T - 0.2, 0.5, TR + 1.6), 0.1, 0.1, 'iron')
bar((T + 0.2, 0, TR + 0.4), (T + 0.2, 0, TR + 1.9), 0.06, 0.06, 'iron')                          # weathercock rod
faces([(T + 0.2, -0.05, TR + 1.6), (T + 0.2, -0.05, TR + 2.1), (T + 0.75, -0.05, TR + 1.75), (T - 0.3, -0.05, TR + 1.7)], [(0, 2, 1), (0, 1, 3)], 'iron')

# ================================================================== the south chapel (stMereEglise07, 13)
# A later classical block against the south face of the tower: rendered walls, round-headed windows under brick
# relieving arches, a hipped slate roof dying into the tower.
SX0, SX1, SY0, SY1, SE = -4.0, 4.4, -T, -10.9, 8.2
box(SX0, SX1, SY1, SY0, 0, SE, 'ashlar')
box(SX0 - 0.15, SX1 + 0.15, SY1 - 0.15, SY0, 0, 0.6, 'ashlar')                                    # plinth
for z0, z1, o in ((SE - 0.35, SE, 0.18),): box(SX0 - o, SX1 + o, SY1 - o, SY0, z0, z1, 'ashlar')    # cornice
sw = Wall((0, SY1, 0), (-1, 0, 0), (0, -1, 0))
for u in (-2.3, 1.9): opening(sw, u, 1.5, 2.4, 3.6, round_head=True, frame=0.3)
ew = Wall((SX1, 0, 0), (0, 1, 0), (1, 0, 0)); opening(ew, -7.8, 1.7, 2.2, 3.9, round_head=True, frame=0.3)
hw = (SX1 - SX0) / 2; RX = (SX0 + SX1) / 2; RZ = SE + hw * 0.9; RY = SY1 + hw
faces([(SX0 - 0.35, SY1 - 0.35, SE), (SX1 + 0.35, SY1 - 0.35, SE), (SX1 + 0.35, SY0, SE), (SX0 - 0.35, SY0, SE), (RX, RY, RZ), (RX, SY0, RZ)],
      [(0, 3, 2, 1), (3, 0, 4, 5), (1, 2, 5, 4), (0, 1, 4), (2, 3, 5)], 'slate')

# ================================================================== choir (east of the tower)
CX0, CX1, CHW, CE = T, 18.0, 4.3, 8.0
CR = CE + CHW * PITCH
gable_block(CX0, CX1, CHW, CE, CR)
gable_roof(CX0, CX1 + 0.3, CHW, CE, CR)
coping(CX1 + 0.1, CHW, CE, CR)
box(CX0, CX1 + 0.15, -CHW - 0.15, CHW + 0.15, 0, 0.6, 'ashlar')
east = Wall((CX1, 0, 0), (0, 1, 0), (1, 0, 0))
opening(east, 0.0, 3.2, 2.6, 6.2, lights=3)
for s in (-1, 1):
    buttress(east, s * (CHW - 0.45), 0.9, 1.3, 6.0, 1.3)
    side = Wall((CX1, s * CHW, 0), (-1, 0, 0), (0, s, 0)); buttress(side, 0.45, 0.9, 1.3, 6.0, 1.3)
    cw = Wall((0, s * CHW, 0), (1, 0, 0) if s > 0 else (-1, 0, 0), (0, s, 0))
    for x in (8.6, 13.4): opening(cw, s * x, 1.2, 3.0, 4.2)
    for x in (6.2, 11.0, 15.8): buttress(cw, s * x, 0.85, 1.0, 5.6)
# sacristy on the north side of the choir
prism_xz([(9.6, 0), (14.0, 0), (14.0, 3.6), (9.6, 3.6)], CHW, CHW + 3.2)
slab((9.4, CHW + 3.5, 3.4), (14.2, CHW + 3.5, 3.4), (9.4, CHW, 5.4), (14.2, CHW, 5.4), 0.16)
sac = Wall((0, CHW + 3.2, 0), (1, 0, 0), (0, 1, 0)); opening(sac, 11.8, 0.8, 1.2, 1.8)

# ================================================================== churchyard: low rubble wall, gate piers, graves
WY, WX0, WX1, WT, WH = 11.0, -31.0, 27.0, 0.55, 1.5   # chest-high on the street (stMereEglise07, 13)
WYS = 13.0                         # the south wall, clear of the south chapel
def wall_run(p, q):
    p, q = Vector((*p, 0)), Vector((*q, 0)); d = (q - p); L = d.length; a = d / L
    side = Vector((-a.y, a.x, 0))
    c = [p + side * WT / 2, q + side * WT / 2, q - side * WT / 2, p - side * WT / 2]
    prism([tuple(v) for v in c], (0, 0, 0), (0, 0, WH), 'rubble')
    c2 = [p - a * 0.02 + side * (WT / 2 + 0.06), q + a * 0.02 + side * (WT / 2 + 0.06), q + a * 0.02 - side * (WT / 2 + 0.06), p - a * 0.02 - side * (WT / 2 + 0.06)]
    prism([tuple(v + Vector((0, 0, WH))) for v in c2], (0, 0, 0), (0, 0, 0.14), 'ashlar')
def pier(x, y):
    box(x - 0.4, x + 0.4, y - 0.4, y + 0.4, 0, 1.6, 'ashlar'); pyramid(x, y, 0.9, 1.6, 2.1)
GATE_S = (8.5, 11.5); GATE_W = (-1.8, 1.8)
wall_run((WX0, WY), (WX1, WY))
wall_run((WX0, -WYS), (GATE_S[0] - 0.4, -WYS)); wall_run((GATE_S[1] + 0.4, -WYS), (WX1, -WYS))
wall_run((WX0, -WYS), (WX0, GATE_W[0] - 0.4)); wall_run((WX0, GATE_W[1] + 0.4), (WX0, WY))
wall_run((WX1, -WYS), (WX1, WY))
for x in GATE_S: pier(x + (-0.4 if x < 10 else 0.4), -WYS)
for y in GATE_W: pier(WX0, y + (-0.4 if y < 0 else 0.4))
# No graves (Joan, 23 Nov: the churchyard is kept clear for play).
# Bills pasted on the street side of the south wall, as in stMereEglise13: German notices and the mairie's posters.
import random
rnd = random.Random(6)
for i in range(7):
    x = rnd.uniform(-26, 22)
    if GATE_S[0] - 1.5 < x < GATE_S[1] + 1.5: continue
    w, h = rnd.uniform(0.45, 0.8), rnd.uniform(0.55, 0.9)
    box(x - w / 2, x + w / 2, -WYS - WT / 2 - 0.035, -WYS - WT / 2 - 0.01, 0.55, 0.55 + h, 'clock')

# ================================================================== mesh, UVs, markers, export
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)   # every piece is a closed solid: point all faces outwards
bm.normal_update()
uv = bm.loops.layers.uv.new('UV')
mat_by_index = {i: m for m, i in MI.items()}
for f in bm.faces:
    t = TILE[mat_by_index[f.material_index]]; n = f.normal
    ax = max(range(3), key=lambda i: abs(n[i]))
    for l in f.loops:
        c = l.vert.co
        if ax == 2: u, v = c.x, c.y                     # roofs and tops: rows of slates run along the eaves (X)
        elif ax == 0: u, v = c.y * (1 if n.x > 0 else -1), c.z
        else: u, v = c.x * (-1 if n.y > 0 else 1), c.z
        l[uv].uv = (u / t, v / t)
# roofs that run along Y (none) would need (y, x); the saddleback and nave slopes face N/S, so (x, y) keeps the rows level
me = bpy.data.meshes.new('ChurchSME'); bm.to_mesh(me); bm.free()
for m, i in sorted(MI.items(), key=lambda kv: kv[1]): me.materials.append(MATS[m])
ob = bpy.data.objects.new('ChurchSME', me); bpy.context.scene.collection.objects.link(ob)
for v in me.polygons: v.use_smooth = False

def marker(name, p):
    mm = bpy.data.meshes.new(name); b = bmesh.new()
    a = b.verts.new((0, 0, 0)); c = b.verts.new((0.002, 0, 0)); d = b.verts.new((0, 0.002, 0)); b.faces.new((a, c, d)); b.to_mesh(mm); b.free()
    o = bpy.data.objects.new(name, mm); bpy.context.scene.collection.objects.link(o); o.location = p
marker('TowerMarker', (0, 0, TR + 0.3))
marker('SteeleMarker', (-(T - 0.1), T - 0.1, TE + 4.3))
print('church faces', len(me.polygons), 'verts', len(me.vertices), 'materials', [m.name for m in me.materials])

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
                         apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', path_mode='STRIP',
                         embed_textures=False, bake_anim=False, mesh_smooth_type='FACE')
print('exported', OUT)

if PREVIEW:   # a quick look from the south-west, like the Signal Corps photo
    import os
    TEX = os.path.join(os.path.dirname(PREVIEW), 'tex')
    for k, m in MATS.items():
        f = {'rubble': 'SME_Rubble', 'ashlar': 'SME_Ashlar', 'slate': 'SME_Slate', 'oak': 'SME_Oak', 'louvre': 'SME_Oak'}.get(k)
        if not f: continue
        nt = m.node_tree; b = nt.nodes['Principled BSDF']
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(os.path.join(TEX, f + '_d.png'))
        uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = 'UV'; nt.links.new(uvn.outputs['UV'], t.inputs['Vector'])
        nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    sc = bpy.context.scene
    g = bpy.data.meshes.new('ground'); g.from_pydata([(-60,-60,0),(60,-60,0),(60,60,0),(-60,60,0)], [], [(0,1,2,3)])
    go = bpy.data.objects.new('ground', g); sc.collection.objects.link(go)
    gm = bpy.data.materials.new('g'); gm.use_nodes = True; gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.25, 0.3, 0.18, 1); g.materials.append(gm)
    view = args[2] if len(args) > 2 else 'sw'
    cams = {'sw': ((-38, -40, 10), (-2, 0, 13)), 'se': ((40, -36, 9), (0, 0, 12)), 'nw': ((-40, 34, 12), (-4, 0, 12)), 'close': ((-14, -22, 4), (-6, 0, 15))}
    loc, tgt = cams[view]
    cd = bpy.data.cameras.new('cam'); cd.lens = 32; co = bpy.data.objects.new('cam', cd); sc.collection.objects.link(co)
    co.location = loc; co.rotation_euler = (Vector(tgt) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler(); sc.camera = co
    sd = bpy.data.lights.new('sun', 'SUN'); sd.energy = 4; so = bpy.data.objects.new('sun', sd); sc.collection.objects.link(so); so.rotation_euler = (math.radians(50), 0, math.radians(-40))
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.6, 0.65, 0.7, 1); w.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.6
    sc.render.engine = 'CYCLES'
    if sc.render.engine == 'CYCLES': sc.cycles.samples = 24; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = 900, 700; sc.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True); print('preview', PREVIEW)
