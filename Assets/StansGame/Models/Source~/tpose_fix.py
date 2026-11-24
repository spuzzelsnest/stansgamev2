# Level template 4a: every character is imported standing in the T-pose (arms straight out, palms down, legs straight).
# The older Mixamo characters (British_Para, British_Officer, German_Panzergren, GermanSoldier) were rigged in an
# A-pose, arms ~45 deg down. This raises each upper arm about the shoulder to horizontal (keeping it pointing outward
# the way it did), bends the skin with it, and makes that the rest pose. Everything else is left as it was.
# usage: python3 -I tpose_fix.py -- <in fbx> <out fbx>
import bpy, sys, math
from mathutils import Vector, Matrix
SRC, OUT = sys.argv[-2], sys.argv[-1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC, use_anim=False); bpy.context.view_layer.update()
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
mw = arm.matrix_world
def bone(sfx): return next(b for b in arm.data.bones if b.name.endswith(sfx))
up = (mw @ bone('Head').head_local - mw @ bone('Hips').head_local).normalized()
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='POSE')
for side in ('LeftArm', 'RightArm'):
    b = bone(side); pb = arm.pose.bones[b.name]
    child = b.children[0]
    d = (mw @ child.head_local - mw @ b.head_local).normalized()
    flat = (d - up * d.dot(up)).normalized()
    before = math.degrees(d.angle(flat))
    rot = d.rotation_difference(flat).to_matrix().to_4x4()
    h = mw @ b.head_local
    world = mw @ pb.matrix
    world = Matrix.Translation(h) @ rot @ Matrix.Translation(-h) @ world
    pb.matrix = mw.inverted() @ world
    bpy.context.view_layer.update()
    print(side, 'raised %.1f deg' % before)
bpy.ops.object.mode_set(mode='OBJECT')
# the skin follows, then the new pose becomes the rest pose
for o in bpy.data.objects:
    if o.type != 'MESH': continue
    mods = [m for m in o.modifiers if m.type == 'ARMATURE']
    if not mods: continue
    if o.data.shape_keys: print('WARNING shape keys on', o.name)
    bpy.context.view_layer.objects.active = o
    name = mods[0].name
    bpy.ops.object.modifier_copy(modifier=name)
    bpy.ops.object.modifier_apply(modifier=name)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='POSE'); bpy.ops.pose.armature_apply(selected=False); bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                         add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False, mesh_smooth_type='FACE')
