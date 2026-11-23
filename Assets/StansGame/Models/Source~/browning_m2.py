# Browning M2HB .50 on the M3 tripod (Joan's m2-browning-machine-gun, Collada). Real gun length 1.654 m.
# Gun to +Y, tripod feet on z=0; M2_Gun pivots on the pintle head (traverses and elevates there), M2_Muzzle at the
# end of the barrel, M2_Tripod static. Materials M2_<n>, textures <material>.jpg.
import bpy, sys, numpy as np, bmesh
from mathutils import Vector, Matrix
NPZ, OUT = sys.argv[-2:]
bpy.ops.wm.read_factory_settings(use_empty=True)
D = np.load(NPZ)
mats = sorted({k.split('__')[0] for k in D.files})
TRIPOD = {'lambert4SG', 'lambert10SG', 'lambert12SG'}
DEC = {'lambert11SG': 0.3, 'lambert4SG': 0.45}
S = 1.654 / 2.0
def conv(V):   # collada Y-up, muzzle -X  ->  Blender Z-up, muzzle +Y
    return np.stack([-V[:, 2], -V[:, 0], V[:, 1]], 1) * S
allV = np.vstack([conv(D[m + '__V']) for m in mats]); zmin = allV[:, 2].min()
piv = Vector(conv(np.array([[0.27, 0.10, 0.0]]))[0]); piv.z -= zmin
muz = Vector(conv(np.array([[-1.0, 0.25, 0.0]]))[0]); muz.z -= zmin
def build(name, keys):
    me = bpy.data.meshes.new(name); bm = bmesh.new(); uv = bm.loops.layers.uv.new()
    for mi, k in enumerate(keys):
        V = conv(D[k + '__V']); V[:, 2] -= zmin; T = D[k + '__T']
        for i in range(0, len(V), 3):
            try: f = bm.faces.new([bm.verts.new(V[i + j]) for j in range(3)])
            except ValueError: continue
            f.material_index = mi
            for j, l in enumerate(f.loops): l[uv].uv = T[i + j]
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    for k in keys:
        m = bpy.data.materials.get('M2_' + k[7:-2]) or bpy.data.materials.new('M2_' + k[7:-2]); me.materials.append(m)
    for p in me.polygons: p.use_smooth = True
    return o
gun = build('M2_Gun', [m for m in mats if m not in TRIPOD])
tri = build('M2_Tripod', [m for m in mats if m in TRIPOD])
for o, r in ((gun, 0.55), (tri, 0.45)):
    bpy.context.view_layer.objects.active = o
    dm = o.modifiers.new('d', 'DECIMATE'); dm.ratio = r; bpy.ops.object.modifier_apply(modifier='d')
    sm = o.modifiers.new('n', 'WEIGHTED_NORMAL') if hasattr(o.modifiers, 'new') else None
# the gun's origin on the pivot
gun.data.transform(Matrix.Translation(-piv)); gun.location = piv
mz = bpy.data.objects.new('M2_Muzzle', None); bpy.context.collection.objects.link(mz); mz.parent = gun; mz.location = muz - piv
def box(o):
    P = [o.matrix_world @ v.co for v in o.data.vertices]
    return [round(min(p[i] for p in P), 3) for i in range(3)], [round(max(p[i] for p in P), 3) for i in range(3)]
print('gun', box(gun), 'tripod', box(tri), 'pivot', [round(x, 3) for x in piv], 'muzzle', [round(x, 3) for x in muz])
print('faces', len(gun.data.polygons) + len(tri.data.polygons), 'materials', [m.name for m in bpy.data.materials])
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True, mesh_smooth_type='FACE')
