"""Sd.Kfz. 2 Kettenkrad for Stan's Game (23 Nov 2026), from Joan's download (_downloads/_Vehicles/sdkfz-2-kettenkrad).
The model is already in metres at true size (3.0 m long, 1.0 m wide, 1.2 m high). This script:
- joins the body, tracks and fittings into one "Body" (materials Kettenkrad_1..4, one per UDIM tile: the UVs of tile
  100n lie in u n-1..n, which a repeating texture reads the same);
- makes the steered front wheel "Wheel_F" and each road wheel / sprocket of the tracks "Wheel_R.." with its pivot on
  its hub (TruckController rolls them; with steerByName only Wheel_F steers);
- thins the heaviest meshes (track links, interleaved road wheels);
- exports flat (no parent-child: see the 1/100 lesson) to Models/Vehicles/Kettenkrad.fbx.
Usage: python3 -I kettenkrad.py -- <in.blend> <out.fbx>"""
import bpy, bmesh, sys
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0])
OUT = args[1]

for o in list(bpy.data.objects):
    if o.type != 'MESH': bpy.data.objects.remove(o, do_unlink=True)
for o in bpy.data.objects:
    o.hide_set(False); o.hide_viewport = False
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects: o.select_set(True)
bpy.context.view_layer.objects.active = bpy.data.objects[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

for i in range(1, 5):
    m = bpy.data.materials.get(f'UDIM{i}')
    if m: m.name = f'Kettenkrad_{i}'

def decimate(o, ratio):
    mod = o.modifiers.new('d', 'DECIMATE'); mod.ratio = ratio
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier='d')

objs = {o.name.split('_low')[0]: o for o in bpy.data.objects}
decimate(objs['Track02'], 0.45); decimate(objs['Track01'], 0.6); decimate(objs['Screws'], 0.5)
decimate(objs['Wheel2'], 0.6); decimate(objs['Wheel3'], 0.7)

def origin_to_centre(o):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')

# ---- the front wheel
fw = objs['FrontWheel']; fw.name = 'Wheel_F'; origin_to_centre(fw)

# ---- the track wheels: split into loose parts, group them on their hubs (the big round parts), one object per hub
wheel_objs = [objs[k] for k in ('Wheel1Outside', 'Wheel1Cogs', 'Wheel2', 'Wheel3')]
parts = []
for o in wheel_objs:
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.separate(type='LOOSE'); bpy.ops.object.mode_set(mode='OBJECT')
    parts += [p for p in bpy.context.selected_objects]
def box(o):
    bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
    mn = Vector([min(v[i] for v in bb) for i in range(3)]); mx = Vector([max(v[i] for v in bb) for i in range(3)])
    return mn, mx
hubs = []   # (centre, radius) of each big wheel disc
for p in parts:
    mn, mx = box(p); size = mx - mn
    if size.y > 0.25 and size.z > 0.25 and abs(size.y - size.z) < 0.08:
        c = (mn + mx) / 2
        if not any((c - h).length < 0.12 for h, _ in hubs): hubs.append((c, max(size.y, size.z) / 2))
groups = {i: [] for i in range(len(hubs))}
for p in parts:
    mn, mx = box(p); c = (mn + mx) / 2
    best = min(range(len(hubs)), key=lambda i: ((c - hubs[i][0]) * Vector((0.3, 1, 1))).length)
    groups[best].append(p)
for i, (c, r) in enumerate(hubs):
    g = groups[i]
    if not g: continue
    bpy.ops.object.select_all(action='DESELECT')
    for p in g: p.select_set(True)
    bpy.context.view_layer.objects.active = g[0]
    if len(g) > 1: bpy.ops.object.join()
    w = bpy.context.view_layer.objects.active
    side = 'L' if c.x < 0 else 'R'
    w.name = f'Wheel_R{side}{i:02d}'
    origin_to_centre(w)
print('KK hubs', len(hubs), [(round(c.x, 2), round(c.y, 2), round(c.z, 2), round(r, 2)) for c, r in hubs])

# ---- the rest is the body
rest = [o for o in bpy.data.objects if not o.name.startswith('Wheel_')]
bpy.ops.object.select_all(action='DESELECT')
for o in rest: o.select_set(True)
bpy.context.view_layer.objects.active = rest[0]
bpy.ops.object.join(); body = bpy.context.view_layer.objects.active; body.name = 'Body'
bpy.ops.object.origin_set(type='ORIGIN_CURSOR')   # the model's own origin: on the ground, mid-length

for o in bpy.data.objects:
    o.parent = None
    for s in o.material_slots:
        if s.material and not s.material.name.startswith('Kettenkrad_'): s.material = bpy.data.materials.get('Kettenkrad_1')
print('KK verts', sum(len(o.data.vertices) for o in bpy.data.objects), 'objects', len(bpy.data.objects))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                         bake_space_transform=True, mesh_smooth_type='FACE', embed_textures=False, path_mode='STRIP')
