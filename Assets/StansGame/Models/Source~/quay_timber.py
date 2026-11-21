# Timber for the canal quays and the bridge pier, from the downloaded log (_downloads/pole):
#   TimberPile.fbx   a driven fender pile, 0.3 m thick, 6.5 m long, standing; origin at its TOP (set it at quay level)
#   TimberWale.fbx   a horizontal waling timber, 0.26 m, 4 m long along +Y; origin at its middle
# Run: python3 -I quay_timber.py -- <unzipped pole dir> <out dir> <tmp dir>
import bpy, sys, os, math, mathutils
from PIL import Image
P, OUT, TMP = sys.argv[sys.argv.index('--') + 1:][:3]
V = mathutils.Vector
os.makedirs(os.path.join(OUT, 'Props/SME'), exist_ok=True); os.makedirs(TMP, exist_ok=True)
def shrink(src, size, name):
    p = os.path.join(TMP, name); im = Image.open(src).convert('RGB'); im.thumbnail((size, size)); im.save(p); return p
def meshes(): return [o for o in bpy.context.scene.objects if o.type == 'MESH']
def bounds(o):
    vs = [v.co for v in o.data.vertices]
    return V([min(v[i] for v in vs) for i in range(3)]), V([max(v[i] for v in vs) for i in range(3)])
def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(P, 'model.glb'))
    m = bpy.data.materials.new('QuayTimber'); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']
    b.inputs['Roughness'].default_value = 0.92
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(shrink(os.path.join(P, 'tex_u0_v0_diffuse.jpg'), 1024, 'QuayTimber_d.png'))
    nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(shrink(os.path.join(P, 'tex_u0_v0_normal.jpg'), 1024, 'QuayTimber_n.png'))
    t.image.colorspace_settings.name = 'Non-Color'
    nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(t.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], b.inputs['Normal'])
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes(): o.select_set(True)
    bpy.context.view_layer.objects.active = meshes()[0]
    for o in meshes():
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    for x in [x for x in bpy.context.scene.objects if x.type != 'MESH']: bpy.data.objects.remove(x)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(meshes()) > 1: bpy.ops.object.join()
    o = meshes()[0]; o.data.materials.clear(); o.data.materials.append(m)
    d = o.modifiers.new('d', 'DECIMATE'); d.ratio = min(1.0, 1500 / len(o.data.polygons)); bpy.ops.object.modifier_apply(modifier='d')
    return o
def shape(o, thick, length):
    lo, hi = bounds(o); size = hi - lo
    la = max(range(3), key=lambda i: size[i])
    s = [thick / size[i] for i in range(3)]; s[la] = length / size[la]
    o.data.transform(mathutils.Matrix.Translation(-(lo + hi) / 2))
    o.data.transform(mathutils.Matrix.Diagonal((s[0], s[1], s[2], 1)))
    if la != 2: o.data.transform(mathutils.Matrix.Rotation(math.pi / 2, 4, 'X' if la == 1 else 'Y'))   # length along Z
def export(o, name):
    o.name = o.data.name = name
    lo, hi = bounds(o)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'Props/SME', name + '.fbx'), use_selection=False, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             path_mode='COPY', embed_textures=True, bake_anim=False, mesh_smooth_type='FACE')
    print('exported', name, tuple(round(x, 2) for x in hi - lo), tuple(round(x, 2) for x in lo), len(o.data.polygons), 'faces')

o = load(); shape(o, 0.3, 6.5)
lo, hi = bounds(o); o.data.transform(mathutils.Matrix.Translation(V((0, 0, -hi.z))))   # origin at the top
export(o, 'TimberPile')
o = load(); shape(o, 0.26, 4.0)
o.data.transform(mathutils.Matrix.Rotation(-math.pi / 2, 4, 'X'))                     # lying along +Y, centred
export(o, 'TimberWale')
