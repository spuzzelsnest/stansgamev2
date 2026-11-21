# Run: python3 -I flakvierling_parts.py -- '<_downloads>/2cm-vierlingflak-38-ww2/source/2cm vierlingflak.blend' <out path without extension>  (bpy 5.x)
# Flakvierling 38 (DFS_studio, CC BY) split into its moving parts for Stan's Game:
#   Flakvierling38_Parts.fbx: Base (triangular base, fixed) > Carriage (turns, origin on the traverse axis)
#   > Cradle (the four guns, origin on the trunnions, levelled so local rest = 0 degrees elevation)
#   + Muzzle1..4 empties at the flash hiders, Seat_Gunner / Seat_LoaderL / Seat_LoaderR empties on the carriage.
import bpy, bmesh, math, sys, os
from mathutils import Vector, Matrix
SRC = sys.argv[sys.argv.index('--')+1]; OUT = sys.argv[sys.argv.index('--')+2]
bpy.ops.wm.open_mainfile(filepath=SRC)
arm = bpy.data.objects['Armature']; arm.data.pose_position = 'REST'
gun = bpy.data.objects['Cylinder.030']
bpy.context.view_layer.update()
B = {b.name: (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local) for b in arm.data.bones}
part_of = {'sit': 'Base', 'main': 'Carriage', 'ik1': 'Carriage', 'rotate': 'Carriage', 'aim': 'Carriage', 'Bone.011': 'Carriage',
           'gunmount': 'Cradle', 'ik3': 'Carriage', 'ik2': 'Carriage', 'b1': 'Cradle', 'b2': 'Cradle', 'b3': 'Cradle', 'b4': 'Cradle'}
me = gun.data.copy(); me.transform(gun.matrix_world)
names = [g.name for g in gun.vertex_groups]
vpart = []
for v in me.vertices:
    g = max(v.groups, key=lambda g: g.weight) if v.groups else None
    vpart.append(part_of.get(names[g.group], 'Carriage') if g else 'Carriage')
# scale / centre as the old export: 2.3 m high, centred in x,y, standing on z = 0
co = [v.co.copy() for v in me.vertices]
lo = Vector([min(c[i] for c in co) for i in range(3)]); hi = Vector([max(c[i] for c in co) for i in range(3)])
s = 2.3 / (hi.z - lo.z); off = Vector((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))
T = lambda p: (p + off) * s
for v in me.vertices: v.co = T(v.co)
traverse = T(B['main'][0]); traverse.z = 0.0
trunnion = T(B['gunmount'][0])
bdir = (B['b1'][1] - B['b1'][0]).normalized(); rest_elev = math.atan2(bdir.z, bdir.y)
print('scale', s, 'traverse', traverse, 'trunnion', trunnion, 'rest elevation', math.degrees(rest_elev))
# level the cradle: rotate it down about the trunnion (x axis) by the rest elevation
R = Matrix.Translation(trunnion) @ Matrix.Rotation(-rest_elev, 4, 'X') @ Matrix.Translation(-trunnion)
for v, p in zip(me.vertices, vpart):
    if p == 'Cradle': v.co = R @ v.co
# muzzles: for each barrel, the furthest-forward vertex near its axis
mz = []
for bn in ('b1', 'b2', 'b3', 'b4'):
    h = R @ T(B[bn][0])
    near = [v.co for v, p in zip(me.vertices, vpart) if p == 'Cradle' and abs(v.co.x - h.x) < 0.06 and abs(v.co.z - h.z) < 0.06 and v.co.y > h.y]
    tip = max(near, key=lambda c: c.y)
    mz.append(Vector((h.x, tip.y, h.z))); print(bn, 'muzzle', mz[-1])
# separate into objects
objs = {}
for part in ('Base', 'Carriage', 'Cradle'):
    bm = bmesh.new(); bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    kill = [bm.verts[i] for i, p in enumerate(vpart) if p != part]
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    m2 = bpy.data.meshes.new(part); bm.to_mesh(m2); bm.free()
    objs[part] = m2
for o in list(bpy.data.objects): bpy.data.objects.remove(o)
sc = bpy.context.scene
mat = bpy.data.materials.new('Flak_Dunkelgelb'); mat.use_nodes = True
mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.42, 0.38, 0.25, 1)
def obj(name, mesh, origin, parent=None):
    mesh.transform(Matrix.Translation(-origin)); mesh.materials.append(mat)
    o = bpy.data.objects.new(name, mesh); sc.collection.objects.link(o)
    o.location = origin
    # (no parenting here: the FBX exporter misplaces children; the Unity builder parents them)
    bpy.context.view_layer.update(); return o
base = obj('Base', objs['Base'], Vector((0, 0, 0)))
car = obj('Carriage', objs['Carriage'], traverse, base)
cra = obj('Cradle', objs['Cradle'], trunnion, car)
def empty(name, p, parent):
    # a 1 mm triangle rather than an empty: the FBX exporter's axis baking places empties wrongly in Unity
    m = bpy.data.meshes.new(name); m.from_pydata([(0, 0, 0), (0.001, 0, 0), (0, 0.001, 0)], [], [(0, 1, 2)])
    e = bpy.data.objects.new(name, m); sc.collection.objects.link(e); e.location = p
    bpy.context.view_layer.update()
for i, p in enumerate(mz): empty(f'Muzzle{i+1}', p, cra)
# seats (from the rig: the gunner behind the guns between the handwheels, a loader on each side)
hw = (T(B['rotate'][0]) + T(B['aim'][0])) / 2
empty('Seat_Gunner', Vector((0.0, -1.05, 1.22)), car)
empty('Wheel_Traverse', T(B['rotate'][0]), car); empty('Wheel_Elevation', T(B['aim'][0]), car)
for side, nm in ((1, 'Seat_LoaderR'), (-1, 'Seat_LoaderL')):
    empty(nm, Vector((side * (abs(mz[0].x) + 0.55), trunnion.y - 0.15, 0.55)), car)
for o in sc.objects: print(o.name, o.type, [round(x, 3) for x in o.matrix_world.translation], o.parent.name if o.parent else '')
lo2 = [min((o.matrix_world @ v.co)[i] for o in sc.objects if o.type == 'MESH' and len(o.data.vertices) > 3 for v in o.data.vertices) for i in range(3)]
hi2 = [max((o.matrix_world @ v.co)[i] for o in sc.objects if o.type == 'MESH' and len(o.data.vertices) > 3 for v in o.data.vertices) for i in range(3)]
print('bounds', [round(x, 2) for x in lo2], [round(x, 2) for x in hi2])
bpy.ops.wm.save_as_mainfile(filepath=OUT + '.blend')
bpy.ops.export_scene.fbx(filepath=OUT + '.fbx', use_selection=False, axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True, object_types={'MESH', 'EMPTY'}, mesh_smooth_type='FACE', path_mode='STRIP')
