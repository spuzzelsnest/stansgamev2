# Prepares the US kit for chapter 2 (Sainte-Mère-Église) from Joan's downloads:
# metres, Z up, centred origin, a tiny "MuzzleMarker" / "NoseMarker" mesh so Unity can turn the model to face +Z.
# Textures are embedded in the FBX. Run: python3 us_kit_1944.py -- <downloads> <out dir> <tex tmp>
import bpy, bmesh, sys, os, math, mathutils
D, OUT, TEX = sys.argv[sys.argv.index('--') + 1:][:3]
V = mathutils.Vector

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def imp(path):
    if path.lower().endswith('.glb'): bpy.ops.import_scene.gltf(filepath=path)
    else: bpy.ops.import_scene.fbx(filepath=path)

def meshes(): return [o for o in bpy.context.scene.objects if o.type == 'MESH']

def apply_all():
    bpy.context.view_layer.update()
    for o in meshes():
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    for o in [o for o in bpy.context.scene.objects if o.type != 'MESH']: bpy.data.objects.remove(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes(): o.select_set(True)
    bpy.context.view_layer.objects.active = meshes()[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def verts():
    return [o.matrix_world @ v.co for o in meshes() for v in o.data.vertices]

def bounds():
    vs = verts(); lo = V((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))); hi = V((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    return lo, hi

def transform(m):
    for o in meshes(): o.data.transform(m); o.data.update()

def normalise(length_axis, real_length):
    lo, hi = bounds()
    s = real_length / (hi - lo)[length_axis]
    transform(mathutils.Matrix.Scale(s, 4))
    lo, hi = bounds()
    transform(mathutils.Matrix.Translation(-(lo + hi) / 2))

def marker(name, p):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    a = bm.verts.new((0, 0, 0)); b = bm.verts.new((0.002, 0, 0)); c = bm.verts.new((0, 0.002, 0))
    bm.faces.new((a, b, c)); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); o.location = p

def end_point(axis, sign, frac=0.02):
    """Average of the vertices at one end of the long axis (the muzzle)."""
    vs = verts(); lo, hi = bounds()
    edge = hi[axis] if sign > 0 else lo[axis]; span = (hi - lo)[axis]
    pick = [v for v in vs if abs(v[axis] - edge) < span * frac]
    p = sum(pick, V()) / len(pick); p[axis] = edge
    return p

def tex_material(mat, base, normal=None, metal=0.0, rough=0.5):
    mat.use_nodes = True
    nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
    bsdf.inputs['Metallic'].default_value = metal; bsdf.inputs['Roughness'].default_value = rough
    if base:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(base); nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    if normal:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(normal); t.image.colorspace_settings.name = 'Non-Color'
        nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(t.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])

def unpack_images():
    for i, img in enumerate(bpy.data.images):
        if img.packed_file or not img.filepath or not os.path.exists(bpy.path.abspath(img.filepath)):
            if img.size[0] == 0: continue
            p = os.path.join(TEX, f"{bpy.path.clean_name(img.name)}_{i}.png")
            img.filepath_raw = p; img.file_format = 'PNG'; img.save()

def export(path):
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                             path_mode='COPY', embed_textures=True, bake_anim=False, mesh_smooth_type='FACE')
    print('exported', path, tuple(round(x, 3) for x in (bounds()[1] - bounds()[0])))

# ---------------------------------------------------------------- M1 Garand: 1107 mm, lies along Y, muzzle at -Y
reset(); imp(os.path.join(D, 'garand/m1_garand_bin.fbx')); apply_all()
T = os.path.join(D, 'garand/m1_garand_textures')
for o in meshes():
    for m in o.data.materials:
        k = m.name.replace('_mtl', '')
        tex_material(m, f"{T}/{k}_mtl_BaseColor.png", f"{T}/{k}_mtl_Normal.png", 0.6 if 'chamber' in k or 'parts' in k else 0.0, 0.45)
normalise(1, 1.107); marker('MuzzleMarker', end_point(1, -1)); export(os.path.join(OUT, 'Weapons/Garand.fbx'))

# ---------------------------------------------------------------- M1A1 carbine (folding stock open): 905 mm, along Y, muzzle at +Y
reset(); imp(os.path.join(D, 'm1a1-carabine/source/M1A1_low.fbx')); apply_all()
for o in meshes():
    for m in o.data.materials: tex_material(m, os.path.join(D, 'm1a1-carabine/textures/M1A1_albedo.jpg'), os.path.join(D, 'm1a1-carabine/textures/M1A1_normal.png'), 0.3, 0.5)
normalise(1, 0.905); marker('MuzzleMarker', end_point(1, +1)); export(os.path.join(OUT, 'Weapons/Carbine.fbx'))

# ---------------------------------------------------------------- Mk II "pineapple": 114 mm tall with the fuze
reset(); imp(os.path.join(D, 'mk2/Mk2Grenade/Mk2grenade.FBX')); apply_all()
T = os.path.join(D, 'mk2/Mk2Grenade/textures')
for o in meshes():
    for m in o.data.materials: tex_material(m, f"{T}/Mk2Grenade_Base_Color.jpg", f"{T}/Mk2Grenade_Normal.jpg", 0.4, 0.55)
normalise(2, 0.114); export(os.path.join(OUT, 'Weapons/MkIIGrenade.fbx'))

# ---------------------------------------------------------------- 2.36-inch rocket launcher M1A1 "bazooka": 1370 mm, along X, muzzle at +X
reset(); imp(os.path.join(D, 'bazooka_us_ww2.glb')); apply_all(); unpack_images()
normalise(0, 1.37); marker('MuzzleMarker', end_point(0, +1, 0.01)); export(os.path.join(OUT, 'Weapons/Bazooka.fbx'))

# ---------------------------------------------------------------- C-47 Skytrain: span 29.0 m, length 19.4 m
reset(); imp(os.path.join(D, 'c47.glb')); apply_all(); unpack_images()
# the glTF materials are rebuilt as plain image -> Base Color so the FBX carries the textures
for m in bpy.data.materials:
    if not m.use_nodes: continue
    img = next((n.image for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image), None)
    if img: tex_material(m, bpy.path.abspath(img.filepath_raw), None, 0.1, 0.6)
    else: tex_material(m, None, None, 0.1, 0.6)
vs = verts()
best = max(range(0, 180), key=lambda a: (lambda c, s: max(v.x * c + v.y * s for v in vs) - min(v.x * c + v.y * s for v in vs))(math.cos(math.radians(a)), math.sin(math.radians(a))))
transform(mathutils.Matrix.Rotation(-math.radians(best), 4, 'Z'))          # wings along X
lo, hi = bounds()
transform(mathutils.Matrix.Scale(29.0 / (hi.x - lo.x), 4))
lo, hi = bounds(); transform(mathutils.Matrix.Translation(-(lo + hi) / 2))
vs = verts(); top = max(vs, key=lambda v: v.z)                               # the top of the fin is at the tail
lo, hi = bounds()
nose_y = lo.y if top.y > 0 else hi.y
marker('NoseMarker', V((0, nose_y, 0)))
print('C-47 length', round(hi.y - lo.y, 2), 'height', round(hi.z - lo.z, 2), 'tail at y', round(top.y, 2))
export(os.path.join(OUT, 'Vehicles/C47.fbx'))
