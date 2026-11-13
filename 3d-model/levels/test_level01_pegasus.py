"""Tests for the Level 01 Pegasus Bridge blockout (plan step S7, acceptance criteria 1-7).

Run (builds into a NEW directory, then tests the result; about 2 minutes):
    "C:\\Program Files\\Blender Foundation\\Blender 5.3\\blender.exe" -b -P 3d-model/levels/test_level01_pegasus.py -- OUTDIR --build
Run against an existing build (no rebuild):
    ... -P 3d-model/levels/test_level01_pegasus.py -- OUTDIR

Run it from the repo root (worktree root). OUTDIR must live under 3d-model/levels/level01_pegasus/_downloads/ (git-ignored).
Writes OUTDIR/test_report.txt and OUTDIR/test_results.json; exits 1 if any check FAILs.
This test does not change the build script or the data and does not start the game. It reads, builds into OUTDIR and writes only there
(plus OUTDIR + "_b" for the second reproducibility build when --build is given).

Check levels: FAIL = acceptance criterion or defect with a pass/fail threshold; INFO = measurement, no threshold.
"""
import sys
sys.dont_write_bytecode = True
import bpy, bmesh, sys, os, math, json, csv, hashlib, subprocess, time, re, struct, random
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from bpy_extras.object_utils import world_to_camera_view

try:
    HERE = os.path.dirname(os.path.abspath(__file__))
except NameError:
    HERE = os.path.abspath(os.path.join(os.getcwd(), "3d-model", "levels"))
L = os.path.join(HERE, "level01_pegasus")
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(ARGS[0])
DO_BUILD = "--build" in ARGS
BLENDER = bpy.app.binary_path
BUILD = os.path.join(HERE, "build_level01_pegasus.py")
BIL = os.path.join(L, "_downloads", "rgealti_box_1m.bil")
BW, BH, BX0, BY1 = 1370, 1070, 460940, 6910350
RES = []   # (id, criterion, status, text)


def rec(cid, crit, ok, text, level="FAIL"):
    status = "PASS" if ok else level
    RES.append((cid, crit, status, text))
    print(f"[{status}] {cid} (crit {crit}): {text}", flush=True)


def info(cid, crit, text):
    RES.append((cid, crit, "INFO", text))
    print(f"[INFO] {cid} (crit {crit}): {text}", flush=True)


def sha(path):
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for b in iter(lambda: fh.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def git(*a):
    return subprocess.run(["git", "-C", REPO, *a], capture_output=True, text=True).stdout


# ---------------------------------------------------------------- source data (independent of the build script)
origin = json.load(open(os.path.join(L, "origin.json"), encoding="utf-8"))
features = {f["id"]: f for f in json.load(open(os.path.join(L, "features.json"), encoding="utf-8"))["features"]}
spots = list(csv.DictReader(open(os.path.join(L, "spots.csv"), encoding="utf-8")))
checks = list(csv.DictReader(open(os.path.join(L, "checkpoints.csv"), encoding="utf-8")))
cams = json.load(open(os.path.join(L, "cameras.json"), encoding="utf-8"))
OX, OY = origin["l93_x"], origin["l93_y"]
BASE = origin["height_base"]["value_m"]
BOX = origin["playable_box_local"]
DEM = np.fromfile(BIL, dtype="<f4").reshape(BH, BW).astype(np.float64)   # rows from the north, 1 m cells
DEMZ = DEM - BASE


def dem_z(x, y):
    """Bilinear 1 m DEM height (z local) at local (x, y), same convention as terrain.py (cell centres)."""
    X, Y = OX + np.asarray(x, float), OY + np.asarray(y, float)
    c = X - BX0 - 0.5
    r = BY1 - Y - 0.5
    c0 = np.clip(np.floor(c).astype(int), 0, BW - 2)
    r0 = np.clip(np.floor(r).astype(int), 0, BH - 2)
    fc, fr = np.clip(c - c0, 0, 1), np.clip(r - r0, 0, 1)
    top = DEM[r0, c0] * (1 - fc) + DEM[r0, c0 + 1] * fc
    bot = DEM[r0 + 1, c0] * (1 - fc) + DEM[r0 + 1, c0 + 1] * fc
    return top * (1 - fr) + bot * fr - BASE


def dem_water(x, y, thr=0.06):
    """True where the 1 m DEM is flat at canal water level (3.4 m NGF = z 0). Canal water or equally flat ground."""
    X, Y = OX + np.asarray(x, float), OY + np.asarray(y, float)
    c = np.clip(np.floor(X - BX0).astype(int), 0, BW - 1)
    r = np.clip(np.floor(BY1 - Y).astype(int), 0, BH - 1)
    return np.abs(DEMZ[r, c]) <= thr


# ======================================================================================
# Criterion 1: one command, clean OUTDIR
# ======================================================================================
def tree_state():
    out = {}
    for root, dirs, files in os.walk(os.path.join(HERE)):
        dirs[:] = [d for d in dirs if d not in ("_downloads", "__pycache__")]
        for f in files:
            p = os.path.join(root, f)
            out[os.path.relpath(p, HERE)] = sha(p)
    return out


def c1_build():
    if DO_BUILD:
        assert not os.path.exists(OUT) or not os.listdir(OUT), "OUTDIR must be new/empty for the clean-build test"
        before = tree_state()
        t = time.time()
        # exactly the README command: from the repo root, relative OUTDIR
        rel = os.path.relpath(OUT, REPO)
        p = subprocess.run([BLENDER, "-b", "-P", "3d-model/levels/build_level01_pegasus.py", "--", rel],
                           cwd=REPO, capture_output=True, text=True)
        dt = time.time() - t
        open(os.path.join(OUT, "_build_stdout.txt"), "w", encoding="utf-8").write(p.stdout + "\n--stderr--\n" + p.stderr)
        rec("C1.1 build exits 0", 1, p.returncode == 0, f"exit {p.returncode}, {dt:.0f} s, command from repo root with relative OUTDIR")
        for tag in ("SMOKE_TEST PASS", "CHECK_POINTS within 0.1 m", "MODEL_ASSERTS PASS", "DONE level01"):
            rec(f"C1.2 build log has '{tag}'", 1, tag in p.stdout, "present" if tag in p.stdout else "missing")
        after = tree_state()
        rec("C1.3 build leaves source/data untouched", 1, before == after,
            "same hashes before/after" if before == after else f"changed: {[k for k in after if before.get(k) != after[k]]}")
        # second build: different cwd, absolute OUTDIR, same result?
        out_b = OUT + "_b"
        os.makedirs(out_b, exist_ok=True)
        p2 = subprocess.run([BLENDER, "-b", "-P", BUILD, "--", out_b], cwd=os.environ.get("TEMP", "."), capture_output=True, text=True)
        rec("C1.4 build from another cwd, absolute OUTDIR", 1, p2.returncode == 0, f"exit {p2.returncode}")
    out_b = OUT + "_b"
    if os.path.isfile(os.path.join(out_b, "topdown.png")):
        for nm in ["topdown.png"] + [f"cam_{c['site_id']}.png" for c in cams["cameras"]]:
            _, a = png_stats(os.path.join(OUT, nm)); _, b = png_stats(os.path.join(out_b, nm))
            d = np.abs(a - b)
            rec(f"C1.5 two clean builds render {nm} the same (max abs pixel diff <= 0.02, <= 0.1% pixels differ)", 1,
                d.max() <= 0.02 and (d > 0.004).mean() <= 0.001,
                f"max diff {d.max():.4f}, {(d > 0.004).mean()*100:.3f}% of pixels differ, sha identical: {sha(os.path.join(OUT, nm)) == sha(os.path.join(out_b, nm))}")
    need = ["level01_pegasus_blockout.blend", "level01_pegasus_blockout.fbx", "topdown.png", "smoke_test.json"] + \
           [f"cam_{c['site_id']}.png" for c in cams["cameras"]]
    miss = [n for n in need if not os.path.isfile(os.path.join(OUT, n)) or os.path.getsize(os.path.join(OUT, n)) < (100 if n.endswith('.json') else 1000)]
    rec("C1.6 outputs present (blend, fbx, topdown, one render per camera)", 1, not miss, "all present" if not miss else f"missing/empty {miss}")
    stray = [f for f in os.listdir(OUT) if f not in need and not f.startswith(("test_", "_build"))]
    rec("C1.7 no stray files in OUTDIR (e.g. _smoke_cube.fbx)", 1, not stray, str(stray or "none"), level="FAIL")


def png_stats(path):
    img = bpy.data.images.load(path)
    px = np.array(img.pixels[:]).reshape(-1, 4)[:, :3]
    bpy.data.images.remove(img)
    return float(px.std()), px


# ======================================================================================
# open the blend
# ======================================================================================
def open_blend():
    bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, "level01_pegasus_blockout.blend"))
    return bpy.context.scene


def tests_model(scene):
    OBJ = bpy.data.objects
    terrain = OBJ["terrain"]
    dg = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(terrain, dg)

    def mesh_z(x, y):
        h = bvh.ray_cast(Vector((x, y, 500.0)), Vector((0, 0, -1)))
        return None if h[0] is None else h[0].z

    # ---------------------------------------------------------------- criterion 2
    vs = np.array([v.co[:] for v in terrain.data.vertices])
    cover = vs[:, 0].min() <= BOX["x_min"] and vs[:, 0].max() >= BOX["x_max"] and vs[:, 1].min() <= BOX["y_min"] and vs[:, 1].max() >= BOX["y_max"]
    rec("C2.1 terrain covers the whole playable box", 2, cover,
        f"mesh x {vs[:,0].min():.0f}..{vs[:,0].max():.0f} y {vs[:,1].min():.0f}..{vs[:,1].max():.0f} vs box x {BOX['x_min']}..{BOX['x_max']} y {BOX['y_min']}..{BOX['y_max']}")
    rec("C2.2 terrain has no NaN, has the expected z range", 2, np.isfinite(vs).all() and abs(vs[:, 2].max() - DEMZ.max()) < 0.2,
        f"z {vs[:,2].min():.2f}..{vs[:,2].max():.2f}; DEM z {DEMZ.min():.2f}..{DEMZ.max():.2f} (min differs because the canal bed is carved)")
    nz = np.array([p.normal.z for p in terrain.data.polygons])
    rec("C2.3 every terrain triangle/quad faces up (winding incl. pinned cells)", 2, (nz > 0).all(), f"{int((nz<=0).sum())} faces with normal z <= 0 of {len(nz)}")

    # named check points, against the RAW 1 m DEM (not checkpoints.csv)
    worst = 0
    txt = []
    for c in checks:
        x, y = float(c["local_x"]), float(c["local_y"])
        src_bil = float(dem_z(x, y)); mz = mesh_z(x, y)
        e = mz - src_bil
        worst = max(worst, abs(e))
        txt.append(f"{c['site_id']}: DEM {src_bil:.3f} mesh {mz:.3f} err {e:+.3f}")
    rec("C2.4 three named check points within 0.1 m of the raw 1 m DEM (mesh ray cast)", 2, worst <= 0.1, "; ".join(txt))
    pinned = json.loads(terrain.get("pinned_check_points", "[]"))
    info("C2.5 pinned vertices", 2, f"terrain['pinned_check_points'] = {pinned}")

    # Honest check: random points, mesh vs 1 m DEM. Exclude points the build deliberately carves (canal trench): flagged by DEM<=0.5 inside x 8+-13.5.
    rng = random.Random(20261113)
    n = 6000
    px = np.array([rng.uniform(BOX["x_min"] + 1, BOX["x_max"] - 1) for _ in range(n)])
    py = np.array([rng.uniform(BOX["y_min"] + 1, BOX["y_max"] - 1) for _ in range(n)])
    dz = dem_z(px, py)
    mz = np.array([mesh_z(a, b) for a, b in zip(px, py)])
    err = mz - dz
    carved = np.zeros(len(px), bool)   # nothing is excluded: if the build carves the canal bed, that shows up as error
    # local slope of the 1 m DEM (rise over 2 m)
    sl = np.hypot(dem_z(px + 1, py) - dem_z(px - 1, py), dem_z(px, py + 1) - dem_z(px, py - 1)) / 2.0
    e = np.abs(err[~carved])
    s = sl[~carved]
    stats = (f"n={int((~carved).sum())} (no exclusions); |err| median {np.median(e):.3f} p95 {np.percentile(e,95):.3f} "
             f"p99 {np.percentile(e,99):.3f} max {e.max():.3f} m")
    tol = 0.10 + 1.0 * s          # 0.1 m plus 1 m of height per metre of slope: a 2 m grid cannot do better than ~slope * 1 m of horizontal error
    rec("C2.6 random-sample mesh vs 1 m DEM: |err| <= 0.10 + 1.0*slope m at >= 99% of points, ", 2,
        (e <= tol).mean() >= 0.99, stats + f"; {(e <= tol).mean()*100:.2f}% within the slope-aware tolerance; worst point: err {e.max():.2f} m at slope {s[np.argmax(e)]:.2f}")
    rec("C2.6b same, plain tolerance: p95 <= 0.15 m, p99 <= 0.30 m", 2, np.percentile(e, 95) <= 0.15 and np.percentile(e, 99) <= 0.30, stats, level="INFO")
    flat = s < 0.05
    rec("C2.7 on flat ground (slope < 0.05) mesh within 0.1 m of the DEM at >= 99% of points", 2,
        (e[flat] <= 0.1).mean() >= 0.99, f"{flat.sum()} flat points, {(e[flat] <= 0.1).mean()*100:.2f}% within 0.1 m, max {e[flat].max():.3f}")
    steep = s >= 0.5
    info("C2.8 error on steep ground (slope >= 0.5)", 2, f"{steep.sum()} points, |err| p95 {np.percentile(e[steep],95) if steep.any() else float('nan'):.3f} max {e[steep].max() if steep.any() else float('nan'):.3f}")
    # does the pin help or hurt its own cell? compare mesh vs DEM in the cell with the pin vs the bare 2 m bilinear in the same cell
    for sid, _err in pinned:
        c = next(r for r in checks if r["site_id"] == sid)
        x0, y0 = float(c["local_x"]), float(c["local_y"])
        cx0 = math.floor(x0 / 2) * 2; cy0 = math.floor(y0 / 2) * 2
        qx = np.array([cx0 + rng.uniform(0, 2) for _ in range(300)]); qy = np.array([cy0 + rng.uniform(0, 2) for _ in range(300)])
        d1 = dem_z(qx, qy); m1 = np.array([mesh_z(a, b) for a, b in zip(qx, qy)])
        info(f"C2.9 pinned cell of site {sid}", 2, f"cell |err| mesh vs DEM mean {np.abs(m1-d1).mean():.3f} max {np.abs(m1-d1).max():.3f}; "
             f"DEM varies {d1.min():.2f}..{d1.max():.2f} inside this 2 m cell (pin is a single-vertex fix, the cell is steep)")

    # ---------------------------------------------------------------- criterion 3: custom props + README
    sc = bpy.context.scene
    rec("C3.1 scene custom props (origin X/Y, base, grid north, sources, tiles) present and equal to origin.json", 3,
        all(k in sc.keys() for k in ("l93_origin_x", "l93_origin_y", "height_base_m", "grid_north_note", "sources", "tiles"))
        and sc["l93_origin_x"] == OX and sc["l93_origin_y"] == OY and abs(sc["height_base_m"] - BASE) < 1e-9,
        f"keys {sorted(sc.keys())}")
    info("C3.2 scene['tiles']", 3, repr(sc.get("tiles")))
    readme = open(os.path.join(L, "README.md"), encoding="utf-8").read()
    for label, token in (("origin X", "461580"), ("origin Y", "6909698"), ("height base", "3.4"), ("grid north", "grid north")):
        rec(f"C3.3 README states {label}", 3, token in readme, f"'{token}' {'found' if token in readme else 'NOT found'}")
    rec("C3.4 README lists the source tile(s) / source box (bbox 460940,6909280,462310,6910350)", 3, "460940" in readme and "6909280" in readme,
        "README has no source tile list or bbox (only credits.md has the WMS BBOX; tiles.md is stale: it plans 6 ASC tiles that were never used)")
    tiles_md = open(os.path.join(L, "tiles.md"), encoding="utf-8").read()
    rec("C3.5 tiles.md consistent with what was actually used (one WMS-R box, no tiles)", 3, any(t in tiles_md.lower() for t in ("superseded", "not used", "no tiles were used", "were never fetched")) and "bil" in tiles_md.lower(),
        "tiles.md must say tiles were not used and name the one WMS-R BIL box (accepts: superseded / not used / no tiles were used / never fetched)")

    # ---------------------------------------------------------------- criterion 4: objects + tags
    need = ["terrain", "canal_water", "orne_water", "orne_east_cut_marker", "bridge_deck_west", "bridge_deck_east", "bridge_span_moving",
            "road_bridge_west", "road_bridge_east", "road_west_bank", "road_armour_approach", "cafe_gondree", "pillbox",
            "church_benouville_nave", "church_benouville_tower", "hedge_east_1", "hedge_east_2",
            "glider_marker_193", "glider_marker_194", "glider_marker_197"]
    miss = [n for n in need if n not in OBJ]
    rec("C4.1 required named objects exist", 4, not miss, "all present" if not miss else f"missing {miss}")
    bad = [o.name for o in OBJ if o.get("source") not in ("lidar-today", "ww2maps-ref", "aerial-1944", "estimate")]
    rec("C4.2 every object has a valid source tag", 4, not bad, f"{len(OBJ)} objects, bad: {bad}")
    span = OBJ["bridge_span_moving"]
    LB = features["lift_bridge"]
    hx, hy = LB["hinge_local_m"]
    wp = np.array([(span.matrix_world @ v.co)[:] for v in span.data.vertices])
    axv = np.array(LB["deck_east_end"]) - np.array(LB["deck_west_end"]); axv = axv / np.linalg.norm(axv)
    tt = (wp[:, :2] - np.array([hx, hy])) @ axv
    rec("C4.3 moving span: object origin at the hinge from features.json, unparented, west end of the span at the hinge, length = moving_span_length_m", 4,
        span.parent is None and abs(span.location.x - hx) < 0.05 and abs(span.location.y - hy) < 0.05 and abs(tt.min()) < 0.5 and abs(tt.max() - LB["moving_span_length_m"]) < 0.5,
        f"origin ({span.location.x:.2f},{span.location.y:.2f},{span.location.z:.2f}) vs features hinge ({hx},{hy}); span runs {tt.min():.2f}..{tt.max():.2f} m along the deck axis (expected 0..{LB['moving_span_length_m']})")
    from mathutils import Matrix
    cb = math.radians(features["canal"]["bearing_deg_east_of_grid_north"])
    axis = Vector((math.sin(cb), math.cos(cb), 0.0))
    h3 = Vector((hx, hy, span.location.z))
    M = Matrix.Translation(h3) @ Matrix.Rotation(math.radians(60), 4, axis) @ Matrix.Translation(-h3)
    rot = np.array([(M @ Vector(p))[:] for p in wp])
    west = tt < 1.0; east = tt > LB["moving_span_length_m"] - 1.0
    dwz = np.abs(rot[west, 2] - wp[west, 2]); dez = np.abs(rot[east, 2] - wp[east, 2])
    rec("C4.4 span rotated 60 deg about the canal-parallel hinge axis through features.json's hinge: west end stays (<= 4 m), east end moves > 20 m (open state)", 4,
        dwz.max() < 4.0 and dez.min() > 20.0,
        f"west end z moves up to {dwz.max():.2f} m, east end z moves {dez.min():.1f}..{dez.max():.1f} m (span {LB['moving_span_length_m']} m, axis bearing {math.degrees(cb):.1f})")

    # ---- (a) canal / bridge / roads versus the DEM
    def dem_water_band(ly):
        row = DEMZ[int(BY1 - OY - ly - 0.5)]
        m = np.abs(row) <= 0.06
        out, i = [], 0
        while i < len(m):
            if m[i]:
                j = i
                while j + 1 < len(m) and m[j + 1]:
                    j += 1
                if j - i + 1 >= 30:
                    out.append((i + BX0 + 0.5 - OX, j + BX0 + 0.5 - OX))
                i = j + 1
            else:
                i += 1
        return out
    track = {}
    seed = [r for r in dem_water_band(0) if r[0] <= 0 <= r[1]]
    assert seed, "origin (bridge point) is not in a DEM water-level band at y=0"
    for step in (20, -20):
        prev = seed[0]
        for ly in (range(0, 640, 20) if step > 0 else range(0, -400, -20)):
            best, bo = None, 0
            for r in dem_water_band(ly):
                ov = min(r[1], prev[1] + 30) - max(r[0], prev[0] - 30)
                if ov > bo:
                    bo, best = ov, r
            if best is None:
                break
            prev = best; track[ly] = best
    ys = np.array(sorted(track)); cs = np.array([(track[y][0] + track[y][1]) / 2 for y in ys])
    slope, icpt = np.polyfit(ys, cs, 1)
    bearing = math.degrees(math.atan(slope))
    fc = np.array(features["canal"]["local_m"])
    diffs = [abs(((track[int(y)][0] + track[int(y)][1]) / 2) - x) for x, y in fc if int(y) in track]
    perp_w = [(track[y][1] - track[y][0]) * math.cos(math.radians(bearing)) for y in ys]
    info("C4.5 DEM water-level band (tracked by this test from the bridge row)", 4,
         f"centre line x = {slope:.3f}*y + {icpt:.1f}; bearing {bearing:.1f} deg east of grid north (features.json: {features['canal']['bearing_deg_east_of_grid_north']}); "
         f"across-canal width mean {np.mean(perp_w):.1f} m, min {min(perp_w):.1f}, max {max(perp_w):.1f} (features.json width_m_today {features['canal']['width_m_today']}); "
         f"features centre line vs this test's DEM centre line: mean {np.mean(diffs):.1f} m, max {max(diffs):.1f} m")
    rec("C4.5b features.json canal centre line agrees with an independent DEM trace (<= 5 m) and its bearing within 1 deg", 4,
        max(diffs) <= 5.0 and abs(features["canal"]["bearing_deg_east_of_grid_north"] - bearing) <= 1.0,
        f"max centre diff {max(diffs):.1f} m, bearing {bearing:.1f} vs {features['canal']['bearing_deg_east_of_grid_north']}")
    cw = OBJ["canal_water"]
    bvh_w = BVHTree.FromObject(cw, bpy.context.evaluated_depsgraph_get())
    cwv = np.array([v.co[:] for v in cw.data.vertices])
    gy = np.arange(max(cwv[:, 1].min(), BOX["y_min"] + 1), min(cwv[:, 1].max(), BOX["y_max"] - 1), 5.0)
    gx = np.arange(cwv[:, 0].min() + 0.5, cwv[:, 0].max(), 2.0)
    G = []
    for b_ in gy:
        for a_ in gx:
            if bvh_w.ray_cast(Vector((a_, b_, 500.0)), Vector((0, 0, -1)))[0] is not None:
                G.append((a_, b_))
    G = np.array(G)
    assert len(G) > 100, "canal_water has no area"
    gz = np.array([mesh_z(a, b) for a, b in G])
    visible = (gz <= 0.01)
    rec("C4.6 canal_water: at least 80% of its area is visible (terrain not above water level)", 4, visible.mean() >= 0.8,
        f"{visible.mean()*100:.0f}% of the water surface is at or above the terrain; {(~visible).mean()*100:.0f}% is buried (terrain above water level)")
    on_dem_water = dem_water(G[:, 0], G[:, 1])
    rec("C4.7 canal_water lies over DEM water-level ground (>= 80%)", 4, on_dem_water.mean() >= 0.8,
        f"{on_dem_water.mean()*100:.0f}% of the water surface sits over DEM ground at water level")
    # water strip from features.json (water edges, one west/east point per 20 m row)
    ww = np.array(features["canal"]["bank_west_local_m"]); we = np.array(features["canal"]["bank_east_local_m"])
    poly = np.vstack([ww, we[::-1]])

    def in_poly(x, y):
        inside = False
        n_ = len(poly)
        for k in range(n_):
            x0_, y0_ = poly[k]; x1_, y1_ = poly[(k + 1) % n_]
            if (y0_ > y) != (y1_ > y) and x < (x1_ - x0_) * (y - y0_) / (y1_ - y0_) + x0_:
                inside = not inside
        return inside
    rng2 = random.Random(5)
    ins = []; outs = []
    for _ in range(1500):
        xx = rng2.uniform(-250, 400); yy = rng2.uniform(-370, 590)
        (ins if in_poly(xx, yy) else outs).append((xx, yy))
    hit = lambda pt: bvh_w.ray_cast(Vector((pt[0], pt[1], 500.0)), Vector((0, 0, -1)))[0] is not None
    # points well inside the strip (> 2 m from any edge) must be water; points > 3 m outside must not be
    def edge_dist(pt):
        pts_ = np.vstack([ww, we])
        return float(np.min(np.hypot(pts_[:, 0] - pt[0], pts_[:, 1] - pt[1])))
    ins_core = [q for q in ins if edge_dist(q) > 6.0]
    outs_far = [q for q in outs if edge_dist(q) > 6.0]
    cov_in = np.mean([hit(q) for q in ins_core]); cov_out = np.mean([hit(q) for q in outs_far])
    rec("C4.8 canal_water covers the features.json water strip (>= 95% of points inside it) and stays inside it (<= 2% of points outside)", 4,
        cov_in >= 0.95 and cov_out <= 0.02,
        f"{len(ins_core)} points inside the 46 m water strip: {cov_in*100:.1f}% under canal_water; {len(outs_far)} points outside: {cov_out*100:.1f}% under canal_water; strip width (perp) = "
        f"{np.mean(np.hypot(ww[:,0]-we[:,0], ww[:,1]-we[:,1])):.1f} m")
    bgs = [OBJ.get("canal_bank_ground_west"), OBJ.get("canal_bank_ground_east")]
    okb = all(o is not None for o in bgs)
    ov = 0; flo = 0.0
    if okb:
        for o in bgs:
            okb = okb and o.get("source") in ("lidar-today", "estimate") and o.get("walkable") in (1, True) and o.get("is_water") in (0, False)
            for v in o.data.vertices:
                q = o.matrix_world @ v.co
                ov += int(in_poly(q.x, q.y) and edge_dist((q.x, q.y)) > 2.0)
                flo = max(flo, abs(q.z - mesh_z(q.x, q.y)))
    rec("C4.8b canal_bank_ground_west/east exist, tagged, walkable, is_water False, outside the water strip (0 vertices), within 0.3 m of the terrain", 4,
        okb and ov == 0 and flo <= 0.3, f"present {[o is not None for o in bgs]}, vertices inside water strip {ov}, max height off terrain {flo:.2f} m")

    def on_water_frac(name):
        ob = OBJ[name]
        pts = np.array([(ob.matrix_world @ v.co)[:] for v in ob.data.vertices])
        return float(dem_water(pts[:, 0], pts[:, 1]).mean()), pts

    for nm in ("road_bridge_east", "road_bridge_west", "road_west_bank", "road_armour_approach"):
        f, pts = on_water_frac(nm)
        rec(f"C4.9 {nm} is on dry ground (< 10% of its vertices over water-level DEM)", 4, f < 0.10,
            f"{f*100:.0f}% of vertices over DEM ground flat at water level (x {pts[:,0].min():.0f}..{pts[:,0].max():.0f}, y {pts[:,1].min():.0f}..{pts[:,1].max():.0f})")
    # bridge: sample the DEM along the deck axis (axis taken from the build's deck objects, not from features.json)
    dw = OBJ["bridge_deck_west"]; de = OBJ["bridge_deck_east"]
    pw = np.array([(dw.matrix_world @ v.co)[:] for v in dw.data.vertices]); pe = np.array([(de.matrix_world @ v.co)[:] for v in de.data.vertices])
    a0 = np.array([pw[:, 0].mean(), pw[:, 1].mean()]); a1 = np.array([pe[:, 0].mean(), pe[:, 1].mean()])
    u = (a1 - a0) / np.linalg.norm(a1 - a0)
    allp = np.vstack([pw, pe, wp])
    t_all = (allp[:, :2] - a0) @ u
    t0, t1 = t_all.min(), t_all.max()
    tts = np.arange(t0 - 40, t1 + 40, 0.5)
    pts_ax = a0[None, :] + tts[:, None] * u[None, :]
    wat = dem_water(pts_ax[:, 0], pts_ax[:, 1])
    k0 = int(np.argmin(np.hypot(pts_ax[:, 0], pts_ax[:, 1])))
    i = k0
    while i > 0 and wat[i - 1]:
        i -= 1
    j = k0
    while j < len(wat) - 1 and wat[j + 1]:
        j += 1
    wb0, wb1 = tts[i], tts[j]
    cross = wb1 - wb0
    sp_len = tt.max() - tt.min()
    # water crossing along the deck axis, from features.json's water strip (not the 99 m flat band)
    in_w = np.array([in_poly(*pt) for pt in pts_ax])
    best = (0, 0, 0); k = 0
    while k < len(in_w):
        if in_w[k]:
            m_ = k
            while m_ + 1 < len(in_w) and in_w[m_ + 1]:
                m_ += 1
            if m_ - k > best[0]:
                best = (m_ - k, k, m_)
            k = m_ + 1
        else:
            k += 1
    i, j = best[1], best[2]
    ws0, ws1 = tts[i], tts[j]
    rec("C4.10a deck extends onto dry ground (outside the water strip) by >= 8 m on both sides, along its own axis", 4, t0 <= ws0 - 8 and t1 >= ws1 + 8,
        f"deck axis t {t0:.1f}..{t1:.1f} m; water strip crossing t {ws0:.1f}..{ws1:.1f} m ({ws1-ws0:.1f} m) -> dry deck {ws0 - t0:.1f} m west, {t1 - ws1:.1f} m east")
    tsp = (wp[:, :2] - a0) @ u
    t_sp0, t_sp1 = float(tsp.min()), float(tsp.max())
    rec("C4.10b moving span lies over the water strip (both ends within 0.5 m of water, inside the crossing) and is 28.4 m (features.json)", 4,
        t_sp0 >= ws0 - 0.5 and t_sp1 <= ws1 + 0.5 and abs((t_sp1 - t_sp0) - features["lift_bridge"]["moving_span_length_m"]) < 0.5,
        f"span t {t_sp0:.1f}..{t_sp1:.1f} ({t_sp1 - t_sp0:.1f} m) vs water t {ws0:.1f}..{ws1:.1f}; fixed deck over water {t_sp0 - ws0:.1f} m west, {ws1 - t_sp1:.1f} m east (features says {features['lift_bridge']['fixed_span_water_side_each_m']})")
    # deck lengths: consistency between features.json fields and the build
    LBf = features["lift_bridge"]
    dwt = (pw[:, :2] - a0) @ u; det = (pe[:, :2] - a0) @ u
    len_w, len_e = float(dwt.max() - dwt.min()), float(det.max() - det.min())
    rec("C4.14a built fixed decks equal features.json fixed_approach_west/east length_m", 4,
        abs(len_w - LBf["fixed_approach_west"]["length_m"]) < 0.5 and abs(len_e - LBf["fixed_approach_east"]["length_m"]) < 0.5,
        f"built {len_w:.1f} and {len_e:.1f} m; features {LBf['fixed_approach_west']['length_m']} and {LBf['fixed_approach_east']['length_m']} m")
    rec("C4.14b features.json total = 2 fixed + moving span", 4,
        abs(LBf["deck_total_length_m"] - (2 * LBf["fixed_approach_west"]["length_m"] + LBf["moving_span_length_m"])) < 0.5,
        f"{LBf['deck_total_length_m']} vs {2*LBf['fixed_approach_west']['length_m'] + LBf['moving_span_length_m']}")
    d_end = math.hypot(LBf["deck_west_end"][0] - LBf["water_west_edge"][0], LBf["deck_west_end"][1] - LBf["water_west_edge"][1])
    rec("C4.14c features.json: fixed_span_water_side_each_m + approach_deck_beyond_each_bank_m = fixed approach length (8.8 + beyond = 36.8)", 4,
        abs(LBf["fixed_span_water_side_each_m"] + LBf["approach_deck_beyond_each_bank_m"] - LBf["fixed_approach_west"]["length_m"]) < 0.5,
        f"8.8 + {LBf['approach_deck_beyond_each_bank_m']} = {LBf['fixed_span_water_side_each_m'] + LBf['approach_deck_beyond_each_bank_m']} but fixed approach is {LBf['fixed_approach_west']['length_m']}; "
        f"the real distance from the water edge to the deck end is {d_end:.1f} m (the 18.0 field still measures from the old 99 m flat-band bank)")
    # deck clearance over the ground on the dry sides
    gaps = []
    for ob_ in (dw, de):
        zb_ = min((ob_.matrix_world @ v.co).z for v in ob_.data.vertices)
        for v in ob_.data.vertices:
            q = ob_.matrix_world @ v.co
            if not in_poly(q.x, q.y):
                gaps.append(zb_ - mesh_z(q.x, q.y))
    info("C4.14d fixed deck underside versus ground on dry land", 4, f"underside is {min(gaps):+.2f}..{max(gaps):+.2f} m above the terrain under the deck ends/corners (positive = floating)")
    info("C4.14e 102 m total bridge", 4, f"deck total {LBf['deck_total_length_m']} m vs 1994 bridge 45.7 m long and 1944 span 28.4 m: the 74 m beyond the moving span is modelled approach causeway, a proxy not a 1944 measurement")
    brg = (math.degrees(math.atan2(u[0], u[1])) + 360) % 360
    ang = (brg - bearing) % 180
    rec("C4.11 deck axis is perpendicular to the DEM canal bearing (90 +- 3 deg)", 4, abs(ang - 90) <= 3.0,
        f"deck bearing {brg:.1f}, DEM canal bearing {bearing:.1f}, angle between {ang:.1f} deg")
    edge_info = []
    for ly in (-200, 0, 200, 400):
        a_, b_ = track[ly]
        r_ = DEMZ[int(BY1 - OY - ly - 0.5)]
        cx0 = int(a_ + OX - BX0 - 0.5); cx1 = int(b_ + OX - BX0 - 0.5)
        edge_info.append(f"y={ly}: DEM reaches {r_[max(cx0 - 25, 0):cx0 + 1].max():.1f} m within 25 m west of the band, {r_[cx1:cx1 + 26].max():.1f} m within 25 m east")
    info("C4.13 DEM band edges", 4, "; ".join(edge_info))
    ow = OBJ["orne_water"]
    opts = np.array([v.co[:] for v in ow.data.vertices])
    ow_on = dem_z(opts[:, 0], opts[:, 1])
    info("C4.12 Orne strip", 4, f"{(ow_on < -1.0).mean()*100:.0f}% of the orne_water vertices are over DEM below -1 m (river bed); x max {opts[:,0].max():.1f} (box x_max {BOX['x_max']}); marker at x {OBJ['orne_east_cut_marker'].location.x:.1f}")

    # ---------------------------------------------------------------- (c) buildings, markers, hedges on the ground
    def foot_report(name, nx=7):
        ob = OBJ[name]
        pts = np.array([(ob.matrix_world @ v.co)[:] for v in ob.data.vertices])
        x0, x1, y0, y1 = pts[:, 0].min(), pts[:, 0].max(), pts[:, 1].min(), pts[:, 1].max()
        gxs = np.linspace(x0, x1, nx); gys = np.linspace(y0, y1, nx)
        gzs = np.array([mesh_z(a, b) for a in gxs for b in gys])
        wat = float(np.mean([dem_water(a, b) for a in gxs for b in gys]))
        base, top = pts[:, 2].min(), pts[:, 2].max()
        return dict(base=base, top=top, gmin=gzs.min(), gmax=gzs.max(), wat=wat, cx=(x0 + x1) / 2, cy=(y0 + y1) / 2, w=x1 - x0, d=y1 - y0)

    for nm in ("cafe_gondree", "west_house_1", "west_house_2", "west_house_3", "pillbox", "church_benouville_nave", "church_benouville_tower",
               "glider_marker_193", "glider_marker_194", "glider_marker_197"):
        r = foot_report(nm)
        ok_ground = r["base"] <= r["gmin"] + 0.05 and r["top"] >= r["gmax"] + 0.5
        rec(f"C(c).1 {nm} sits on the ground (base at or below lowest ground under it, top above highest)", 4, ok_ground,
            f"centre ({r['cx']:.1f},{r['cy']:.1f}) footprint {r['w']:.1f}x{r['d']:.1f}; base z {r['base']:.2f}, ground under footprint {r['gmin']:.2f}..{r['gmax']:.2f}, top {r['top']:.2f}")
        rec(f"C(c).2 {nm} is on dry land (< 25% of footprint over water-level DEM)", 4, r["wat"] < 0.25,
            f"{r['wat']*100:.0f}% of the footprint samples are over DEM at water level")
    # distance from the glider markers to their spot pins
    for sid in ("193", "194", "197"):
        sp = next(s for s in spots if s["site_id"] == sid); g = OBJ[f"glider_marker_{sid}"]
        gb = foot_report(f"glider_marker_{sid}")
        d = math.hypot(gb["cx"] - float(sp["local_x"]), gb["cy"] - float(sp["local_y"]))
        rec(f"C(c).3 glider marker {sid} is centred on its spot coordinates (<= 1 m)", 4, d <= 1.0, f"{d:.2f} m from spots.csv")
    # church vs spot 1038
    ch = foot_report("church_benouville_nave"); sp = next(s for s in spots if s["site_id"] == "1038")
    rec("C(c).4 church nave is centred on spot 1038 (<= 1 m)", 4, math.hypot(ch["cx"] - float(sp["local_x"]), ch["cy"] - float(sp["local_y"])) <= 1.0,
        f"nave centre ({ch['cx']:.1f},{ch['cy']:.1f}) vs spot ({sp['local_x']},{sp['local_y']}); features.json itself says the pin 'may be the photographer', so the church position is unverified")
    # overlaps: cafe vs roads, pillbox vs bridge deck/road
    def overlaps(bname, rname, pad=0.0):
        b = OBJ[bname]; r = OBJ[rname]
        bp = np.array([(b.matrix_world @ v.co)[:] for v in b.data.vertices]); rp = np.array([(r.matrix_world @ v.co)[:] for v in r.data.vertices])
        inside = (rp[:, 0] >= bp[:, 0].min() - pad) & (rp[:, 0] <= bp[:, 0].max() + pad) & (rp[:, 1] >= bp[:, 1].min() - pad) & (rp[:, 1] <= bp[:, 1].max() + pad)
        return int(inside.sum())
    for b, r in (("cafe_gondree", "road_west_bank"), ("cafe_gondree", "road_bridge_west"), ("pillbox", "road_bridge_east"), ("pillbox", "bridge_deck_east"),
                 ("west_house_1", "road_west_bank"), ("west_house_2", "road_west_bank"), ("west_house_3", "road_west_bank"),
                 ("west_house_1", "road_bridge_west"), ("west_house_3", "road_bridge_west")):
        n = overlaps(b, r)
        rec(f"C(c).5 {b} does not sit inside {r}", 4, n == 0, f"{n} {r} vertices fall inside the {b} footprint ")
    # hedgerows
    f, pts = on_water_frac("trench_line")
    rec("C(c).6t trench_line stands on dry land (< 25% of vertices over water-level DEM)", 4, f < 0.25, f"{f*100:.0f}% over DEM water-level ground")
    for nm in ("hedge_east_1", "hedge_east_2"):
        f, pts = on_water_frac(nm)
        base_gap = float(np.mean([p[2] - mesh_z(p[0], p[1]) for p in pts]))
        rec(f"C(c).6 {nm} stands on dry land (< 25% of vertices over water-level DEM)", 4, f < 0.25,
            f"{f*100:.0f}% over DEM water-level ground; hedge vertices x {pts[:,0].min():.0f}..{pts[:,0].max():.0f} y {pts[:,1].min():.0f}..{pts[:,1].max():.0f}")
        ob = OBJ[nm]
        rec(f"C(c).7 {nm} is embedded in the ground (lowest vertices below terrain)", 4,
            all(p[2] <= mesh_z(p[0], p[1]) + 0.01 for p in pts if p[2] < mesh_z(p[0], p[1]) + 1.0), "bottom ring below the surface")

    # ---------------------------------------------------------------- criterion 5: spots + cameras
    sp_obj = [o for o in OBJ if o.name.startswith("spot_")]
    ids = sorted(int(o["site_id"]) for o in sp_obj)
    rec("C5.1 16 spot empties with the site ids of spots.csv", 5, len(sp_obj) == 16 and ids == sorted(int(s["site_id"]) for s in spots), f"{len(sp_obj)} empties, ids {ids}")
    bad = []; water = []
    for s in spots:
        o = OBJ[f"spot_{s['site_id']}"]
        x, y = float(s["local_x"]), float(s["local_y"])
        mzz = mesh_z(x, y)
        if o.type != "EMPTY" or abs(o.location.x - x) > 0.01 or abs(o.location.y - y) > 0.01 or abs(o.location.z - mzz) > 0.05:
            bad.append((s["site_id"], tuple(round(v, 2) for v in o.location), round(mzz, 2)))
        # independent: spot coordinate from lat/lng via the geo module vs local
        if dem_water(x, y):
            water.append(s["site_id"])
    rec("C5.2 each spot empty is at its spots.csv x,y and on the terrain surface (z within 0.05 m of the mesh)", 5, not bad, f"mismatches: {bad}")
    info("C5.3 spots whose pin is over water-level DEM", 5, f"{water} (a pin over the canal is expected for 995, the bridge centre)")
    # lat/lng -> local cross-check with an independent implementation of Lambert-93 is geo.py; run its self-check here
    sys.path.insert(0, L)
    import geo
    worst = 0
    for s in spots:
        lx, ly = geo.wgs84_to_local(float(s["lat"]), float(s["lng"]))
        worst = max(worst, math.hypot(lx - float(s["local_x"]), ly - float(s["local_y"])))
    rec("C5.4 spots.csv local x,y agree with lat/lng through geo.py (<= 0.05 m)", 5, worst <= 0.05, f"worst {worst:.3f} m")
    # cameras
    sc.render.resolution_x, sc.render.resolution_y = 640, 360
    for c in cams["cameras"]:
        co = OBJ[f"cam_{c['site_id']}"]
        tgt = c["target_local_m"]
        tz = mesh_z(tgt["x"], tgt["y"])
        vec = world_to_camera_view(sc, co, Vector((tgt["x"], tgt["y"], tz + 1.5)))
        rec(f"C5.5 camera {c['site_id']} frames its own target point", 5, 0 <= vec.x <= 1 and 0 <= vec.y <= 1 and vec.z > 0,
            f"target ({tgt['x']},{tgt['y']}) projects to ({vec.x:.2f},{vec.y:.2f}) depth {vec.z:.0f} m; camera z {co.location.z:.2f}, ground {mesh_z(co.location.x, co.location.y):.2f}")
        std, px = png_stats(os.path.join(OUT, f"cam_{c['site_id']}.png"))
        rec(f"C5.6 render cam_{c['site_id']}.png is not blank", 5, std > 0.03, f"pixel std {std:.3f}")
    # which landmarks does each camera see? (info, for Stan's by-eye check)
    lm = {"bridge_span_moving": None, "cafe_gondree": None, "pillbox": None, "glider_marker_193": None, "glider_marker_194": None, "glider_marker_197": None,
          "church_benouville_tower": None}
    for c in cams["cameras"]:
        co = OBJ[f"cam_{c['site_id']}"]
        seen = []
        for nm in lm:
            o = OBJ[nm]; p = o.matrix_world.translation if nm == "bridge_span_moving" else Vector(((o.bound_box[0][0] + o.bound_box[6][0]) / 2, (o.bound_box[0][1] + o.bound_box[6][1]) / 2, o.bound_box[6][2] / 2 + o.matrix_world.translation.z))
            q = world_to_camera_view(sc, co, o.matrix_world @ Vector(((o.bound_box[0][0] + o.bound_box[6][0]) / 2, (o.bound_box[0][1] + o.bound_box[6][1]) / 2, (o.bound_box[0][2] + o.bound_box[6][2]) / 2)))
            if 0 <= q.x <= 1 and 0 <= q.y <= 1 and q.z > 0:
                seen.append(f"{nm}@{q.z:.0f}m")
        info(f"C5.7 landmarks in frame, cam_{c['site_id']} ('{c['view']}')", 5, ", ".join(seen) or "none")

    # ---------------------------------------------------------------- criterion 6: no ww2maps image/caption
    imgs = [i.name for i in bpy.data.images if i.name not in ("Render Result", "Viewer Node")]
    rec("C6.1 no image datablocks in the .blend", 6, not imgs, f"images: {imgs}")
    rec("C6.2 no text datablocks in the .blend", 6, not bpy.data.texts, f"texts: {[t.name for t in bpy.data.texts]}")
    # caption / photo-name strings from the geojson for level 01 sites
    gj = json.load(open(os.path.join("C:/Projects/Personal/AIassistant/TheMind/Projects/StansGameV2 - Level Maps/StansGameV2 level maps.geojson"), encoding="utf-8")) \
        if os.path.exists("C:/Projects/Personal/AIassistant/TheMind/Projects/StansGameV2 - Level Maps/StansGameV2 level maps.geojson") else None
    names = sorted({f["properties"]["name"] for f in gj["features"] if f["properties"].get("level") == "01" and f["properties"].get("name")
                    and re.fullmatch(r"[A-Za-z]{1,4}\d{1,3}[A-Za-z]?", f["properties"]["name"])}) if gj else []
    info("C6.0 photo-name tokens searched for (level 01 sites in the map-sheet geojson)", 6, str(names))
    # uncompressed copy of the blend, so its bytes are greppable
    raw_blend = os.path.join(OUT, "test_uncompressed.blend")
    bpy.ops.wm.save_as_mainfile(filepath=raw_blend, compress=False, copy=True)
    blobs = {"blend(uncompressed)": open(raw_blend, "rb").read(), "fbx": open(os.path.join(OUT, "level01_pegasus_blockout.fbx"), "rb").read()}
    os.remove(raw_blend)
    for p in os.listdir(L):
        fp = os.path.join(L, p)
        if os.path.isfile(fp):
            blobs[p] = open(fp, "rb").read()
    blobs["build_level01_pegasus.py"] = open(BUILD, "rb").read()
    found = []
    for k, b in blobs.items():
        low = b.lower()
        for m in re.finditer(rb"(https?://[^\s\"']*ww2maps[^\s\"']*|ww2maps\.net/[^\s\"']+|ww2maps[a-z0-9.\-_]*\.(png|jpe?g|gif|webp))", low):
            found.append((k, m.group(0).decode("latin1")))
        for n in names:
            if re.search(rb"(?<![a-z0-9])" + re.escape(n.lower().encode()) + rb"(?![a-z0-9])", low):
                found.append((k, f"photo name {n}"))
        if re.search(rb"\.(jpg|jpeg)\b", low) and k.endswith((".py", ".json", ".csv", ".md")):
            found.append((k, "jpg reference"))
    found = sorted(set(found))
    rec("C6.3 no ww2maps domain/url and no ww2maps photo name (pbNN) in .blend, FBX, data or script", 6, not found,
        "clean" if not found else f"{found}")
    info("C6.4 'ww2maps' as a plain word", 6, f"occurrences of 'ww2maps' (incl. tag 'ww2maps-ref') per file: " +
         ", ".join(f"{k}:{len(re.findall(b'ww2maps', b.lower()))}" for k, b in blobs.items() if b"ww2maps" in b.lower()))
    # strings from the cameras.json 'view' captions: are they paraphrased thumbnail captions?
    info("C6.5 camera 'view' labels stored in .blend/FBX", 6, "; ".join(f"{c['site_id']}: {c['view']}" for c in cams["cameras"]) +
         ". These read like short captions of the photos; cameras.json says no image was opened, so they come from the sheet text. Stan to confirm they are his own labels, not ww2maps caption text.")
    cred = open(os.path.join(L, "credits.md"), encoding="utf-8").read()
    rec("C6.6 credits file names IGN and Etalab 2.0", 6, "IGN" in cred and "Etalab" in cred and "2.0" in cred, "credits.md")
    # images in repo (outside _downloads)
    out = git("ls-files", "-o", "--exclude-standard") + git("ls-files")
    imgs_repo = [l for l in out.splitlines() if re.search(r"\.(png|jpe?g|webp|gif|tga|tif)$", l, re.I) and "level" in l.lower() and "3d-model/levels" in l]
    rec("C6.7 no image files tracked or untracked under 3d-model/levels (outside git-ignored _downloads)", 6, not imgs_repo, f"{imgs_repo}")


# ======================================================================================
# FBX re-import
# ======================================================================================
def tests_fbx():
    scene = bpy.context.scene
    ref = {o.name: (o.type, tuple(o.matrix_world.translation), tuple(o.dimensions)) for o in bpy.data.objects if o.name != "topdown_cam"}
    terr = bpy.data.objects["terrain"]
    dg = bpy.context.evaluated_depsgraph_get()
    bvh_ref = BVHTree.FromObject(terr, dg)
    fbx = os.path.join(OUT, "level01_pegasus_blockout.fbx")
    data = open(fbx, "rb").read()
    i = data.find(b"UnitScaleFactor"); j = i + len(b"UnitScaleFactor")
    for _ in range(3):
        n = struct.unpack("<I", data[j + 1:j + 5])[0]; j += 5 + n
    usf = struct.unpack("<d", data[j + 1:j + 9])[0]
    rec("F1 FBX UnitScaleFactor is 100 (1 file unit = 1 m)", 1, abs(usf - 100) < 1e-9, f"UnitScaleFactor {usf}")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx, axis_forward="-Z", axis_up="Y")
    imp = {o.name.split(".")[0] if o.name not in ref else o.name: o for o in bpy.context.scene.objects}
    names_ref = set(ref)
    names_imp = {o.name for o in bpy.context.scene.objects}
    rec("F2 FBX re-import brings back every object by name", 1, names_ref <= names_imp, f"missing {sorted(names_ref - names_imp)[:8]}, extra {sorted(names_imp - names_ref)[:8]}")
    t = bpy.data.objects.get("terrain")
    if t is None:
        rec("F3 terrain re-imported", 1, False, "no terrain object"); return
    dims_ref = ref["terrain"][2]
    rec("F3 terrain dimensions after re-import equal the .blend (scale and axes, <= 1 cm)", 1, all(abs(a - b) < 0.01 for a, b in zip(t.dimensions, dims_ref)),
        f"imported {tuple(round(v,2) for v in t.dimensions)} vs blend {tuple(round(v,2) for v in dims_ref)} (Z up, metres)")
    mv = np.array([(t.matrix_world @ v.co)[:] for v in t.data.vertices])
    rec("F4 terrain world z range equals blend (-5.5..25 m), x/y extent equals box-covering grid", 1,
        abs(mv[:, 2].max() - DEMZ.max()) < 0.2 and mv[:, 0].min() <= BOX["x_min"] and mv[:, 1].max() >= BOX["y_max"],
        f"x {mv[:,0].min():.0f}..{mv[:,0].max():.0f} y {mv[:,1].min():.0f}..{mv[:,1].max():.0f} z {mv[:,2].min():.2f}..{mv[:,2].max():.2f}")
    # random-point comparison, imported terrain vs original
    depsg = bpy.context.evaluated_depsgraph_get()
    bvh_i = BVHTree.FromObject(t, depsg)
    rng = random.Random(7); worst = 0
    for _ in range(300):
        x = rng.uniform(BOX["x_min"] + 1, BOX["x_max"] - 1); y = rng.uniform(BOX["y_min"] + 1, BOX["y_max"] - 1)
        a = bvh_i.ray_cast(Vector((x, y, 500)), Vector((0, 0, -1))); b = bvh_ref.ray_cast(Vector((x, y, 500)), Vector((0, 0, -1)))
        if a[0] is None or b[0] is None:
            worst = 99; break
        worst = max(worst, abs(a[0].z - b[0].z))
    rec("F5 imported terrain surface equals the .blend surface at 300 random points (<= 1 mm)", 1, worst <= 0.001, f"worst {worst*1000:.3f} mm")
    s995 = bpy.data.objects.get("spot_995"); sp = bpy.data.objects.get("bridge_span_moving")
    rec("F6 spot_995 and the bridge hinge land at the right local position after re-import", 3,
        s995 is not None and sp is not None and abs(s995.matrix_world.translation.x + 0.06) < 0.01 and abs(s995.matrix_world.translation.y - 0.37) < 0.01
        and abs(sp.matrix_world.translation.x - features['lift_bridge']['hinge_local_m'][0]) < 0.01 and abs(sp.matrix_world.translation.y - features['lift_bridge']['hinge_local_m'][1]) < 0.01,
        f"spot_995 {tuple(round(v,2) for v in s995.matrix_world.translation) if s995 else None}, span {tuple(round(v,2) for v in sp.matrix_world.translation) if sp else None}")
    rec("F7 custom props survive the FBX (source tag on terrain)", 1, t.get("source") == "lidar-today", f"terrain['source'] = {t.get('source')!r}", level="INFO")
    # raw axes: identity import should show Y up
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx, use_manual_orientation=True, axis_forward="Y", axis_up="Z")
    t2 = bpy.data.objects["terrain"]
    mv2 = np.array([(t2.matrix_world @ v.co)[:] for v in t2.data.vertices])
    ext = list(mv2.max(axis=0) - mv2.min(axis=0))
    rec("F8 raw FBX axes: Y is up (terrain's 27 m height range is on file Y; file Z = -north, 1060 m)", 1, ext[1] < 60 and ext[2] > 900,
        f"raw dims (x,y,z) {tuple(round(v,1) for v in ext)}")


# ======================================================================================
# Criterion 7: repo hygiene
# ======================================================================================
def tests_repo():
    rel = "3d-model/levels/level01_pegasus/_downloads/out_x/level01_pegasus_blockout"
    attr = git("check-attr", "filter", "--", rel + ".blend", rel + ".fbx", "3d-model/levels/level01_pegasus/_downloads/out_x/topdown.png")
    rec("C7.1 .blend, .fbx (and the PNG renders) are covered by LFS patterns", 7, attr.count("lfs") == 3, attr.strip().replace("\n", " | "))
    ig = git("check-ignore", "-v", "3d-model/levels/level01_pegasus/_downloads/rgealti_box_1m.bil", "3d-model/levels/level01_pegasus/_downloads/out_x/a.blend")
    rec("C7.2 raw DEM and build outputs under _downloads are git-ignored", 7, ig.count("_downloads") >= 2, ig.strip().replace("\n", " | "))
    st = git("status", "--porcelain", "--untracked-files=all")
    assets = [l for l in st.splitlines() if "Assets/" in l or "Assets\\" in l]
    rec("C7.3 nothing under Assets/ changed or added", 7, not assets, str(assets or "clean"))
    diff = git("diff", "--name-only", "origin/main...HEAD") + git("diff", "--name-only")
    rec("C7.4 no Assets/ file differs from the base branch", 7, "Assets/" not in diff, "clean" if "Assets/" not in diff else diff)
    stray = [l for l in st.splitlines() if "__pycache__" in l or l.strip().endswith(".pyc")]
    unignored = [l for l in git("ls-files", "-o", "--exclude-standard").splitlines() if "__pycache__" in l]
    rec("C7.5 no __pycache__ / .pyc files would be committed with the data folder", 7, not unignored, f"{len(unignored)} untracked, not ignored: {unignored}")
    bigs = [l for l in git("ls-files", "-o", "--exclude-standard").splitlines() if os.path.isfile(os.path.join(REPO, l)) and os.path.getsize(os.path.join(REPO, l)) > 1_000_000]
    info("C7.6 untracked files over 1 MB that would be committed as plain git (not LFS)", 7, str([(b, round(os.path.getsize(os.path.join(REPO, b)) / 1e6, 2)) for b in bigs]))
    rec("C7.7 heights_2m.csv (2.3 MB text) is it acceptable outside LFS?", 7, os.path.getsize(os.path.join(L, "heights_2m.csv")) < 5_000_000,
        f"{os.path.getsize(os.path.join(L, 'heights_2m.csv'))/1e6:.2f} MB, plain text (*.csv text rule)", level="INFO")


def main():
    c1_build()
    scene = open_blend()
    tests_model(scene)
    open_blend()
    tests_fbx()
    tests_repo()
    nf = sum(1 for r in RES if r[2] == "FAIL")
    lines = [f"{r[2]:5} {r[0]} (crit {r[1]}): {r[3]}" for r in RES]
    lines.append(f"\nTOTAL: {sum(1 for r in RES if r[2]=='PASS')} pass, {nf} fail, {sum(1 for r in RES if r[2]=='INFO')} info")
    open(os.path.join(OUT, "test_report.txt"), "w", encoding="utf-8").write("\n".join(lines))
    json.dump(RES, open(os.path.join(OUT, "test_results.json"), "w", encoding="utf-8"), indent=1)
    print("\n".join(lines[-1:]))
    sys.stdout.flush()
    sys.exit(1 if nf else 0)


main()
