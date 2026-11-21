# Joan's "mortars" download is one file with three Indonesian Pindad mortars (post-war designs). Split into three props,
# each standing in for the closest WW2 type (all three are Brandt-pattern muzzle-loaders, like the 1944 ones):
#   Mo-3 81 mm (bipod)         -> Mortar81.fbx         US M1 81 mm / German 8 cm GrW 34 / British ML 3-inch
#   Mo-2 60 mm LR (bipod)      -> Mortar60.fbx         US M2 60 mm (company mortar of the 505th)
#   Mo-1 60 mm Commando (tube) -> Mortar60Commando.fbx British ML 2-inch (platoon mortar, D Coy Ox & Bucks at Pegasus Bridge)
# Blender X east, Y = the way it fires, Z up; base centred on the origin.
# Run: python3 -I mortars_split.py -- <downloads dir> <out dir> <tmp dir>
import bpy, sys, os, math, mathutils
from PIL import Image
D, OUT, TMP = sys.argv[sys.argv.index('--') + 1:][:3]
V = mathutils.Vector
os.makedirs(TMP, exist_ok=True); os.makedirs(os.path.join(OUT, 'Props/SME'), exist_ok=True)
MT = os.path.join(D, 'mortars/textures')
def meshes(): return [o for o in bpy.context.scene.objects if o.type == 'MESH']
def shrink(src, size, name):
    p = os.path.join(TMP, name); im = Image.open(src).convert('RGB'); im.thumbnail((size, size)); im.save(p); return p
def tex_material(mat, base, normal):
    mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); b = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(b.outputs['BSDF'], out.inputs['Surface']); b.inputs['Metallic'].default_value = 0.4; b.inputs['Roughness'].default_value = 0.55
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(base); nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(normal); t.image.colorspace_settings.name = 'Non-Color'
    nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(t.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], b.inputs['Normal'])
TEX = {  # material name in the source -> texture prefix
    'Pindad Mo-1 Kal. 60 mm': 'Pindad Mo-1 Kal. 60 mm CO_Pindad Mo-1 Kal. 60 mm',
    'Base and Mortar 2': 'Pindad Mo-3 Kal. 81 mm_Base and Mortar', 'Small Parts 2': 'Pindad Mo-3 Kal. 81 mm_Small Parts',
    'Base and Mortar': 'Pindad Mo-2 Kal. 60 mm LR_Base and Mortar', 'Small Parts': 'Pindad Mo-2 Kal. 60 mm LR_Small Parts',
}
JOBS = [('Mortar81', {'Base', 'Mortar', 'Pindad Mo-3 Kal. 81 mm'}),
        ('Mortar60', {'Pindad Mo-2 Kal. 60 mm LR'}),
        ('Mortar60Commando', {'Pindad Mo-1 Kal. 60 mm CO'})]
for name, keep in JOBS:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(D, 'mortars/source/Pindad Mortars Sketchfab.fbx'))
    bpy.context.view_layer.update()
    for o in list(meshes()):
        if o.name not in keep: bpy.data.objects.remove(o)
    for o in meshes():
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    for o in [o for o in bpy.context.scene.objects if o.type != 'MESH']: bpy.data.objects.remove(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes(): o.select_set(True)
    bpy.context.view_layer.objects.active = meshes()[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(meshes()) > 1: bpy.ops.object.join()
    o = meshes()[0]; o.name = o.data.name = name
    for m in o.data.materials:
        key = m.name[:-4] if m.name[-4:-3] == '.' and m.name[-3:].isdigit() else m.name
        prefix = TEX[key]; tag = name + '_' + ('Small' if 'Small' in key else 'Main')
        tex_material(m, shrink(f'{MT}/{prefix}_BaseColor.png', 1024, tag + '_d.png'), shrink(f'{MT}/{prefix}_Normal.png', 1024, tag + '_n.png'))
        m.name = tag
    o.data.transform(mathutils.Matrix.Rotation(math.pi, 4, 'Z'))     # the tubes lean towards -Y in the source
    vs = [v.co for v in o.data.vertices]
    lo = V([min(v[i] for v in vs) for i in range(3)]); hi = V([max(v[i] for v in vs) for i in range(3)])
    o.data.transform(mathutils.Matrix.Translation(V((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))))
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'Props/SME', name + '.fbx'), use_selection=False, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             path_mode='COPY', embed_textures=True, bake_anim=False, mesh_smooth_type='FACE')
    print('exported', name, tuple(round(x, 2) for x in hi - lo), len(o.data.polygons), 'faces')
