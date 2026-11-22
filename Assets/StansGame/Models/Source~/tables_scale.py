import bpy, sys, math
from mathutils import Vector, Matrix
SRC, NAME, OUT = sys.argv[-3:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
bpy.context.view_layer.update()
ms = [o for o in bpy.data.objects if o.type == 'MESH']
for o in ms:
    o.data = o.data.copy(); o.data.transform(o.matrix_world); o.parent = None; o.matrix_world = Matrix()
for o in [o for o in bpy.data.objects if o.type != 'MESH']: bpy.data.objects.remove(o)
P = [v.co for o in ms for v in o.data.vertices]
lo = Vector([min(p[i] for p in P) for i in range(3)]); hi = Vector([max(p[i] for p in P) for i in range(3)]); sz = hi - lo
# the up axis: the table top is the largest flat area, so up is the axis with the most faces facing along it
votes = [0.0, 0.0, 0.0]
for o in ms:
    for p in o.data.polygons:
        n = p.normal; i = max(range(3), key=lambda k: abs(n[k])); votes[i] += p.area
up = max(range(3), key=lambda k: votes[k])
print(NAME, 'size', [round(x, 3) for x in sz], 'up', 'xyz'[up])
c = (lo + hi) / 2
for o in ms: o.data.transform(Matrix.Translation(-c))
if up == 0:
    for o in ms: o.data.transform(Matrix.Rotation(-math.pi / 2, 4, 'Y'))
elif up == 1:
    for o in ms: o.data.transform(Matrix.Rotation(math.pi / 2, 4, 'X'))
P = [v.co for o in ms for v in o.data.vertices]
lo = Vector([min(p[i] for p in P) for i in range(3)]); hi = Vector([max(p[i] for p in P) for i in range(3)])
# legs down: the top (most area) must be at the high end
area_hi = sum(p.area for o in ms for p in o.data.polygons if abs(p.normal.z) > 0.9 and p.center.z > (lo.z + hi.z) / 2)
area_lo = sum(p.area for o in ms for p in o.data.polygons if abs(p.normal.z) > 0.9 and p.center.z < (lo.z + hi.z) / 2)
if area_lo > area_hi * 1.5:
    for o in ms: o.data.transform(Matrix.Rotation(math.pi, 4, 'X'))
P = [v.co for o in ms for v in o.data.vertices]
lo = Vector([min(p[i] for p in P) for i in range(3)]); hi = Vector([max(p[i] for p in P) for i in range(3)])
s = 0.76 / (hi.z - lo.z)
for o in ms: o.data.transform(Matrix.Translation(Vector((0, 0, -lo.z)))); o.data.transform(Matrix.Scale(s, 4))
# long side along x
P = [v.co for o in ms for v in o.data.vertices]
if max(p.y for p in P) - min(p.y for p in P) > max(p.x for p in P) - min(p.x for p in P):
    for o in ms: o.data.transform(Matrix.Rotation(math.pi / 2, 4, 'Z'))
P = [v.co for o in ms for v in o.data.vertices]
print(NAME, 'real size', [round(max(p[i] for p in P) - min(p[i] for p in P), 2) for i in range(3)], 'faces', sum(len(o.data.polygons) for o in ms))
for o in ms:
    o.name = NAME
    for m in o.data.materials:
        if m: m.name = NAME
for img in bpy.data.images:
    if img.size[0] > 0 and 'normal' not in img.name.lower():
        img.filepath_raw = f'/home/claude/assets/tbl_{NAME}_{img.name}.png'; img.file_format = 'PNG'; img.save(); print('image', img.name, img.size[:]); 
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True, mesh_smooth_type='FACE')
