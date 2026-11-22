# Willys MB jeep (willy-jeep/source/Jeep.fbx, 154k faces): real size (3.36 m long), nose to +Y, wheels on the ground at
# z=0, named Wheel_FL/FR/RL/RR with their pivots on the hubs (TruckController turns and spins them), lighter mesh.
import bpy, sys, math
from mathutils import Vector, Matrix
SRC, OUT = sys.argv[-2:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
bpy.context.view_layer.update()
ms = [o for o in bpy.data.objects if o.type == 'MESH']
for o in ms:
    o.data = o.data.copy(); o.data.transform(o.matrix_world); o.parent = None; o.matrix_world = Matrix()
for o in [o for o in bpy.data.objects if o.type != 'MESH']: bpy.data.objects.remove(o)
def box(objs):
    P = [v.co for o in objs for v in o.data.vertices]
    return Vector([min(p[i] for p in P) for i in range(3)]), Vector([max(p[i] for p in P) for i in range(3)])
lo, hi = box(ms); c = (lo + hi) / 2
for o in ms: o.data.transform(Matrix.Translation(Vector((-c.x, -c.y, -lo.z))))
front = [o for o in ms if o.name.startswith('Front')]
fy = sum((box([o])[0].y + box([o])[1].y) / 2 for o in front) / len(front)
print('front wheels at y', round(fy, 2))
if fy < 0:
    for o in ms: o.data.transform(Matrix.Rotation(math.pi, 4, 'Z'))
lo, hi = box(ms)
s = 3.36 / (hi.y - lo.y)
for o in ms: o.data.transform(Matrix.Scale(s, 4))
lo, hi = box(ms); print('size', [round(x, 2) for x in hi - lo], 'scale', round(s, 4))
def decimate(o, target):
    n0 = len(o.data.polygons)
    if n0 <= target: return
    bpy.context.view_layer.objects.active = o
    d = o.modifiers.new('dec', 'DECIMATE'); d.ratio = target / n0
    bpy.ops.object.modifier_apply(modifier='dec')
    print(o.name, n0, '->', len(o.data.polygons))
body = next(o for o in ms if o.name == 'Body')
for o in ms:
    t = {'Body': 15000, 'Seats': 2500, 'SpareTyre': 1000, 'Panel': 1200, 'Side Headlights': 900, 'Tools': 1200}.get(o.name)
    if o.name.startswith(('Front_', 'Rear_')): t = 1100
    if t: decimate(o, t)
for o in ms:
    if o.name.startswith(('Front_', 'Rear_')):
        a, b = box([o]); h = (a + b) / 2
        o.name = 'Wheel_' + ('F' if o.name.startswith('Front') else 'R') + ('L' if h.x < 0 else 'R')
        o.data.transform(Matrix.Translation(-h)); o.location = h
        print(o.name, 'hub', [round(x, 3) for x in h], 'radius', round((b.z - a.z) / 2, 3))
for o in ms:
    for p in o.data.polygons: p.use_smooth = True
    # (no parenting: children came into Unity without their offsets once the unit scale was baked; the game finds
    # the Wheel_ parts by name anywhere under the model)
print('materials', sorted({m.name for o in ms for m in o.data.materials if m}))
print('faces', sum(len(o.data.polygons) for o in ms))
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True, mesh_smooth_type='FACE')
