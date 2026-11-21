# Batch 7 grenades from _downloads: Nebelhandgranate 39 (smoke), AN-M8 HC (smoke), No. 82 "Gammon" bomb.
# Each: real size, long axis up (Blender Z -> Unity Y), origin at the middle, textures shrunk to 1024 and embedded.
# Run: python3 -I grenades_batch7.py -- <downloads dir> <nb39 glb (converted from the .dae with trimesh)> <out dir> <tmp dir>
import bpy, sys, os, mathutils
from PIL import Image
D, NBGLB, OUT, TMP = sys.argv[sys.argv.index('--') + 1:][:4]
V = mathutils.Vector
def shrink(src, name, size=1024, mode='RGB'):
    p = os.path.join(TMP, name); im = Image.open(src).convert(mode); im.thumbnail((size, size)); im.save(p); return p
def reset(): bpy.ops.wm.read_factory_settings(use_empty=True)
def meshes(): return [o for o in bpy.context.scene.objects if o.type == 'MESH']
def mat(name, base, normal=None, rough=0.6, metal=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']
    b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(base); nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    if normal:
        n = nt.nodes.new('ShaderNodeTexImage'); n.image = bpy.data.images.load(normal); n.image.colorspace_settings.name = 'Non-Color'
        nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(n.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], b.inputs['Normal'])
    return m
def finish(name, keep, long_len, axis_long=2):
    for o in [o for o in bpy.context.scene.objects if o.type != 'MESH' or o not in keep]:
        if o.type == 'MESH' and o not in keep: bpy.data.objects.remove(o)
    for o in keep:
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    for o in [o for o in bpy.context.scene.objects if o.type != 'MESH']: bpy.data.objects.remove(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in keep: o.select_set(True)
    bpy.context.view_layer.objects.active = keep[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(keep) > 1: bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    vs = [v.co for v in o.data.vertices]
    lo = V([min(v[i] for v in vs) for i in range(3)]); hi = V([max(v[i] for v in vs) for i in range(3)])
    s = long_len / (hi - lo)[axis_long]
    o.data.transform(mathutils.Matrix.Translation(-(lo + hi) / 2))
    o.data.transform(mathutils.Matrix.Scale(s, 4))
    if axis_long == 1: o.data.transform(mathutils.Matrix.Rotation(1.5708, 4, 'X'))
    o.name = o.data.name = name
    vs = [v.co for v in o.data.vertices]
    print(name, 'size', [round(max(v[i] for v in vs) - min(v[i] for v in vs), 3) for i in range(3)], len(o.data.polygons), 'faces')
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + '.fbx'), use_selection=False, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             path_mode='COPY', embed_textures=True, bake_anim=False, mesh_smooth_type='FACE')

# --- Nebelhandgranate 39: 36 cm long, the smoke head on top
reset(); bpy.ops.import_scene.gltf(filepath=NBGLB)
T = os.path.join(D, 'nebelhandgranate-39/textures/')
m = mat('Nebelhandgranate39', shrink(T + 'Nebelhandgranate_39_albedo.jpeg', 'nb_d.png'), shrink(T + 'Nebelhandgranate_39_normal.png', 'nb_n.png'), 0.65, 0.2)
ks = meshes()
for o in ks: o.data.materials.clear(); o.data.materials.append(m)
finish('Smoke_Nebelhandgranate39', ks, 0.36)

# --- AN-M8 HC: 14.5 cm tall canister with its lever
reset(); bpy.ops.import_scene.fbx(filepath=os.path.join(D, 'm8-hc-smoke-grenade/source/Little projectF.fbx'))
T = os.path.join(D, 'm8-hc-smoke-grenade/textures/')
mats = {'Body': mat('M8_Body', shrink(T + 'Body_Base_color.png', 'm8_body.png'), shrink(T + 'Body_Normal_OpenGL.png', 'm8_body_n.png'), 0.55, 0.3),
        'Metal2': mat('M8_Metal', shrink(T + 'Metal2_Base_color.png', 'm8_metal.png', 512), None, 0.45, 0.7),
        'Pure metal': mat('M8_Lever', shrink(T + 'Pure_metal_Base_color.png', 'm8_lever.png', 512), None, 0.4, 0.8)}
ks = meshes()
for o in ks:
    for i, sl in enumerate(o.material_slots):
        if sl.material and sl.material.name.split('.')[0] in mats: o.material_slots[i].material = mats[sl.material.name.split('.')[0]]
finish('Smoke_ANM8', ks, 0.145)

# --- No. 82 Gammon bomb: the stockinette bag and its cap; the file has two, keep one; ~15 cm across filled
reset(); bpy.ops.import_scene.fbx(filepath=os.path.join(D, 'no-82-gammonbomb/source/GammonBomb_cap.fbx'))
T = os.path.join(D, 'no-82-gammonbomb/textures/')
g = mat('Gammonbomb', shrink(T + 'Gammonbomb_Base_Color.png', 'gb_d.png'), shrink(T + 'Gammonbomb_Normal.png', 'gb_n.png'), 0.85, 0.0)
ks = [o for o in meshes() if '.' not in o.name]
for o in ks: o.data.materials.clear(); o.data.materials.append(g)
finish('Grenade_No82_Gammon', ks, 0.15, axis_long=0)
