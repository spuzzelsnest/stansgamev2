# Download batch 5 for Pegasus Bridge: a German field bunker, a garden shed and a small house from Bénouville.
#   FieldBunker.fbx     the ww2-field-bunker (Collada, centimetres; converted with trimesh to bunker.glb first):
#                       6.35 x 10.8 m with its entrance ramp, room 5.1 x 5.2 m and 2.4 m high inside, a firing slit
#                       in each wall. Origin: middle of the room at ground level; +Y = the entrance side.
#   GardenShed.fbx      the shed: 1.7 x 2.2 m, 2 m high. Origin: middle of the floor; +Y = the door side.
#   BenouvilleHouse.fbx the small house without its display plinth: 11.7 x 7.6 m, 9 m to the chimneys.
#                       Origin: middle of the footprint at ground level; +Y = the street front.
# Blender X right, Y forward, Z up (as the other Models/Props/SME files).
# Run: python3 -I downloads_batch5.py -- <downloads dir> <bunker.glb> <shed .blend> <out dir> <tmp dir>
import bpy, sys, os, math, mathutils
from PIL import Image
D, GLB, SHED, OUT, TMP = sys.argv[sys.argv.index('--') + 1:][:5]
V = mathutils.Vector
os.makedirs(TMP, exist_ok=True); os.makedirs(os.path.join(OUT, 'Props/SME'), exist_ok=True)
Image.MAX_IMAGE_PIXELS = None
def shrink(src, size, name, mode='RGB'):
    p = os.path.join(TMP, name); im = Image.open(src).convert(mode); im.thumbnail((size, size)); im.save(p); return p
def tex_mat(name, base, normal=None, metal=0.0, rough=0.85):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree
    b = nt.nodes['Principled BSDF']; b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(base); nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    if normal:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(normal); t.image.colorspace_settings.name = 'Non-Color'
        nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(t.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], b.inputs['Normal'])
    return m
def meshes(): return [o for o in bpy.context.scene.objects if o.type == 'MESH']
def join_all(name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes(): o.select_set(True)
    bpy.context.view_layer.objects.active = meshes()[0]
    bpy.ops.object.convert(target='MESH')
    for o in meshes():
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes(): o.select_set(True)
    bpy.context.view_layer.objects.active = meshes()[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(meshes()) > 1: bpy.ops.object.join()
    o = meshes()[0]; o.name = o.data.name = name
    for x in [x for x in bpy.context.scene.objects if x.type != 'MESH']: bpy.data.objects.remove(x)
    return o
def place(o, centre, scale=1.0, turn=0.0):
    o.data.transform(mathutils.Matrix.Translation(-centre))
    o.data.transform(mathutils.Matrix.Rotation(turn, 4, 'Z') @ mathutils.Matrix.Scale(scale, 4))
def export(o, name):
    vs = [v.co for v in o.data.vertices]
    lo = V([min(v[i] for v in vs) for i in range(3)]); hi = V([max(v[i] for v in vs) for i in range(3)])
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'Props/SME', name + '.fbx'), use_selection=False, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             path_mode='COPY', embed_textures=True, bake_anim=False, mesh_smooth_type='FACE')
    print('exported', name, tuple(round(x, 2) for x in lo), tuple(round(x, 2) for x in hi), len(o.data.polygons), 'faces')

# ---------------------------------------------------------------- field bunker
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
T = os.path.join(D, 'ww2-field-bunker/textures')
concrete = tex_mat('Bunker_Concrete', shrink(f'{T}/concrete_albedo.jpeg', 1024, 'Bunker_Concrete_d.png'), shrink(f'{T}/concrete_normal.png', 1024, 'Bunker_Concrete_n.png'))
interior = tex_mat('Bunker_Interior', shrink(f'{T}/interior_albedo.jpeg', 1024, 'Bunker_Interior_d.png'), shrink(f'{T}/interior_normal.png', 1024, 'Bunker_Interior_n.png'))
for o in meshes():
    o.data.materials.clear(); o.data.materials.append(interior if o.name == 'meshId1' else concrete)
b = join_all('FieldBunker')
place(b, V((0.0, -82.0, 0.98)), 0.01)            # centimetres -> metres, middle of the room
export(b, 'FieldBunker')

# ---------------------------------------------------------------- garden shed
bpy.ops.wm.open_mainfile(filepath=SHED)
# the roof and walls are mirrored about the "Mirror" object: apply the modifiers first, then drop it
bpy.ops.object.select_all(action='DESELECT')
for o in meshes(): o.select_set(True)
bpy.context.view_layer.objects.active = meshes()[0]
bpy.ops.object.convert(target='MESH')
for o in list(bpy.context.scene.objects):
    if o.type != 'MESH' or o.name == 'Mirror': bpy.data.objects.remove(o)
T = os.path.join(D, 'shed/textures')
wood = tex_mat('Shed_Wood', shrink(f'{T}/TexturesCom_WoodSiding5_2x2_1K_albedo.png', 1024, 'Shed_Wood_d.png'))
felt = tex_mat('Shed_Felt', shrink(f'{T}/TexturesCom_Asphalt6_1x1_1K_albedo.png', 512, 'Shed_Felt_d.png'))
for o in meshes():
    for i, m in enumerate(o.data.materials):
        o.data.materials[i] = felt if m and m.name == 'Felt' else wood
s = join_all('GardenShed')
vs = [v.co for v in s.data.vertices]
place(s, V(((min(v.x for v in vs) + max(v.x for v in vs)) / 2, (min(v.y for v in vs) + max(v.y for v in vs)) / 2, min(v.z for v in vs))))
export(s, 'GardenShed')

# ---------------------------------------------------------------- Bénouville house (without the display plinth)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(D, 'small-house-in-benouville-for-pegasus-bridge-map/source/BenouvilleSmallHouse.fbx'))
for o in list(bpy.context.scene.objects):
    if o.type != 'MESH' or o.name != 'LowPolyHouse': bpy.data.objects.remove(o)
T = os.path.join(D, 'small-house-in-benouville-for-pegasus-bridge-map/textures')
ext = tex_mat('Benouville_Exterior', shrink(f'{T}/newMesh_Exterior_BaseColor.png', 2048, 'Benouville_Exterior_d.png'))
inn = tex_mat('Benouville_Interior', shrink(f'{T}/newMesh_Interior_BaseColor.png', 1024, 'Benouville_Interior_d.png'))
o = meshes()[0]
for i, m in enumerate(o.data.materials): o.data.materials[i] = inn if m and 'Interior' in m.name else ext
h = join_all('BenouvilleHouse')
place(h, V(((-6.35 + 5.31) / 2, (-4.75 + 2.86) / 2, -0.08)), 1.0, math.pi)   # the street front was at -Y: turn it to +Y
export(h, 'BenouvilleHouse')
