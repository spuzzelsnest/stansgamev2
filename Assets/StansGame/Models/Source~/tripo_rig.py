# Rig a Tripo character (one T-pose mesh, ~1 m tall, facing -Y) for Unity like Models/Civilian/French_Civilian.fbx:
# real height in metres, a "mixamorig:" skeleton whose joints are measured from the mesh, heat weights computed on a
# watertight voxel proxy and transferred back, max 4 weights per vertex. Any rig Tripo made is thrown away so every
# character has the same skeleton. Heavy meshes are decimated first (UVs kept).
# usage: python3 -I tripo_rig.py <in.fbx> <out.fbx> <height m> [max tris]
import bpy, bmesh, sys, math
import numpy as np
from mathutils import Vector
SRC, OUT, HEIGHT = sys.argv[-4], sys.argv[-3], float(sys.argv[-2])
MAXTRIS = int(sys.argv[-1])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
for o in list(bpy.data.objects):
    if o.type == 'ARMATURE': bpy.data.objects.remove(o)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
body = max(meshes, key=lambda o: len(o.data.vertices))
for o in meshes:
    if o is not body: bpy.data.objects.remove(o)
body.parent = None; body.modifiers.clear(); body.vertex_groups.clear(); body.name = 'Body'
for o in bpy.data.objects: o.select_set(o is body)
bpy.context.view_layer.objects.active = body
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# real size: feet on the ground, centred, HEIGHT tall
V = np.array([v.co[:] for v in body.data.vertices])
mn, mx = V.min(0), V.max(0)
s = HEIGHT / (mx[2] - mn[2])
for v in body.data.vertices:
    c = v.co; v.co = Vector(((c.x - (mn[0] + mx[0]) / 2) * s, (c.y - (mn[1] + mx[1]) / 2) * s, (c.z - mn[2]) * s))
body.data.update()

# heavy (the 1.9M-triangle paratrooper): collapse-decimate, UVs are kept
tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
if tris > MAXTRIS:
    d = body.modifiers.new('dec', 'DECIMATE'); d.ratio = MAXTRIS / tris; d.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier='dec')
    print('decimated', tris, '->', sum(len(p.vertices) - 2 for p in body.data.polygons))

V = np.array([v.co[:] for v in body.data.vertices]); H = HEIGHT
x, y, z = V[:, 0], V[:, 1], V[:, 2]
def band(z0, z1, xsel=None):
    m = (z >= z0) & (z <= z1)
    if xsel is not None: m &= xsel
    return m
# the arm line: everything well outside the torso
arms = np.abs(x) > 0.24 * H
za = float(np.median(z[arms])) if arms.sum() > 50 else 0.81 * H
handx = float(np.percentile(np.abs(x[arms]), 99.5))
# shoulder width: the torso just under the arms
tor = band(za - 0.10 * H, za - 0.05 * H, np.abs(x) < 0.22 * H)
sx = float(np.percentile(np.abs(x[tor]), 97)) if tor.sum() > 20 else 0.12 * H
yc = float(np.median(y[band(0.5 * H, 0.75 * H, np.abs(x) < 0.1 * H)]))
def leg(zc, side):
    m = band(zc - 0.02 * H, zc + 0.02 * H, (x * side > 0.01 * H) & (np.abs(x) < 0.2 * H))
    if m.sum() < 5: return 0.08 * H * side, yc
    return float(np.median(x[m])), float(np.median(y[m]))
kx, ky = leg(0.28 * H, 1); ax, ay = leg(0.07 * H, 1)
if kx > ax + 0.04 * H: kx = ax + 0.04 * H   # a long skirt: the knee is above the ankle, not at the hem
toe_y = float(np.percentile(y[band(0, 0.04 * H, x > 0.01 * H)], 2))   # the front of the shoe (front is -Y)
armz_at = lambda px: float(np.median(z[(np.abs(np.abs(x) - px) < 0.02 * H) & arms])) if ((np.abs(np.abs(x) - px) < 0.02 * H) & arms).sum() > 3 else za
sx = min(max(sx, 0.08 * H), 0.105 * H)   # webbing and packs make the chest look wider than the shoulder joint
wrx = handx - 0.105 * H; elx = (sx * 0.92 + wrx) / 2
J = {  # left side (+x) and the middle, Blender metres
    'Hips': (0, yc, 0.52 * H), 'Spine': (0, yc, 0.565 * H), 'Spine1': (0, yc, 0.63 * H), 'Spine2': (0, yc, 0.70 * H),
    'Neck': (0, yc, 0.815 * H), 'Head': (0, yc, 0.87 * H), 'HeadTop_End': (0, yc, H),
    'LeftShoulder': (0.02 * H, yc, za - 0.015 * H), 'LeftArm': (sx * 0.92, yc, armz_at(sx)),
    'LeftForeArm': (elx, yc, armz_at(elx)), 'LeftHand': (wrx, yc, armz_at(wrx)), 'LeftHandEnd': (handx, yc, armz_at(wrx)),
    'LeftUpLeg': (0.05 * H, yc, 0.505 * H), 'LeftLeg': (kx, ky, 0.28 * H), 'LeftFoot': (ax, ay, 0.055 * H),
    'LeftToeBase': (ax, (ay + toe_y) / 2, 0.016 * H), 'LeftToe_End': (ax, toe_y, 0.012 * H),
}
def P(n):
    if n.startswith('Right'):
        a, b, c = J['Left' + n[5:]]; return Vector((-a, b, c))
    return Vector(J[n])
chain = [('Hips', None, 'Spine'), ('Spine', 'Hips', 'Spine1'), ('Spine1', 'Spine', 'Spine2'), ('Spine2', 'Spine1', 'Neck'),
         ('Neck', 'Spine2', 'Head'), ('Head', 'Neck', 'HeadTop_End'), ('HeadTop_End', 'Head', None)]
for sd in ('Left', 'Right'):
    chain += [(sd + 'Shoulder', 'Spine2', sd + 'Arm'), (sd + 'Arm', sd + 'Shoulder', sd + 'ForeArm'), (sd + 'ForeArm', sd + 'Arm', sd + 'Hand'),
              (sd + 'Hand', sd + 'ForeArm', sd + 'HandEnd'), (sd + 'UpLeg', 'Hips', sd + 'Leg'), (sd + 'Leg', sd + 'UpLeg', sd + 'Foot'),
              (sd + 'Foot', sd + 'Leg', sd + 'ToeBase'), (sd + 'ToeBase', sd + 'Foot', sd + 'Toe_End'), (sd + 'Toe_End', sd + 'ToeBase', None)]
ad = bpy.data.armatures.new('Armature'); arm = bpy.data.objects.new('Armature', ad); bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
eb = {}
for n, par, tail in chain:
    if n.endswith('HandEnd'): continue
    b = ad.edit_bones.new('mixamorig:' + n); h = P(n)
    t = P(tail) if tail else h + (h - P(par)).normalized() * 0.06
    b.head = h; b.tail = t; eb[n] = b
for n, par, tail in chain:
    if n in eb and par: eb[n].parent = eb[par]; eb[n].use_connect = False
for b in ad.edit_bones: b.align_roll(Vector((0, -1, 0)) if abs((b.tail - b.head).normalized().y) < 0.9 else Vector((0, 0, 1)))
bpy.ops.object.mode_set(mode='OBJECT')
print('joints: arm line z %.2f, shoulders x %.2f, hands x %.2f, knee x %.2f, ankle x %.2f' % (za, sx, handx, kx, ax))

# weights: watertight voxel proxy (Tripo meshes have loose bits), heat weights there, transferred back
proxy = body.copy(); proxy.data = body.data.copy(); bpy.context.scene.collection.objects.link(proxy); proxy.name = 'proxy'
m = proxy.modifiers.new('r', 'REMESH'); m.mode = 'VOXEL'; import os; m.voxel_size = float(os.environ.get('VOXEL', 0.009 * H / 1.8))
bpy.context.view_layer.objects.active = proxy; bpy.ops.object.modifier_apply(modifier='r')
bm = bmesh.new(); bm.from_mesh(proxy.data); bm.verts.ensure_lookup_table()
seen = set(); kill = []
for v in bm.verts:
    if v.index in seen: continue
    st = [v]; comp = [v]; seen.add(v.index)
    while st:
        q = st.pop()
        for e in q.link_edges:
            o = e.other_vert(q)
            if o.index not in seen: seen.add(o.index); st.append(o); comp.append(o)
    if len(comp) < 3000: kill += comp
bmesh.ops.delete(bm, geom=kill, context='VERTS'); bm.to_mesh(proxy.data); bm.free()
arm2 = arm.copy(); arm2.data = arm.data.copy(); bpy.context.scene.collection.objects.link(arm2)
for o in (proxy, arm2): o.scale = (10, 10, 10)
for o in bpy.data.objects: o.select_set(o in (proxy, arm2))
bpy.context.view_layer.objects.active = arm2
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
proxy.modifiers.clear(); proxy.parent = None; proxy.scale = (0.1, 0.1, 0.1)
for o in bpy.data.objects: o.select_set(o is proxy)
bpy.context.view_layer.objects.active = proxy
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
bpy.data.objects.remove(arm2)
for b in arm.data.bones:
    if b.name not in body.vertex_groups: body.vertex_groups.new(name=b.name)
dt = body.modifiers.new('dt', 'DATA_TRANSFER'); dt.object = proxy; dt.use_vert_data = True; dt.data_types_verts = {'VGROUP_WEIGHTS'}
dt.vert_mapping = 'POLYINTERP_NEAREST'; dt.layers_vgroup_select_src = 'ALL'; dt.layers_vgroup_select_dst = 'NAME'
bpy.context.view_layer.objects.active = body; bpy.ops.object.modifier_apply(modifier='dt')
bpy.data.objects.remove(proxy)
for o in bpy.data.objects: o.select_set(o is body)
bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
bpy.ops.object.vertex_group_normalize_all(lock_active=False)
body.parent = arm; am = body.modifiers.new('Armature', 'ARMATURE'); am.object = arm
unw = sum(1 for v in body.data.vertices if len(v.groups) == 0)
print('verts', len(body.data.vertices), 'unweighted', unw)
for sl in body.material_slots:
    if sl.material: sl.material.name = 'Body'
for o in bpy.data.objects: o.select_set(o in (arm, body))
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                         add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False, mesh_smooth_type='FACE')
print('wrote', OUT)
