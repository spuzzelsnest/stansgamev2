# M4A3E8 Sherman (manex3d generated GLB, 1.94M faces -> 70k): hull / turret / gun cut apart for TankController
# (turret traverses on its ring, gun elevates on its trunnions), nose and gun to +Y, tracks on the ground at z=0.
# Scaled 6.7x from the model's units: width 3.0 m and turret roof 2.94 m as the real E8 (the generated hull runs a little short).
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix
OUT = sys.argv[-1]
bpy.ops.wm.open_mainfile(filepath='/home/claude/assets/sherman_dec.blend')
t = bpy.data.objects['Sherman']
V = [v.co for v in t.data.vertices]
lo = Vector([min(p[i] for p in V) for i in range(3)]); hi = Vector([max(p[i] for p in V) for i in range(3)]); c = (lo + hi) / 2
t.data.transform(Matrix.Translation(Vector((-c.x, -c.y, -lo.z))))
# in these units the gun points down -y: classify each face by its centre
def part(f):
    m = f.calc_center_median()
    if max(v.co.z for v in f.verts) > 0.475: return 'drop'   # the aerials and the pennant
    if m.z > 0.295 and m.z < 0.375 and m.y < -0.06 and abs(m.x) < 0.11 and (m.y < -0.11 and abs(m.x) < 0.04 or m.y >= -0.11): return 'gun'
    if m.z > 0.285 and -0.13 < m.y < 0.30 and abs(m.x) < 0.17: return 'turret'
    return 'hull'
bm = bmesh.new(); bm.from_mesh(t.data); bm.faces.ensure_lookup_table()
labels = [part(f) for f in bm.faces]
print({k: labels.count(k) for k in set(labels)})
objs = {}
for name in ('hull', 'turret', 'gun'):
    b2 = bm.copy(); b2.faces.ensure_lookup_table()
    bmesh.ops.delete(b2, geom=[f for f, l in zip(b2.faces, labels) if l != name], context='FACES')
    me = bpy.data.meshes.new('M4_' + name.capitalize()); b2.to_mesh(me); b2.free()
    for mt in t.data.materials: me.materials.append(mt)
    o = bpy.data.objects.new('M4_' + name.capitalize(), me); bpy.context.collection.objects.link(o); objs[name] = o
bpy.data.objects.remove(t)
ho = objs['hull']; bpy.context.view_layer.objects.active = ho
d = ho.modifiers.new('dec', 'DECIMATE'); d.ratio = 36000 / len(ho.data.polygons); bpy.ops.object.modifier_apply(modifier='dec')
S = 6.7
R = Matrix.Rotation(math.pi, 4, 'Z') @ Matrix.Scale(S, 4)   # gun to +y, real size
for o in objs.values(): o.data.transform(R)
gv = [v.co for v in objs['gun'].data.vertices]
tip = max(gv, key=lambda p: p.y)
ring = R @ Vector((0.0, 0.08, 0.282)); trunnion = R @ Vector((0.0, -0.075, 0.335))
muzzle = Vector((0.0, tip.y, trunnion.z))
for o, piv in ((objs['turret'], ring), (objs['gun'], trunnion)):
    o.data.transform(Matrix.Translation(-piv)); o.location = piv
objs['turret'].parent = objs['hull']; objs['turret'].location = ring
objs['gun'].parent = objs['turret']; objs['gun'].location = trunnion - ring
mz = bpy.data.objects.new('M4_Muzzle', None); bpy.context.collection.objects.link(mz); mz.parent = objs['gun']; mz.location = muzzle - trunnion
for o in objs.values():
    for p in o.data.polygons: p.use_smooth = True
bpy.context.view_layer.update()
for o in objs.values(): print(o.name, 'top', round(max((o.matrix_world @ v.co).z for v in o.data.vertices), 2))
P = [o.matrix_world @ v.co for o in objs.values() for v in o.data.vertices]
lo = Vector([min(p[i] for p in P) for i in range(3)]); hi = Vector([max(p[i] for p in P) for i in range(3)])
print('size', [round(x, 2) for x in hi - lo], 'ring', [round(x, 2) for x in ring], 'trunnion', [round(x, 2) for x in trunnion], 'muzzle y', round(muzzle.y, 2))
for o in objs.values(): print(o.name, len(o.data.polygons))
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True, mesh_smooth_type='FACE')
# check: the turret turned 40 degrees, the gun raised 10
objs['turret'].rotation_euler = (0, 0, math.radians(40)); objs['gun'].rotation_euler = (math.radians(10), 0, 0)
sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.color_type = 'TEXTURE'
sc.render.resolution_x, sc.render.resolution_y = 800, 500
cam = bpy.data.objects.new('c', bpy.data.cameras.new('c')); sc.collection.objects.link(cam); sc.camera = cam
d = Vector((1.0, -0.4, 0.45)).normalized(); tgt = Vector((0, 0, 1.3)); cam.location = tgt + d * 13; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
sc.render.filepath = '/home/claude/assets/sh_cut.png'; bpy.ops.render.render(write_still=True)
