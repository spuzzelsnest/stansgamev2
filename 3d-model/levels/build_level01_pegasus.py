"""Level 01 Pegasus Bridge blockout (StansGameV2), headless Blender build.

Run:  blender -b -P 3d-model/levels/build_level01_pegasus.py -- OUTDIR

Stages: S2 smoke test (2 m cube, FBX round trip, axis and unit check), S4 terrain, canal, Orne
east cut and lift bridge, S5 low proxies, spot empties, cameras, renders, .blend and .fbx.
Data comes from level01_pegasus/ (origin.json, heights_2m.csv, checkpoints.csv, features.json,
spots.csv, cameras.json). Nothing is downloaded; no ww2maps image or caption is read or written.
Local frame: X east, Y Lambert-93 grid north, Z up, 1 unit = 1 m, z = NGF-IGN69 - height_base.
Exits non-zero (assert / exception) if any check fails.
"""
import bpy, bmesh, sys, os, math, json, csv, struct
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
L = os.path.join(HERE, "level01_pegasus")
OUT = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
os.makedirs(OUT, exist_ok=True)
FBX_NAME = "level01_pegasus_blockout.fbx"
FBX_KW = dict(axis_forward="-Z", axis_up="Y", global_scale=1.0, apply_unit_scale=True,
              apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False)


def log(*a):
    print("[L01]", *a, flush=True)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    s = bpy.context.scene
    s.unit_settings.system = "METRIC"
    s.unit_settings.scale_length = 1.0
    s.unit_settings.length_unit = "METERS"
    return s


# ======================================================================================
# S2: smoke test. 2 m cube at (3, 5, 1) in Blender (Z up). FBX out, FBX back in.
# ======================================================================================
def fbx_unit_scale(path):
    """Read UnitScaleFactor from a binary FBX (P node: 'UnitScaleFactor','double','Number','',value)."""
    data = open(path, "rb").read()
    i = data.find(b"UnitScaleFactor")
    if i < 0:
        return None
    j = i + len(b"UnitScaleFactor")
    for _ in range(3):  # skip 'double', 'Number', ''
        assert data[j:j + 1] == b"S", "unexpected FBX property layout"
        n = struct.unpack("<I", data[j + 1:j + 5])[0]
        j += 5 + n
    assert data[j:j + 1] == b"D"
    return struct.unpack("<d", data[j + 1:j + 9])[0]


def smoke_test():
    reset()
    bpy.ops.mesh.primitive_cube_add(size=2.0, location=(3.0, 5.0, 1.0))
    cube = bpy.context.active_object
    cube.name = "smoke_cube"
    d = cube.dimensions
    assert all(abs(v - 2.0) < 1e-6 for v in d), f"cube not 2 m: {tuple(d)}"
    path = os.path.join(OUT, "_smoke_cube.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, **FBX_KW)
    usf = fbx_unit_scale(path)

    def reimport(**kw):
        reset()
        bpy.ops.import_scene.fbx(filepath=path, **kw)
        o = [x for x in bpy.context.scene.objects if x.type == "MESH"][0]
        return tuple(round(v, 4) for v in o.location), tuple(round(v, 4) for v in o.dimensions)

    # 1) default importer: should give the original Blender values back (Z up, metres)
    loc, dim = reimport(axis_forward="-Z", axis_up="Y")
    assert dim == (2.0, 2.0, 2.0), f"re-imported size {dim}"
    assert loc == (3.0, 5.0, 1.0), f"re-imported location {loc}"
    # 2) identity axis mapping: shows the raw file axes. Expect Y up: file (x, y, z) = (3, 1, -5)
    raw_loc, raw_dim = reimport(use_manual_orientation=True, axis_forward="Y", axis_up="Z")
    assert raw_loc == (3.0, 1.0, -5.0), f"raw FBX axes {raw_loc} (expected Y up: 3, 1, -5)"
    os.remove(path)
    res = {"cube_size_m": 2.0, "blender_location_m": [3, 5, 1], "reimport_location_m": list(loc),
           "reimport_dimensions_m": list(dim), "raw_file_location_xyz": list(raw_loc),
           "raw_file_dimensions": list(raw_dim), "fbx_UnitScaleFactor": usf,
           "export_axis_forward": FBX_KW["axis_forward"], "export_axis_up": FBX_KW["axis_up"],
           "export_apply_scale_options": FBX_KW["apply_scale_options"], "result": "PASS"}
    json.dump(res, open(os.path.join(OUT, "smoke_test.json"), "w"), indent=1)
    log("SMOKE_TEST PASS", res)
    return res


smoke = smoke_test()
scene = reset()  # cube is gone: fresh empty scene for the real build

# ======================================================================================
# Data
# ======================================================================================
origin = json.load(open(os.path.join(L, "origin.json"), encoding="utf-8"))
features = {f["id"]: f for f in json.load(open(os.path.join(L, "features.json"), encoding="utf-8"))["features"]}
cams = json.load(open(os.path.join(L, "cameras.json"), encoding="utf-8"))
spots = list(csv.DictReader(open(os.path.join(L, "spots.csv"), encoding="utf-8")))
checks = list(csv.DictReader(open(os.path.join(L, "checkpoints.csv"), encoding="utf-8")))
with open(os.path.join(L, "heights_2m.csv"), encoding="utf-8") as fh:
    hdr = next(csv.reader([next(fh)] + [""]))  # header line
    hvals = dict(zip(hdr, next(csv.reader([next(fh)]))))
    X0 = float(hvals["x0_local_m"]); YTOP = float(hvals["y_top_local_m"]); CELL = float(hvals["cell_m"])
    ROWS = int(hvals["rows"]); COLS = int(hvals["cols"]); BASE = float(hvals["height_base_m"])
G = np.loadtxt(os.path.join(L, "heights_2m.csv"), delimiter=",", skiprows=2)
assert G.shape == (ROWS, COLS), f"grid {G.shape} != header {(ROWS, COLS)}"
assert abs(BASE - origin["height_base"]["value_m"]) < 1e-9
box = origin["playable_box_local"]
log("grid", G.shape, "z range", float(G.min()), float(G.max()))

WATER_Z = 0.0                      # canal water level = 3.4 m NGF = height base
CANAL = features["canal"]
BEARING = math.radians(CANAL["bearing_deg_east_of_grid_north"])   # canal direction, 28.3 deg east of grid north
CANAL_DEPTH = 2.5                  # bed below water level, estimate (1:2 bank slopes)
ORNE_CLIP_X = box["x_max"] + 1.3   # 726 per features.json
BR = features["lift_bridge"]
SRC_TAGS = {"lidar-today", "ww2maps-ref", "aerial-1944", "estimate"}
DECK_W, DECK_T = BR["deck_width_m"], 0.6
DECK_W0 = Vector((*BR["deck_west_end"], 0.0)); DECK_W1 = Vector((*BR["deck_east_end"], 0.0))
DECK_LEN = (DECK_W1 - DECK_W0).length
AX = (DECK_W1 - DECK_W0).normalized()          # unit vector along the deck (grid 118.3 deg)
PERP = Vector((-AX.y, AX.x, 0.0))
SPAN_LEN = BR["moving_span_length_m"]                      # 28.4 m, 1944 Scherzer span (estimate)
FIX_W = BR["fixed_approach_west"]["length_m"]; FIX_E = BR["fixed_approach_east"]["length_m"]   # fixed spans, each 36.8 m
assert abs(DECK_LEN - BR["deck_total_length_m"]) < 0.5 and abs(DECK_LEN - FIX_W - FIX_E - SPAN_LEN) < 0.5

# --- carve the canal channel into the DEM array (bank polylines from features.json) ---
xs = X0 + np.arange(COLS) * CELL
ys = YTOP - np.arange(ROWS) * CELL


def bank_x(poly):
    """x of a bank polyline at every grid row, linear extension beyond its y range along the canal bearing."""
    py = np.array([q[1] for q in poly], float); px = np.array([q[0] for q in poly], float)
    t = math.tan(BEARING)
    out = np.interp(ys, py, px)
    out = np.where(ys < py[0], px[0] + (ys - py[0]) * t, out)
    return np.where(ys > py[-1], px[-1] + (ys - py[-1]) * t, out)


BW_X, BE_X = bank_x(CANAL["bank_west_local_m"]), bank_x(CANAL["bank_east_local_m"])
dist_in = np.minimum(xs[None, :] - BW_X[:, None], BE_X[:, None] - xs[None, :])   # >0 inside the band
bed = WATER_Z - np.clip(dist_in / 2.0, 0.0, CANAL_DEPTH)
# Bank profile: the DEM banks are used as they are (1 m source, steep sides, flat bed at water level). Carving a bed here
# would move the terrain away from the source DEM by up to CANAL_DEPTH, so CARVE_BED stays off.
CARVE_BED = False
GC = np.where((dist_in > 0) & (G <= 0.5) & CARVE_BED, np.minimum(G, bed), G)


def ground(x, y):
    """Bilinear height (z local) of the carved terrain grid."""
    c = (x - X0) / CELL
    r = (YTOP - y) / CELL
    c = min(max(c, 0), COLS - 1.0001); r = min(max(r, 0), ROWS - 1.0001)
    c0, r0 = int(c), int(r); fc, fr = c - c0, r - r0
    return float((GC[r0, c0] * (1 - fc) + GC[r0, c0 + 1] * fc) * (1 - fr) +
                 (GC[r0 + 1, c0] * (1 - fc) + GC[r0 + 1, c0 + 1] * fc) * fr)


# ======================================================================================
# helpers
# ======================================================================================
COLL = {}


def coll(name):
    if name not in COLL:
        c = bpy.data.collections.new(name)
        scene.collection.children.link(c)
        COLL[name] = c
    return COLL[name]


MATS = {}


def mat(name, rgb):
    if name not in MATS:
        m = bpy.data.materials.new(name)
        m.diffuse_color = (*rgb, 1.0)
        MATS[name] = m
    return MATS[name]


def finish(name, me, cname, mname, rgb, source, **props):
    ob = bpy.data.objects.new(name, me)
    coll(cname).objects.link(ob)
    me.materials.append(mat(mname, rgb))
    assert source in SRC_TAGS, source
    ob["source"] = source
    for k, v in props.items():
        ob[k] = v
    return ob


def bm_to_mesh(bm, name, normals=True):
    if normals:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return me


def add_box(bm, cx, cy, zb, zt, w, d, rot=0.0):
    c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    vs = []
    for z in (zb, zt):
        for ox, oy in ((-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)):
            vs.append(bm.verts.new((cx + ox * c - oy * s, cy + ox * s + oy * c, z)))
    for f in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
        bm.faces.new([vs[i] for i in f])


def footprint_pts(cx, cy, w, d, rot=0.0, step=1.0):
    """Sample points over a (rotated) rectangle footprint, edges included."""
    c, s_ = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    nx, ny = max(1, int(math.ceil(w / step))), max(1, int(math.ceil(d / step)))
    return [(cx + (-w / 2 + w * i / nx) * c - (-d / 2 + d * j / ny) * s_,
             cy + (-w / 2 + w * i / nx) * s_ + (-d / 2 + d * j / ny) * c) for i in range(nx + 1) for j in range(ny + 1)]


def building(name, fid, cx, cy, w, d, h, rgb, source, cname="Buildings", rot=0.0, sink=0.3, **props):
    """Box whose base sits at the lowest ground under the footprint (minus sink); top above the highest ground."""
    zs = [ground(x, y) for x, y in footprint_pts(cx, cy, w, d, rot)]
    zb = min(zs) - sink
    zt = max(min(zs) + h, max(zs) + 0.5)
    bm = bmesh.new()
    add_box(bm, cx, cy, zb, zt, w, d, rot)
    me = bm_to_mesh(bm, name)
    return finish(name, me, cname, name.split("_")[0] + "_mat", rgb, source, feature_id=fid,
                  height_m=h, footprint_w_m=w, footprint_d_m=d, base_z_local=zb, **props)


def densify(pts, step=3.0):
    out = [pts[0]]
    for (x0, y0), (x1, y1) in zip(pts[:-1], pts[1:]):
        n = max(1, int(math.hypot(x1 - x0, y1 - y0) // step))
        out += [(x0 + (x1 - x0) * k / n, y0 + (y1 - y0) * k / n) for k in range(1, n + 1)]
    return out


def offsets(pts, w):
    res = []
    for i, (x, y) in enumerate(pts):
        a = pts[max(i - 1, 0)]; b = pts[min(i + 1, len(pts) - 1)]
        tx, ty = b[0] - a[0], b[1] - a[1]
        n = math.hypot(tx, ty) or 1.0
        nx, ny = -ty / n, tx / n
        res.append(((x + nx * w / 2, y + ny * w / 2), (x - nx * w / 2, y - ny * w / 2)))
    return res


def ribbon(name, pts, width, z_off, z_fixed=None):
    """Flat strip draped on the terrain (or at a fixed z)."""
    pts = densify(pts)
    bm = bmesh.new()
    rows = []
    for (lx, ly), (rx, ry) in offsets(pts, width):
        zl = z_fixed if z_fixed is not None else ground(lx, ly) + z_off
        zr = z_fixed if z_fixed is not None else ground(rx, ry) + z_off
        rows.append((bm.verts.new((lx, ly, zl)), bm.verts.new((rx, ry, zr))))
    for (l0, r0), (l1, r1) in zip(rows[:-1], rows[1:]):
        bm.faces.new((l0, r0, r1, l1))
    return bm


def prism(name, pts, width, height, sink=0.3):
    """Closed solid strip (hedgerows): bottom sink below ground, top height above ground."""
    pts = densify(pts)
    bm = bmesh.new()
    rows = []
    for (lx, ly), (rx, ry) in offsets(pts, width):
        zl, zr = ground(lx, ly), ground(rx, ry)
        rows.append([bm.verts.new((lx, ly, zl - sink)), bm.verts.new((rx, ry, zr - sink)),
                     bm.verts.new((lx, ly, zl + height)), bm.verts.new((rx, ry, zr + height))])
    for a, b in zip(rows[:-1], rows[1:]):
        for i, j in ((0, 1), (1, 3), (3, 2), (2, 0)):
            bm.faces.new((a[i], a[j], b[j], b[i]))
    for r in (rows[0], rows[-1]):
        bm.faces.new((r[0], r[1], r[3], r[2]))
    return bm


def patch(name, cx, cy, w, d, off, step=8.0):
    """Rectangular area draped on the terrain."""
    nx, ny = max(1, int(w // step)), max(1, int(d // step))
    bm = bmesh.new()
    vs = [[bm.verts.new((cx - w / 2 + w * i / nx, cy - d / 2 + d * j / ny,
                         ground(cx - w / 2 + w * i / nx, cy - d / 2 + d * j / ny) + off))
           for i in range(nx + 1)] for j in range(ny + 1)]
    for j in range(ny):
        for i in range(nx):
            bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))
    return bm


def planar(name, rect, z):
    x0, y0, x1, y1 = rect
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in ((x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z))]
    bm.faces.new(vs)
    return bm


# ======================================================================================
# S4: scene properties, terrain, canal, Orne cut, bridge
# ======================================================================================
SOURCES = ["RGE ALTI 1 m, IGN, Etalab Open Licence 2.0 (heights, WMS-R box fetched 2026-11-13, resampled to 2 m)",
           "Photo-spot coordinates: map-sheet geojson in the project vault (coordinates only, no images)",
           "Everything tagged 'estimate' in features.json is a placeholder, not a measurement"]
scene["level"] = "01 Pegasus Bridge"
scene["crs"] = origin["crs"]
scene["l93_origin_x"] = origin["l93_x"]
scene["l93_origin_y"] = origin["l93_y"]
scene["height_base_m"] = BASE
scene["height_datum"] = origin["height_base"]["datum"]
scene["height_note"] = "z_local = NGF-IGN69 height - height_base_m"
scene["local_frame"] = "local = Lambert-93 - origin; X east, Y grid north, Z up; 1 unit = 1 m"
scene["grid_north_note"] = (f"Local +Y is Lambert-93 GRID north. Meridian convergence {origin['north']['meridian_convergence_deg']} deg "
                            "(true north is about 2.4 deg east of local +Y at this longitude).")
scene["sources"] = " | ".join(SOURCES)
scene["tiles"] = "none: heights from one RGE ALTI 1 m WMS-R box (BBOX 460940,6909280,462310,6910350 EPSG:2154), see credits.md"
scene["box_local"] = json.dumps(box)
for c in checks:  # tidy numeric props
    pass

# ---- terrain ----
V = np.empty((ROWS * COLS, 3))
V[:, 0] = np.tile(xs, ROWS)
V[:, 1] = np.repeat(ys, COLS)
V[:, 2] = GC.reshape(-1)
rr, cc = np.meshgrid(np.arange(ROWS - 1), np.arange(COLS - 1), indexing="ij")
i00 = (rr * COLS + cc).reshape(-1)
F = np.stack([i00, i00 + COLS, i00 + COLS + 1, i00 + 1], axis=1)  # TL, BL, BR, TR = CCW seen from above
# At 2 m, interpolation across a steep bank can miss the source height by a little more than 0.1 m. Where the
# 2 m grid alone misses a check point, its cell gets one extra vertex at the exact source height (4 triangles).
faces = F.tolist()
verts = V.tolist()
fz = list(GC[:-1, :-1].reshape(-1))
pinned = []
for c in checks:
    x, y, zs = float(c["local_x"]), float(c["local_y"]), float(c["z_local_m"])
    grid_err = ground(x, y) - zs
    if abs(grid_err) <= 0.08:
        continue
    cc0, rr0 = int((x - X0) // CELL), int((YTOP - y) // CELL)
    k = rr0 * (COLS - 1) + cc0
    tl, bl, br, tr = faces[k]
    verts.append((x, y, zs)); pv = len(verts) - 1
    faces[k] = [tl, bl, pv]            # reuse the cell's slot for one triangle, append the other three
    faces += [[bl, br, pv], [br, tr, pv], [tr, tl, pv]]
    fz += [fz[k]] * 3
    pinned.append((c["site_id"], round(grid_err, 3)))
    log("PIN check point", c["site_id"], "2 m grid error was %.3f m" % grid_err)
me = bpy.data.meshes.new("terrain")
me.from_pydata(verts, [], faces)
fz = np.array(fz)
for nm, rgb in (("terrain_low", (0.45, 0.55, 0.62)), ("terrain_field", (0.45, 0.62, 0.30)), ("terrain_high", (0.55, 0.50, 0.32))):
    me.materials.append(mat(nm, rgb))
idx = np.where(fz <= 0.05, 0, np.where(fz < 3.0, 1, 2)).astype(np.int32)
me.polygons.foreach_set("material_index", idx)
me.update()
terrain = bpy.data.objects.new("terrain", me)
coll("Terrain").objects.link(terrain)
terrain["source"] = "lidar-today"
terrain["terrain_note"] = "RGE ALTI 1 m resampled to 2 m, not carved; check-point cells pinned"
terrain["grid"] = f"{ROWS}x{COLS} cells, cell {CELL} m"
terrain["pinned_check_points"] = json.dumps(pinned)
bpy.context.view_layer.update()

# ---- terrain surface lookup (ray cast on the real mesh) ----
def surf(x, y):
    hit, loc, nrm, fi = terrain.ray_cast(Vector((x, y, 200.0)), Vector((0, 0, -1)))
    assert hit, f"no terrain under ({x}, {y})"
    return loc.z


# ---- canal: water plane between the DEM bank polylines, bank outlines ----
bw = [tuple(q) for q in CANAL["bank_west_local_m"]]
be = [tuple(q) for q in CANAL["bank_east_local_m"]]
assert len(bw) == len(be)
bm = bmesh.new()
rows_ = [(bm.verts.new((a[0], a[1], WATER_Z + 0.06)), bm.verts.new((b[0], b[1], WATER_Z + 0.06))) for a, b in zip(bw, be)]
for (w0, e0), (w1, e1) in zip(rows_[:-1], rows_[1:]):
    bm.faces.new((w0, e0, e1, w1))
canal = finish("canal_water", bm_to_mesh(bm, "canal_water"), "Water", "water", (0.20, 0.42, 0.62), CANAL["source_tag"], feature_id="canal",
               water_level_ngf_m=CANAL["water_level_ngf_m"], water_level_tag=CANAL["water_level_tag"],
               bearing_deg_east_of_grid_north=CANAL["bearing_deg_east_of_grid_north"],
               width_m_today_mean=CANAL["width_m_today"], width_m_1944_estimate=CANAL["width_m_1944"], width_1944_tag=CANAL["width_1944_tag"],
               bank_profile="DEM banks as they are in the 1 m grid (steep sides, flat water-level bed); no carving, so terrain heights stay the source DEM",
               note="Band between bank_west_local_m and bank_east_local_m of features.json (DEM water-level band; may be wider than the 1944 canal).")
for nm, pts in (("canal_bank_west", bw), ("canal_bank_east", be)):
    finish(nm, bm_to_mesh(ribbon(nm, pts, 1.5, 0.15), nm), "Water", "bank_line", (0.85, 0.80, 0.30), CANAL["source_tag"], feature_id="canal",
           note="Bank line from features.json, drawn as a thin strip on the ground.")

# ---- bank ground (towpath / bank beside the water, flat at water level): dry and walkable ----
BG = features["canal_bank_ground"]
for side in ("west", "east"):
    st = BG[f"{side}_strip_local_m"]
    we, bed_ = [tuple(q) for q in st["water_edge"]], [tuple(q) for q in st["band_edge"]]
    assert len(we) == len(bed_)
    bm = bmesh.new()
    rr = [(bm.verts.new((a[0], a[1], ground(*a) + 0.05)), bm.verts.new((b[0], b[1], ground(*b) + 0.05))) for a, b in zip(we, bed_)]
    for (a0, b0), (a1, b1) in zip(rr[:-1], rr[1:]):
        bm.faces.new((a0, a1, b1, b0) if side == "west" else (a0, b0, b1, a1))
    nm = f"canal_bank_ground_{side}"
    finish(nm, bm_to_mesh(bm, nm), "Water", "bank_ground", (0.62, 0.58, 0.40), BG["source_tag"], feature_id="canal_bank_ground", walkable=True,
           is_water=False, height_z_local_m=BG["height_z_local_m"], note=BG["note"])

# ---- Orne east cut ----
orne_pts = [tuple(p) for p in features["orne_cut"]["local_m"]]
(xa, ya), (xb, yb) = orne_pts[-2], orne_pts[-1]
t = (ORNE_CLIP_X - xa) / (xb - xa)
orne_pts = orne_pts[:-1] + [(ORNE_CLIP_X, ya + (yb - ya) * t)]
orne_z = float(G.min()) + 0.1
bm = ribbon("orne", orne_pts, features["orne_cut"]["width_m_today"], 0, z_fixed=orne_z)
for v in bm.verts:
    v.co.x = min(v.co.x, ORNE_CLIP_X)
orne = finish("orne_water", bm_to_mesh(bm, "orne_water"), "Water", "water", (0.20, 0.42, 0.62), "lidar-today",
              feature_id="orne_cut", width_m=features["orne_cut"]["width_m_today"], water_z_local=orne_z,
              note="Cut at the box east edge x=%.1f; centre line read from the 1 m grid, width not measured to a bank." % ORNE_CLIP_X)
ey = orne_pts[-1][1]
bm = bmesh.new(); add_box(bm, ORNE_CLIP_X - 0.5, ey, orne_z, orne_z + 4.0, 1.0, 46.0)
finish("orne_east_cut_marker", bm_to_mesh(bm, "orne_east_cut_marker"), "Water", "marker_red", (0.85, 0.15, 0.15), "lidar-today",
       feature_id="orne_cut", note="Marks the explicit east cut of the Orne at the playable box edge.")

# ---- lift bridge (proxy; deck height, width, hinge are estimates, crossing from the DEM banks) ----
ang = math.degrees(math.atan2(AX.y, AX.x))                      # box rotation: local X along the deck axis
DECK_TOP = max(ground(*(DECK_W0 + AX * t).xy) for t in np.arange(0, DECK_LEN + 0.1, 2.0)) + 0.1
log("bridge deck top z %.2f (highest ground along the axis + 0.1), axis rot %.1f deg" % (DECK_TOP, ang))


def deck_box(bm, t0, t1, ox=0.0, oy=0.0, zb=None, zt=None, wd=DECK_W, org=Vector((0, 0, 0))):
    mid = DECK_W0 + AX * ((t0 + t1) / 2) - org
    add_box(bm, mid.x, mid.y, DECK_TOP - DECK_T if zb is None else zb, DECK_TOP if zt is None else zt, t1 - t0, wd, ang)


bm = bmesh.new(); deck_box(bm, 0, FIX_W)
finish("bridge_deck_west", bm_to_mesh(bm, "bridge_deck_west"), "Bridge", "bridge", (0.35, 0.36, 0.38), BR["source_tag"],
       feature_id="lift_bridge", length_m=FIX_W, note="Fixed span, west side: 8.8 m from the hinge to the water edge, then over the bank ground to the deck end.")
bm = bmesh.new(); deck_box(bm, DECK_LEN - FIX_E, DECK_LEN)
finish("bridge_deck_east", bm_to_mesh(bm, "bridge_deck_east"), "Bridge", "bridge", (0.35, 0.36, 0.38), BR["source_tag"],
       feature_id="lift_bridge", length_m=FIX_E, note="Fixed span, east side, from the east end of the moving span over the water edge and bank ground to the deck end.")
HINGE = Vector((BR["hinge_local_m"][0], BR["hinge_local_m"][1], DECK_TOP - DECK_T / 2))
assert abs((Vector((*BR["hinge_local_m"], 0)) - DECK_W0).length - FIX_W) < 0.5, "hinge not at the end of the west fixed span"
bm = bmesh.new()  # object origin = hinge; object rotated so local X runs along the deck, local Y along the canal (hinge axis)
dm = SPAN_LEN / 2        # span centre measured from the hinge along the deck (local X)
add_box(bm, dm, 0, -DECK_T / 2, DECK_T / 2, SPAN_LEN, DECK_W)
for side in (-1, 1):  # girders
    add_box(bm, dm, side * (DECK_W / 2 + 0.2), -DECK_T / 2, DECK_T / 2 + 3.5, SPAN_LEN, 0.4)
span = finish("bridge_span_moving", bm_to_mesh(bm, "bridge_span_moving"), "Bridge", "bridge_span", (0.55, 0.20, 0.18), BR["source_tag"],
              feature_id="lift_bridge", length_m=SPAN_LEN, deck_top_z_local=DECK_TOP, axis_bearing_deg_grid=BR["axis_bearing_deg_grid"],
              hinge_local=json.dumps(list(HINGE)), hinge_note=BR["hinge_note"],
              open_state_note="Pivot is the object origin at the west bank point; local X along the deck, local Y along the canal (hinge axis). Rotate about local Y by a negative angle to lift the east end.")
span.location = HINGE
span.rotation_euler = (0.0, 0.0, math.radians(ang))
scene["bridge_note"] = "Lift bridge: crossing and span length from the DEM canal banks (lidar-today); deck height, width, hinge end are estimates. Deck top = highest ground along the axis + 0.1 m."

# ---- S4 assert: mesh heights at the check points (ray cast on the real mesh) ----
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
chk_report = []
for c in checks:
    x, y, z_src = float(c["local_x"]), float(c["local_y"]), float(c["z_local_m"])
    hit, loc, nrm, fi = terrain.ray_cast(Vector((x, y, 200.0)), Vector((0, 0, -1)))
    assert hit, f"check point {c['site_id']} not over the terrain mesh"
    err = loc.z - z_src
    chk_report.append((c["site_id"], z_src, round(loc.z, 3), round(err, 3)))
    assert abs(err) <= 0.1, f"check point {c['site_id']} ({c['source'][:20]}): mesh {loc.z:.3f} vs source {z_src:.3f}, off by {err:.3f} m"
log("CHECK_POINTS within 0.1 m (site, source z, mesh z, error):", chk_report)
vmin = np.array(V.min(axis=0)); vmax = np.array(V.max(axis=0))
assert vmin[0] <= box["x_min"] and vmax[0] >= box["x_max"] and vmin[1] <= box["y_min"] and vmax[1] >= box["y_max"], "terrain does not cover the playable box"
log("TERRAIN extent x %.1f..%.1f y %.1f..%.1f covers box" % (vmin[0], vmax[0], vmin[1], vmax[1]), "verts", len(me.vertices), "faces", len(me.polygons))

# ======================================================================================
# S5: roads, buildings, vegetation, markers
# ======================================================================================
ROAD = (0.30, 0.30, 0.30)


def road(name, fid, pts):
    f = features[fid]
    bm = ribbon(name, pts, f["width_m"], 0.08)
    return finish(name, bm_to_mesh(bm, name), "Roads", "road", ROAD, f["source_tag"], feature_id=fid, width_m=f["width_m"], note=f["note"])


road("road_bridge_west", "road_bridge", [tuple(q) for q in features["road_bridge"]["west_local_m"]])
road("road_bridge_east", "road_bridge", [tuple(q) for q in features["road_bridge"]["east_local_m"]])
road("road_west_bank", "road_west_bank", [tuple(q) for q in features["road_west_bank"]["local_m"]])
road("road_armour_approach", "road_armour_approach", [tuple(q) for q in features["road_armour_approach"]["local_m"]])

for fid in ("cafe_gondree", "west_house_1", "west_house_2", "west_house_3", "pillbox"):
    f = features[fid]
    (cx, cy), (w, d) = f["local_m"][0], f["footprint_m"]
    col = (0.62, 0.55, 0.45) if fid != "pillbox" else (0.50, 0.50, 0.48)
    building(fid, fid, cx, cy, w, d, f["height_m"], col, f["source_tag"], note=f["note"])

f = features["trench_line"]
bm = ribbon("trench_line", [tuple(p) for p in f["local_m"]], f["width_m"], 0.05)
finish("trench_line", bm_to_mesh(bm, "trench_line"), "Buildings", "trench", (0.20, 0.15, 0.10), f["source_tag"],
       feature_id="trench_line", width_m=f["width_m"], depth_m=f["depth_m"], note=f["note"] + " Drawn as a flat strip; depth is a property only.")

f = features["church_benouville"]
(cx, cy), (w, d) = f["local_m"][0], f["footprint_m"]
building("church_benouville_nave", "church_benouville", cx, cy, w, d, 10.0, (0.72, 0.70, 0.64), f["source_tag"],
         position_tag=f["position_tag"], note=f["note"])
building("church_benouville_tower", "church_benouville", cx + w / 2 - 2.5, cy, 5.0, 5.0, f["tower_height_m"], (0.72, 0.70, 0.64),
         f["source_tag"], position_tag=f["position_tag"], note="Tower on the east end of the nave (placement is a guess).")

f = features["chateau_grounds"]
(cx, cy), (w, d) = f["local_m"][0], f["size_m"]
finish("chateau_grounds", bm_to_mesh(patch("chateau", cx, cy, w, d, 0.12), "chateau_grounds"), "Vegetation", "grounds", (0.30, 0.50, 0.28),
       f["source_tag"], feature_id="chateau_grounds", size_m=json.dumps(f["size_m"]), note=f["note"])

for fid in ("hedge_east_1", "hedge_east_2"):
    f = features[fid]
    bm = prism(fid, [tuple(p) for p in f["local_m"]], f["width_m"], f["height_m"])
    finish(fid, bm_to_mesh(bm, fid), "Vegetation", "hedge", (0.15, 0.38, 0.15), f["source_tag"], feature_id=fid,
           width_m=f["width_m"], height_m=f["height_m"], note=f["note"])

for sid in ("193", "194", "197"):
    f = features[f"glider_marker_{sid}"]
    (cx, cy), (fl, fw) = f["local_m"][0], f["footprint_m"]  # length (along heading) x span
    # base at the lowest ground under the footprint (1 m samples) minus 0.5 m, top above the highest ground
    building(f"glider_marker_{sid}", f["id"], cx, cy, fw, fl, f["height_m"], (0.85, 0.65, 0.15), f["source_tag"], cname="Markers",
             rot=-f["heading_deg_grid"], sink=0.5, site_id=int(sid), length_m=fl, span_m=fw, size_tag=f["size_tag"],
             heading_deg_grid=f["heading_deg_grid"], note=f["note"])

# ---- 16 photo-spot empties ----
for s in spots:
    sid, x, y = s["site_id"], float(s["local_x"]), float(s["local_y"])
    e = bpy.data.objects.new(f"spot_{sid}", None)
    e.empty_display_type = "SPHERE"
    e.empty_display_size = 3.0
    e.location = (x, y, surf(x, y))  # on the mesh surface (ray cast), not the bilinear grid
    coll("Spots").objects.link(e)
    e["source"] = "ww2maps-ref"
    e["site_id"] = int(sid)
    e["role"] = s["role"]
    e["place"] = s["place"]
    e["lat"] = float(s["lat"]); e["lng"] = float(s["lng"])
    e["l93_x"] = float(s["l93_x"]); e["l93_y"] = float(s["l93_y"])
    e["note"] = "Coordinates only, approximate (tens of metres). No image or caption stored."

# ---- 5 key-view cameras ----
EYE = cams["eye_height_m"]
for c in cams["cameras"]:
    sid = c["site_id"]
    x, y = c["position_local_m"]["x"], c["position_local_m"]["y"]
    rel = Vector((x, y, 0.0)) - DECK_W0
    on_deck = 0.0 <= rel.dot(AX) <= DECK_LEN and abs(rel.dot(PERP)) <= DECK_W / 2
    zb = DECK_TOP if on_deck else surf(x, y)  # standing on the deck, not in the canal bed
    cd = bpy.data.cameras.new(f"cam_{sid}")
    cd.lens = 28.0
    cd.clip_end = 3000.0
    co = bpy.data.objects.new(f"cam_{sid}", cd)
    co.location = (x, y, zb + EYE)
    co.rotation_euler = (math.radians(90 + c["pitch_deg"]), 0.0, math.radians(-c["heading_deg_grid"]))
    coll("Cameras").objects.link(co)
    co["source"] = "ww2maps-ref"
    co["site_id"] = sid
    co["view"] = c["view"]
    co["heading_deg_grid"] = c["heading_deg_grid"]
    co["pitch_deg"] = c["pitch_deg"]
    co["confidence"] = c["confidence"]
    co["status"] = c["status"]
    co["note"] = c["how_estimated"]
bpy.context.view_layer.update()
for c in cams["cameras"]:  # verify the heading maths: camera forward vector vs the stated grid heading
    co = bpy.data.objects[f"cam_{c['site_id']}"]
    fwd = co.matrix_world.to_3x3() @ Vector((0, 0, -1))
    hdg = (math.degrees(math.atan2(fwd.x, fwd.y)) + 360) % 360
    assert abs(hdg - c["heading_deg_grid"]) < 0.01, f"camera {c['site_id']} heading {hdg} != {c['heading_deg_grid']}"

# ======================================================================================
# Asserts on the model (acceptance 3, 4, 5, 6)
# ======================================================================================
need = ["terrain", "canal_water", "orne_water", "orne_east_cut_marker", "bridge_deck_west", "bridge_deck_east", "bridge_span_moving",
        "road_bridge_west", "road_bridge_east", "road_west_bank", "road_armour_approach", "cafe_gondree", "west_house_1", "west_house_2",
        "west_house_3", "pillbox", "trench_line", "church_benouville_nave", "church_benouville_tower", "chateau_grounds", "hedge_east_1",
        "hedge_east_2", "glider_marker_193", "glider_marker_194", "glider_marker_197"]
for n in need:
    assert n in bpy.data.objects, f"missing object {n}"
for ob in bpy.data.objects:
    assert "source" in ob.keys() and ob["source"] in SRC_TAGS, f"{ob.name} has no valid source tag"
assert len([o for o in bpy.data.objects if o.name.startswith("spot_")]) == 16
assert len({o["site_id"] for o in bpy.data.objects if o.name.startswith("spot_")}) == 16
assert len([o for o in bpy.data.objects if o.type == "CAMERA"]) == 5
for k in ("l93_origin_x", "l93_origin_y", "height_base_m", "grid_north_note", "sources", "tiles"):
    assert k in scene.keys(), f"scene property {k} missing"
assert scene["l93_origin_x"] == 461580 and scene["l93_origin_y"] == 6909698
assert tuple(span.location) == tuple(HINGE) and span.parent is None
for ob in bpy.data.objects:  # no ww2maps domain or image name anywhere in custom props
    for k in ob.keys():
        assert "ww2maps.net" not in str(ob[k]).lower(), f"{ob.name}.{k}"
for k in scene.keys():
    assert "ww2maps.net" not in str(scene[k]).lower(), k
log("MODEL_ASSERTS PASS", len(bpy.data.objects), "objects")

# ======================================================================================
# Renders
# ======================================================================================
world = bpy.data.worlds.new("sky")
world.color = (0.62, 0.72, 0.85)
scene.world = world
r = scene.render
r.engine = "BLENDER_WORKBENCH"
r.image_settings.file_format = "PNG"
r.film_transparent = False
sh = scene.display.shading
sh.light = "STUDIO"; sh.color_type = "MATERIAL"; sh.show_shadows = False
scene.view_settings.view_transform = "Standard"

top_d = bpy.data.cameras.new("topdown_cam")
top_d.type = "ORTHO"
top_d.ortho_scale = 1400.0
top_d.clip_end = 3000.0
top = bpy.data.objects.new("topdown_cam", top_d)
top["source"] = "estimate"
top.location = ((box["x_min"] + box["x_max"]) / 2, (box["y_min"] + box["y_max"]) / 2, 600.0)
coll("Render").objects.link(top)
scene.camera = top
r.resolution_x, r.resolution_y, r.resolution_percentage = 1400, 1100, 100
r.filepath = os.path.join(OUT, "topdown.png")
bpy.ops.render.render(write_still=True)
assert os.path.getsize(r.filepath) > 1000
log("RENDER", r.filepath)

r.resolution_x, r.resolution_y = 640, 360
for c in cams["cameras"]:
    scene.camera = bpy.data.objects[f"cam_{c['site_id']}"]
    r.filepath = os.path.join(OUT, f"cam_{c['site_id']}.png")
    bpy.ops.render.render(write_still=True)
    assert os.path.getsize(r.filepath) > 1000
    log("RENDER", r.filepath)
scene.camera = top

# ======================================================================================
# Outputs: .blend and .fbx
# ======================================================================================
blend = os.path.join(OUT, "level01_pegasus_blockout.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend, compress=True)
for ob in bpy.data.objects:
    ob.select_set(ob.name != "topdown_cam")
bpy.context.view_layer.objects.active = terrain
fbx = os.path.join(OUT, FBX_NAME)
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"MESH", "EMPTY", "CAMERA"},
                         use_custom_props=True, mesh_smooth_type="FACE", add_leaf_bones=False, **FBX_KW)
for pth in (blend, fbx):
    assert os.path.getsize(pth) > 1000
    log("WROTE", pth, "%.2f MB" % (os.path.getsize(pth) / 1e6))
with open(fbx, "rb") as fh:
    assert b"ww2maps.net" not in fh.read().lower()
log("DONE level01 blockout: smoke PASS, check points PASS, model asserts PASS")
