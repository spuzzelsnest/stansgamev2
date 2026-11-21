# Handley Page Halifax B.III (CC0, airmuseums.co.uk via Sketchfab): GLB -> FBX for Unity, nose to +Y, centred, textures embedded.
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix
SRC, OUT = sys.argv[-2:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
bpy.context.view_layer.update()
ms = [o for o in bpy.data.objects if o.type == 'MESH']
for o in ms:
    o.data.transform(o.matrix_world); o.parent = None; o.matrix_world = Matrix()
for o in [o for o in bpy.data.objects if o.type != 'MESH']: bpy.data.objects.remove(o)
fin = bpy.data.objects['fin_1']
fy = sum(v.co.y for v in fin.data.vertices) / len(fin.data.vertices)
print('fin y', round(fy, 2), 'verts', sum(len(o.data.vertices) for o in ms))
if fy > 0:
    for o in ms: o.data.transform(Matrix.Rotation(math.pi, 4, 'Z'))
P = [v.co for o in ms for v in o.data.vertices]
lo = Vector([min(p[i] for p in P) for i in range(3)]); hi = Vector([max(p[i] for p in P) for i in range(3)])
c = (lo + hi) / 2
for o in ms: o.data.transform(Matrix.Translation(-c))
print('size', [round(x, 2) for x in hi - lo])
# in flight: undercarriage up (the model stands on its wheels) - drop the main wheels, their legs and the tail wheel
def gear(lo, hi):
    main = 3.0 < abs((lo.x + hi.x) / 2) < 4.4 and 2.0 < (lo.y + hi.y) / 2 < 5.8 and lo.z < -0.9
    tail = abs((lo.x + hi.x) / 2) < 0.7 and -9.5 < (lo.y + hi.y) / 2 < -8.3 and lo.z < -1.95
    return main or tail
removed = 0
for o in ms:
    bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table()
    seen = set(); kill = []
    for v in bm.verts:
        if v.index in seen: continue
        st = [v]; comp = [v]; seen.add(v.index)
        while st:
            a = st.pop()
            for e in a.link_edges:
                b = e.other_vert(a)
                if b.index not in seen: seen.add(b.index); st.append(b); comp.append(b)
        lo = Vector([min(x.co[i] for x in comp) for i in range(3)]); hi = Vector([max(x.co[i] for x in comp) for i in range(3)])
        if gear(lo, hi): kill += comp
    if kill:
        bmesh.ops.delete(bm, geom=kill, context='VERTS'); bm.to_mesh(o.data); removed += len(kill)
    bm.free()
print('gear verts removed', removed)
# D-Day recognition stripes (painted on the 38 Group tugs on 3-4 June 1944): five 18-inch bands, white-black-white-
# black-white, round each wing outboard of the outer engines and round the rear fuselage. The textures are planar
# projections (top, bottom, side), so each band is painted where it falls on each projection.
import numpy as np, os
def projmap(matname, axes):
    X = []; U = []
    for o in ms:
        uvl = o.data.uv_layers.active.data
        for p in o.data.polygons:
            if o.data.materials[p.material_index].name != matname: continue
            for li in p.loop_indices:
                co = o.data.vertices[o.data.loops[li].vertex_index].co
                X.append([co[axes[0]], co[axes[1]], 1.0]); U.append(uvl[li].uv[:])
    if len(X) < 10: return None
    X = np.array(X); U = np.array(U)
    cu = np.linalg.lstsq(X, U[:, 0], rcond=None)[0]; cv = np.linalg.lstsq(X, U[:, 1], rcond=None)[0]
    err = np.abs(X @ cu - U[:, 0]).max() + np.abs(X @ cv - U[:, 1]).max()
    return cu, cv, err
def image_of(matname):
    m = bpy.data.materials[matname]
    for n in m.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image and '_col' in n.image.name: return n.image
B = 0.4572
WING = [(sx * 10.5 - 2.5 * B + i * B, sx * 10.5 - 2.5 * B + (i + 1) * B, i % 2 == 0) for sx in (-1, 1) for i in range(5)]
FUS_Y = -6.0
FUS = [(FUS_Y - 2.5 * B + i * B, FUS_Y - 2.5 * B + (i + 1) * B, i % 2 == 0) for i in range(5)]
os.makedirs('/tmp/haltex', exist_ok=True)
for matname, axes in (('halifax_paint_top', (0, 1)), ('halifax_paint_bottom', (0, 1)), ('halifax_paint_side', (1, 2))):
    pm = projmap(matname, axes); img = image_of(matname)
    if not pm or not img: print('no map', matname); continue
    cu, cv, err = pm; print(matname, 'planar fit error', round(err, 4))
    w, h = img.size; px = np.array(img.pixels[:]).reshape(h, w, 4)
    vv, uu = np.mgrid[0:h, 0:w]; U = (uu + 0.5) / w; V = (vv + 0.5) / h
    # invert the 2x2 map: world (a, b) for every pixel
    A = np.array([[cu[0], cu[1]], [cv[0], cv[1]]]); Ai = np.linalg.inv(A)
    a = Ai[0, 0] * (U - cu[2]) + Ai[0, 1] * (V - cv[2]); b = Ai[1, 0] * (U - cu[2]) + Ai[1, 1] * (V - cv[2])
    col = lambda white: np.array([0.86, 0.85, 0.80, 1.0]) if white else np.array([0.03, 0.03, 0.03, 1.0])
    if axes == (0, 1):
        for x0, x1, white in WING: px[(a >= x0) & (a < x1) & (np.abs(b) < 6.0)] = col(white)
        for y0, y1, white in FUS: px[(b >= y0) & (b < y1) & (np.abs(a) < 1.3)] = col(white)
    else:
        for y0, y1, white in FUS: px[(a >= y0) & (a < y1)] = col(white)
    img.pixels = px.ravel().tolist()
    img.filepath_raw = f'/tmp/haltex/{img.name}.png'; img.file_format = 'PNG'; img.save()
    if img.packed_file: img.unpack(method='REMOVE')
    img.filepath = f'/tmp/haltex/{img.name}.png'; img.reload()
# The four propellers (de Havilland three-blade airscrews on the Hercules engines) are welded into the big
# 'aerial_mast' mesh: cut each one out - its three blades, the blade-tip pieces, the hub and the spinner - into its own
# object with the origin on the propeller shaft, so the game can spin it (Scripts/Environment/PropellerSpin.cs).
# The engines' own cylinders and cowling just behind the hub stay put.
HUBS = {'Prop_PortOuter': (-7.75, 5.21, 0.86), 'Prop_PortInner': (-3.75, 6.14, 0.76),
        'Prop_StbdInner': (3.75, 6.14, 0.76), 'Prop_StbdOuter': (7.75, 5.21, 0.86)}
def prop_parts(o):
    bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table()
    seen = set(); out = {k: [] for k in HUBS}
    for v in bm.verts:
        if v.index in seen: continue
        st = [v]; comp = [v]; seen.add(v.index)
        while st:
            a = st.pop()
            for e in a.link_edges:
                b = e.other_vert(a)
                if b.index not in seen: seen.add(b.index); st.append(b); comp.append(b)
        for name, (hx, hy, hz) in HUBS.items():
            rs = [math.hypot(c.co.x - hx, c.co.z - hz) for c in comp]
            ys = [c.co.y for c in comp]
            if max(rs) > 2.4 or min(ys) < hy - 0.45 or max(ys) > hy + 1.0: continue
            cx = sum(c.co.x for c in comp) / len(comp); cz = sum(c.co.z for c in comp) / len(comp)
            on_axis = math.hypot(cx - hx, cz - hz) < 0.12
            if max(rs) > 0.9 or (on_axis and len(comp) in (224, 161)):   # blades and tips / hub and spinner
                out[name].append([x.index for x in comp])
    bm.free()
    return out
src = bpy.data.objects['aerial_mast']
for name in HUBS:
    comps = prop_parts(src)[name]   # again after every cut: the vertex numbers change
    idx = set(i for c in comps for i in c)
    print(name, 'components', len(comps), 'verts', len(idx))
    if not idx: continue
    for v in src.data.vertices: v.select = v.index in idx
    for e in src.data.edges: e.select = all(v in idx for v in e.vertices)
    for p in src.data.polygons: p.select = all(v in idx for v in p.vertices)
    bpy.ops.object.select_all(action='DESELECT')
    src.select_set(True); bpy.context.view_layer.objects.active = src
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_mode(type='FACE')
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.separate(type='SELECTED'); bpy.ops.object.mode_set(mode='OBJECT')
    new = [o for o in bpy.context.selected_objects if o != src][0]
    new.name = name; new.data.name = name
    hx, hy, hz = HUBS[name]
    new.data.transform(Matrix.Translation((-hx, -hy, -hz))); new.location = (hx, hy, hz)   # origin on the shaft
    for p in src.data.polygons: p.select = False
ms = [o for o in bpy.data.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in ms: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH'}, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', path_mode='COPY', embed_textures=True, mesh_smooth_type='FACE')
