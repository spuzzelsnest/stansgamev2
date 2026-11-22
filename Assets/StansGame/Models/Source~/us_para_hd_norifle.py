# US_Paratrooper_HD: cut away the M1 rifle moulded on his back (Tripo made body and rifle one mesh), so as the
# player he carries only the Garand in his hands. Faces behind his right shoulder whose texture is not the olive
# drab of the kit (the wood and steel of the rifle) are deleted, then any small loose bits left over.
# python3 -I us_para_hd_norifle.py <in.fbx> <BaseColor.jpg> <out.fbx>
import bpy, sys, bmesh, colorsys
from mathutils import Vector
src, tex, out = sys.argv[-3:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
m = [o for o in bpy.data.objects if o.type == 'MESH'][0]
img = bpy.data.images.load(tex); W, H = img.size; px = img.pixels[:]
def col(uv):
    x = int((uv.x % 1) * W) % W; y = int((uv.y % 1) * H) % H; i = (y * W + x) * 4; return px[i:i + 3]
bm = bmesh.new(); bm.from_mesh(m.data); uvl = bm.loops.layers.uv.active; mw = m.matrix_world
cut = []
for f in bm.faces:
    c = mw @ f.calc_center_median()
    if not (-0.42 < c.x < -0.10 and 0.80 < c.z < 1.95 and -0.02 < c.y < 0.42): continue   # behind the right shoulder
    uv = sum((l[uvl].uv for l in f.loops), Vector((0, 0))) / len(f.loops)
    h, s, v = colorsys.rgb_to_hsv(*col(uv))
    if not (0.10 < h < 0.25 and s > 0.25 and v > 0.18): cut.append(f)
bmesh.ops.delete(bm, geom=cut, context='FACES')
# small islands left behind (a sight, the muzzle tip)
bm.verts.ensure_lookup_table(); seen = set(); small = []
for v in bm.verts:
    if v.index in seen: continue
    st = [v]; comp = []; seen.add(v.index)
    while st:
        a = st.pop(); comp.append(a)
        for e in a.link_edges:
            b = e.other_vert(a)
            if b.index not in seen: seen.add(b.index); st.append(b)
    if len(comp) < 300: small += comp
bmesh.ops.delete(bm, geom=small, context='VERTS')
bm.to_mesh(m.data); m.data.update()
print('faces cut', len(cut), 'loose verts removed', len(small), 'faces left', len(m.data.polygons))
for o in bpy.data.objects: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                         add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False, mesh_smooth_type='FACE')
print('wrote', out)
