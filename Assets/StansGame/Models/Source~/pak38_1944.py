# 5 cm PaK 38 anti-tank gun, modelled from scratch to the real gun (overall 4.75 m, width 1.85 m, height 1.05 m):
# L/60 barrel with the double-baffle muzzle brake, the spaced double shield bent back at the top, cast spoked wheels
# with solid tyres, tubular split trails opened out with spades, ZF 3x8 sight and handwheels on the left.
# Three moving parts so the gun can be laid in the game:
#   Pak38_Carriage  wheels, axle, trails (fixed)            origin: ground under the axle
#   Pak38_Upper     shield, cradle, recoil gear, sight      origin: traverse pivot (on the axle line)
#   Pak38_Barrel    barrel, breech, muzzle brake            origin: trunnions (elevation pivot)
# and two markers: Pak38_Muzzle (the muzzle) and Pak38_Sight (the gunner's eye at the sight).
# Blender X right, Y = the way it fires, Z up.  Run: python3 -I pak38_1944.py -- <out dir> <tmp dir>
import bpy, sys, os, math, random, mathutils
from PIL import Image, ImageDraw, ImageFilter
OUT, TMP = sys.argv[sys.argv.index('--') + 1:][:2]
V = mathutils.Vector
random.seed(1944); os.makedirs(TMP, exist_ok=True); os.makedirs(os.path.join(OUT, 'Props/SME'), exist_ok=True)

# ---------------------------------------------------------------- textures (dunkelgelb, olive camouflage, wear)
def paint_tex(path, size=1024):
    im = Image.new('RGB', (size, size), (172, 150, 104)); d = ImageDraw.Draw(im)
    for _ in range(14):   # sprayed olive-green bands, soft-edged
        x, y = random.randrange(size), random.randrange(size); r = random.randrange(90, 220)
        pts = [(x + r * math.cos(a) * random.uniform(0.5, 1.4), y + r * math.sin(a) * random.uniform(0.3, 0.9)) for a in [i * math.pi / 9 for i in range(18)]]
        d.polygon(pts, fill=(78, 84, 56))
    im = im.filter(ImageFilter.GaussianBlur(9)); px = im.load()
    for _ in range(size * size // 5):
        x, y = random.randrange(size), random.randrange(size); k = random.uniform(0.9, 1.06)
        r, g, b = px[x, y]; px[x, y] = (int(min(255, r * k)), int(min(255, g * k)), int(min(255, b * k)))
    d = ImageDraw.Draw(im)
    for _ in range(900):   # chips to red primer and bare steel, small streaks
        x, y = random.randrange(size), random.randrange(size); s = random.randrange(1, 4)
        d.ellipse((x, y, x + s, y + s), fill=random.choice([(64, 60, 54), (92, 86, 76), (110, 62, 46)]))
    for _ in range(140):
        x, y = random.randrange(size), random.randrange(size); d.line((x, y, x + random.randrange(-3, 3), y + random.randrange(10, 40)), fill=(120, 108, 84), width=1)
    grad = Image.linear_gradient('L').resize((size, size))
    im = Image.composite(Image.new('RGB', (size, size), (98, 86, 66)), im, grad.point(lambda v: int(max(0, v - 170) * 1.1)))
    im.save(path)
def flat_tex(path, col, noise=0.08, size=256):
    im = Image.new('RGB', (size, size), col); px = im.load()
    for y in range(size):
        for x in range(size):
            k = 1 + random.uniform(-noise, noise); px[x, y] = tuple(int(max(0, min(255, c * k))) for c in col)
    im.save(path)
paint_tex(os.path.join(TMP, 'Pak38_Paint.png'))
flat_tex(os.path.join(TMP, 'Pak38_Steel.png'), (60, 58, 54))
flat_tex(os.path.join(TMP, 'Pak38_Rubber.png'), (34, 33, 31), 0.12)

bpy.ops.wm.read_factory_settings(use_empty=True)
def material(name, tex, metal, rough):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree
    b = nt.nodes['Principled BSDF']; b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(tex); nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    return m
PAINT = material('Pak38_Paint', os.path.join(TMP, 'Pak38_Paint.png'), 0.25, 0.65)
STEEL = material('Pak38_Steel', os.path.join(TMP, 'Pak38_Steel.png'), 0.65, 0.4)
RUBBER = material('Pak38_Rubber', os.path.join(TMP, 'Pak38_Rubber.png'), 0.0, 0.9)
GROUPS = {'Carriage': [], 'Upper': [], 'Barrel': []}
CUR = ['Carriage']
def finish(o, mat, uv=0.8, bevel=0.0):
    o.data.materials.append(mat)
    if bevel > 0:
        m = o.modifiers.new('b', 'BEVEL'); m.width = bevel; m.segments = 1; m.limit_method = 'ANGLE'
    bpy.context.view_layer.objects.active = o; o.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.cube_project(cube_size=uv); bpy.ops.object.mode_set(mode='OBJECT'); o.select_set(False)
    GROUPS[CUR[0]].append(o); return o
def box(size, loc, rot=(0, 0, 0), mat=PAINT, bevel=0.006):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot); o = bpy.context.active_object
    o.scale = size; bpy.ops.object.transform_apply(location=False, rotation=False, scale=True); return finish(o, mat, bevel=bevel)
def cyl(r, depth, loc, rot=(0, 0, 0), mat=PAINT, verts=24, r2=None):
    if r2 is None: bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc, rotation=rot)
    else: bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=depth, location=loc, rotation=rot)
    return finish(bpy.context.active_object, mat, 0.4)
def tube(a, b, r, mat=PAINT, verts=12, r2=None):
    a, b = V(a), V(b); d = b - a
    o = cyl(r, d.length, (a + b) / 2, mat=mat, verts=verts, r2=r2)
    o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = V((0, 0, 1)).rotation_difference(d.normalized()); return o
def bar(a, b, w, h, mat=PAINT):
    a, b = V(a), V(b); d = b - a
    o = box((w, d.length, h), (a + b) / 2, mat=mat)
    o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = V((0, 1, 0)).rotation_difference(d.normalized()); return o
def torus(R, r, loc, rot, mat=STEEL):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, major_segments=36 if R > 0.2 else 20, minor_segments=8, location=loc, rotation=rot)
    return finish(bpy.context.active_object, mat)
X90 = (math.radians(90), 0, 0); Y90 = (0, math.radians(90), 0)
AX, WR = 0.84, 0.405          # half track, wheel radius
CZ, TY = 0.86, 0.06           # bore height, trunnion y

# ======================================================== carriage: wheels, axle, trails
CUR[0] = 'Carriage'
for s in (-1, 1):
    x = s * AX
    torus(WR - 0.045, 0.045, (x, 0, WR), Y90, RUBBER)                # solid rubber tyre
    torus(WR - 0.085, 0.022, (x, 0, WR), Y90, PAINT)                 # cast rim
    cyl(0.1, 0.17, (x, 0, WR), Y90, PAINT, 20)                       # hub
    cyl(0.06, 0.06, (x + s * 0.1, 0, WR), Y90, STEEL, 16)            # hub cap
    for k in range(10):                                              # ten spokes
        a = k * 2 * math.pi / 10
        bar((x, 0.09 * math.cos(a), WR + 0.09 * math.sin(a)), (x, 0.32 * math.cos(a), WR + 0.32 * math.sin(a)), 0.034, 0.03)
    for k in range(6):
        a = k * math.pi / 3
        cyl(0.014, 0.03, (x + s * 0.09, 0.075 * math.cos(a), WR + 0.075 * math.sin(a)), Y90, STEEL, 8)
tube((-AX, 0, WR), (AX, 0, WR), 0.042, STEEL, 12)                     # axle
for s in (-1, 1):                                                    # leaf springs over the axle
    box((0.07, 0.42, 0.05), (s * 0.5, 0, WR + 0.07), mat=STEEL)
box((0.92, 0.36, 0.14), (0, -0.02, WR + 0.12))                       # bottom carriage (cross-beam)
box((0.34, 0.42, 0.16), (0, -0.06, WR + 0.24))                       # traverse bed
for s in (-1, 1):                                                    # tubular trails, opened out
    a = V((s * 0.22, -0.3, WR + 0.14)); b = V((s * 0.66, -2.16, 0.16))
    tube(a, b, 0.065, PAINT, 16, r2=0.05)
    box((0.18, 0.24, 0.2), (s * 0.24, -0.32, WR + 0.12))             # trail hinge
    for t in (0.35, 0.62, 0.8):                                      # clamps / fittings along the trail
        p = a.lerp(b, t); tube(p - V((0.07, 0, 0)), p + V((0.07, 0, 0)), 0.073, STEEL, 12)
    box((0.42, 0.05, 0.34), (b.x + s * 0.02, b.y - 0.05, 0.12), (math.radians(-28), 0, 0))   # spade
    box((0.42, 0.18, 0.04), (b.x + s * 0.02, b.y + 0.03, 0.25))                              # spade top plate
    p = a.lerp(b, 0.72); tube(p + V((s * 0.0, 0, 0.07)), p + V((s * 0.0, 0, 0.2)), 0.012, STEEL, 8)
    torus(0.05, 0.01, (p.x, p.y, p.z + 0.24), Y90)                   # lifting handle
    torus(0.06, 0.016, (b.x, b.y - 0.2, 0.2), X90)                   # towing eye
# ======================================================== upper carriage: shield, cradle, recoil gear, sight
CUR[0] = 'Upper'
SY = 0.34
def shield_plate(y, w, thick, top):
    # lower part upright, upper part bent back, the outer edges bent back to the sides
    lo_h, up_h = 0.42, top - 0.42 - 0.44
    box((w, thick, lo_h), (0, y, 0.44 + lo_h / 2), (math.radians(-6), 0, 0))
    yb = y - math.sin(math.radians(22)) * up_h / 2 - 0.02
    box((w - 0.08, thick, up_h), (0, yb, 0.44 + lo_h + up_h / 2 - 0.01), (math.radians(-22), 0, 0))
    for s in (-1, 1):
        box((0.24, thick, lo_h + up_h * 0.8), (s * (w / 2 + 0.09), y - 0.08, 0.44 + (lo_h + up_h * 0.8) / 2), (math.radians(-10), 0, s * math.radians(-30)))
shield_plate(SY, 1.5, 0.014, 1.14)
shield_plate(SY - 0.06, 1.44, 0.012, 1.1)                            # the spaced inner plate
box((1.3, 0.016, 0.26), (0, SY + 0.03, 0.29), (math.radians(10), 0, 0))   # lower apron between the wheels
for s in (-1, 1):
    for k in range(7):                                               # rivets along the top and the bends
        cyl(0.011, 0.03, (s * (0.12 + k * 0.1), SY + 0.012, 0.85), X90, PAINT, 6)
    for k in range(4):
        cyl(0.011, 0.03, (s * 0.74, SY - 0.02, 0.5 + k * 0.1), X90, PAINT, 6)
box((0.3, 0.06, 0.24), (0, SY + 0.02, CZ), mat=PAINT)                # mantlet plate round the barrel opening
box((0.1, 0.03, 0.06), (-0.32, SY + 0.03, 1.0), mat=RUBBER)          # sight aperture
box((0.13, 0.02, 0.08), (-0.32, SY + 0.045, 1.05), (math.radians(-30), 0, 0))   # its sliding cover
box((0.24, 1.1, 0.16), (0, TY + 0.05, CZ - 0.16))                    # cradle
for k in range(9):                                                   # teeth of the elevating arc
    box((0.03, 0.03, 0.035), (-0.13, TY - 0.3 + k * 0.035, CZ - 0.27 - 0.06 * math.sin(k * 0.35)), mat=STEEL)
box((0.03, 0.34, 0.16), (-0.13, TY - 0.15, CZ - 0.3))                # elevating arc
for s in (-1, 1):
    tube((s * 0.08, TY - 0.32, CZ + 0.08), (s * 0.08, TY + 0.72, CZ + 0.08), 0.045, PAINT, 16)   # recoil buffer, recuperator
    box((0.06, 0.12, 0.12), (s * 0.15, TY, CZ - 0.04))               # trunnion brackets
box((0.05, 0.3, 0.07), (-0.2, TY - 0.05, CZ + 0.16), mat=STEEL)      # ZF 3x8 sight
tube((-0.2, TY - 0.2, CZ + 0.16), (-0.2, TY - 0.32, CZ + 0.16), 0.026, RUBBER, 10)
box((0.04, 0.06, 0.12), (-0.2, TY + 0.02, CZ + 0.08), mat=STEEL)     # sight bracket
torus(0.11, 0.012, (-0.3, TY - 0.2, CZ - 0.16), Y90)                 # elevating handwheel
tube((-0.36, TY - 0.2 + 0.11, CZ - 0.16), (-0.42, TY - 0.2 + 0.11, CZ - 0.16), 0.012, STEEL, 8)
torus(0.09, 0.011, (-0.24, TY - 0.36, CZ - 0.42), X90)               # traversing handwheel
box((0.12, 0.22, 0.08), (0.24, -0.12, CZ - 0.34), mat=PAINT)         # tool box
# ======================================================== barrel group (elevates; slides back in recoil)
CUR[0] = 'Barrel'
B0, B1 = TY - 0.36, TY + 2.12
tube((0, B0 + 0.3, CZ), (0, B1, CZ), 0.062, PAINT, 28, r2=0.045)    # the long L/60 tube
tube((0, B0, CZ), (0, B0 + 0.42, CZ), 0.072, PAINT, 28)              # jacket at the breech end
tube((0, B0 + 0.42, CZ), (0, B0 + 0.47, CZ), 0.08, STEEL, 28)        # collar
box((0.22, 0.32, 0.22), (0, B0 - 0.1, CZ), mat=STEEL)                # breech ring (horizontal sliding block)
box((0.24, 0.12, 0.1), (0.02, B0 - 0.2, CZ + 0.02), mat=STEEL)      # breech block
bar((0.12, B0 - 0.18, CZ + 0.05), (0.2, B0 - 0.36, CZ + 0.1), 0.025, 0.025, STEEL)   # breech lever
box((0.3, 0.06, 0.04), (0, B0 - 0.32, CZ - 0.1), mat=STEEL)          # recoil guard
MB = B1
tube((0, MB - 0.02, CZ), (0, MB + 0.05, CZ), 0.055, STEEL, 24)
for yb in (MB + 0.09, MB + 0.25):                                    # double-baffle muzzle brake
    box((0.16, 0.045, 0.12), (0, yb, CZ), mat=STEEL, bevel=0.01)
for s in (-1, 1):
    box((0.02, 0.2, 0.12), (s * 0.07, MB + 0.17, CZ), mat=STEEL)
for zz in (-1, 1):
    box((0.14, 0.2, 0.02), (0, MB + 0.17, CZ + zz * 0.05), mat=STEEL)
tube((0, MB + 0.27, CZ), (0, MB + 0.28, CZ), 0.023, RUBBER, 12)      # bore

# ---------------------------------------------------------------- join each group; origins at the pivots
PIVOT = {'Carriage': V((0, 0, 0)), 'Upper': V((0, 0, WR + 0.32)), 'Barrel': V((0, TY, CZ))}
made = {}
for name, objs in GROUPS.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.convert(target='MESH')                             # applies the bevels
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join()
    j = bpy.context.active_object; j.name = j.data.name = 'Pak38_' + name
    j.data.transform(mathutils.Matrix.Translation(-PIVOT[name])); j.location = PIVOT[name]
    made[name] = j
    print(j.name, len(j.data.polygons), 'faces')
for nm, loc in (('Pak38_Muzzle', V((0, MB + 0.3, CZ))), ('Pak38_Sight', V((-0.2, TY - 0.42, CZ + 0.16)))):
    e = bpy.data.objects.new(nm, None); bpy.context.scene.collection.objects.link(e); e.location = loc
allv = [made[n].matrix_world @ v.co for n in made for v in made[n].data.vertices]
lo = V([min(p[i] for p in allv) for i in range(3)]); hi = V([max(p[i] for p in allv) for i in range(3)])
print('Pak38 size', tuple(round(x, 2) for x in hi - lo), 'faces', sum(len(m.data.polygons) for m in made.values()))
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'Props/SME/Pak38.fbx'), use_selection=False, object_types={'MESH', 'EMPTY'},
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
                         path_mode='COPY', embed_textures=True, bake_anim=False, mesh_smooth_type='FACE')
# previews
sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.color_type = 'TEXTURE'
sc.render.resolution_x = 900; sc.render.resolution_y = 560; sc.world = bpy.data.worlds.new('w')
cam = bpy.data.cameras.new('c'); co = bpy.data.objects.new('c', cam); sc.collection.objects.link(co); sc.camera = co; cam.lens = 38
for i, (loc, tgt) in enumerate((((3.4, 3.6, 1.9), (0, 0.2, 0.6)), ((-3.6, -2.6, 1.7), (0, -0.3, 0.6)), ((-1.6, 0.9, 1.25), (0, 0.3, 0.75)))):
    co.location = loc; co.rotation_euler = (V(tgt) - V(loc)).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(TMP, f'pak38_v{i}.png'); bpy.ops.render.render(write_still=True)
