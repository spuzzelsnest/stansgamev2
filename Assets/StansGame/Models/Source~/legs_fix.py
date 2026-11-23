# German_Heer_C (printscreen 41): he walked with both legs forward together - the left leg's skin was weighted mostly
# to the RIGHT thigh and neither shin bone held anything. Re-weighted rigidly along each leg by side (x) and height:
# hips -> thigh in 8 cm at the hip joint, thigh -> shin in 8 cm at the knee, shin -> foot in 5 cm at the ankle; toes
# and everything above the hips left as they were. Vertices in the middle (the cape between the legs) are shared.
# usage: python3 -I legs_fix.py -- <in fbx> <out fbx>
import bpy, sys
SRC, OUT = sys.argv[-2], sys.argv[-1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC); bpy.context.view_layer.update()
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
body = [o for o in bpy.data.objects if o.type == 'MESH'][0]
def head(b): return arm.matrix_world @ arm.data.bones['mixamorig:' + b].head_local
def smooth(t): t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)
G = lambda n: body.vertex_groups.get('mixamorig:' + n) or body.vertex_groups.new(name='mixamorig:' + n)
legset = {'Hips', 'LeftUpLeg', 'RightUpLeg', 'LeftLeg', 'RightLeg', 'LeftFoot', 'RightFoot'}
names = {g.index: g.name.replace('mixamorig:', '') for g in body.vertex_groups}
hipZ = head('LeftUpLeg').z; kneeZ = head('LeftLeg').z; ankleZ = head('LeftFoot').z
changed = 0
for v in body.data.vertices:
    if not v.groups: continue
    top = max(v.groups, key=lambda g: g.weight)
    if names.get(top.group) not in legset: continue
    p = body.matrix_world @ v.co
    if p.z > hipZ + 0.06: continue
    def chain(side):
        a = smooth((hipZ + 0.04 - p.z) / 0.08)            # hips -> thigh
        b = smooth((kneeZ + 0.04 - p.z) / 0.08)           # thigh -> shin
        c = smooth((ankleZ + 0.025 - p.z) / 0.05)         # shin -> foot
        return {'Hips': 1 - a, side + 'UpLeg': a * (1 - b), side + 'Leg': a * b * (1 - c), side + 'Foot': a * b * c}
    s = smooth((p.x + 0.03) / 0.06)                       # 0 = right side (x < 0), 1 = left
    w = {}
    for side, k in (('Left', s), ('Right', 1 - s)):
        if k <= 0: continue
        for n, wt in chain(side).items(): w[n] = w.get(n, 0) + wt * k
    for g in list(v.groups): body.vertex_groups[g.group].remove([v.index])
    for n, wt in w.items():
        if wt > 1e-3: G(n).add([v.index], wt, 'REPLACE')
    changed += 1
print('re-weighted', changed)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                         add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False, mesh_smooth_type='FACE')
