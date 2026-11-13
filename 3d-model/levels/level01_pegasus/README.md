# Level 01 Pegasus Bridge - Blender blockout

Reference blockout of the Pegasus Bridge area, built by script from the data in this folder. Not a copy of
Joan's Unity scene; nothing in `Assets/` is touched.

## Run

```
"C:\Program Files\Blender Foundation\Blender 5.3\blender.exe" -b -P 3d-model/levels/build_level01_pegasus.py -- OUTDIR
```

Run from the repo root. Blender 5.3.0, no add-ons, no downloads. About 25 s. Use an `OUTDIR` under
`3d-model/levels/level01_pegasus/_downloads/out/` (git-ignored) while developing. The script stops with an
assertion if a check fails (smoke test, check-point heights, named objects, source tags, scene properties,
no ww2maps domain in any custom property or the FBX).

## Outputs (in OUTDIR)

| File | What |
|---|---|
| `level01_pegasus_blockout.blend` | full scene, custom properties on scene and objects (about 9.7 MB) |
| `level01_pegasus_blockout.fbx` | same scene for Unity (about 8.9 MB, binary FBX 7400) |
| `topdown.png` | orthographic top-down render, grid north up, 1400 x 1100 |
| `cam_193.png`, `cam_992.png`, `cam_989.png`, `cam_995.png`, `cam_1038.png` | one 640 x 360 Workbench render per key-view camera |
| `smoke_test.json` | result of the S2 axis and unit test |

## Frame, units and axes

- 1 Blender unit = 1 m, Z up, X east, Y Lambert-93 **grid** north (about 2.4 degrees off true north here).
- `local = Lambert-93 - origin`, origin X 461580, Y 6909698 (site 995, "bridge centre"). `z = NGF-IGN69 height - 3.4 m`.
- Origin, height base, grid-north note and sources are scene custom properties in the `.blend` (`l93_origin_x`, `l93_origin_y`,
  `height_base_m`, `grid_north_note`, `sources`, `tiles`). Every object has a `source` property (`lidar-today`, `ww2maps-ref`,
  `aerial-1944`, `estimate`); almost everything except the terrain heights is `estimate` (see `features.json`).
- **Source box:** one IGN WMS-Raster GetMap, layer `ELEVATION.ELEVATIONGRIDCOVERAGE.HIGHRES` (RGE ALTI, 1 m), CRS EPSG:2154,
  `BBOX=460940,6909280,462310,6910350`, 1370 x 1070 cells, fetched 2026-11-13. No tiles were used (the six-tile plan in `tiles.md`
  is superseded; `tiles.md` and `credits.md` say what was actually fetched).
- Terrain: 2 m grid, 680 x 531 vertices, x -632..726, y -416..644 (covers the playable box). Heights are RGE ALTI 1 m resampled
  to 2 m, not LiDAR HD, so they can differ a little from Joan's terrain.

## S2 smoke test: FBX units and axes (result: PASS)

A 2 m cube at Blender (3, 5, 1), exported with `axis_forward='-Z'`, `axis_up='Y'`, `global_scale=1`,
`apply_scale_options='FBX_SCALE_ALL'`, then imported again:

| Check | Result |
|---|---|
| Size after round trip | 2 x 2 x 2 m |
| Location after round trip (Blender import, defaults) | (3, 5, 1), Z up again |
| Raw file axes (import with identity axis mapping) | (3, 1, -5): file Y = Blender Z (up), file Z = -Blender Y |
| FBX `UnitScaleFactor` | 100 (1 file unit = 100 cm = 1 m, so 1 unit = 1 m in Unity) |

So the file is Y up, 1 unit = 1 m. Blender (X east, Y north, Z up) becomes file (X, Z, -Y). Unity's importer converts
this right-handed file to its left-handed system by mirroring one axis, so check the orientation once in Unity against a known
point (the bridge deck is at local (0, 0), the church at (-115, 484)) before trusting it; that step was not run here. The cube is not kept
in the scene; the script builds it in a throw-away scene, checks it, deletes the temp FBX and starts a fresh empty scene.

## What is in the scene

Collections `Terrain`, `Water`, `Bridge`, `Roads`, `Buildings`, `Vegetation`, `Markers`, `Spots`, `Cameras`, `Render`.

- `terrain`; `canal_water` (46 m wide strip at water level 3.4 m NGF, centred on the traced line, 28.3 degrees east of grid north, width an estimate)
  with `canal_bank_west` and `canal_bank_east` outlines at the water edges. `canal_bank_ground_west` and `canal_bank_ground_east` are the rest of the DEM's
  flat band (towpath or bank, about 26 m each side): dry, walkable ground (`walkable=True`, `is_water=False`), not water. The terrain is not carved, so it
  stays the source DEM; the water plane sits 0.06 m above water level to avoid z-fighting. `orne_water` and `orne_east_cut_marker` (Orne cut explicitly at x = 726).
- Lift bridge, from the `lift_bridge` entry: axis 118.3 degrees grid (at right angles to the canal) through the origin, 102 m deck in total:
  `bridge_deck_west` and `bridge_deck_east` (fixed spans, 36.8 m each) and `bridge_span_moving` (28.4 m, the 1944 Scherzer span, estimate; deck plus two girders).
  The span's **object origin is the hinge** at (10.8, -5.8); the object is rotated so local X runs along the deck and local Y along the canal, so rotating it about
  local Y by a negative angle lifts the east end. Which end really lifts, and Joan's `BridgeLift` hinge, are not known (estimate).
- `road_bridge_west`, `road_bridge_east`, `road_west_bank`, `road_armour_approach`; `cafe_gondree`, `west_house_1..3`, `pillbox`, `trench_line`,
  `church_benouville_nave`, `church_benouville_tower`, `chateau_grounds`, `hedge_east_1`, `hedge_east_2`; `glider_marker_193`, `glider_marker_194`, `glider_marker_197`.
- 16 empties `spot_<site id>` (coordinates only; no image or caption from any website) and 5 cameras `cam_<site id>` from `cameras.json`
  (eye height 1.6 m, heading clockwise from grid north, all low-confidence first estimates to be aligned in S6).
- No trees yet: `features.json` has none.

## Known limits

- **Canal:** the band is the DEM strip that is flat at water level, so it may be wider than today's water and than the 1944 canal (about 35 m, estimate).
- **Snapping:** buildings and glider markers have their base at the lowest ground under the footprint (1 m samples) and the top above the highest ground;
  spot empties sit on the mesh surface (ray cast, not the grid).
- **Check points:** at 2 m resolution two of the three check points (sites 992, 193) missed the 1 m source height by 0.109 and 0.094 m, so the
  script adds one vertex at the exact source height in those cells (logged as `PIN`, listed in `terrain["pinned_check_points"]`). All three then
  match the source within 0.1 m (asserted against the real mesh with a ray cast).
- Only the whole box is one terrain mesh; FBX is about 8.9 MB. Use Git LFS for `.blend` and `.fbx`.

## FBX import note for Joan's Unity project

1. Import the `.fbx` into a folder **outside** the live scene's assets (or a scratch scene) first; nothing here replaces his terrain.
2. Model import settings: Scale Factor 1, Convert Units on (the file says 1 unit = 1 m), keep Y up as exported, no extra rotation.
3. Place the root at the shared origin (Lambert-93 X 461580, Y 6909698 = local (0, 0)); heights are `NGF - 3.4 m`. Joan's own origin and heading
   are not known; local +Y is grid north, not true north.
4. Custom properties (`source`, `feature_id`, ...) come through as FBX user properties if "Import Custom Props" style tooling is used; they are not needed for placement.

## Files

`origin.json`, `spots.csv`, `cameras.json`, `features.json`, `heights_2m.csv`, `checkpoints.csv`, `geo.py`, `terrain.py`, `tiles.md`, `credits.md` (data, by RÓISÍN).
`../build_level01_pegasus.py` (this build). Credits: IGN RGE ALTI, Etalab Open Licence 2.0, see `credits.md`.
