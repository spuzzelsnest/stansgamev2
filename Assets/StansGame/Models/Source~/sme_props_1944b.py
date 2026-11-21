# Chapter 2 props from Joan's third batch of downloads (13 Nov 2026). Blender X = east, Y = north (the prop's front), Z up.
#   Opel Blitz 3-ton truck (Call of Duty WWII rip, OBJ + DDS)      -> Models/Props/SME/OpelBlitz.fbx   6.0 m, front +Y
#   81 mm mortar (Pindad Mo-3, close to the US M1 81 mm)           -> Models/Props/SME/Mortar81.fbx    fires towards +Y
#   Galvanised bucket (low poly)                                   -> Models/Props/SME/Bucket.fbx      0.31 m
#   Telephone pole with cross-arm and three insulators              -> Models/Props/SME/TelegraphPole.fbx 9.4 m, arm along X
#   Muddy wooden ladder                                            -> Models/Props/SME/Ladder.fbx      5.1 m
# Run: python3 -I sme_props_1944b.py -- <downloads dir> <extracted dir> <out dir> <tmp dir>
import bpy, sys, os, math, mathutils
from PIL import Image
D, EX, OUT, TMP = sys.argv[sys.argv.index('--') + 1:][:4]
V = mathutils.Vector
os.makedirs(TMP, exist_ok=True); os.makedirs(os.path.join(OUT, 'Props/SME'), exist_ok=True)

def reset(): bpy.ops.wm.read_factory_settings(use_empty=True)
def meshes(): return [o for o in bpy.context.scene.objects if o.type == 'MESH']
def shrink(src, size, name):
    p = os.path.join(TMP, name); im = Image.open(src); im = im.convert('RGBA' if im.mode in ('RGBA', 'LA') else 'RGB')
    im.thumbnail((size, size)); im.save(p); return p
def tex_material(mat, base, normal=None, metal=0.0, rough=0.6):
    mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); b = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(b.outputs['BSDF'], out.inputs['Surface']); b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
    if base:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(base); nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    if normal:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(normal); t.image.colorspace_settings.name = 'Non-Color'
        nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(t.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], b.inputs['Normal'])
def apply_all():
    bpy.context.view_layer.update()
    for o in meshes():
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    for o in [o for o in bpy.context.scene.objects if o.type != 'MESH']: bpy.data.objects.remove(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes(): o.select_set(True)
    bpy.context.view_layer.objects.active = meshes()[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
def join(name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes(): o.select_set(True)
    bpy.context.view_layer.objects.active = meshes()[0]; bpy.ops.object.join(); meshes()[0].name = name; meshes()[0].data.name = name
def verts(): return [o.matrix_world @ v.co for o in meshes() for v in o.data.vertices]
def bounds():
    vs = verts(); return V([min(v[i] for v in vs) for i in range(3)]), V([max(v[i] for v in vs) for i in range(3)])
def transform(m):
    for o in meshes(): o.data.transform(m); o.data.update()
def ground_centre():
    lo, hi = bounds(); transform(mathutils.Matrix.Translation(V((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))))
def export(name):
    path = os.path.join(OUT, 'Props/SME', name + '.fbx')
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', path_mode='COPY', embed_textures=True, bake_anim=False, mesh_smooth_type='FACE')
    lo, hi = bounds(); print('exported', name, tuple(round(x, 2) for x in hi - lo), sum(len(o.data.polygons) for o in meshes()), 'faces')

# ---------------------------------------------------------------- Opel Blitz
reset(); bpy.ops.wm.obj_import(filepath=os.path.join(EX, 'opel/cod_wwll_opel_blitz/opel_blitz.obj')); apply_all()
TX = os.path.join(EX, 'opel/cod_wwll_opel_blitz')
for o in meshes():
    for m in o.data.materials:
        key = m.name.lower().split('.')[0]                      # tex_46218_0
        dds = next((os.path.join(TX, f) for f in os.listdir(TX) if f.lower() == key + '.dds'), None)
        nrm = next((os.path.join(TX, f) for f in os.listdir(TX) if f.lower() == key[:-2].lower() + '_9.dds'), None)
        try: base = shrink(dds, 1024, f'opel_{key}.png') if dds else None
        except Exception as e: print('dds?', dds, e); base = None
        try: nm = shrink(nrm, 1024, f'opel_{key}_n.png') if nrm else None
        except Exception: nm = None
        tex_material(m, base, None, 0.2, 0.7)
        m.name = 'OpelBlitz_' + key.split('_')[1]
lo, hi = bounds(); transform(mathutils.Matrix.Scale(6.02 / (hi.y - lo.y), 4)); ground_centre()
# which end is the bonnet? the cab end is lower than the tarpaulin over the cargo bed
lo, hi = bounds(); vs = verts()
zf = max((v.z for v in vs if v.y > hi.y - 0.6), default=0); zb = max((v.z for v in vs if v.y < lo.y + 0.6), default=0)
if zf > zb: transform(mathutils.Matrix.Rotation(math.pi, 4, 'Z'))
join('OpelBlitz'); export('OpelBlitz')

# ---------------------------------------------------------------- 81 mm mortar
reset(); bpy.ops.import_scene.fbx(filepath=os.path.join(D, 'mortars/source/Pindad Mortars Sketchfab.fbx'))
bpy.context.view_layer.update()
keep = {'Base', 'Mortar', 'Pindad Mo-3 Kal. 81 mm'}
for o in list(meshes()):
    if o.name not in keep: bpy.data.objects.remove(o)
apply_all()
MT = os.path.join(D, 'mortars/textures')
for o in meshes():
    for m in o.data.materials:
        part = 'Small Parts' if 'Small' in m.name else 'Base and Mortar'
        tex_material(m, shrink(f'{MT}/Pindad Mo-3 Kal. 81 mm_{part}_BaseColor.png', 1024, f'mortar_{part[:4]}_d.png'),
                     shrink(f'{MT}/Pindad Mo-3 Kal. 81 mm_{part}_Normal.png', 1024, f'mortar_{part[:4]}_n.png'), 0.4, 0.55)
        m.name = 'Mortar81_' + part.replace(' ', '')
transform(mathutils.Matrix.Rotation(math.pi, 4, 'Z'))       # the tube leans over the bipod towards -Y in the source
ground_centre(); join('Mortar81'); export('Mortar81')

# ---------------------------------------------------------------- bucket
reset(); bpy.ops.import_scene.fbx(filepath=os.path.join(EX, 'bucket/Bucket/Bucket_LP.fbx')); apply_all()
BT = os.path.join(EX, 'bucket/Bucket/Textures')
for o in meshes():
    for m in o.data.materials: tex_material(m, shrink(f'{BT}/Bucket_Color.png', 1024, 'bucket_d.png'), shrink(f'{BT}/Bucket_Normal.png', 1024, 'bucket_n.png'), 0.6, 0.5); m.name = 'Bucket'
lo, hi = bounds(); transform(mathutils.Matrix.Scale(0.31 / (hi.z - lo.z), 4)); ground_centre(); join('Bucket'); export('Bucket')

# ---------------------------------------------------------------- telegraph pole (the plain one, no transformer)
reset(); bpy.ops.import_scene.fbx(filepath=os.path.join(D, 'telephone-poles/source/Telephone Poles.fbx'))
bpy.context.view_layer.update()
for o in list(meshes()):
    if not (o.parent and o.parent.name == 'Small_Pole2'): bpy.data.objects.remove(o)
apply_all()
PT = os.path.join(D, 'telephone-poles/textures')
for o in meshes():
    for m in o.data.materials:
        if m.name.startswith('Wood'): tex_material(m, shrink(f'{PT}/Wood_albedo.jpg', 1024, 'pole_wood_d.png'), shrink(f'{PT}/Wood_normal.png', 1024, 'pole_wood_n.png'), 0.0, 0.85); m.name = 'Pole_Wood'
        elif m.name.startswith('Metal'): tex_material(m, shrink(f'{PT}/Metal_albedo.jpg', 512, 'pole_metal_d.png'), None, 0.6, 0.5); m.name = 'Pole_Metal'
# the pole itself on the origin (not the middle of the cross-arm)
pole = [v for v in verts() if v.z < 1.0]; c = sum(pole, V()) / len(pole)
transform(mathutils.Matrix.Translation(V((-c.x, -c.y, 0))))
join('TelegraphPole'); export('TelegraphPole')
lo, hi = bounds(); print('pole arm x', round(lo.x, 2), round(hi.x, 2), 'top', round(hi.z, 2))

# ---------------------------------------------------------------- ladder
reset(); bpy.ops.import_scene.fbx(filepath=os.path.join(D, 'ladder-muddy/source/Muddy_Ladder_FBX.fbx')); apply_all()
LT = os.path.join(D, 'ladder-muddy/textures')
for o in meshes():
    for m in o.data.materials: tex_material(m, shrink(f'{LT}/Ladder_BaseColor.png', 1024, 'ladder_d.png'), shrink(f'{LT}/Ladder_Normal.png', 1024, 'ladder_n.png'), 0.0, 0.85); m.name = 'Ladder'
ground_centre(); join('Ladder'); export('Ladder')
