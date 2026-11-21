# M1 helmet (steel pot with the narrow brim, camouflage net) and the T-5 reserve parachute pack worn on the chest.
# Metres, Z up, origin at the helmet's rim centre / the pack's back face. Run: python3 us_para_kit.py -- <out dir>
import bpy, bmesh, sys, os, math
OUT = sys.argv[sys.argv.index('--') + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name, rgb, rough=0.8):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value = (*rgb, 1); b.inputs['Roughness'].default_value = rough
    return m

def export(objs, path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', path_mode='STRIP',
                             bake_anim=False, mesh_smooth_type='FACE')

# ---------------------------------------------------------------- M1 helmet: ~ 0.27 m wide, 0.30 m long, 0.17 m high
bm = bmesh.new()
bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=16, radius=1.0)
for v in list(bm.verts):
    if v.co.z < -0.02: bm.verts.remove(v)
for v in bm.verts:
    v.co.x *= 0.132; v.co.y *= 0.148; v.co.z = max(v.co.z, 0) * 0.165
# the brim: a narrow flared ring, a touch deeper at the back
rim = [v for v in bm.verts if v.co.z < 0.004]
ret = bmesh.ops.extrude_vert_indiv(bm, verts=rim) if False else None
edges = [e for e in bm.edges if all(v.co.z < 0.004 for v in e.verts)]
ext = bmesh.ops.extrude_edge_only(bm, edges=edges)
for v in [g for g in ext['geom'] if isinstance(g, bmesh.types.BMVert)]:
    d = v.co.xy.normalized()
    back = 1.0 + 0.4 * max(0.0, -d.y)
    v.co.x += d.x * 0.016 * back; v.co.y += d.y * 0.016 * back; v.co.z -= 0.012 * back
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
me = bpy.data.meshes.new('M1Helmet'); bm.to_mesh(me); bm.free()
helmet = bpy.data.objects.new('M1Helmet', me); bpy.context.scene.collection.objects.link(helmet)
helmet.data.materials.append(mat('M1Helmet_OD', (0.17, 0.18, 0.11)))
mod = helmet.modifiers.new('thick', 'SOLIDIFY'); mod.thickness = 0.006
bpy.context.view_layer.objects.active = helmet; helmet.select_set(True); bpy.ops.object.modifier_apply(modifier='thick')
# the net: a slightly larger, rougher shell in a darker drab
net = helmet.copy(); net.data = helmet.data.copy(); net.name = 'M1Helmet_Net'; bpy.context.scene.collection.objects.link(net)
net.scale = (1.035, 1.035, 1.03); net.data.materials[0] = mat('M1Helmet_Net', (0.22, 0.21, 0.13), 1.0)
bpy.context.view_layer.objects.active = net; bpy.ops.object.select_all(action='DESELECT'); net.select_set(True)
bpy.ops.object.transform_apply(scale=True)
# leave the net only on the dome (not the brim edge): drop its lowest ring
bm = bmesh.new(); bm.from_mesh(net.data)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < 0.02], context='VERTS'); bm.to_mesh(net.data); bm.free()
# chin strap hanging loose at the sides, as the paras wore it (A-yoke with the leather chin cup)
straps = []
for s in (-1, 1):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(s * 0.128, 0.0, -0.05)); st = bpy.context.object
    st.scale = (0.006, 0.016, 0.09); st.name = 'ChinStrap'; st.data.materials.append(mat('M1_Strap', (0.20, 0.17, 0.10)))
    bpy.ops.object.transform_apply(scale=True); straps.append(st)
export([helmet, net] + straps, os.path.join(OUT, 'M1Helmet.fbx'))

# ---------------------------------------------------------------- T-5 reserve parachute, worn on the chest
for o in list(bpy.data.objects): bpy.data.objects.remove(o)
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0.06, 0)); pack = bpy.context.object
pack.scale = (0.34, 0.12, 0.24); pack.name = 'ReserveChute'
bpy.ops.object.transform_apply(scale=True)
bev = pack.modifiers.new('bevel', 'BEVEL'); bev.width = 0.025; bev.segments = 3; bpy.ops.object.modifier_apply(modifier='bevel')
pack.data.materials.append(mat('Reserve_OD', (0.30, 0.29, 0.19), 0.9))
bpy.ops.mesh.primitive_cube_add(size=1, location=(0.11, 0.125, 0.0)); handle = bpy.context.object
handle.scale = (0.05, 0.02, 0.03); handle.name = 'RipcordHandle'; bpy.ops.object.transform_apply(scale=True)
handle.data.materials.append(mat('Ripcord_Steel', (0.5, 0.5, 0.5), 0.3))
export([pack, handle], os.path.join(OUT, 'ReserveChute.fbx'))
print('ok')
