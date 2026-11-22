# Vickers .303 on its tripod (vickers-machine-gun OBJ scene): gun, tripod, ammunition box and belt; floor and sandbags
# left out (the pit has its own). Real size: the gun 1.156 m long. Gun to +Y, tripod feet on z=0, gun pivot on the
# tripod head (it traverses and elevates there), a muzzle marker at the end of the barrel.
import bpy, sys, math
from mathutils import Vector, Matrix
SRC, OUT = sys.argv[-2:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=SRC)
bpy.context.view_layer.update()
ms = {o.name.split('_')[0]: o for o in bpy.data.objects if o.type == 'MESH'}
print(list(ms))
for k in ('DirtFloor', 'Sandbags'):
    if k in ms: bpy.data.objects.remove(ms.pop(k))
for o in ms.values():
    o.data = o.data.copy(); o.data.transform(o.matrix_world); o.matrix_world = Matrix()
# the OBJ was saved Z-up and read as Y-up: stand it up (the tripod's feet below the gun)
for o in ms.values(): o.data.transform(Matrix.Rotation(math.pi / 2, 4, 'X'))
def zc(o): P = [v.co.z for v in o.data.vertices]; return (min(P) + max(P)) / 2, min(P)
if zc(ms['Gun'])[0] < zc(ms['GunBody'])[0]:
    for o in ms.values(): o.data.transform(Matrix.Rotation(math.pi, 4, 'X'))
def box(o):
    P = [v.co for v in o.data.vertices]
    return Vector([min(p[i] for p in P) for i in range(3)]), Vector([max(p[i] for p in P) for i in range(3)])
gun = ms['Gun']; lo, hi = box(gun)
ax = max(range(3), key=lambda i: hi[i] - lo[i])
print('gun long axis', 'xyz'[ax], round(hi[ax] - lo[ax], 3))
L = hi[ax] - lo[ax]; S = 1.156 / L
# which end is the muzzle: the slimmer end (the spade grips and the lock are bulky at the back)
def spread(sel):
    P = [v.co for v in gun.data.vertices if sel(v.co[ax])]
    o = [i for i in range(3) if i != ax]
    return sum(max(p[i] for p in P) - min(p[i] for p in P) for i in o)
lo_s = spread(lambda a: a < lo[ax] + L * 0.06); hi_s = spread(lambda a: a > hi[ax] - L * 0.06)
sign = 1 if hi_s < lo_s else -1
print('muzzle at', 'max' if sign > 0 else 'min', round(lo_s, 3), round(hi_s, 3))
# rotate so the muzzle points +Y
d = Vector((0, 0, 0)); d[ax] = sign
R = d.to_track_quat('Y', 'Z').to_matrix().to_4x4().inverted()
for o in ms.values(): o.data.transform(Matrix.Scale(S, 4)); o.data.transform(R)
allP = [v.co for o in ms.values() for v in o.data.vertices]
zmin = min(p.z for p in allP)
for o in ms.values(): o.data.transform(Matrix.Translation(Vector((0, 0, -zmin))))
glo, ghi = box(gun); tlo, thi = box(ms['GunBody'])
print('gun', [round(x, 2) for x in glo], [round(x, 2) for x in ghi], 'tripod', [round(x, 2) for x in tlo], [round(x, 2) for x in thi])
# the tripod head: the tripod's highest points
top = [v.co for v in ms['GunBody'].data.vertices if v.co.z > thi.z - 0.04]
hx = sum(p.x for p in top) / len(top); hy = sum(p.y for p in top) / len(top)
piv = Vector((hx, hy, (glo.z + ghi.z) / 2))
muz = Vector(((glo.x + ghi.x) / 2, ghi.y, piv.z))
# centre the whole on the pivot's ground point
off = Vector((-piv.x, -piv.y, 0))
for o in ms.values(): o.data.transform(Matrix.Translation(off))
piv += off; muz += off
if 'Ammobelt' in ms:
    b = ms['Ammobelt']; bpy.context.view_layer.objects.active = b
    dm = b.modifiers.new('d', 'DECIMATE'); dm.ratio = 5000 / len(b.data.polygons); bpy.ops.object.modifier_apply(modifier='d')
names = {'Gun': 'Vickers_Gun', 'GunBody': 'Vickers_Tripod', 'Ammobox': 'Vickers_AmmoBox', 'Ammobelt': 'Vickers_Belt'}
for k, o in ms.items(): o.name = names.get(k, k)
gun.data.transform(Matrix.Translation(-piv)); gun.location = piv
if 'Ammobelt' in ms:
    b = ms['Ammobelt']; b.data.transform(Matrix.Translation(-piv)); b.parent = gun   # the belt rides with the gun
mz = bpy.data.objects.new('Vickers_Muzzle', None); bpy.context.collection.objects.link(mz); mz.parent = gun; mz.location = muz - piv
for o in ms.values():
    for p in o.data.polygons: p.use_smooth = True
print('pivot', [round(x, 3) for x in piv], 'muzzle', [round(x, 3) for x in muz], 'faces', sum(len(o.data.polygons) for o in ms.values()))
print('materials', sorted({m.name for o in ms.values() for m in o.data.materials if m}))
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True, mesh_smooth_type='FACE')
