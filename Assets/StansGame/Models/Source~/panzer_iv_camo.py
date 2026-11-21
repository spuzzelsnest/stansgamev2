# Panzer IV Ausf. H in three-tone camouflage (_downloads/panzer-iv-medium-tank-toshueyi, a 3DS game rip) for the
# Château de Bénouville armour. Blender has no 3DS importer any more, so the file is read by r3ds.py (in this folder).
# Split into PzIV_Hull, PzIV_Turret (origin on the turret ring) and PzIV_Gun (origin on the trunnions) so
# TankController can traverse and elevate them. Blender X right, Y front, Z up; 7.02 m long with the gun.
# usage: python3 -I panzer_iv_camo.py <3ds> <texture dir> <out.fbx>
import bpy, bmesh, sys, os
from mathutils import Vector
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from r3ds import read3ds
SRC, TEX, OUT = sys.argv[-3], sys.argv[-2], sys.argv[-1]
bpy.ops.wm.read_factory_settings(use_empty=True)
objs, _ = read3ds(SRC)
objs = [o for o in objs if o['v']]
allv = [v for o in objs for v in o['v']]
lo = [min(v[i] for v in allv) for i in range(3)]; hi = [max(v[i] for v in allv) for i in range(3)]
S = 7.02 / (hi[1] - lo[1]); cx = (lo[0] + hi[0]) / 2; cy = (lo[1] + hi[1]) / 2
def conv(v): return Vector((-(v[0] - cx) * S, -(v[1] - cy) * S, (v[2] - lo[2]) * S))   # source front is -y: turn 180° so it faces +Y (a turn, not a mirror)
def mat(name, img):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
    t = m.node_tree.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(os.path.join(TEX, img))
    m.node_tree.links.new(t.outputs['Color'], b.inputs['Base Color']); b.inputs['Roughness'].default_value = 0.7; b.inputs['Metallic'].default_value = 0.2
    return m
BODY = mat('PzIV_Body', 'Body_D.png'); CHAIN = mat('PzIV_Tracks', 'Chain_D.png')
# turret box in source units: the turret draw call's bounds
tur = next(o for o in objs if o['name'] == 'DrawCall_1')
tz0 = min(v[2] for v in tur['v']) - 1; tx = [min(v[0] for v in tur['v']) - 2, max(v[0] for v in tur['v']) + 2]
ty = [min(v[1] for v in tur['v']) - 2, max(v[1] for v in tur['v']) + 2]
def in_turret(vs): return all(v[2] > tz0 and tx[0] < v[0] < tx[1] and ty[0] < v[1] < ty[1] for v in vs)
parts = {'PzIV_Hull': bmesh.new(), 'PzIV_Turret': bmesh.new(), 'PzIV_Gun': bmesh.new()}
uvl = {k: b.loops.layers.uv.new('UVMap') for k, b in parts.items()}
def add(bm, uvlayer, o, faces, mi):
    vmap = {}
    for f in faces:
        vs = []
        for i in f:
            if i not in vmap: vmap[i] = bm.verts.new(conv(o['v'][i]))
            vs.append(vmap[i])
        try: nf = bm.faces.new(vs)
        except ValueError: continue
        nf.material_index = mi
        for l, i in zip(nf.loops, f):
            if o['uv']: l[uvlayer].uv = o['uv'][i]
for o in objs:
    mi = 1 if o['name'] == 'Chain' else 0
    if o['name'] in ('DrawCall_6', 'DrawCall_7'): add(parts['PzIV_Gun'], uvl['PzIV_Gun'], o, o['f'], mi); continue
    if o['name'] == 'DrawCall_1': add(parts['PzIV_Turret'], uvl['PzIV_Turret'], o, o['f'], mi); continue
    if o['name'] == 'DrawCall_8':
        # mixed: split into loose parts, the ones on the turret go with it
        adj = {}
        for f in o['f']:
            for a in f: adj.setdefault(a, set()).update(f)
        seen = set(); tf, hf = [], []
        comp_of = {}
        for s in adj:
            if s in seen: continue
            st = [s]; comp = set([s]); seen.add(s)
            while st:
                a = st.pop()
                for b in adj[a]:
                    if b not in seen: seen.add(b); comp.add(b); st.append(b)
            on = in_turret([o['v'][i] for i in comp])
            for i in comp: comp_of[i] = on
        for f in o['f']: (tf if comp_of[f[0]] else hf).append(f)
        add(parts['PzIV_Turret'], uvl['PzIV_Turret'], o, tf, mi); add(parts['PzIV_Hull'], uvl['PzIV_Hull'], o, hf, mi)
        continue
    add(parts['PzIV_Hull'], uvl['PzIV_Hull'], o, o['f'], mi)
made = {}
for k, bm in parts.items():
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    me = bpy.data.meshes.new(k); bm.to_mesh(me); bm.free()
    me.materials.append(BODY); me.materials.append(CHAIN)
    ob = bpy.data.objects.new(k, me); bpy.context.collection.objects.link(ob); made[k] = ob
def origin(ob, p):
    ob.data.transform(__import__('mathutils').Matrix.Translation(-p)); ob.location = p
tv = [v.co for v in made['PzIV_Turret'].data.vertices]
ring = Vector(((min(v.x for v in tv) + max(v.x for v in tv)) / 2, (min(v.y for v in tv) + max(v.y for v in tv)) / 2, min(v.z for v in tv)))
origin(made['PzIV_Turret'], ring)
gv = [v.co for v in made['PzIV_Gun'].data.vertices]
gy0 = min(v.y for v in gv); gz = sum(v.z for v in gv) / len(gv)
trun = Vector((ring.x, gy0 + 0.05, gz))
origin(made['PzIV_Gun'], trun)
made['PzIV_Gun'].parent = made['PzIV_Turret']; made['PzIV_Gun'].location = trun - ring
made['PzIV_Turret'].parent = made['PzIV_Hull']
muzzle = bpy.data.objects.new('PzIV_Muzzle', None); muzzle.parent = made['PzIV_Gun']
muzzle.location = (0, max(v.y for v in gv) - gy0, 0); bpy.context.collection.objects.link(muzzle)
print({k: (len(o.data.vertices), tuple(round(x, 2) for x in o.dimensions)) for k, o in made.items()}, 'ring', ring, 'trunnion', trun)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH', 'EMPTY'}, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', path_mode='COPY', embed_textures=True, mesh_smooth_type='FACE', bake_anim=False)
