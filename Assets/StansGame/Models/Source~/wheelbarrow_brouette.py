# Old French wooden wheelbarrow (brouette), photogrammetry scan from _downloads/brouette: levelled on its wheel and
# legs, wheel to the front (+Y), decimated, 1K texture embedded. python3 -I wheelbarrow.py <obj> <tex> <out.fbx>
import bpy, bmesh, sys, numpy as np
from mathutils import Vector, Matrix
OBJ, TEX, OUT = sys.argv[-3:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=OBJ)
o = [x for x in bpy.data.objects if x.type == 'MESH'][0]
o.data.transform(o.matrix_world); o.matrix_world = Matrix(); o.data.transform(Matrix.Scale(0.001, 4))
V = np.array([v.co[:] for v in o.data.vertices])
# 1) the scan comes roughly upright (+Z) but tilted
# 2) rest on the support (wheel + two legs): the convex-hull face facing most downwards with the centre over it
import scipy.spatial as ss
hull = ss.ConvexHull(V[::7]); P = V[::7]; com = V.mean(0)
cands = []
for s, eq in zip(hull.simplices, hull.equations):
    nn = eq[:3]
    if nn[2] > -0.6: continue
    t = P[s]; area = np.linalg.norm(np.cross(t[1] - t[0], t[2] - t[0])) / 2
    cands.append((area, nn))
cands.sort(key=lambda c: -c[0])
nn = cands[0][1]
R2 = Vector(nn).rotation_difference(Vector((0, 0, -1))).to_matrix().to_4x4()
o.data.transform(R2); V = np.array([v.co[:] for v in o.data.vertices])
# 3) long axis along Y, wheel (the far ground contact) at +Y
c = V.mean(0); u, s, vt = np.linalg.svd((V - c)[:, :2][::20], full_matrices=False)
ax = vt[0]; ang = np.arctan2(ax[0], ax[1])
o.data.transform(Matrix.Rotation(ang, 4, 'Z')); V = np.array([v.co[:] for v in o.data.vertices])
zmin = V[:, 2].min(); ground = V[V[:, 2] < zmin + 0.03]
if abs(ground[:, 1].max() - V[:, 1].max()) < abs(ground[:, 1].min() - V[:, 1].min()):
    o.data.transform(Matrix.Rotation(np.pi, 4, 'Z')); V = np.array([v.co[:] for v in o.data.vertices])
lo, hi = V.min(0), V.max(0)
o.data.transform(Matrix.Translation(Vector((-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2]))))
print('size', (hi - lo).round(3), 'support faces', len(cands))
# 4) decimate, material
bpy.context.view_layer.objects.active = o; o.select_set(True)
m = o.modifiers.new('d', 'DECIMATE'); m.ratio = 12000 / len(o.data.polygons)
bpy.ops.object.modifier_apply(modifier='d')
o.data.materials.clear()
mat = bpy.data.materials.new('Wheelbarrow'); mat.use_nodes = True
t = mat.node_tree.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(TEX)
b = mat.node_tree.nodes['Principled BSDF']; mat.node_tree.links.new(t.outputs['Color'], b.inputs['Base Color']); b.inputs['Roughness'].default_value = 0.85
o.data.materials.append(mat); o.name = 'Wheelbarrow'
for p in o.data.polygons: p.use_smooth = True
print('faces', len(o.data.polygons))
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH'}, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', path_mode='COPY', embed_textures=True, mesh_smooth_type='FACE')
