# Props for Stan's Game (run: python3 -I props1944.py -- outdir):
#   Brushwood.fbx   - three piles of cut branches with dead leaves (pb11: south-west of glider 91)
#   Pillbox.fbx     - German field pillbox (board-marked concrete, embrasure to the west, entrance at the back)
#   HorsaBridge.fbx - the Ranville swing bridge over the Orne: lattice deck girders (Horsa_Span, 50 m between
#                     the abutment faces, scaled to the river in Unity), the round centre pier (Horsa_Pier) and one
#                     masonry abutment block (Horsa_Abutment, placed at both ends)
# Blender: X east, Y north, Z up, metres. Every object gets box-projected UVs (1 UV unit = 2 m).
import bpy, bmesh, sys, math, random
from mathutils import Vector, Matrix

OUTDIR = sys.argv[sys.argv.index('--') + 1]
random.seed(1944)

bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name, rgb, metal=0.0, rough=0.7):
    m = bpy.data.materials.new(name)
    try:
        b = m.node_tree.nodes['Principled BSDF']
        b.inputs['Base Color'].default_value = (*rgb, 1); b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
    except Exception:
        m.diffuse_color = (*rgb, 1)
    return m

M = {
    'bark':   mat('Brush_Bark', (0.35, 0.29, 0.22)),
    'leaves': mat('Brush_Leaves', (0.40, 0.34, 0.20)),
    'conc':   mat('Pillbox_Concrete', (0.58, 0.57, 0.54)),
    'turf':   mat('Pillbox_Turf', (0.30, 0.35, 0.20)),
    'iron':   mat('HorsaBridge_Iron', (0.30, 0.31, 0.30), 0.5, 0.5),
    'deck':   mat('HorsaBridge_Road', (0.26, 0.25, 0.23)),
    'stone':  mat('HorsaBridge_Stone', (0.52, 0.50, 0.46)),
}

exec(open('/tmp/bscripts/bgeo_class.txt').read().replace("def finish(s, name, origin=(0, 0, 0)):", "def finish_unused(s, name, origin=(0, 0, 0)):"))

def finish(b, name, tile=2.0, uv=True):
    bm = b.bm
    bm.normal_update()
    if uv:
        layer = bm.loops.layers.uv.new('UVMap')
        for f in bm.faces:
            n = f.normal
            ax = max(range(3), key=lambda k: abs(n[k]))
            for l in f.loops:
                c = l.vert.co
                if ax == 0: u, v = c.y, c.z
                elif ax == 1: u, v = c.x, c.z
                else: u, v = c.x, c.y
                l[layer].uv = (u / tile, v / tile)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for m, i in sorted(b.mi.items(), key=lambda kv: kv[1]): me.materials.append(M[m])
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    return ob

def export(objs, path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', path_mode='STRIP',
                             bake_anim=False, mesh_smooth_type='FACE')

def clear():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o)

# ===================================================================== brushwood
def tube(b, p, q, r0, r1, m='bark', sides=6):
    p = Vector(p); q = Vector(q); a = (q - p).normalized()
    u = Vector((0, 0, 1)) if abs(a.z) < 0.9 else Vector((1, 0, 0))
    s1 = a.cross(u).normalized(); s2 = a.cross(s1).normalized()
    ring = lambda c, r: [tuple(c + (s1 * math.cos(2 * math.pi * k / sides) + s2 * math.sin(2 * math.pi * k / sides)) * r) for k in range(sides)]
    v = ring(p, r0) + ring(q, r1)
    f = [tuple(range(sides))[::-1], tuple(range(sides, 2 * sides))]
    for k in range(sides):
        j = (k + 1) % sides; f.append((k, j, sides + j, sides + k))
    b._faces(v, f, m)

def leaf_card(b, c, size):
    c = Vector(c); yaw = random.uniform(0, math.pi)
    for k in range(2):
        a = yaw + k * math.pi / 2
        d = Vector((math.cos(a), math.sin(a), 0)) * size / 2
        up = Vector((random.uniform(-0.3, 0.3), random.uniform(-0.3, 0.3), 1)).normalized() * size / 2
        v = [tuple(c - d - up), tuple(c + d - up), tuple(c + d + up), tuple(c - d + up)]
        b._faces(v, [(0, 1, 2, 3)], 'leaves')

def brush_pile(name, L, W, H, n):
    b = Builder()
    for i in range(n):
        # lay the branches roughly along the pile, the higher ones shorter, the pile rounded on top
        u = random.uniform(-0.5, 0.5); v = random.uniform(-0.5, 0.5)
        h = H * (1 - (2 * u) ** 2) * (1 - (2 * v) ** 2) * random.uniform(0.3, 1.0)
        length = random.uniform(1.8, 4.5) * (1 - 0.4 * h / H)
        yaw = random.gauss(0, 0.45) + (math.pi if random.random() < 0.5 else 0)
        pitch = random.uniform(-0.35, 0.35)
        c = Vector((u * L, v * W, h + 0.05))
        d = Vector((math.cos(yaw) * math.cos(pitch), math.sin(yaw) * math.cos(pitch), math.sin(pitch))) * length / 2
        r = random.uniform(0.025, 0.09)
        p, q = c - d, c + d
        if p.z < 0.03: p.z = 0.03
        if q.z < 0.03: q.z = 0.03
        tube(b, p, q, r, r * 0.35)
        for k in range(random.randint(1, 3)):                      # side shoots
            t = random.uniform(0.35, 0.9); s = p + (q - p) * t
            dd = (q - p).normalized(); side = Vector((-dd.y, dd.x, 0)) * random.choice((-1, 1))
            e = s + (dd * 0.6 + side * random.uniform(0.3, 0.8) + Vector((0, 0, random.uniform(0.1, 0.5)))).normalized() * random.uniform(0.5, 1.4)
            tube(b, s, e, r * 0.45, r * 0.15, sides=4)
            if random.random() < 0.4: leaf_card(b, e, random.uniform(0.35, 0.6))
        if random.random() < 0.3: leaf_card(b, q, random.uniform(0.4, 0.7))
    return finish(b, name, tile=1.0)

piles = [brush_pile('Brush_A', 6.0, 3.2, 1.3, 120), brush_pile('Brush_B', 4.5, 2.6, 1.1, 85), brush_pile('Brush_C', 7.5, 3.6, 1.5, 150)]
export(piles, OUTDIR + '/Brushwood.fbx'); clear()

# ===================================================================== pillbox
b = Builder()
X0, X1, Y0, Y1 = -2.6, 2.6, -2.6, 2.6        # outer footprint
T, ZB, ZR = 1.0, -0.9, 2.1                    # wall thickness, foundation bottom, roof underside (ground = 0)
# west wall (towards the bridge) with the embrasure: a slit 1.1 x 0.35 at 1.1 m, splayed outwards
SL0, SL1, SZ0, SZ1 = -0.55, 0.55, 1.05, 1.40
b.box(X0, X0 + T, Y0, SL0, ZB, ZR, 'conc'); b.box(X0, X0 + T, SL1, Y1, ZB, ZR, 'conc')
b.box(X0, X0 + T, SL0, SL1, ZB, SZ0, 'conc'); b.box(X0, X0 + T, SL0, SL1, SZ1, ZR, 'conc')
for k, (y0, y1) in enumerate(((SL0 - 0.35, SL0), (SL1, SL1 + 0.35))):    # splay steps
    b.box(X0 - 0.01, X0 + 0.35, y0, y1, SZ0 - 0.2, SZ1 + 0.2) if False else None
b.box(X1 - T, X1, Y0, Y1, ZB, ZR, 'conc')                                       # east (back) wall: the door is in the north wall
b.box(X0, X1, Y0, Y0 + T, ZB, ZR, 'conc')                                       # south wall
b.box(X0, -0.4, Y1 - T, Y1, ZB, ZR, 'conc'); b.box(0.5, X1, Y1 - T, Y1, ZB, ZR, 'conc') # north wall with the doorway
b.box(-0.4, 0.5, Y1 - T, Y1, 1.85, ZR, 'conc')                                  # lintel
b.box(X0 + T, X1 - T, Y0 + T, Y1 - T, ZB, -0.05, 'conc')                        # floor slab (inside floor just below ground)
b.box(X0 - 0.3, X1 + 0.3, Y0 - 0.3, Y1 + 0.3, ZR, ZR + 0.85, 'conc')             # roof slab, overhanging
b.box(X0 - 0.15, X1 + 0.15, Y0 - 0.15, Y1 + 0.15, ZR + 0.85, ZR + 0.95, 'conc')  # chamfer step
# blast wall in front of the door, with a gap to walk round
b.box(-1.4, 0.9, Y1 + 0.9, Y1 + 1.5, ZB, 1.8, 'conc')
b.box(0.9, 1.5, Y1, Y1 + 1.5, ZB, 1.8, 'conc')
# MG table under the slit
b.box(X0 + T, X0 + T + 0.6, -0.6, 0.6, 0.0, 0.95, 'conc')
pill = finish(b, 'Pillbox', tile=2.0)
# turf laid on the roof for camouflage: a lumpy sod blanket a little larger than the roof, thickest in the middle
tb = Builder()
n = 14; R0 = 3.15
verts = []; faces = []
for j in range(n + 1):
    for i in range(n + 1):
        x = -R0 + 2 * R0 * i / n; y = -R0 + 2 * R0 * j / n
        e = max(abs(x), abs(y)) / R0
        z = ZR + 0.95 + (0.45 * (1 - e ** 3) + random.uniform(-0.05, 0.08)) if e < 0.999 else ZR + 0.7
        verts.append((x * (1.0 + 0.03 * random.uniform(-1, 1)), y * (1.0 + 0.03 * random.uniform(-1, 1)), z))
for j in range(n):
    for i in range(n):
        a0 = j * (n + 1) + i; faces.append((a0, a0 + 1, a0 + n + 2, a0 + n + 1))
tb._faces(verts, faces, 'turf')
turf = finish(tb, 'Pillbox_Turf', tile=2.0)
export([pill, turf], OUTDIR + '/Pillbox.fbx'); clear()

# ===================================================================== Horsa Bridge (Ranville)
S = Builder()
HALF, GY, RD = 25.0, 3.4, 0.0                 # half span (abutment face to face), girder plane, road level
DEPTH = 2.6                                   # deck girders, below the road
S.box(-HALF, HALF, -GY, GY, -0.30, RD - 0.05, 'iron')                    # deck plate and stringers
S.box(-HALF, HALF, -2.6, 2.6, RD - 0.05, RD, 'deck')                     # road
for s in (-1, 1):
    S.box(-HALF, HALF, s * 2.6, s * GY, RD - 0.05, RD + 0.15, 'iron')    # footway edge
    y = s * GY
    S.box(-HALF, HALF, y - 0.25, y + 0.25, RD - 0.35, RD - 0.20, 'iron')  # top chord
    S.box(-HALF, HALF, y - 0.25, y + 0.25, -DEPTH, -DEPTH + 0.15, 'iron')  # bottom chord
    S.box(-HALF, HALF, y - 0.04, y + 0.04, -DEPTH + 0.15, -DEPTH + 0.45, 'iron')
    S.box(-HALF, HALF, y - 0.04, y + 0.04, RD - 0.65, RD - 0.35, 'iron')
    n = 20; p = 2 * HALF / n
    for i in range(n + 1):                                                 # verticals
        x = -HALF + i * p
        S.box(x - 0.1, x + 0.1, y - 0.12, y + 0.12, -DEPTH, RD - 0.2, 'iron')
    for i in range(n):                                                     # double lattice (the X pattern in the photo)
        x0 = -HALF + i * p
        for a, bb in (((x0, -DEPTH + 0.3), (x0 + p, -0.5)), ((x0, -0.5), (x0 + p, -DEPTH + 0.3))):
            S.bar((a[0], y, a[1]), (bb[0], y, bb[1]), 0.10, 0.06, 'iron', up=(0, 1, 0))
    # railing of vertical bars on the footway edge (pb: Horsa Bridge 1944)
    S.box(-HALF, HALF, y - 0.04, y + 0.04, RD + 1.05, RD + 1.12, 'iron')
    S.box(-HALF, HALF, y - 0.03, y + 0.03, RD + 0.18, RD + 0.24, 'iron')
    x = -HALF
    while x <= HALF:
        S.box(x - 0.015, x + 0.015, y - 0.015, y + 0.015, RD + 0.15, RD + 1.06, 'iron'); x += 0.16
    for x in [-HALF + k * 2.5 for k in range(21)]:
        S.box(x - 0.05, x + 0.05, y - 0.05, y + 0.05, RD + 0.15, RD + 1.15, 'iron')
for i in range(0, 21):                                                     # cross girders
    x = -HALF + i * 2.5
    S.box(x - 0.12, x + 0.12, -GY, GY, -DEPTH + 0.15, -DEPTH + 0.45, 'iron')
span = finish(S, 'Horsa_Span', tile=2.0)

P = Builder()                                                              # centre pier: the swing bridge's turntable
P.cyl_y(0, 0, 0, 0, 0, 'stone') if False else None
def drum(b, r, z0, z1, m, n=24):
    v = []
    for z in (z0, z1):
        for k in range(n):
            a = 2 * math.pi * k / n; v.append((r * math.cos(a), r * math.sin(a), z))
    f = [tuple(range(n))[::-1], tuple(range(n, 2 * n))]
    for k in range(n):
        j = (k + 1) % n; f.append((k, j, n + j, n + k))
    b._faces(v, f, m)
drum(P, 3.2, -14.0, -DEPTH - 0.9, 'stone')
drum(P, 3.5, -DEPTH - 0.9, -DEPTH - 0.55, 'stone')                      # coping
drum(P, 2.6, -DEPTH - 0.55, -DEPTH, 'iron')                              # the turntable drum
pier = finish(P, 'Horsa_Pier', tile=2.0)

A = Builder()                                                              # abutment, river face at x = 0, running back to +x
AL, AW = 11.0, 4.6
A.box(0, AL, -AW, AW, -14.0, RD - 0.02, 'stone')
A.box(0, AL, -2.6, 2.6, RD - 0.02, RD, 'deck')
for s in (-1, 1):
    A.box(0, AL, s * 2.6, s * AW, RD - 0.02, RD + 0.15, 'stone')
    A.box(0.2, AL, s * (AW - 0.45), s * AW, RD + 0.15, RD + 1.0, 'stone')  # parapet walls
    A.box(-0.15, 0.6, s * (AW - 0.6), s * AW, RD + 0.15, RD + 1.25, 'stone')
A.box(-0.25, 0.0, -AW - 0.2, AW + 0.2, -DEPTH - 0.4, -DEPTH - 0.1, 'stone')  # bearing ledge
abut = finish(A, 'Horsa_Abutment', tile=2.0)
export([span, pier, abut], OUTDIR + '/HorsaBridge.fbx')
print('done')
