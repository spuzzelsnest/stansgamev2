# Download batch 6: Models/Props/SME/
#   Checkpoint.fbx       the checkpoint barrier (boom on two stands), its STOP sign repainted as a German "HALT!" plate
#   BorderPost.fbx       the black-and-white striped post (photoscan, decimated), 1.3 m
#   TrenchSpade.fbx      the German entrenching tool, 0.6 m
#   AntiGliderPole.fbx   the log as a "Rommelspargel" anti-glider pole, 3.6 m, standing (origin at its foot)
#   AntiGliderPoleLying.fbx  the same lying down along +Y (origin under its middle)
# Origins on the ground, Y forward (as the other Models/Props/SME files).
# Run: python3 -I downloads_batch6.py -- <downloads dir> <work dir with unzipped pole/ and post/> <checkpoint atlas png> <out dir> <tmp dir>
import bpy, sys, os, math, mathutils
from PIL import Image
D, WK, ATLAS, OUT, TMP = sys.argv[sys.argv.index('--') + 1:][:5]
V = mathutils.Vector
os.makedirs(os.path.join(OUT, 'Props/SME'), exist_ok=True); os.makedirs(TMP, exist_ok=True)
Image.MAX_IMAGE_PIXELS = None
def shrink(src, size, name):
    p = os.path.join(TMP, name); im = Image.open(src).convert('RGB'); im.thumbnail((size, size)); im.save(p); return p
def tex_mat(name, base, normal=None, rough=0.8, metal=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']
    b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(base); nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    if normal:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(normal); t.image.colorspace_settings.name = 'Non-Color'
        nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(t.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], b.inputs['Normal'])
    return m
def meshes(): return [o for o in bpy.context.scene.objects if o.type == 'MESH']
def join(name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes(): o.select_set(True)
    bpy.context.view_layer.objects.active = meshes()[0]
    bpy.ops.object.convert(target='MESH')
    for o in meshes():
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    for x in [x for x in bpy.context.scene.objects if x.type != 'MESH']: bpy.data.objects.remove(x)
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes(): o.select_set(True)
    bpy.context.view_layer.objects.active = meshes()[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(meshes()) > 1: bpy.ops.object.join()
    o = meshes()[0]; o.name = o.data.name = name; return o
def decimate(o, faces):
    if len(o.data.polygons) <= faces: return
    m = o.modifiers.new('d', 'DECIMATE'); m.ratio = faces / len(o.data.polygons)
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier='d')
def bounds(o):
    vs = [v.co for v in o.data.vertices]
    return V([min(v[i] for v in vs) for i in range(3)]), V([max(v[i] for v in vs) for i in range(3)])
def ground(o, scale=None, height=None):
    lo, hi = bounds(o)
    s = scale if scale else (height / (hi.z - lo.z))
    o.data.transform(mathutils.Matrix.Translation(-V(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))))
    o.data.transform(mathutils.Matrix.Scale(s, 4))
def export(o, name):
    lo, hi = bounds(o)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'Props/SME', name + '.fbx'), use_selection=False, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             path_mode='COPY', embed_textures=True, bake_anim=False, mesh_smooth_type='FACE')
    print('exported', name, tuple(round(x, 2) for x in hi - lo), len(o.data.polygons), 'faces')

# ---- checkpoint barrier (drop the baked shadow planes)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(D, 'checkpoint-barrier/source/checkpoint.fbx'))
for o in list(bpy.context.scene.objects):
    if o.type == 'MESH' and o.name.lower().startswith('shadow'): bpy.data.objects.remove(o)
m = tex_mat('Checkpoint_1944', shrink(ATLAS, 1024, 'Checkpoint_1944_d.png'), rough=0.7)
for o in meshes(): o.data.materials.clear(); o.data.materials.append(m)
o = join('Checkpoint'); ground(o, scale=1.0); export(o, 'Checkpoint')

# ---- striped post (photoscan: decimate, 1.3 m)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=os.path.join(WK, 'post/model.obj'))
m = tex_mat('BorderPost', shrink(os.path.join(WK, 'post/tex_u1_v1.jpg'), 2048, 'BorderPost_d.png'), rough=0.85)
for o in meshes(): o.data.materials.clear(); o.data.materials.append(m)
o = join('BorderPost'); decimate(o, 24000); ground(o, height=1.3); export(o, 'BorderPost')

# ---- entrenching tool, 0.6 m
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(D, 'German-trench-shovel-asset/source/Shovel.fbx'))
T = os.path.join(D, 'German-trench-shovel-asset/textures')
m = tex_mat('TrenchSpade', shrink(f'{T}/Shovel_Base_Color.png', 1024, 'TrenchSpade_d.png'), rough=0.6, metal=0.3)
for o in meshes(): o.data.materials.clear(); o.data.materials.append(m)
o = join('TrenchSpade')
lo, hi = bounds(o)
long_axis = max(range(3), key=lambda i: hi[i] - lo[i])
if long_axis != 2: o.data.transform(mathutils.Matrix.Rotation(math.pi / 2, 4, 'X' if long_axis == 1 else 'Y'))
ground(o, height=0.6); export(o, 'TrenchSpade')

# ---- the log as an anti-glider pole: 3.6 m long, 0.22 m thick
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(WK, 'pole/model.glb'))
m = tex_mat('AntiGliderPole', shrink(os.path.join(WK, 'pole/tex_u0_v0_diffuse.jpg'), 1024, 'AntiGliderPole_d.png'),
            shrink(os.path.join(WK, 'pole/tex_u0_v0_normal.jpg'), 1024, 'AntiGliderPole_n.png'), rough=0.9)
for o in meshes(): o.data.materials.clear(); o.data.materials.append(m)
o = join('AntiGliderPole'); decimate(o, 1500)
lo, hi = bounds(o); size = hi - lo
la = max(range(3), key=lambda i: size[i])
s = [0.22 / size[i] for i in range(3)]; s[la] = 3.6 / size[la]
o.data.transform(mathutils.Matrix.Translation(-(lo + hi) / 2))
o.data.transform(mathutils.Matrix.Diagonal((s[0], s[1], s[2], 1)))
if la != 2: o.data.transform(mathutils.Matrix.Rotation(math.pi / 2, 4, 'X' if la == 1 else 'Y'))   # stand it up
lo, hi = bounds(o); o.data.transform(mathutils.Matrix.Translation(V((0, 0, -lo.z))))
export(o, 'AntiGliderPole')
o.data.transform(mathutils.Matrix.Translation(V((0, 0, -1.8))))
o.data.transform(mathutils.Matrix.Rotation(-math.pi / 2, 4, 'X'))                                     # lying along +Y
lo, hi = bounds(o); o.data.transform(mathutils.Matrix.Translation(V((0, 0, -lo.z))))
o.name = o.data.name = 'AntiGliderPoleLying'; export(o, 'AntiGliderPoleLying')
