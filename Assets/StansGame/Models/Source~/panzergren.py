# Joan's CC0 Panzergrenadier (_downloads/_Characters/ww2-panzergrenadier): already skinned to a Mixamo skeleton in a T-pose,
# so it only needs clean material names, smaller textures and an FBX for Unity (German_Panzergren.fbx in Models/German).
# usage: python3 -I panzergren.py -- <outdir>
import bpy, sys, os
from PIL import Image
OUT = sys.argv[-1]
D = '/mnt/user-data/uploads/stansGameV2/_downloads/_Characters/ww2-panzergrenadier/'
bpy.ops.wm.open_mainfile(filepath=D + 'source/ww2panzergrenadier2kCC0.blend')
NAMES = {'Material.003': ('PzGren_Body', 'panzergren_low2k', 2048), 'helmetm42_low': ('PzGren_Helmet', 'helmetm42', 1024),
         'scrimwire_low': ('PzGren_Wire', 'scrimwirem42', 512), 'scrimleaves_low': ('PzGren_Leaves', 'scrimleaves1', 512),
         'ammopouchesgerman_low': ('PzGren_Pouches', 'ammopouchesgerman', 1024)}
for m in bpy.data.materials:
    if m.name not in NAMES: continue
    new, tex, px = NAMES[m.name]; m.name = new
    for kind, ext in (('diff', '.png'), ('norm', '.png')):
        f = D + 'textures/' + tex + '_' + kind + '.png'
        if not os.path.exists(f): continue
        im = Image.open(f); im.thumbnail((px, px))
        alpha = kind == 'diff' and im.mode == 'RGBA' and new in ('PzGren_Leaves',)
        if alpha: im.save(f'{OUT}/{new}.png', optimize=True)
        elif kind == 'diff': im.convert('RGB').save(f'{OUT}/{new}.jpg', quality=88)
        else: im.convert('RGB').resize((min(px, 1024),) * 2).save(f'{OUT}/{new}_N.jpg', quality=92)
for o in bpy.data.objects:
    if o.type == 'MESH':
        for p in o.data.polygons: p.use_smooth = True
# metres: 1.80 m to the top of the helmet, boots on the ground
a = bpy.data.objects['Armature']
dg = bpy.context.evaluated_depsgraph_get()
zs = [(o.matrix_world @ v.co).z for o in bpy.data.objects if o.type == 'MESH' for v in o.evaluated_get(dg).to_mesh().vertices]
k = 1.80 / (max(zs) - min(zs))
a.location.z -= min(zs); a.scale = (k, k, k); a.location *= k
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
print({o.name: len(o.data.polygons) for o in bpy.data.objects if o.type == 'MESH'})
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=f'{OUT}/German_Panzergren.fbx', use_selection=True, axis_forward='-Z', axis_up='Y',
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
    bake_anim=False, use_armature_deform_only=True, path_mode='STRIP', mesh_smooth_type='FACE')
