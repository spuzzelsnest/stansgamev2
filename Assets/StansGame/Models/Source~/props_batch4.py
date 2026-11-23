# Joan's downloads of 22 Nov, evening (batch 4) to game-ready FBX: one mesh per model, metres, standing on z = 0,
# centred, front to +Y, decimated to a budget; every material renamed to a plain name with its colour texture written
# beside it as Textures/<key>/<name>.jpg (1024 px; a flat colour swatch when the material has no texture).
# usage: python3 -I props_batch4.py -- <key> <out dir>
import bpy, sys, os, re, math
from mathutils import Vector, Matrix
from PIL import Image
D = '/mnt/user-data/uploads/stansGameV2/_downloads/'
SPEC = {
    # camp fire: logs and stones round the embers, 1.3 m across
    'CampFire': dict(src=D + '_Objects/camp-fire/source/model.fbx', scale=1.0, budget=4000,
                     tex={'08_Default': D + '_Objects/camp-fire/textures/08_-_Default_albedo.jpg'}),
    # a brick well with an iron hand pump and a bucket (795k triangles in the download), 1.2 m to the pump head
    'WaterPump': dict(src=D + '_Objects/old-water-pump/source/Sketchfab_2022_10_16_11_02_06.blend', height=1.2, budget=9000,
                      tex={'GreenRustyMetal': D + '_Objects/old-water-pump/textures/RustyPaintedMetal01_2K_BaseColor.png',
                           'coral_fort_wall_03': D + '_Objects/old-water-pump/textures/coral_fort_wall_03_diff_2k.jpeg',
                           'rough_wood': D + '_Objects/old-water-pump/textures/rough_wood_diff_2k.jpeg',
                           'rust_coarse_01': D + '_Objects/old-water-pump/textures/rust_coarse_01_diff_2k.jpeg'}),
    # Opel Blitz with the field kitchen (Feldküche) on its bed, 7.1 m with the kitchen's tow bar
    'FieldKitchenTruck': dict(src=D + '_Vehicles/ww2-field-kitchen-truck-based-on-opel/source/field kitchen truck.blend', scale=1.0, budget=30000,
                              texdir=D + '_Vehicles/ww2-field-kitchen-truck-based-on-opel/textures/'),
    # timber watchtower with a guard hut, 7.4 m to the tower roof
    'WatchtowerHut': dict(src=D + '_Objects/watchtower and guard house/source/watchtower and guard house.glb', scale=1.0, budget=30000, packed=True),
}
key, OUT = sys.argv[-2], sys.argv[-1]
S = SPEC[key]
bpy.ops.wm.read_factory_settings(use_empty=True)
src = S['src']
if src.endswith('.blend'): bpy.ops.wm.open_mainfile(filepath=src)
elif src.endswith('.glb'): bpy.ops.import_scene.gltf(filepath=src)
else: bpy.ops.import_scene.fbx(filepath=src)
bpy.context.view_layer.update()
os.makedirs(f'{OUT}/Textures/{key}', exist_ok=True)

def clean(n):
    n = re.sub(r'\.\d+$', '', n)
    return re.sub(r'[^A-Za-z0-9]+', '_', n).strip('_') or 'Mat'

def base_image(m):
    if not m or not m.use_nodes: return None
    for n in m.node_tree.nodes:
        if n.type == 'BSDF_PRINCIPLED':
            l = n.inputs['Base Color'].links
            while l:
                nd = l[0].from_node
                if nd.type == 'TEX_IMAGE': return nd.image
                ins = [i for i in nd.inputs if i.links]
                l = ins[0].links if ins else None
    for n in m.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image and not re.search(r'nor|rough|metal|disp|height|ao|spec', n.image.name, re.I): return n.image
    return None

def base_colour(m):
    if m and m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type == 'BSDF_PRINCIPLED': return tuple(n.inputs['Base Color'].default_value[:3])
    return tuple(m.diffuse_color[:3]) if m else (0.5, 0.5, 0.5)

# the textures: from the spec, the packed image, or the image's file found among the download's textures
done = {}
for m in list(bpy.data.materials):
    c = clean(m.name)
    if c in done: continue
    out = f'{OUT}/Textures/{key}/{c}.jpg'
    f = None
    for k, v in S.get('tex', {}).items():
        if c.startswith(k) or k.startswith(c): f = v
    img = base_image(m)
    try:
        if f and os.path.exists(f):
            im = Image.open(f).convert('RGB')
        elif img and img.packed_file and S.get('packed'):
            p = f'/tmp/{key}_{c}.png'; img.filepath_raw = p; img.file_format = 'PNG'; img.save(); im = Image.open(p).convert('RGB')
        elif img and S.get('texdir'):
            base = os.path.basename(bpy.path.abspath(img.filepath) or img.name).replace('\\', '/').split('/')[-1]
            base = re.sub(r'\.\d+$', '', base)
            cand = [x for x in os.listdir(S['texdir']) if os.path.splitext(x)[0].lower() == os.path.splitext(base)[0].lower()]
            im = Image.open(S['texdir'] + cand[0]).convert('RGB') if cand else None
        else: im = None
    except Exception as e:
        print('  texture failed', c, e); im = None
    if im is None:
        col = tuple(int(255 * max(0, min(1, x)) ** (1 / 2.2)) for x in base_colour(m))
        im = Image.new('RGB', (8, 8), col); kind = 'colour'
    else:
        im.thumbnail((1024, 1024)); kind = 'texture'
    im.save(out, quality=88)
    done[c] = kind
    m.name = c
print('materials', done)

# one mesh: modifiers applied, everything joined, decimated to the budget
dg = bpy.context.evaluated_depsgraph_get()
ms = [o for o in bpy.data.objects if o.type == 'MESH' and o.visible_get()]
for o in list(bpy.data.objects):
    if o not in ms: bpy.data.objects.remove(o)
for o in ms:
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    me.transform(o.matrix_world); o.modifiers.clear(); o.data = me; o.parent = None; o.matrix_world = Matrix()
bpy.ops.object.select_all(action='DESELECT')
for o in ms: o.select_set(True)
bpy.context.view_layer.objects.active = ms[0]
if len(ms) > 1: bpy.ops.object.join()
o = bpy.context.view_layer.objects.active; o.name = key; o.data.name = key
n = sum(len(p.vertices) - 2 for p in o.data.polygons)
if n > S['budget']:
    d = o.modifiers.new('d', 'DECIMATE'); d.ratio = S['budget'] / n; bpy.ops.object.modifier_apply(modifier='d')
V = [v.co for v in o.data.vertices]
lo = Vector([min(p[i] for p in V) for i in range(3)]); hi = Vector([max(p[i] for p in V) for i in range(3)])
c = (lo + hi) / 2
o.data.transform(Matrix.Translation(Vector((-c.x, -c.y, -lo.z))))
s = S['height'] / (hi.z - lo.z) if 'height' in S else S.get('scale', 1.0)
o.data.transform(Matrix.Scale(s, 4))
for p in o.data.polygons: p.use_smooth = True
V = [v.co for v in o.data.vertices]
lo = Vector([min(p[i] for p in V) for i in range(3)]); hi = Vector([max(p[i] for p in V) for i in range(3)])
print('RESULT', key, 'tris', n, '->', sum(len(p.vertices) - 2 for p in o.data.polygons), 'size', [round(x, 2) for x in hi - lo])
bpy.ops.export_scene.fbx(filepath=f'{OUT}/{key}.fbx', use_selection=False, axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                         bake_space_transform=True, mesh_smooth_type='FACE', embed_textures=False, path_mode='STRIP')
