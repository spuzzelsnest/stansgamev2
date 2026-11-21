# Bénouville (Pegasus) Bridge as built in 1934 and seen in June 1944 - a Scherzer rolling-lift bascule.
# Blender: X along the bridge (east = tower/tail, +), Y across (north +), Z up. Metres.
# Track top z = 0, the segments touch it at x = 0 when the bridge is down; segment centre (0, 0, R).
# Road surface z = 1.25. Free end (west) at x = -24.4. Two objects: "Leaf" (moves; origin = segment centre)
# and "Base" (fixed: pit, track girders). Usage: python3 -I bridge1934.py -- out.fbx [check]
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index('--') + 1:]
OUT = args[0]
R = 5.0                      # rolling radius
ROAD = 1.25
TIP = -24.4                  # west end of the leaf
TAIL = 6.7                   # east end of the leaf deck
GY = 3.45                    # plane of the main girders, segments, tower legs, track
HW = 3.15                    # half width inside the girders (road + kerbs)
HT = 2.98                    # half width of the tail behind the segments (clears the tracks when it swings down)
TOP = 10.4                   # tower top

bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name, rgb, metal=0.0, rough=0.6):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*rgb, 1); b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
    return m

M = {
    'paint': mat('Bridge1934_Paint', (0.56, 0.58, 0.57), 0.3, 0.55),     # mid grey in the June 1944 photographs
    'dark':  mat('Bridge1934_Iron', (0.30, 0.31, 0.31), 0.5, 0.5),       # rolling treads, track, rack, ladders
    'road':  mat('Bridge1934_Road', (0.24, 0.23, 0.22), 0.0, 0.9),       # tarred road over the steel deck
    'cw':    mat('Bridge1934_Counterweight', (0.62, 0.63, 0.61), 0.2, 0.6),
    'glass': mat('Bridge1934_Glass', (0.06, 0.08, 0.10), 0.0, 0.1),
    'wood':  mat('Bridge1934_Planks', (0.33, 0.28, 0.22), 0.0, 0.85),
    'conc':  mat('Bridge1934_Concrete', (0.55, 0.53, 0.50), 0.0, 0.9),
}

class Builder:
    def __init__(s): s.bm = bmesh.new(); s.mi = {}
    def _mi(s, m):
        if m not in s.mi: s.mi[m] = len(s.mi)
        return s.mi[m]
    def _faces(s, verts, faces, m):
        vs = [s.bm.verts.new(v) for v in verts]
        i = s._mi(m)
        for f in faces:
            try:
                fc = s.bm.faces.new([vs[k] for k in f]); fc.material_index = i
            except ValueError: pass
    def box(s, x0, x1, y0, y1, z0, z1, m='paint'):
        x0, x1 = min(x0, x1), max(x0, x1); y0, y1 = min(y0, y1), max(y0, y1); z0, z1 = min(z0, z1), max(z0, z1)
        v = [(x0,y0,z0),(x1,y0,z0),(x1,y1,z0),(x0,y1,z0),(x0,y0,z1),(x1,y0,z1),(x1,y1,z1),(x0,y1,z1)]
        s._faces(v, [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)], m)
    def bar(s, p, q, w, h, m='paint', up=(0,0,1)):
        """square-ish member from p to q, w across (local side), h in the 'up' plane"""
        p = Vector(p); q = Vector(q); d = (q - p); L = d.length
        if L < 1e-4: return
        a = d / L; u = Vector(up)
        if abs(a.dot(u)) > 0.95: u = Vector((1, 0, 0)) if abs(a.x) < 0.9 else Vector((0, 1, 0))
        side = a.cross(u).normalized(); up2 = side.cross(a).normalized()
        c = []
        for t in (p, q):
            for sx, sz in ((-1,-1),(1,-1),(1,1),(-1,1)):
                c.append(tuple(t + side * (sx * w / 2) + up2 * (sz * h / 2)))
        s._faces(c, [(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)], m)
    def prism_xz(s, prof, y0, y1, m='paint'):
        """extrude a convex/simple polygon given in (x, z) between y0 and y1"""
        n = len(prof)
        v = [(x, y0, z) for x, z in prof] + [(x, y1, z) for x, z in prof]
        f = [tuple(range(n))[::-1], tuple(range(n, 2 * n))]
        for i in range(n):
            j = (i + 1) % n; f.append((i, j, n + j, n + i))
        s._faces(v, f, m)
    def ring(s, cx, cz, r0, r1, a0, a1, y0, y1, m='paint', steps=24):
        """annular sector in the XZ plane (angles in degrees), extruded in Y"""
        for k in range(steps):
            t0 = math.radians(a0 + (a1 - a0) * k / steps); t1 = math.radians(a0 + (a1 - a0) * (k + 1) / steps)
            pts = [(cx + r0 * math.cos(t0), cz + r0 * math.sin(t0)), (cx + r1 * math.cos(t0), cz + r1 * math.sin(t0)),
                   (cx + r1 * math.cos(t1), cz + r1 * math.sin(t1)), (cx + r0 * math.cos(t1), cz + r0 * math.sin(t1))]
            s.prism_xz(pts, y0, y1, m)
    def cyl_y(s, cx, cz, r, y0, y1, m='paint', n=16):
        pts = [(cx + r * math.cos(2 * math.pi * k / n), cz + r * math.sin(2 * math.pi * k / n)) for k in range(n)]
        s.prism_xz(pts, y0, y1, m)
    def finish(s, name, origin=(0, 0, 0)):
        me = bpy.data.meshes.new(name); s.bm.to_mesh(me); s.bm.free()
        for m, i in sorted(s.mi.items(), key=lambda kv: kv[1]): me.materials.append(M[m])
        me.transform(Matrix.Translation(-Vector(origin)))
        ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
        ob.location = origin
        return ob

def lattice_rail(b, y, x0, x1, h=1.1, base=ROAD + 0.12, pitch=1.6):
    """the 1934 parapet: posts, top rail, bottom rail and a diagonal (diamond) lattice between them"""
    n = max(1, round((x1 - x0) / pitch)); p = (x1 - x0) / n
    b.box(x0, x1, y - 0.04, y + 0.04, base + h - 0.06, base + h, 'paint')            # top rail (flat bar on edge)
    b.box(x0, x1, y - 0.05, y + 0.05, base + h - 0.10, base + h - 0.06, 'paint')     # its cap
    b.box(x0, x1, y - 0.03, y + 0.03, base, base + 0.10, 'paint')                    # bottom rail
    for i in range(n + 1):
        x = x0 + i * p
        b.box(x - 0.045, x + 0.045, y - 0.05, y + 0.05, base, base + h, 'paint')     # angle-iron posts
    lo, hi = base + 0.10, base + h - 0.10
    for i in range(n):
        xa = x0 + i * p
        for k in range(2):                                                            # two diamonds per panel
            u0 = xa + k * p / 2; u1 = u0 + p / 2
            b.bar((u0, y, lo), (u1, y, hi), 0.05, 0.014, 'paint', up=(0, 1, 0))
            b.bar((u0, y, hi), (u1, y, lo), 0.05, 0.014, 'paint', up=(0, 1, 0))

def handrail(b, p0, p1, z, h=1.0):
    p0 = Vector(p0); p1 = Vector(p1); L = (p1 - p0).length; n = max(1, round(L / 1.5))
    b.bar((p0.x, p0.y, z + h), (p1.x, p1.y, z + h), 0.05, 0.05, 'paint')
    b.bar((p0.x, p0.y, z + h * 0.5), (p1.x, p1.y, z + h * 0.5), 0.035, 0.035, 'paint')
    b.bar((p0.x, p0.y, z + 0.06), (p1.x, p1.y, z + 0.06), 0.02, 0.1, 'paint')
    for i in range(n + 1):
        c = p0 + (p1 - p0) * (i / n)
        b.bar((c.x, c.y, z), (c.x, c.y, z + h), 0.05, 0.05, 'paint')

def ladder(b, bottom, top, width=0.5):
    bottom = Vector(bottom); top = Vector(top); d = top - bottom; L = d.length
    across = Vector((0, 1, 0)) if abs(d.normalized().y) < 0.5 else Vector((1, 0, 0))
    for s_ in (-1, 1):
        o = across * (s_ * width / 2)
        b.bar(bottom + o, top + o + d.normalized() * 0.9, 0.05, 0.07, 'dark')         # stringers carry on above the landing
    n = int(L / 0.3)
    for i in range(1, n):
        c = bottom + d * (i / n)
        b.bar(c - across * width / 2, c + across * width / 2, 0.03, 0.03, 'dark')

# ============================================================ the leaf (moves)
L = Builder()
# deck: steel deck plate with the tarred road on top, kerbs/footway strips inside the girders
L.box(TIP, -0.7, -HW, HW, ROAD - 0.30, ROAD - 0.05, 'paint')
L.box(-0.7, TAIL, -HT, HT, ROAD - 0.30, ROAD - 0.05, 'paint')        # the tail is narrower: it swings down between the tracks
L.box(TIP + 0.05, TAIL, -2.75, 2.75, ROAD - 0.05, ROAD, 'road')
for s in (-1, 1):
    L.box(TIP, -0.7, s * 2.75, s * HW, ROAD - 0.05, ROAD + 0.12, 'paint')                  # raised kerb / footway
    L.box(-0.7, TAIL, s * 2.75, s * HT, ROAD - 0.05, ROAD + 0.12, 'paint')
# cross girders under the deck (seen from the canal)
x = TIP + 0.4
while x < TAIL:
    w = GY if x < -0.9 else HT
    L.box(x - 0.12, x + 0.12, -w, w, ROAD - 0.75 if x < -14 else ROAD - 0.95, ROAD - 0.30, 'paint')
    x += 1.8
# main plate girders, deepest near the tower, tapering towards the free end; they end in the rolling segments
def bottom(xx):
    return 0.45 if xx > -12 else 0.45 + (0.40 * (min(-12, xx) + 12) / (TIP + 12))   # 0.45 .. 0.85 above the track
for s in (-1, 1):
    y = s * GY
    prof = [(TIP, ROAD - 0.40), (TIP, ROAD + 0.22), (1.1, ROAD + 0.22), (1.1, bottom(1.1)), (-12, bottom(-12)), (TIP + 1.0, bottom(TIP + 1.0))]
    prof = [(TIP, ROAD - 0.40), (TIP + 1.0, 0.85), (-12, 0.45), (1.1, 0.45), (1.1, ROAD + 0.22), (TIP, ROAD + 0.22)]
    L.prism_xz(prof, y - 0.04, y + 0.04, 'paint')                                             # web
    L.box(TIP, 1.1, y - 0.22, y + 0.22, ROAD + 0.22, ROAD + 0.30, 'paint')                    # top flange
    L.bar((TIP + 1.0, y, 0.85), (-12, y, 0.45), 0.40, 0.06, 'paint', up=(0, 0, 1))            # bottom flange
    L.box(-12, 1.1, y - 0.20, y + 0.20, 0.40, 0.46, 'paint')
    L.bar((TIP, y, ROAD - 0.40), (TIP + 1.0, y, 0.85), 0.40, 0.06, 'paint')
    xx = TIP + 0.9
    while xx < 2.0:                                                                           # web stiffeners
        L.box(xx - 0.05, xx + 0.05, y - 0.16, y + 0.16, bottom(xx) + 0.05, ROAD + 0.22, 'paint')
        xx += 1.5
    # rolling segment: a curved plate girder, quarter circle and a bit, ending in a heavy cast tread
    L.ring(0, R, 3.55, 4.80, -100, 12, y - 0.04, y + 0.04, 'paint', 28)                       # web
    L.ring(0, R, 4.80, 5.00, -100, 12, y - 0.30, y + 0.30, 'dark', 40)                        # tread (rolls on the track)
    L.ring(0, R, 3.45, 3.60, -100, 12, y - 0.20, y + 0.20, 'paint', 28)                       # inner flange
    for a in range(-95, 13, 9):                                                               # radial stiffeners + tread teeth
        t = math.radians(a)
        L.bar((3.5 * math.cos(t), y, R + 3.5 * math.sin(t)), (4.85 * math.cos(t), y, R + 4.85 * math.sin(t)), 0.30, 0.08, 'paint', up=(0, 1, 0))
    # spokes from the hub to the inner flange, and the hub itself
    for a in (-95, -60, -25, 10):
        t = math.radians(a)
        L.bar((0, y, R), (3.5 * math.cos(t), y, R + 3.5 * math.sin(t)), 0.30, 0.22, 'paint', up=(0, 1, 0))
    L.cyl_y(0, R, 0.55, y - 0.25, y + 0.25, 'paint')
# torsion tube/axle between the two segments, high over the road
L.cyl_y(0, R, 0.30, -GY, GY, 'paint')

# ------------------------------------------------------------ tower (each side in the girder plane)
XF, XR, ZG = -0.6, 4.4, 6.3        # front leg, rear leg, gallery level
for s in (-1, 1):
    y = s * GY
    L.bar((XF, y, ROAD + 0.2), (XF, y, TOP), 0.42, 0.42, 'paint', up=(1, 0, 0))               # front leg
    L.bar((XR, y, R - 0.2), (XR, y, TOP), 0.38, 0.38, 'paint', up=(1, 0, 0))                  # rear leg, standing on the segment
    L.bar((-10.6, y, ROAD + 0.25), (XF + 0.2, y, TOP), 0.42, 0.34, 'paint', up=(0, 1, 0))     # main strut down to the span
    L.bar((XF, y, TOP - 0.4), (3.3, y, 1.75), 0.30, 0.24, 'paint', up=(0, 1, 0))              # back stay to the segment
    L.bar((-10.6 + 3.2, y, ROAD + 2.9), (XF, y, ROAD + 2.9), 0.22, 0.2, 'paint')              # struts between main strut and leg
    L.bar((-10.6 + 5.3, y, ZG - 0.2), (XF, y, ZG - 0.2), 0.22, 0.2, 'paint')
    L.bar((-10.6 + 3.2, y, ROAD + 2.9), (XF, y, ZG - 0.2), 0.14, 0.12, 'paint', up=(0, 1, 0))  # lacing
    L.bar((-10.6 + 5.3, y, ZG - 0.2), (XF, y, ROAD + 2.9), 0.14, 0.12, 'paint', up=(0, 1, 0))
    L.bar((XF, y, ZG - 0.2), (XR, y, ZG - 0.2), 0.30, 0.45, 'paint')                         # gallery beam
    L.bar((XF, y, TOP), (XR, y, TOP), 0.30, 0.40, 'paint')                                   # top boom
    L.bar((XF, y, ZG + 0.2), (XR, y, TOP - 0.3), 0.14, 0.12, 'paint', up=(0, 1, 0))
    L.bar((XF, y, TOP - 0.3), (XR, y, ZG + 0.2), 0.14, 0.12, 'paint', up=(0, 1, 0))
    L.bar((XR + 0.1, y, TOP - 0.3), (4.9, y, ZG + 1.2), 0.2, 0.2, 'paint', up=(0, 1, 0))      # back knee
# cross girders and portal braces between the two sides
for xx, zz, hh in ((XF, TOP, 0.5), (XR, TOP, 0.5), (XF, ZG - 0.25, 0.5), (XR, ZG - 0.25, 0.5), (1.9, ZG - 0.25, 0.45), (3.1, ZG - 0.25, 0.45)):
    L.box(xx - 0.18, xx + 0.18, -GY, GY, zz - hh / 2, zz + hh / 2, 'paint')
for s in (-1, 1):
    L.bar((XF, s * GY, ZG - 1.4), (XF, s * 1.6, ZG - 0.4), 0.2, 0.2, 'paint', up=(1, 0, 0))   # portal knees over the road
    L.bar((XF, s * GY, TOP - 1.2), (XF, s * 1.6, TOP - 0.2), 0.2, 0.2, 'paint', up=(1, 0, 0))
# operators' gallery: plank floor on the beams, handrail round it
GX0, GX1, GYW = -3.4, 4.3, 4.25
L.box(GX0, GX1, -GYW, GYW, ZG, ZG + 0.08, 'wood')
L.box(GX0, GX1, -GYW, -GYW + 0.12, ZG - 0.18, ZG + 0.08, 'paint'); L.box(GX0, GX1, GYW - 0.12, GYW, ZG - 0.18, ZG + 0.08, 'paint')
L.box(GX0, GX0 + 0.12, -GYW, GYW, ZG - 0.18, ZG + 0.08, 'paint'); L.box(GX1 - 0.12, GX1, -GYW, GYW, ZG - 0.18, ZG + 0.08, 'paint')
handrail(L, (GX0, -GYW + 0.06), (GX0, GYW - 0.06), ZG + 0.08)
handrail(L, (GX1, -GYW + 0.06), (GX1, GYW - 0.06), ZG + 0.08)
for s in (-1, 1):
    handrail(L, (GX0 + 1.3, s * (GYW - 0.06)), (GX1, s * (GYW - 0.06)), ZG + 0.08)            # gap at the front corners for the ladders
# counterweight: a riveted steel box filled with concrete, high at the back of the tower
CX0, CX1, CW0, CW1, CY = 0.2, 4.7, 6.75, TOP + 0.5, 3.0
L.box(CX0, CX1, -CY, CY, CW0, CW1, 'cw')
for zz in (CW0 + 1.2, CW0 + 2.4):                                                             # plate seams (rivet straps)
    L.box(CX0 - 0.03, CX1 + 0.03, -CY - 0.03, CY + 0.03, zz - 0.05, zz + 0.05, 'cw')
for xx in (CX0 + 1.1, CX0 + 2.25, CX0 + 3.4):
    L.box(xx - 0.05, xx + 0.05, -CY - 0.03, CY + 0.03, CW0, CW1, 'cw')
L.box(CX0 - 0.1, CX1 + 0.1, -CY - 0.1, CY + 0.1, CW1 - 0.08, CW1 + 0.05, 'cw')               # top angle
# the driver's cabin on the gallery, in front of the counterweight: windows on three sides, door at the side
KX0, KX1, KZ1, KY = -3.1, 0.15, ZG + 2.45, 1.75
t = 0.08
L.box(KX0, KX1, -KY, KY, ZG + 0.08, ZG + 0.95, 'paint')                                       # lower walls
L.box(KX0, KX1, -KY, KY, KZ1 - 0.35, KZ1, 'paint')                                            # above the windows
for yy in (-KY, -0.55, 0.55, KY):                                                             # window posts (front)
    L.box(KX0, KX0 + t, yy - 0.06, yy + 0.06, ZG + 0.95, KZ1 - 0.35, 'paint')
L.box(KX0 + 0.02, KX0 + 0.05, -KY, KY, ZG + 0.95, KZ1 - 0.35, 'glass')
for s in (-1, 1):
    y = s * KY
    L.box(KX0, KX1, y - t / 2, y + t / 2, ZG + 0.95, KZ1 - 0.35, 'glass')
    for xx in (KX0, -1.6, KX1):
        L.box(xx - 0.06, xx + 0.06, y - t / 2 - 0.01, y + t / 2 + 0.01, ZG + 0.95, KZ1 - 0.35, 'paint')
L.box(KX1 - t, KX1, -KY, KY, ZG + 0.08, KZ1, 'paint')                                         # back wall (against the counterweight)
L.box(KX0 - 0.25, KX1 + 0.1, -KY - 0.25, KY + 0.25, KZ1, KZ1 + 0.1, 'paint')                  # roof
L.box(KX0 + 0.3, KX0 + 1.1, -0.5, 0.5, ZG + 0.08, ZG + 1.05, 'dark')                          # the controller inside
L.bar((KX0 + 0.7, 0, ZG + 1.05), (KX0 + 0.85, 0, ZG + 1.5), 0.05, 0.05, 'dark')
# side landings at road level by the tower, and the ladders up to the gallery (June 1944 photograph)
for s in (-1, 1):
    y0, y1 = s * (GY + 0.25), s * (GY + 1.15)
    L.box(-3.4, -1.35, y0, y1, ROAD - 0.05, ROAD + 0.05, 'wood')
    L.box(-3.4, -1.35, y1 - s * 0.08, y1, ROAD - 0.25, ROAD + 0.05, 'paint')
    L.bar((-3.4, s * GY, ROAD - 0.6), (-3.4, y1, ROAD - 0.05), 0.15, 0.15, 'paint')           # brackets
    L.bar((-1.4, s * GY, ROAD - 0.6), (-1.4, y1, ROAD - 0.05), 0.15, 0.15, 'paint')
    handrail(L, (-3.4, y1), (-1.35, y1), ROAD + 0.05)
    handrail(L, (-3.4, y0), (-3.4, y1), ROAD + 0.05)
    ladder(L, (-2.3, s * (GY + 0.75), ROAD + 0.05), (-2.85, s * (GY + 0.75), ZG + 0.08))
# the parapets along both sides of the road, on the kerb
for s in (-1, 1):
    lattice_rail(L, s * (HW - 0.06), TIP + 0.15, -0.75)
    lattice_rail(L, s * (HT - 0.06), -0.65, TAIL - 0.15)

leaf = L.finish('Leaf', origin=(0, 0, R))

# ============================================================ the base (fixed): pit, rolling track, pier
B = Builder()
PX0, PX1, PY, PZ = -1.2, 9.2, 4.2, -4.7          # outer footprint of the pier/pit and its floor
B.box(PX0, PX0 + 0.4, -PY, PY, PZ, 0.0, 'conc')                                    # front wall, canal face (top at the track)
B.box(PX1 - 0.4, PX1, -PY, PY, PZ, ROAD - 0.05, 'conc')                            # back wall
for s in (-1, 1):
    B.box(PX0, PX1, s * 3.8, s * PY, PZ, ROAD - 0.05, 'conc')                      # side walls
B.box(PX0, PX1, -PY, PY, PZ - 0.4, PZ, 'conc')                                     # floor
for s in (-1, 1):                                                                  # rolling tracks on their girders
    y = s * GY
    B.box(-0.8, PX1 - 0.4, y - 0.32, y + 0.32, -0.08, 0.0, 'dark')                 # track plate
    B.box(-0.8, PX1 - 0.4, y - 0.05, y + 0.05, -0.9, -0.08, 'dark')                # web
    B.box(-0.8, PX1 - 0.4, y - 0.25, y + 0.25, -0.98, -0.9, 'dark')                # bottom flange
    xx = -0.6
    while xx < PX1 - 0.5:                                                          # sockets for the segment teeth
        B.box(xx - 0.06, xx + 0.06, y - 0.34, y - 0.30, -0.08, 0.04, 'dark'); B.box(xx - 0.06, xx + 0.06, y + 0.30, y + 0.34, -0.08, 0.04, 'dark')
        xx += 0.55
    for xx in (-0.4, 1.9, 4.2, 6.5, 8.6):                                          # columns down to the floor
        B.box(xx - 0.25, xx + 0.25, y - 0.25, y + 0.25, PZ, -0.98, 'conc')
    B.box(PX0, PX1, s * 3.75, s * 3.8, -1.0, ROAD - 0.05, 'conc')                  # inner face of the slot
# fixed road plate over the back of the pit, where nothing of the leaf ever passes below the road
B.box(7.15, PX1 - 0.4, -3.1, 3.1, ROAD - 0.06, ROAD, 'road')
B.box(7.15, PX1 - 0.4, -3.15, 3.15, ROAD - 0.32, ROAD - 0.06, 'dark')
for xx in (7.3, 8.0, 8.65):
    B.box(xx - 0.1, xx + 0.1, -3.1, 3.1, ROAD - 0.75, ROAD - 0.32, 'dark')
    B.box(xx - 0.1, xx + 0.1, -3.1, -2.9, PZ, ROAD - 0.75, 'dark'); B.box(xx - 0.1, xx + 0.1, 2.9, 3.1, PZ, ROAD - 0.75, 'dark')
base = B.finish('Base')

# ============================================================ swing check
if 'check' in args:
    me = leaf.data
    loc = [v.co.copy() for v in me.vertices]                       # relative to the segment centre
    fixed = []                                                     # (x0,x1,y0,y1,z0,z1) solids the leaf must not enter
    fixed += [(PX0, PX0 + 0.4, -PY, PY, PZ, 0.0), (PX1 - 0.4, PX1, -PY, PY, PZ, ROAD - 0.05)]
    for s in (-1, 1):
        fixed += [(PX0, PX1, min(s*3.75, s*PY), max(s*3.75, s*PY), PZ, ROAD - 0.05), (-0.8, PX1 - 0.4, min(s*(GY-0.34), s*(GY+0.34)), max(s*(GY-0.34), s*(GY+0.34)), -1.0, 0.0)]
    fixed += [(PX0, PX1, -PY, PY, PZ - 0.4, PZ)]
    def inside_ground(p):
        if PX0 <= p.x <= PX1 and abs(p.y) <= PY: return False      # over the pit: the box list decides
        return p.z < ROAD - 0.05 and p.x > PX0                     # road/bank east of the canal
    for deg in range(0, 86, 5):
        th = math.radians(deg)
        rot = Matrix.Rotation(th, 3, 'Y')                           # free end (-x) goes up
        c = Vector((R * th, 0, R))
        hits = 0; worst = None
        for v in loc:
            p = c + rot @ v
            e_ = 0.03
            bad = inside_ground(p) or any(a + e_ < p.x < b - e_ and cc + e_ < p.y < d - e_ and e + e_ < p.z < f - e_ for a, b, cc, d, e, f in fixed)
            if bad:
                hits += 1
                if worst is None or p.z < worst.z: worst = p; wv = v
        print(f'{deg:3d} deg: {hits} verts collide' + (f', lowest at x {worst.x:.2f} y {worst.y:.2f} z {worst.z:.2f} (part at rest x {wv.x:.2f} y {wv.y:.2f} z {wv.z+R:.2f})' if worst else ''))
    mx = -99
    for deg in range(0, 68, 1):
        th = math.radians(deg); rot = Matrix.Rotation(th, 3, 'Y'); c = Vector((R * th, 0, R))
        for v in loc:
            p = c + rot @ v
            if p.z < ROAD + 0.05 and abs(p.y) < 3.1 and p.x > mx: mx = p.x
    print('max x of the leaf below road level while it swings:', round(mx, 2))
    zs = [v.co.z + R for v in me.vertices]
    print('leaf verts', len(me.vertices), 'height', round(max(zs), 2))

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
                         apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', path_mode='STRIP',
                         bake_anim=False, mesh_smooth_type='FACE')
print('exported', OUT)
