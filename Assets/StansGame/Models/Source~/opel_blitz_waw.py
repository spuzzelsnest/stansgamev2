# Opel Blitz 3.6-36S, covered and open-bed, from the CoD World at War truck pack (V_Trucks.fbx).
# Blender X = right, Y = front, Z up (same convention as sme_props_1944b.py). 6.02 m long, wheels as separate objects.
import bpy, bmesh, os, sys, mathutils
from mathutils import Vector
SRC = sys.argv[-3]; TEX = sys.argv[-2]; OUT = sys.argv[-1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
bpy.context.view_layer.update()

def mat(name, img, rough=0.75, metal=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes['Principled BSDF']
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(os.path.join(TEX, img))
    nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    return m
MATS = {'opel_blitz': mat('OpelBlitz_Body', 'OpelBlitz_BaseColor.jpg'),
        'wheel': mat('OpelBlitz_Wheel', 'OpelBlitz_Wheel_BaseColor.jpg', 0.9),
        'glass': mat('OpelBlitz_Glass', 'OpelBlitz_Glass_BaseColor.jpg', 0.15)}
def remap(o):
    for i, s in enumerate(o.material_slots):
        n = (s.material.name if s.material else '').lower()
        k = 'glass' if 'glass' in n else 'wheel' if 'wheel' in n else 'opel_blitz'
        o.material_slots[i].material = MATS[k]

def bake(o):
    """apply modifiers (armature in rest pose) and the world matrix, unparent"""
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    me.transform(o.matrix_world)
    n = bpy.data.objects.new(o.name.replace('.smd', ''), me); bpy.context.scene.collection.objects.link(n)
    for i, s in enumerate(o.material_slots): n.data.materials[i] = s.material
    return n, o

def build(tag, arm_name, xs):
    arm = bpy.data.objects[arm_name]
    hubs = {b.name.replace('tag_wheel_', ''): arm.matrix_world @ b.head_local for b in arm.data.bones if b.name.startswith('tag_wheel')}
    src = [o for o in bpy.data.objects if o.type == 'MESH' and 'opel_blitz' in o.name and abs((o.matrix_world @ Vector([sum(c[i] for c in o.bound_box) / 8 for i in range(3)])).x - xs) < 1.5
           and (('covered' in o.name) == (tag == 'Covered'))]
    print(tag, [o.name for o in src])
    body, wheels = [], {k: [] for k in hubs}
    for o in src:
        n, _ = bake(o); remap(n)
        if 'wheels' not in n.name: body.append(n); continue
        # split the wheel set into loose parts and give each to the nearest hub (in x and y only: hubs sit at z 0 in the rip)
        bm = bmesh.new(); bm.from_mesh(n.data); bm.verts.ensure_lookup_table()
        parts = []; seen = set()
        for v in bm.verts:
            if v.index in seen: continue
            st = [v]; comp = []; seen.add(v.index)
            while st:
                a = st.pop(); comp.append(a.index)
                for e in a.link_edges:
                    b = e.other_vert(a)
                    if b.index not in seen: seen.add(b.index); st.append(b)
            parts.append(comp)
        bm.free()
        groups = {k: [] for k in hubs}; rest = []
        for comp in parts:
            c = sum((n.data.vertices[i].co for i in comp), Vector()) / len(comp)
            k, d = min(((k, ((c.x - h.x) ** 2 + (c.y - h.y) ** 2) ** 0.5) for k, h in hubs.items()), key=lambda t: t[1])
            (groups[k] if d < 0.6 else rest).extend(comp)
        for k, idx in list(groups.items()) + [('rest', rest)]:
            if not idx: continue
            keep = set(idx)
            c = n.copy(); c.data = n.data.copy(); bpy.context.scene.collection.objects.link(c)
            bm = bmesh.new(); bm.from_mesh(c.data); bm.verts.ensure_lookup_table()
            bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in keep], context='VERTS'); bm.to_mesh(c.data); bm.free()
            if k == 'rest': body.append(c)
            else: c.name = 'Wheel_' + k; wheels[k].append(c)
        bpy.data.objects.remove(n)
    # join the body
    bpy.ops.object.select_all(action='DESELECT')
    for o in body: o.select_set(True)
    bpy.context.view_layer.objects.active = body[0]; bpy.ops.object.join(); b = bpy.context.active_object; b.name = 'OpelBlitz' + tag
    allp = [b] + [w for l in wheels.values() for w in l]
    pts = [o.matrix_world @ v.co for o in allp for v in o.data.vertices]
    lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)])
    s = 6.02 / (hi.y - lo.y)
    M = mathutils.Matrix.Scale(s, 4) @ mathutils.Matrix.Rotation(3.14159265, 4, 'Z') @ mathutils.Matrix.Translation(Vector(((lo.x + hi.x) / -2, (lo.y + hi.y) / -2, -lo.z)))
    for o in allp:
        o.data.transform(M)
    # wheels: origin at the hub so they can turn
    for o in allp[1:]:
        vs = [v.co for v in o.data.vertices]; c = Vector([(min(v[i] for v in vs) + max(v[i] for v in vs)) / 2 for i in range(3)])
        o.data.transform(mathutils.Matrix.Translation(-c)); o.location = c
        o.name = 'Wheel_' + ('F' if c.y > 0 else 'R') + ('L' if c.x < 0 else 'R')
    print(tag, 'scale', round(s, 3), 'size', [round((hi[i] - lo[i]) * s, 2) for i in range(3)], [o.name for o in allp])
    return allp

for tag, arm, xs in (('Covered', 'Opel Blitz covered_ARM', 4.0), ('Open', 'Opel Blitz_ARM', 8.0)):
    objs = build(tag, arm, xs)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, f'OpelBlitz{tag}.fbx'), use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', path_mode='COPY', embed_textures=True,
                             bake_anim=False, mesh_smooth_type='FACE')
    for o in objs: bpy.data.objects.remove(o)
