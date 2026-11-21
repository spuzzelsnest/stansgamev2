# Chapter 2 props from Joan's second batch of downloads (Sketchfab, CC BY):
#   Thompson M1A1 (OBJ + textures)                  -> Models/Weapons/Thompson.fbx   (813 mm, MuzzleMarker)
#   2 cm Flakvierling 38 (DFS_studio, .blend rig)   -> Models/Vehicles/Flakvierling38.fbx (pose of frame 1 baked, ~2.3 m high, MuzzleMarker)
#   US Army parachute (amaraklov)                   -> Models/US/T5Canopy.fbx (6 m across inflated, origin at the risers)
#   Eglise d'Huberville (paullemaignen, photogrammetry) -> Models/Town/ChurchHuberville.fbx (church cut out of the scan,
#        ground at z = 0, decimated, TowerMarker at the top of the tower)
# Run: python3 sme_props_1944.py -- <downloads dir> <thompson obj> <out dir> <tmp dir>
import bpy, bmesh, sys, os, math, mathutils
import numpy as np
from PIL import Image
D, TOMMY, OUT, TMP = sys.argv[sys.argv.index('--') + 1:][:4]
V = mathutils.Vector
os.makedirs(TMP, exist_ok=True)

def reset(): bpy.ops.wm.read_factory_settings(use_empty=True)
def meshes(): return [o for o in bpy.context.scene.objects if o.type == 'MESH']

def shrink(src, size, name):
    p = os.path.join(TMP, name)
    im = Image.open(src); im = im.convert('RGBA' if im.mode == 'RGBA' else 'RGB')
    im.thumbnail((size, size)); im.save(p); return p

def tex_material(mat, base, normal=None, metal=0.0, rough=0.5):
    mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
    bsdf.inputs['Metallic'].default_value = metal; bsdf.inputs['Roughness'].default_value = rough
    if base:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(base); nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    if normal:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(normal); t.image.colorspace_settings.name = 'Non-Color'
        nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(t.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])

def apply_all():
    bpy.context.view_layer.update()
    for o in meshes():
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    for o in [o for o in bpy.context.scene.objects if o.type != 'MESH']: bpy.data.objects.remove(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes(): o.select_set(True)
    bpy.context.view_layer.objects.active = meshes()[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def verts(): return [o.matrix_world @ v.co for o in meshes() for v in o.data.vertices]
def bounds():
    vs = verts(); return V((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))), V((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
def transform(m):
    for o in meshes(): o.data.transform(m); o.data.update()

def marker(name, p):
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    a = bm.verts.new((0, 0, 0)); b = bm.verts.new((0.002, 0, 0)); c = bm.verts.new((0, 0.002, 0)); bm.faces.new((a, b, c)); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); o.location = p

def export(path, embed=True):
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             path_mode='COPY' if embed else 'STRIP', embed_textures=embed, bake_anim=False, mesh_smooth_type='FACE')
    lo, hi = bounds(); print('exported', os.path.basename(path), tuple(round(x, 2) for x in hi - lo), sum(len(o.data.polygons) for o in meshes()), 'faces')

# ---------------------------------------------------------------- Thompson M1A1: 813 mm, along X, muzzle at +X
reset(); bpy.ops.wm.obj_import(filepath=TOMMY); apply_all()
T = os.path.join(D, 'submachine-gun-thompson/textures')
for o in meshes():
    for m in o.data.materials:
        k = 'UP' if 'UP' in m.name else 'LOW'
        tex_material(m, shrink(f'{T}/{k}_albedo.jpg', 2048, f'tommy_{k}_a.png'), shrink(f'{T}/{k}_normal.png', 2048, f'tommy_{k}_n.png'), 0.5, 0.45)
lo, hi = bounds(); transform(mathutils.Matrix.Scale(0.813 / (hi.x - lo.x), 4))
lo, hi = bounds(); transform(mathutils.Matrix.Translation(-(lo + hi) / 2))
vs = verts(); lo, hi = bounds()
tip = [v for v in vs if v.x > hi.x - 0.01]; p = sum(tip, V()) / len(tip); p.x = hi.x
marker('MuzzleMarker', p); export(os.path.join(OUT, 'Weapons/Thompson.fbx'))

# ---------------------------------------------------------------- Flakvierling 38: the rigged gun at frame 1, baked
reset(); bpy.ops.wm.open_mainfile(filepath=os.path.join(D, '2cm-vierlingflak-38-ww2/source/2cm vierlingflak.blend'))
sc = bpy.context.scene; sc.frame_set(1)
gun = bpy.data.objects['Cylinder.030']
dg = bpy.context.evaluated_depsgraph_get(); me = bpy.data.meshes.new_from_object(gun.evaluated_get(dg), depsgraph=dg)
me.transform(gun.matrix_world)
for o in list(bpy.data.objects): bpy.data.objects.remove(o)
flak = bpy.data.objects.new('Flakvierling38', me); sc.collection.objects.link(flak)
me.materials.clear(); m = bpy.data.materials.new('Flak_Dunkelgelb'); me.materials.append(m)
tex_material(m, None, None, 0.3, 0.6); m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.42, 0.38, 0.25, 1)
lo, hi = bounds(); transform(mathutils.Matrix.Scale(2.3 / (hi.z - lo.z), 4))
lo, hi = bounds(); transform(mathutils.Matrix.Translation(V((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))))
vs = verts(); lo, hi = bounds()
tip = [v for v in vs if v.y > hi.y - 0.05 and v.z > (lo.z + hi.z) / 2]
marker('MuzzleMarker', sum(tip, V()) / len(tip) if tip else V((0, hi.y, hi.z)))
export(os.path.join(OUT, 'Vehicles/Flakvierling38.fbx'), embed=False)

# ---------------------------------------------------------------- T-5 canopy: 6 m across inflated, origin at the risers
reset(); bpy.ops.import_scene.gltf(filepath=os.path.join(D, 'US army_parachute.glb')); apply_all()
src = {}
for img in bpy.data.images:
    p = os.path.join(TMP, f'chute_{img.name}.png'); img.filepath_raw = p; img.file_format = 'PNG'; img.save(); src[img.name] = p
mat = meshes()[0].data.materials[0]
base = next((l.from_node.image.name for l in mat.node_tree.links if l.to_socket.name == 'Base Color' and l.from_node.type == 'TEX_IMAGE'), None)
nrm = next((l.from_node.image.name for l in mat.node_tree.links if l.to_node.type == 'NORMAL_MAP' and l.from_node.type == 'TEX_IMAGE'), None)
tex_material(mat, shrink(src[base], 1024, 'chute_base.png') if base else None, shrink(src[nrm], 1024, 'chute_normal.png') if nrm else None, 0.0, 0.85)
mat.name = 'T5_Canopy'
lo, hi = bounds(); transform(mathutils.Matrix.Scale(6.0 / (hi.x - lo.x), 4))
lo, hi = bounds(); transform(mathutils.Matrix.Translation(V((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))))
export(os.path.join(OUT, 'US/T5Canopy.fbx'))

# ---------------------------------------------------------------- Huberville church, cut out of the village scan
# (numpy instead of bmesh: the scan has 720k faces)
reset(); bpy.ops.import_scene.gltf(filepath=os.path.join(D, 'eglise-normandie-france/source/eglisehuberville.glb')); apply_all()
o = meshes()[0]; img = next(iter(bpy.data.images))
p = os.path.join(TMP, 'church_atlas.png'); img.filepath_raw = p; img.file_format = 'PNG'; img.save()
X0, X1, Y0, Y1 = -19.5, 20.5, -8.0, 12.5        # the church and its walls (from the top view of the scan)
GROUND = 3.77
me = o.data
nv = len(me.vertices); co = np.empty(nv * 3, np.float32); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
nl = len(me.loops); lv = np.empty(nl, np.int64); me.loops.foreach_get('vertex_index', lv)
uv = np.empty(nl * 2, np.float32); me.uv_layers.active.data.foreach_get('uv', uv); uv = uv.reshape(-1, 2)
npoly = len(me.polygons); ls = np.empty(npoly, np.int64); me.polygons.foreach_get('loop_start', ls)
tot = np.empty(npoly, np.int64); me.polygons.foreach_get('loop_total', tot)
assert (tot == 3).all()
tri = lv[ls[:, None] + np.arange(3)]
P = co[tri]                                                    # (n, 3, 3)
inside = ((P[..., 0] > X0) & (P[..., 0] < X1) & (P[..., 1] > Y0) & (P[..., 1] < Y1)).all(1)
above = (P[..., 2] > GROUND + 0.25).any(1)
keep = np.where(inside & above)[0]
used, inv = np.unique(tri[keep].ravel(), return_inverse=True)
faces = inv.reshape(-1, 3)
nme = bpy.data.meshes.new('ChurchHuberville')
nme.from_pydata((co[used] - np.array([(X0 + X1) / 2, (Y0 + Y1) / 2, GROUND], np.float32)).tolist(), [], faces.tolist())
nme.uv_layers.new(name='UV')
kuv = uv[(ls[keep][:, None] + np.arange(3)).ravel()]
nme.uv_layers['UV'].data.foreach_set('uv', kuv.ravel())
for q in list(bpy.data.objects): bpy.data.objects.remove(q)
o = bpy.data.objects.new('ChurchHuberville', nme); bpy.context.scene.collection.objects.link(o)
print('church faces kept', len(faces))
bpy.context.view_layer.objects.active = o; o.select_set(True)
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT"); bpy.ops.mesh.remove_doubles(threshold=0.0005); bpy.ops.object.mode_set(mode="OBJECT")
dec = o.modifiers.new('dec', 'DECIMATE'); dec.ratio = min(1.0, 170000 / len(faces)); dec.use_symmetry = False; bpy.ops.object.modifier_apply(modifier='dec')
m = bpy.data.materials.new('ChurchHuberville'); o.data.materials.append(m)
tex_material(m, shrink(p, 4096, 'ChurchHuberville_Atlas.png'), None, 0.0, 0.9)
vs = verts(); top = max(vs, key=lambda v: v.z)
marker('TowerMarker', top)
print('church tower top', tuple(round(x, 2) for x in top))
export(os.path.join(OUT, 'Town/ChurchHuberville.fbx'))
