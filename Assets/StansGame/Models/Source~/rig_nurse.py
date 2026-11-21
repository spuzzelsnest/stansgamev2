# The nurse (_downloads/nurse, "Anya Nurse.fbx": a rigged game character, cm, facing +X, A-pose, lower-case Mixamo-like
# bone names) made ready for Unity like Models/Civilian/French_Civilian.fbx: metres, facing -Y, bones renamed
# "mixamorig:<Name>" (CivilianBones in PegasusLevelBuilder.Civilians.cs), arms raised to a T-pose and applied as rest.
# Materials named for the builder (textures assigned in Unity). usage: python3 -I rig_nurse.py <in.fbx> <out.fbx>
import bpy, math, sys
from mathutils import Vector, Matrix
SRC, OUT = sys.argv[-2], sys.argv[-1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
# the naked body underneath is only needed where skin shows; keep it (hands, arms, legs)
# scale cm -> m and turn to face -Y, applied
for o in bpy.data.objects: o.select_set(True)
bpy.context.view_layer.objects.active = arm
arm.scale = (arm.scale[0] * 0.01,) * 3 if abs(arm.dimensions.z) > 10 else arm.scale
arm.rotation_euler.z += math.radians(-90)
bpy.context.view_layer.update()
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
print('height', max((o.matrix_world @ Vector(c)).z for o in meshes for c in o.bound_box))
NAMES = {'hips': 'Hips', 'spine': 'Spine', 'spine1': 'Spine1', 'spine2': 'Spine2', 'neck': 'Neck', 'head': 'Head'}
for s, S in (('left', 'Left'), ('right', 'Right')):
    for a, b in (('shoulder', 'Shoulder'), ('arm', 'Arm'), ('forearm', 'ForeArm'), ('hand', 'Hand'), ('upleg', 'UpLeg'), ('leg', 'Leg'), ('foot', 'Foot'), ('toebase', 'ToeBase')):
        NAMES[s + a] = S + b
for b in arm.data.bones:
    if b.name in NAMES: b.name = 'mixamorig:' + NAMES[b.name]
# arms out to a T-pose (horizontal, sideways), then make it the rest pose
bpy.ops.object.mode_set(mode='POSE')
P = arm.pose.bones
def rot(name, frm, to):
    pb = P[name]; h = pb.head.copy()
    R = frm.normalized().rotation_difference(to.normalized())
    pb.matrix = Matrix.Translation(h) @ R.to_matrix().to_4x4() @ Matrix.Translation(-h) @ pb.matrix
    bpy.context.view_layer.update()
for S, sx in (('Left', 1), ('Right', -1)):
    a = P[f'mixamorig:{S}Arm']; f = P[f'mixamorig:{S}ForeArm']; h = P[f'mixamorig:{S}Hand']
    side = Vector((sx, 0, 0)) if (a.head.x * sx) > 0 else Vector((-sx, 0, 0))
    rot(a.name, f.head - a.head, side)
    rot(f.name, h.head - f.head, side)
bpy.ops.object.mode_set(mode='OBJECT')
for o in meshes:
    mod = next((m for m in o.modifiers if m.type == 'ARMATURE'), None)
    if not mod: continue
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='POSE')
bpy.ops.pose.select_all(action='SELECT'); bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode='OBJECT')
for o in meshes:
    m = o.modifiers.new('Armature', 'ARMATURE'); m.object = arm
# materials named for the builder
REN = {'eye_lashes': 'Nurse_Lashes', 'parralaxeyes': 'Nurse_Eyes', 'head': 'Nurse_Head', 'hair': 'Nurse_Hair', 'nurse_body': 'Nurse_Dress', 'nakedbody': 'Nurse_Skin'}
for o in meshes:
    for s in o.material_slots:
        if not s.material: continue
        for k, v in REN.items():
            if k in s.material.name:
                m = bpy.data.materials.get(v) or bpy.data.materials.new(v); s.material = m; break
    o.name = o.material_slots[0].material.name.replace('Nurse_', 'Nurse ') if o.material_slots else o.name
arm.name = 'Nurse'
la = arm.data.bones['mixamorig:LeftArm']; print('left arm', la.head_local, la.tail_local, 'left foot', arm.data.bones['mixamorig:LeftFoot'].head_local)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
    bake_space_transform=True, add_leaf_bones=False, mesh_smooth_type='FACE', object_types={'ARMATURE', 'MESH'},
    use_armature_deform_only=False, bake_anim=False, path_mode='STRIP')
