# British_Infantry (Tripo, rigged by tripo_rig.py): the arms bent like rubber hoses (printscreens 21, 33) because the
# shoulder (clavicle) bone held most of the upper arm and the elbow blend ran half the length of the arm. Re-weighted
# rigidly along each arm (T-pose, arms along x): clavicle -> upper arm in 9 cm at the shoulder joint, upper arm ->
# forearm in 7 cm at the elbow, forearm -> hand in 5 cm at the wrist. Everything else is left as it was.
# usage: python3 -I arms_fix.py -- <in fbx> <out fbx>
import bpy, sys
from mathutils import Vector
SRC, OUT = sys.argv[-2], sys.argv[-1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC); bpy.context.view_layer.update()
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
body = [o for o in bpy.data.objects if o.type == 'MESH'][0]
def head(b): return arm.matrix_world @ arm.data.bones['mixamorig:' + b].head_local
def smooth(t): t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)
changed = 0
for side in ('Right', 'Left'):
    sgn = -1 if head(side + 'Arm').x < 0 else 1
    js, je, jw = head(side + 'Arm').x * sgn, head(side + 'ForeArm').x * sgn, head(side + 'Hand').x * sgn
    names = [side + n for n in ('Shoulder', 'Arm', 'ForeArm', 'Hand')]
    groups = {n: body.vertex_groups.get('mixamorig:' + n) or body.vertex_groups.new(name='mixamorig:' + n) for n in names}
    gidx = {g.index for g in groups.values()}
    for v in body.data.vertices:
        if not any(g.group in gidx and g.weight > 0.05 for g in v.groups): continue
        x = (body.matrix_world @ v.co).x * sgn
        if x < js - 0.05: continue          # the chest and back: as they were
        # weights along the chain: shoulder | upper arm | forearm | hand
        a = smooth((x - (js - 0.05)) / 0.09)          # clavicle -> upper arm
        b = smooth((x - (je - 0.035)) / 0.07)         # upper arm -> forearm
        c = smooth((x - (jw - 0.025)) / 0.05)         # forearm -> hand
        w = {side + 'Shoulder': 1 - a, side + 'Arm': a * (1 - b), side + 'ForeArm': a * b * (1 - c), side + 'Hand': a * b * c}
        # whatever else it was weighted to (spine, neck) goes: an arm vertex follows the arm
        for g in list(v.groups):
            body.vertex_groups[g.group].remove([v.index])
        for n, wt in w.items():
            if wt > 1e-3: groups[n].add([v.index], wt, 'REPLACE')
        changed += 1
print('re-weighted', changed)
bpy.ops.object.select_all(action='SELECT')
# the same export as tripo_rig.py: UnitScaleFactor 1 (FBX_SCALE_UNITS wrote 100 and the body vanished in Unity, printscreen 36)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                         add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False, mesh_smooth_type='FACE')
