# Rigging the CoD2 Normandy Germans (_downloads/normandy-nazis-call-of-duty-2-hd, five unrigged figures in one FBX) for Unity.
# python3 -I rig_germans.py <Default_Nazis.fbx> <outdir> <letter>
# One shared atlas (German_Normandy_BaseColor.jpg, 4096^2: uniform sheet on top, officer sheet bottom-left, three heads),
# a Mixamo-named skeleton fitted to measured joints, bone-heat weights on a voxel proxy at 10x scale, arms raised to a T-pose.
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix
SRC, OUT, WHICH = sys.argv[-3], sys.argv[-2], sys.argv[-1]
FIG = {'A': (5.4, 'German_Heer_A'), 'B': (22.05, 'German_Heer_B'), 'C': (10.9, 'German_Heer_C'), 'O': (16.5, 'German_Officer')}
cx, NAME = FIG[WHICH]
REG = {'german_normandy_uniform_col_nz': (0, .5, 1, .5), 'german_officer_col_nz': (0, 0, .5, .5), 'ericp_head_col': (.5, .25, .25, .25),
       'grman_normndy__heads_col': (.75, .25, .25, .25), 'dan_german_col': (.5, 0, .25, .25)}
KEEP = int(__import__('os').environ.get('KEEP', '1500'))
S = 0.25   # CoD2 units -> metres (crown at 7.1 units = 1.78 m)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
bpy.context.view_layer.update()
parts = []
for o in list(bpy.data.objects):
    if o.type != 'MESH': bpy.data.objects.remove(o); continue
    c = sum((o.matrix_world @ v.co for v in o.data.vertices), Vector()) / len(o.data.vertices)
    if abs(c.x - cx) > 2.6: bpy.data.objects.remove(o); continue
    img = [n.image.name for n in o.material_slots[0].material.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image][0].split('.dds')[0]
    u0, v0, w, h = REG[img]
    for d in o.data.uv_layers[0].data:
        d.uv = Vector((u0 + min(max(d.uv.x, 0), 1) * w, v0 + min(max(d.uv.y, 0), 1) * h))
    o.parent = None
    parts.append(o)
bpy.ops.object.select_all(action='DESELECT')
for o in parts: o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.object.join()
body = bpy.context.active_object; body.name = NAME
body.data.transform(Matrix.Scale(S, 4) @ Matrix.Translation(Vector((-cx, 0, 0))))
body.data.materials.clear(); body.data.materials.append(bpy.data.materials.new(NAME))
# the rip is made of draw-call strips: drop the zero-area joining triangles (they stretch into long spikes once skinned)
bm = bmesh.new(); bm.from_mesh(body.data)
deg = [f for f in bm.faces if f.calc_area() < 1e-9]
bmesh.ops.delete(bm, geom=deg, context='FACES_ONLY')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
bm.to_mesh(body.data); bm.free()
print('degenerate faces removed', len(deg))
print('dims', [round(x, 3) for x in body.dimensions])

U = 1  # joints in CoD2 units relative to the figure (left side +x, front -y)
J = {'Hips': (0, -0.05, 3.6), 'Spine': (0, -0.05, 3.95), 'Spine1': (0, -0.05, 4.4), 'Spine2': (0, -0.07, 4.9),
     'Neck': (0, -0.15, 5.9), 'Head': (0, -0.3, 6.25), 'HeadTop_End': (0, -0.3, 7.1),
     'LeftShoulder': (0.15, -0.1, 5.7), 'LeftArm': (0.75, -0.1, 5.7), 'LeftForeArm': (1.4, -0.5, 5.05),
     'LeftHand': (1.78, -1.0, 4.55), 'LeftHandEnd': (1.94, -1.8, 4.06),   # the gloves point forward (measured on the glove pieces)
     'LeftUpLeg': (0.35, 0.0, 3.45), 'LeftLeg': (0.42, -0.12, 1.9), 'LeftFoot': (0.45, 0.15, 0.35),
     'LeftToeBase': (0.48, -0.4, 0.1), 'LeftToe_End': (0.5, -0.75, 0.05)}
def P(n):
    if n.startswith('Right'):
        x, y, z = J['Left' + n[5:]]; return Vector((-x, y, z)) * S
    return Vector(J[n]) * S
chain = [('Hips', None, 'Spine'), ('Spine', 'Hips', 'Spine1'), ('Spine1', 'Spine', 'Spine2'), ('Spine2', 'Spine1', 'Neck'),
         ('Neck', 'Spine2', 'Head'), ('Head', 'Neck', 'HeadTop_End'), ('HeadTop_End', 'Head', None)]
for s in ('Left', 'Right'):
    chain += [(s + 'Shoulder', 'Spine2', s + 'Arm'), (s + 'Arm', s + 'Shoulder', s + 'ForeArm'), (s + 'ForeArm', s + 'Arm', s + 'Hand'),
              (s + 'Hand', s + 'ForeArm', s + 'HandEnd'), (s + 'UpLeg', 'Hips', s + 'Leg'), (s + 'Leg', s + 'UpLeg', s + 'Foot'),
              (s + 'Foot', s + 'Leg', s + 'ToeBase'), (s + 'ToeBase', s + 'Foot', s + 'Toe_End'), (s + 'Toe_End', s + 'ToeBase', None)]
ad = bpy.data.armatures.new('Armature'); arm = bpy.data.objects.new('Armature', ad); bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
eb = {}
for n, par, tail in chain:
    b = ad.edit_bones.new('mixamorig:' + n); h = P(n)
    t = P(tail) if tail else h + (h - P(par)).normalized() * 0.06
    b.head = h; b.tail = t; eb[n] = b
for n, par, tail in chain:
    if par: eb[n].parent = eb[par]; eb[n].use_connect = False
for b in ad.edit_bones: b.align_roll(Vector((0, -1, 0)) if abs((b.tail - b.head).normalized().z) > 0.7 else Vector((0, 0, 1)))
bpy.ops.object.mode_set(mode='OBJECT')

# proxy: watertight voxel remesh -> heat weights -> transfer
proxy = body.copy(); proxy.data = body.data.copy(); bpy.context.scene.collection.objects.link(proxy); proxy.name = 'proxy'
m = proxy.modifiers.new('r', 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = 0.008
bpy.context.view_layer.objects.active = proxy; bpy.ops.object.modifier_apply(modifier='r')
bm = bmesh.new(); bm.from_mesh(proxy.data); bm.verts.ensure_lookup_table()
seen = set(); comps = []
for v in bm.verts:
    if v.index in seen: continue
    st = [v]; comp = [v]; seen.add(v.index)
    while st:
        x = st.pop()
        for e in x.link_edges:
            o = e.other_vert(x)
            if o.index not in seen: seen.add(o.index); st.append(o); comp.append(o)
    comps.append(comp)
kill = [v for c in comps if len(c) < KEEP for v in c]
bmesh.ops.delete(bm, geom=kill, context='VERTS'); bm.to_mesh(proxy.data); bm.free()
print('proxy verts', len(proxy.data.vertices), 'parts', sorted([len(c) for c in comps], reverse=True)[:12])
arm2 = arm.copy(); arm2.data = arm.data.copy(); bpy.context.scene.collection.objects.link(arm2)
for o in (proxy, arm2): o.scale = (10, 10, 10)
for o in bpy.data.objects: o.select_set(o in (proxy, arm2))
bpy.context.view_layer.objects.active = arm2
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
proxy.modifiers.clear(); proxy.parent = None; proxy.scale = (0.1, 0.1, 0.1)
for o in bpy.data.objects: o.select_set(o == proxy)
bpy.context.view_layer.objects.active = proxy
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
bpy.data.objects.remove(arm2)
print('proxy unweighted', sum(1 for v in proxy.data.vertices if len(v.groups) == 0))
for b in arm.data.bones:
    if b.name not in body.vertex_groups: body.vertex_groups.new(name=b.name)
dt = body.modifiers.new('dt', 'DATA_TRANSFER'); dt.object = proxy; dt.use_vert_data = True; dt.data_types_verts = {'VGROUP_WEIGHTS'}
dt.vert_mapping = 'POLYINTERP_NEAREST'; dt.layers_vgroup_select_src = 'ALL'; dt.layers_vgroup_select_dst = 'NAME'
bpy.context.view_layer.objects.active = body; bpy.ops.object.modifier_apply(modifier='dt')
bpy.data.objects.remove(proxy)
for o in bpy.data.objects: o.select_set(o == body)
# Heat weights let the jacket flanks, the canteen and the bayonet follow the A-pose arms (which hang forward, close to the
# body). Below the shoulders, only what lies within a sleeve's radius of the arm bones keeps arm weights; the gloves
# (painted from the head sheets, bottom-right of the atlas) go to the hand bones outright.
G = {n: body.vertex_groups[n] for n in body.vertex_groups.keys()}
GI = {g.index: g.name for g in body.vertex_groups}
def segd(p, a, b):
    ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared)); return (p - (a + ab * t)).length, t
uvl = body.data.uv_layers[0].data
glove = set()
for poly in body.data.polygons:
    for li in poly.loop_indices:
        u = uvl[li].uv
        if u.x >= 0.5 and u.y < 0.5: glove.add(body.data.loops[li].vertex_index)
fixed = nh = 0
for v in body.data.vertices:
    p = v.co / S; side = 'Left' if p.x > 0 else 'Right'; sx = 1 if p.x > 0 else -1
    if p.z > 5.0 and v.index in glove: continue      # the face
    A_, E_, W_, T_ = [Vector((J[k][0] * sx, J[k][1], J[k][2])) for k in ('LeftArm', 'LeftForeArm', 'LeftHand', 'LeftHandEnd')]
    if v.index in glove and abs(p.x) > 0.9:
        for g in list(v.groups): body.vertex_groups[g.group].remove([v.index])
        G[f'mixamorig:{side}Hand'].add([v.index], 1.0, 'REPLACE'); nh += 1; continue
    if p.z >= 5.35 or abs(p.x) >= 0.85 + (5.35 - p.z) * (0.55 / 0.9) - 0.05: continue
    armish = [(g.group, g.weight) for g in v.groups if any(k in GI[g.group] for k in ('Arm', 'Hand', 'Shoulder'))]
    if not armish: continue
    for gi, w in armish: body.vertex_groups[gi].remove([v.index])
    if not any(g.weight > 0 for g in v.groups):
        z = p.z; G['mixamorig:' + ('Spine2' if z > 4.9 else 'Spine1' if z > 4.2 else 'Spine' if z > 3.8 else 'Hips')].add([v.index], 1.0, 'REPLACE')
    fixed += 1
print('arm weights fixed on', fixed, 'hand verts', nh)
bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
bpy.ops.object.vertex_group_normalize_all(lock_active=False)
body.parent = arm; am = body.modifiers.new('Armature', 'ARMATURE'); am.object = arm
print('unweighted', sum(1 for v in body.data.vertices if len(v.groups) == 0))

# arms to a T-pose, applied as rest
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='POSE')
def rot(pbn, frm, to):
    pb = arm.pose.bones[pbn]; h = pb.head.copy()
    R = frm.normalized().rotation_difference(to.normalized())
    pb.matrix = Matrix.Translation(h) @ R.to_matrix().to_4x4() @ Matrix.Translation(-h) @ pb.matrix
    bpy.context.view_layer.update()
PB = arm.pose.bones
for s, sx in (('Left', 1), ('Right', -1)):
    a = PB[f'mixamorig:{s}Arm']; f = PB[f'mixamorig:{s}ForeArm']; h = PB[f'mixamorig:{s}Hand']
    rot(a.name, f.head - a.head, Vector((sx, 0, 0)))
    rot(f.name, h.head - f.head, Vector((sx, 0, 0)))
    rot(h.name, h.tail - h.head, Vector((sx, 0, 0)))
bpy.ops.object.mode_set(mode='OBJECT')
bpy.context.view_layer.objects.active = body
bpy.ops.object.modifier_apply(modifier='Armature')
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='POSE')
bpy.ops.pose.select_all(action='SELECT'); bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode='OBJECT')
am = body.modifiers.new('Armature', 'ARMATURE'); am.object = arm
bpy.ops.wm.save_as_mainfile(filepath=f'/home/claude/ger/{NAME}.blend')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=f'{OUT}/{NAME}.fbx', use_selection=True, axis_forward='-Z', axis_up='Y',
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True, add_leaf_bones=False,
    mesh_smooth_type='FACE', object_types={'ARMATURE', 'MESH'}, use_armature_deform_only=False, bake_anim=False, path_mode='STRIP')
print('done', NAME)
