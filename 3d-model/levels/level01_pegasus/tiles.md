# Level 01 Pegasus Bridge - terrain source (what was actually used)

**No tiles were used.** The heights come from one WMS-Raster GetMap box, fetched on 2026-11-13 with Stan's yes. The S1 plan of 6 RGE ALTI ASC tiles was dropped (see "Why not tiles").

## The one source file

| Item | Value |
|---|---|
| File | `_downloads/rgealti_box_1m.bil` (git-ignored, not committed) |
| Size | 5,863,600 bytes = 1370 x 1070 cells x 4 bytes |
| Service | IGN WMS-Raster on data.geopf.fr, `https://data.geopf.fr/wms-r/wms` |
| Layer | `ELEVATION.ELEVATIONGRIDCOVERAGE.HIGHRES` (used as RGE ALTI, 1 m) |
| CRS | EPSG:2154 (Lambert-93) |
| BBOX | `460940,6909280,462310,6910350` (X 460940-462310, Y 6909280-6910350) |
| WIDTH x HEIGHT | 1370 x 1070 (1 m cells) |
| Format | `image/x-bil;bits=32`: little-endian float32, rows from the north; cell (r, c) covers X 460940+c..+1 and Y 6910350-r..-1 |
| Heights | metres, NGF-IGN69 |
| Check | min -2.09 m, max 24.95 m, mean 7.27 m, no nodata; bridge point (461580, 6909698) reads 3.40 m (= `height_base`) |
| Coverage | the whole playable box (X 460948.78-462304.68, Y 6909283.49-6910340.56) plus a margin of at least 3.4 m on every side |
| Licence | Etalab Open Licence 2.0, attribution "Source: IGN" (see `credits.md`) |

Derived files: `heights_2m.csv`, `checkpoints.csv` (both by `terrain.py`).

## Whether the layer is lidar-derived

Not confirmed. The layer name says HIGHRES and the IGN notice read for RGE ALTI says lidar is used for flood-prone, coastal and large forest areas and image correlation elsewhere. Which applies to this box is not known; do not claim LiDAR HD equivalence with Joan's terrain.

## Why not tiles

IGN publishes RGE ALTI 1 m ASC here only as a whole departement archive. For Calvados (D014) the data.geopf.fr feed entry `RGEALTI_2-0_1M_ASC_LAMB93-IGN69_D014_2023-01-03` is one `.7z` of 3,035,545,330 bytes (about 3.04 GB), with no per-tile download. The box request above is 0.19% of that size. The six 1 km tiles that would have been needed (NW corners X 460-462 km, Y 6910-6911 km) were never fetched and are not in the repo; their expected file names were never confirmed.

## Open points

- The WMS-R grid is a service rendering of the layer, not the original ASC tiles; edge cells may be interpolated by the service (not checked).
- A LiDAR HD MNT 0.5 m download would be a separate ask; its catalogue path was not found.

Owner: [[ROISIN|RÓISÍN]] - ask RÓISÍN first about anything in this note.
