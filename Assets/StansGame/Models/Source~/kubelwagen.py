# VW Kübelwagen (Typ 82): GLB (1.15M faces, untextured) -> light FBX for Unity, nose to +Y, wheels on the ground at z=0, materials by part.
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix
SRC, OUT = sys.argv[-2:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
bpy.context.view_layer.update()
ms = [o for o in bpy.data.objects if o.type == 'MESH']
for o in ms:
    o.data = o.data.copy(); o.data.transform(o.matrix_world); o.parent = None; o.matrix_world = Matrix()
for o in [o for o in bpy.data.objects if o.type != 'MESH']: bpy.data.objects.remove(o)
# drop the tiny bits (bolts, buttons, wires): invisible at game distance
for o in list(ms):
    if max(o.dimensions) < 0.05 or o.name.startswith('Bolt') or 'emit' in str([m.name for m in o.data.materials if m]):
        bpy.data.objects.remove(o); ms.remove(o)
P = [v.co for o in ms for v in o.data.vertices]
lo = Vector([min(p[i] for p in P) for i in range(3)]); hi = Vector([max(p[i] for p in P) for i in range(3)])
print('size', [round(x, 2) for x in hi - lo])
ax = max(range(3), key=lambda i: hi[i] - lo[i])           # the long axis
up = 2 if ax != 2 else 1
c = (lo + hi) / 2
for o in ms: o.data.transform(Matrix.Translation(-c))
# long axis -> Y, up -> Z
if ax == 0: R = Matrix.Rotation(math.pi / 2, 4, 'Z')
elif ax == 2: R = Matrix.Rotation(-math.pi / 2, 4, 'X')
else: R = Matrix()
for o in ms: o.data.transform(R)
P = [v.co for o in ms for v in o.data.vertices]
lo = Vector([min(p[i] for p in P) for i in range(3)]); hi = Vector([max(p[i] for p in P) for i in range(3)])
print('rot size', [round(x, 2) for x in hi - lo])
# glTF is Y-up: after import Blender is Z-up already; check the tyres are at the bottom
tyre = [v.co for o in ms if any(m and m.name == 'Material.003' for m in o.data.materials) for v in o.data.vertices]
tz = sum(p.z for p in tyre) / len(tyre)
print('tyre mean z', round(tz, 2), 'lo', round(lo.z, 2), 'hi', round(hi.z, 2))
if tz > (lo.z + hi.z) / 2:
    for o in ms: o.data.transform(Matrix.Rotation(math.pi, 4, 'Y'))
# the windscreen glass sits ahead of the middle: nose to +Y
glass = [v.co for o in ms if any(m and 'Glass' in m.name for m in o.data.materials) for v in o.data.vertices]
gy = sum(p.y for p in glass) / max(1, len(glass))
print('glass mean y', round(gy, 2))
if gy > 0:   # measured: the glass mean lies behind the middle on this model
    for o in ms: o.data.transform(Matrix.Rotation(math.pi, 4, 'Z'))
P = [v.co for o in ms for v in o.data.vertices]
lo = Vector([min(p[i] for p in P) for i in range(3)])
for o in ms: o.data.transform(Matrix.Translation(Vector((0, 0, -lo.z))))
# real Kübelwagen: 3.74 m long
P = [v.co for o in ms for v in o.data.vertices]
L = max(p.y for p in P) - min(p.y for p in P)
s = 3.74 / L
for o in ms: o.data.transform(Matrix.Scale(s, 4))
print('scale', round(s, 3))
# materials by part
def mat(name, col, rough=0.8, metal=0.0, alpha=1.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*col, 1); b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    b.inputs['Alpha'].default_value = alpha; m.diffuse_color = (*col, alpha)
    return m
MAP = {'CP-White Flake': mat('Kw_Body', (0.55, 0.47, 0.30)), 'Material.003': mat('Kw_Tyre', (0.05, 0.05, 0.05), 0.95),
       'CW-Nickel-matte': mat('Kw_Metal', (0.12, 0.12, 0.12), 0.6, 0.6), 'CW-Silver-matte': mat('Kw_Metal', (0.12, 0.12, 0.12)),
       'Material.001': mat('Kw_Canvas', (0.36, 0.33, 0.24)), 'Material.004': mat('Kw_Canvas', (0.36, 0.33, 0.24)),
       'CW-Floor-Pine-R': mat('Kw_Wood', (0.30, 0.22, 0.14)), 'Material': mat('Kw_Seat', (0.20, 0.17, 0.12)), 'Material.002': mat('Kw_Metal', (0.1, 0.1, 0.1)),
       'Glass': mat('Glass_Pane', (0.6, 0.65, 0.7), 0.1, 0.0, 0.35)}
def target(n):
    if n is None: return MAP['CP-White Flake']
    for k, v in MAP.items():
        if n == k or n.startswith(k + '.') or (k == 'Glass' and 'glass' in n.lower()): return v
    return MAP['CP-White Flake']
for o in ms:
    for i, m in enumerate(o.data.materials): o.data.materials[i] = target(m.name if m else None)
# one object, merged by material; then split into loose parts to find the four road wheels (tyre + rim + hub islands round each axle end)
bpy.ops.object.select_all(action='DESELECT')
for o in ms: o.select_set(True)
bpy.context.view_layer.objects.active = ms[0]
bpy.ops.object.join()
car = bpy.context.view_layer.objects.active; car.name = 'Body'
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.remove_doubles(threshold=0.0005); bpy.ops.object.mode_set(mode='OBJECT')
# hubs: the tyre faces, below the axle height, in four quarters (the spare wheel on the bonnet stands high and stays on the body)
tyre_i = [i for i, m in enumerate(car.data.materials) if m and m.name == 'Kw_Tyre']
pts = [car.data.vertices[v].co.copy() for p in car.data.polygons if p.material_index in tyre_i for v in p.vertices]
zt = sorted(p.z for p in pts); zlow = zt[int(len(zt) * 0.05)]
R = None; hubs = {}
for sx in (-1, 1):
    for sy in (-1, 1):
        q = [p for p in pts if p.x * sx > 0.45 and p.y * sy > 0.5 and p.z < 0.74]   # not the spare on the bonnet
        if not q: continue
        lo = Vector([min(p[i] for p in q) for i in range(3)]); hi = Vector([max(p[i] for p in q) for i in range(3)])
        hubs[(sx, sy)] = ((lo + hi) / 2, (hi.z - lo.z) / 2)
print('hubs', {k: (tuple(round(x, 2) for x in v[0]), round(v[1], 3)) for k, v in hubs.items()})
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='DESELECT'); bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.separate(type='LOOSE'); bpy.ops.object.mode_set(mode='OBJECT')
parts = [o for o in bpy.data.objects if o.type == 'MESH']
print('loose parts', len(parts))
groups = {k: [] for k in hubs}; body = []
for o in parts:
    vs = [v.co for v in o.data.vertices]
    lo = Vector([min(p[i] for p in vs) for i in range(3)]); hi = Vector([max(p[i] for p in vs) for i in range(3)])
    c = (lo + hi) / 2; sz = hi - lo; put = None
    for k, (h, r) in hubs.items():
        if abs(c.x - h.x) < 0.22 and (Vector((0, c.y, c.z)) - Vector((0, h.y, h.z))).length < r * 0.6 and max(sz.y, sz.z) <= 2 * r + 0.04:
            put = k; break
    (groups[put] if put else body).append(o)
def joined(objs, name, ratio_target):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = name
    n0 = len(o.data.polygons)
    d = o.modifiers.new('dec', 'DECIMATE'); d.ratio = min(1.0, ratio_target / max(1, n0))
    bpy.ops.object.modifier_apply(modifier='dec')
    for p in o.data.polygons: p.use_smooth = True
    print(name, n0, '->', len(o.data.polygons))
    return o
names = {(-1, 1): 'Wheel_FL', (1, 1): 'Wheel_FR', (-1, -1): 'Wheel_RL', (1, -1): 'Wheel_RR'}
car = joined(body, 'Body', 37000)
for k, objs in groups.items():
    if not objs: print('NO WHEEL', k); continue
    w = joined(objs, names[k], 2000)
    # pivot at the hub, so the wheel turns about its own axle
    h = hubs[k][0]
    w.data.transform(Matrix.Translation(-h)); w.location = h
    w.parent = car
print('wheel radius', round(sum(v[1] for v in hubs.values()) / len(hubs), 3))
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True, mesh_smooth_type='FACE')
# preview
import os
if os.environ.get('KW_PREVIEW'):
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); bpy.context.collection.objects.link(cam)
    cam.location = (5.5, 5.0, 2.6); cam.rotation_euler = (math.radians(72), 0, math.radians(132))
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); bpy.context.collection.objects.link(sun); sun.rotation_euler = (0.7, 0.2, 0.5)
    sc = bpy.context.scene; sc.camera = cam; sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.color_type = 'MATERIAL'
    sc.render.resolution_x, sc.render.resolution_y = 640, 400; sc.render.filepath = '/tmp/kw/preview.png'
    w = bpy.data.worlds.new('w'); sc.world = w
    bpy.ops.render.render(write_still=True)
