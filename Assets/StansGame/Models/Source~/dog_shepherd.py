# German shepherd (_downloads/german-shepherd-rig, rigged "Wolf" armature, no animation) -> FBX for Unity with three
# hand-keyed loops: Idle (panting, looking about, tail), Walk (four-beat) and Trot (diagonal pairs).
import bpy, sys, math
from mathutils import Vector, Quaternion, Matrix
SRC, TEXDIR, OUT = sys.argv[-3:]
bpy.ops.wm.open_mainfile(filepath=SRC)
for o in list(bpy.data.objects):
    if o.type in ('CAMERA', 'LIGHT'): bpy.data.objects.remove(o)
arm = bpy.data.objects['Wolf.001']
# bone-parented meshes (mouth, teeth, tongue) -> skinned to that bone, so the FBX carries them cleanly
for o in [o for o in bpy.data.objects if o.type == 'MESH' and o.parent_type == 'BONE']:
    bone = o.parent_bone; mw = o.matrix_world.copy()
    o.parent = arm; o.parent_type = 'OBJECT'; o.matrix_world = mw
    g = o.vertex_groups.new(name=bone); g.add([v.index for v in o.data.vertices], 1.0, 'REPLACE')
    if not any(m.type == 'ARMATURE' for m in o.modifiers):
        m = o.modifiers.new('Armature', 'ARMATURE'); m.object = arm
# materials -> the four textures of the download (flat images, Unity sets the hair to alpha cut-out)
TEX = {'body': '-cger_shepherd_body_org1_col.png', 'head': '-cger_shepherd_head_org1_col.png', 'hair': '-cger_shepherd_hair_org1_col.png', 'mouth': '-cger_shepherd_mouth_org1_col.png'}
for m in bpy.data.materials:
    n = m.name.lower()
    key = 'hair' if 'hair' in n else 'mouth' if 'mouth' in n else 'head' if 'head' in n else 'body' if 'body' in n else None
    if not key or not m.use_nodes: continue
    m.name = 'Dog_' + key.capitalize()
    for nd in list(m.node_tree.nodes):
        if nd.type == 'TEX_IMAGE': m.node_tree.nodes.remove(nd)
    b = [nd for nd in m.node_tree.nodes if nd.type == 'BSDF_PRINCIPLED']
    if not b: continue
    t = m.node_tree.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(f'{TEXDIR}/{TEX[key]}')
    m.node_tree.links.new(t.outputs['Color'], b[0].inputs['Base Color'])
    if key == 'hair': m.node_tree.links.new(t.outputs['Alpha'], b[0].inputs['Alpha'])

# ---- animation, keyed in armature space: X = the dog's side axis (swing fore/aft), Z = up (wag, look about)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='POSE')
PB = arm.pose.bones
for pb in PB: pb.rotation_mode = 'QUATERNION'
def rot(name, axis, deg):
    """rest-relative rotation of a bone about an armature-space axis"""
    pb = PB[name]; ax = pb.bone.matrix_local.to_3x3().inverted() @ Vector(axis)
    return Quaternion(ax.normalized(), math.radians(deg))
def key(frame, poses, loc=None):
    for pb in PB: pb.rotation_quaternion = Quaternion()
    acc = {}
    for name, axis, deg in poses: acc[name] = rot(name, axis, deg) @ acc.get(name, Quaternion())
    for name, q in acc.items(): PB[name].rotation_quaternion = q
    PB['Root'].location = loc or Vector()
    for pb in PB:
        pb.keyframe_insert('rotation_quaternion', frame=frame)
    PB['Root'].keyframe_insert('location', frame=frame)
X, Z = (1, 0, 0), (0, 0, 1)
TAIL = ['spine.003', 'spine.002', 'spine.001', 'spine']
def legs(t, phase, amp, lift):
    out = []
    for leg, ph in phase.items():
        a = 2 * math.pi * (t + ph); s = math.sin(a); c = math.cos(a)
        swing = max(0.0, c)                                   # leg coming forward: paw lifted
        if leg.startswith('front'):
            side = leg.split('.')[1]
            out += [(f'front_thigh.{side}', X, -amp * s), (f'front_shin.{side}', X, lift * swing), (f'front_foot.{side}', X, lift * 0.8 * swing)]
        else:
            side = leg.split('.')[1]
            out += [(f'thigh.{side}', X, -amp * s), (f'shin.{side}', X, -lift * 0.7 * swing), (f'foot.{side}', X, lift * 0.9 * swing)]
    return out
def make(name, frames, fn):
    act = bpy.data.actions.new(name); arm.animation_data_create(); arm.animation_data.action = act
    for f in range(frames + 1): key(f, *fn(f / frames))
    return act
FPS = 30; bpy.context.scene.render.fps = FPS
walk = {'front.L': 0.25, 'front.R': 0.75, 'hind.L': 0.0, 'hind.R': 0.5}
trot = {'front.L': 0.0, 'front.R': 0.5, 'hind.L': 0.5, 'hind.R': 0.0}
def tail(t, amp, speed=1): return [(b, Z, amp * (0.6 + i * 0.2) * math.sin(2 * math.pi * (t * speed - i * 0.08))) for i, b in enumerate(TAIL)] + [('spine.003', X, -12)]
def f_walk(t):
    p = legs(t, walk, 20, 30) + tail(t, 10, 1) + [('spine.010', X, 3 * math.sin(4 * math.pi * t)), ('Head', X, 2 * math.sin(4 * math.pi * t + 1))]
    return p, Vector((0, 0, 0.04 * math.sin(4 * math.pi * t)))
def f_trot(t):
    p = legs(t, trot, 30, 45) + tail(t, 6, 1) + [('spine.010', X, -6), ('Jaw', X, 10 + 4 * math.sin(4 * math.pi * t))]
    return p, Vector((0, 0, 0.09 * math.sin(4 * math.pi * t)))
def f_idle(t):
    look = 14 * math.sin(2 * math.pi * t) + 6 * math.sin(6 * math.pi * t)
    p = [('spine.010', Z, look * 0.5), ('Head', Z, look * 0.6), ('Head', X, 4 * math.sin(4 * math.pi * t)),
         ('Jaw', X, 12 + 5 * math.sin(2 * math.pi * t * 12)),                         # panting
         ('Ear_L', X, 8 * max(0, math.sin(2 * math.pi * t * 3))), ('Ear_R', X, 8 * max(0, math.sin(2 * math.pi * t * 3 + 2)))]
    p += tail(t, 8, 2)
    return p, Vector((0, 0, 0.02 * math.sin(2 * math.pi * t * 6)))   # breathing
acts = [make('Idle', 120, f_idle), make('Walk', 30, f_walk), make('Trot', 16, f_trot)]
for pb in PB: pb.rotation_quaternion = Quaternion()
PB['Root'].location = Vector()
bpy.ops.object.mode_set(mode='OBJECT')
arm.animation_data.action = None
for a in acts:   # FBX export with all actions: each becomes a take
    tr = arm.animation_data.nla_tracks.new(); tr.strips.new(a.name, 0, a); tr.mute = True
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    if o.type in ('ARMATURE', 'MESH'): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'ARMATURE', 'MESH'}, axis_forward='-Z', axis_up='Y',
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', add_leaf_bones=False, mesh_smooth_type='FACE',
                         bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                         bake_anim_simplify_factor=0.0, path_mode='STRIP')
for im in list(bpy.data.images):
    if im.filepath.endswith('.dds') or im.name == 'Render Result': bpy.data.images.remove(im)
bpy.ops.wm.save_as_mainfile(filepath='/home/claude/x/dog_out.blend')
print('done', [a.name for a in acts])
