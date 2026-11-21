# Download batch 4 (13 Nov 2026): crates, a barrel, a tarpaulined supply stack, ammo boxes. Blender X east, Y = front, Z up.
#   crates (crates.fbx: cratetall/cratewide/cratesmall/barrel, merged stacks)  -> Barrel, CrateTall, CrateWide, CrateSmall, CrateStackA/B
#   warehouse-crates (boxes under a tarpaulin)                                 -> SupplyStack
#   wooden-crates (Wooden_box.zip: three small boxes)                          -> AmmoBox1..3
# Run: python3 -I sme_props_1944c.py -- <downloads dir> <wooden_crates extracted dir> <out dir> <tmp dir>
import bpy, sys, os, math, mathutils
from PIL import Image
D, WOOD, OUT, TMP = sys.argv[sys.argv.index('--') + 1:][:4]
V = mathutils.Vector
os.makedirs(TMP, exist_ok=True); os.makedirs(os.path.join(OUT, 'Props/SME'), exist_ok=True)
def reset(): bpy.ops.wm.read_factory_settings(use_empty=True)
def meshes(): return [o for o in bpy.context.scene.objects if o.type == 'MESH']
def shrink(src, size, name):
    p = os.path.join(TMP, name); im = Image.open(src); im = im.convert('RGB'); im.thumbnail((size, size)); im.save(p); return p
def tex_material(mat, base, normal=None, rough=0.8):
    mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); b = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(b.outputs['BSDF'], out.inputs['Surface']); b.inputs['Roughness'].default_value = rough
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
def bounds(objs):
    vs = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    return V([min(v[i] for v in vs) for i in range(3)]), V([max(v[i] for v in vs) for i in range(3)])
def export_only(objs, name, scale=1.0):
    """export a copy of objs, joined, scaled, base centred on the origin"""
    bpy.ops.object.select_all(action='DESELECT')
    copies = []
    for o in objs:
        c = o.copy(); c.data = o.data.copy(); bpy.context.scene.collection.objects.link(c); copies.append(c)
    for c in copies: c.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    if len(copies) > 1: bpy.ops.object.join()
    j = bpy.context.view_layer.objects.active; j.name = j.data.name = name
    j.data.transform(mathutils.Matrix.Scale(scale, 4))
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.delete_loose(); bpy.ops.object.mode_set(mode='OBJECT')
    lo, hi = bounds([j]); j.data.transform(mathutils.Matrix.Translation(V((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))))
    bpy.ops.object.select_all(action='DESELECT'); j.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'Props/SME', name + '.fbx'), use_selection=True, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             path_mode='COPY', embed_textures=True, bake_anim=False, mesh_smooth_type='FACE')
    lo, hi = bounds([j]); print('exported', name, tuple(round(x, 2) for x in hi - lo), len(j.data.polygons), 'faces')
    bpy.data.objects.remove(j)

# ---------------------------------------------------------------- crates.fbx
reset(); bpy.ops.import_scene.fbx(filepath=os.path.join(D, 'crates/source/crates.fbx')); apply_all()
CT = os.path.join(D, 'crates/textures')
mat = meshes()[0].data.materials[0]; tex_material(mat, shrink(f'{CT}/cratesAlbedo.png', 1024, 'crates_d.png'), shrink(f'{CT}/cratesormal.png', 1024, 'crates_n.png')); mat.name = 'Crates_Wood'
byname = {o.name: o for o in meshes()}
lo, hi = bounds([byname['barrel']]); S = 0.88 / (hi.z - lo.z)          # a 200-litre drum is 88 cm high
print('crates scale', round(S, 3))
for n, out in (('barrel', 'Barrel'), ('cratetall', 'CrateTall'), ('cratewide', 'CrateWide'), ('cratesmall', 'CrateSmall'),
               ('cratemerged1', 'CrateStackA'), ('cratesmerged2', 'CrateStackB')):
    if n in byname: export_only([byname[n]], out, S)

# ---------------------------------------------------------------- warehouse crates under a tarpaulin
reset(); bpy.ops.import_scene.fbx(filepath=os.path.join(D, 'warehouse-crates/source/Warehouse Crates.fbx')); apply_all()
WT = os.path.join(D, 'warehouse-crates/textures')
done = set()
for o in meshes():
    for m in o.data.materials:
        if m.name in done: continue
        done.add(m.name)
        k = 'Cloth' if 'Cloth' in m.name else 'Box'
        tex_material(m, shrink(f'{WT}/Crates_{k}_BaseColor.png', 1024, f'wh_{k}_d.png'), shrink(f'{WT}/Crates_{k}_Normal.png', 1024, f'wh_{k}_n.png'), 0.85)
        m.name = 'SupplyStack_' + k
export_only(meshes(), 'SupplyStack')

# ---------------------------------------------------------------- three small wooden (ammunition) boxes
reset(); bpy.ops.import_scene.fbx(filepath=os.path.join(WOOD, 'wooden_crates.fbx')); apply_all()
TT = os.path.join(WOOD, 'textures')
m = meshes()[0].data.materials[0]; tex_material(m, shrink(f'{TT}/wooden_crates_Albedo.png', 1024, 'ammo_d.png'), shrink(f'{TT}/wooden_crates_Normal.png', 1024, 'ammo_n.png')); m.name = 'AmmoBox_Wood'
for i, o in enumerate(sorted(meshes(), key=lambda o: o.name)): export_only([o], f'AmmoBox{i + 1}')
