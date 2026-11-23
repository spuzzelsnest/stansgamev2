# Joan's Bedford (bedford_oxa_mesh.stl, 568k triangles, no materials or UVs): a Bedford OX 30-cwt lorry with an
# open load bed carrying a pedestal anti-aircraft gun and a rear-facing gun barrel. Made into three game models:
#   BedfordOX_Gun     as downloaded, the guns on the bed
#   BedfordOX         the guns taken off, an empty bed
#   BedfordOX_Loaded  the guns taken off, the bed loaded with ammunition boxes, sacks and jerrycans
# Scale from the wheelbase (243 model units = 3.35 m, the OX's 11 ft): 5.2 m long without the barrel, 1.8 m wide,
# 1.0 m wheels (10.50-16 tyres). Front (the cab) to +Y, on the ground at z = 0. The wheels are cut out as
# Wheel_FL/FR/RL/RR with their pivots on the hubs (TruckController turns and rolls them). Materials: SCC 15 olive drab
# paint, tyres, gun metal, crates, sacking, jerrycans - textures written beside the models (box-mapped in metres).
# usage: python3 -I bedford_oxd.py -- <stl> <out dir>
import bpy, bmesh, sys, os, math, random
from mathutils import Vector, Matrix
from PIL import Image, ImageFilter
import numpy as np
SRC, OUT = sys.argv[-2], sys.argv[-1]
S = 3.35 / 243.0
FRONT_Y, REAR_Y, HUB_Z, R = 146.0, -97.0, -78.5, 37.0      # model units, measured from the side view
BED_TOP_Z, CAB_Y = 2.0, 45.0
os.makedirs(f'{OUT}/Textures/BedfordOX', exist_ok=True)
TEX = f'{OUT}/Textures/BedfordOX'

# ---- textures (tileable, 512 px = 1 m)
def noise_tex(name, base, var=0.06, streak=False, size=512):
    rng = np.random.default_rng(abs(hash(name)) % 2**32)
    a = rng.normal(0, 1, (size // 8, size // 8))
    img = Image.fromarray(((a - a.min()) / (a.max() - a.min()) * 255).astype('uint8')).resize((size, size), Image.BICUBIC)
    n = np.array(img).astype(float) / 255 - 0.5
    fine = rng.normal(0, 0.25, (size, size))
    if streak: fine = np.repeat(rng.normal(0, 0.5, (1, size)), size, 0) * 0.6 + fine * 0.4
    k = 1 + (n * 0.6 + fine * 0.4) * var * 4
    rgb = np.clip(np.array(base)[None, None, :] * k[..., None], 0, 1)
    Image.fromarray((rgb * 255).astype('uint8')).save(f'{TEX}/{name}.jpg', quality=88)
noise_tex('Paint_SCC15', (0.30, 0.31, 0.21), 0.05)
noise_tex('Tyre', (0.07, 0.07, 0.07), 0.08)
noise_tex('GunMetal', (0.16, 0.17, 0.15), 0.05, streak=True)
noise_tex('Sacking', (0.55, 0.47, 0.33), 0.12)
noise_tex('Jerrycan', (0.26, 0.29, 0.19), 0.04)
planks = '/mnt/user-data/uploads/stansGameV2/Assets/StansGame/Textures/Village/Village_Planks.jpg'
if not os.path.exists(planks): planks = '/home/claude/assets/village/Village_Planks.jpg'
Image.open(planks).convert('RGB').resize((512, 512)).save(f'{TEX}/Crate.jpg', quality=88)

def mat(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    return m

def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.stl_import(filepath=SRC)
    o = bpy.data.objects[0]
    d = o.modifiers.new('d', 'DECIMATE'); d.ratio = 56000 / len(o.data.polygons)
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier='d')
    return o

def split(o, test, name):
    """Moves the faces for which test(centre) is true into a new object."""
    bm = bmesh.new(); bm.from_mesh(o.data)
    faces = [f for f in bm.faces if test(f.calc_center_median(), max(v.co.z for v in f.verts))]
    me2 = bpy.data.meshes.new(name); bm2 = bmesh.new()
    vm = {}
    for f in faces:
        vs = []
        for v in f.verts:
            if v not in vm: vm[v] = bm2.verts.new(v.co)
            vs.append(vm[v])
        try: bm2.faces.new(vs)
        except ValueError: pass
    bmesh.ops.delete(bm, geom=faces, context='FACES')
    bm.to_mesh(o.data); bm.free(); bm2.to_mesh(me2); bm2.free()
    n = bpy.data.objects.new(name, me2); bpy.context.collection.objects.link(n)
    return n

def build(variant):
    body = load(); body.name = 'Bedford_Body'
    V = [v.co for v in body.data.vertices]
    cx = (min(p.x for p in V) + max(p.x for p in V)) / 2
    body.data.transform(Matrix.Translation(Vector((-cx, 0, 0))))
    wheels = {}
    for key, yc, side in (('FL', FRONT_Y, -1), ('FR', FRONT_Y, 1), ('RL', REAR_Y, -1), ('RR', REAR_Y, 1)):
        wheels[key] = split(body, lambda c, zt, yc=yc, side=side: c.x * side > 34 and (Vector((c.y - yc, c.z - HUB_Z))).length < R + 2, 'Wheel_' + key)
    weapons = split(body, lambda c, zt: (zt > BED_TOP_Z + 1.5 and c.y < CAB_Y) or (zt > 43.0 and abs(c.x) < 12), 'Bedford_Guns')   # the bed guns and the AA gun on the cab roof
    if variant != 'Gun':
        bpy.data.objects.remove(weapons); weapons = None
    objs = [body] + list(wheels.values()) + ([weapons] if weapons else [])
    # bed floor: the level of the upward faces in the middle of the bed
    zs = sorted(f.center.z for f in body.data.polygons if abs(f.center.x) < 40 and -160 < f.center.y < 20 and f.normal.z > 0.9 and f.center.z < -20)
    floor = zs[len(zs) // 2] if zs else -62.0
    # to metres: ground at z = 0
    lo = min((o.matrix_world @ v.co).z for o in objs for v in o.data.vertices)
    M = Matrix.Scale(S, 4) @ Matrix.Translation(Vector((0, -(FRONT_Y + REAR_Y) / 2, -lo)))
    for o in objs: o.data.transform(M)
    floor_m = (floor - lo) * S
    # materials
    for o, mn in [(body, 'Paint_SCC15')] + [(w, 'Tyre') for w in wheels.values()] + ([(weapons, 'GunMetal')] if weapons else []):
        o.data.materials.clear(); o.data.materials.append(mat(mn))
    # wheel pivots on the hubs
    for k, w in wheels.items():
        P = [v.co for v in w.data.vertices]
        c = Vector([(min(p[i] for p in P) + max(p[i] for p in P)) / 2 for i in range(3)])
        w.data.transform(Matrix.Translation(-c)); w.location = c
    if variant == 'Loaded':
        cargo = bpy.data.meshes.new('Bedford_Cargo'); bm = bmesh.new()
        rnd = random.Random(1944)
        mats = ['Crate', 'Sacking', 'Jerrycan']
        def box(c, size, mi, yaw=0.0):
            r = bmesh.ops.create_cube(bm, size=1.0)
            vs = r['verts']
            bmesh.ops.scale(bm, vec=size, verts=vs)
            bmesh.ops.rotate(bm, verts=vs, cent=Vector(), matrix=Matrix.Rotation(yaw, 3, 'Z'))
            bmesh.ops.translate(bm, verts=vs, vec=c)
            for f in {f for v in vs for f in v.link_faces}: f.material_index = mi
        # the bed: x +-0.75, from just behind the cab back to the tailboard
        yb0, yb1 = (REAR_Y - 70 - (FRONT_Y + REAR_Y) / 2) * S + 0.25, (CAB_Y - 8 - (FRONT_Y + REAR_Y) / 2) * S - 0.1
        y = yb0
        while y < yb1 - 0.4:
            for x in (-0.42, 0.0, 0.42):
                h = 0.0
                for lvl in range(rnd.choice((2, 3, 3, 4))):
                    sz = Vector((0.38, 0.55, 0.3)); box(Vector((x, y + 0.28, floor_m + h + sz.z / 2)), sz, 0, rnd.uniform(-0.06, 0.06)); h += sz.z
            y += 0.62
        # sacks along the front, jerrycans at the tailboard
        for i in range(4):
            box(Vector((-0.55 + i * 0.37, yb1 - 0.25, floor_m + 0.16)), Vector((0.34, 0.6, 0.3)), 1, rnd.uniform(-0.2, 0.2))
        for i in range(6):
            box(Vector((-0.6 + i * 0.24, yb0 - 0.05, floor_m + 0.24)), Vector((0.17, 0.35, 0.47)), 2)
        bm.to_mesh(cargo); bm.free()
        co = bpy.data.objects.new('Bedford_Cargo', cargo); bpy.context.collection.objects.link(co)
        for m in mats: cargo.materials.append(mat(m))
        objs.append(co)
    # box-mapped UVs in metres, smooth shading
    for o in objs:
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.cube_project(cube_size=1.0); bpy.ops.object.mode_set(mode='OBJECT')
        for p in o.data.polygons: p.use_smooth = o.name.startswith('Wheel')
    name = {'Gun': 'BedfordOX_Gun', 'Plain': 'BedfordOX', 'Loaded': 'BedfordOX_Loaded'}[variant]
    tri = sum(len(p.vertices) - 2 for o in objs for p in o.data.polygons)
    P = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    print('RESULT', name, 'tris', tri, 'size', [round(max(p[i] for p in P) - min(p[i] for p in P), 2) for i in range(3)], 'floor', round(floor_m, 2),
          {k: [round(x, 2) for x in w.location] for k, w in wheels.items()})
    bpy.ops.export_scene.fbx(filepath=f'{OUT}/{name}.fbx', use_selection=False, axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                             bake_space_transform=True, mesh_smooth_type='FACE', embed_textures=False, path_mode='STRIP')

for v in ('Gun', 'Plain', 'Loaded'): build(v)
