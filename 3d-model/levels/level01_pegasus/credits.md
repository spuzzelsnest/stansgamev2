# Level 01 Pegasus Bridge - credits and data sources

## Terrain heights

- **Data:** RGE ALTI 1 m, via the layer `ELEVATION.ELEVATIONGRIDCOVERAGE.HIGHRES`, IGN (Institut national de l'information geographique et forestiere).
- **Service:** IGN WMS-Raster on data.geopf.fr (`https://data.geopf.fr/wms-r/wms`).
- **Licence:** Etalab Open Licence 2.0. Attribution: "Source: IGN, RGE ALTI".
- **Fetched:** 2026-11-13 (by JACK, with Stan's yes).
- **Request (parameters as relayed to ROISIN; the literal URL string was not passed on):**
  - `SERVICE=WMS`, `REQUEST=GetMap`, version 1.3.0 assumed (the capabilities document at that endpoint is 1.3.0)
  - `LAYERS=ELEVATION.ELEVATIONGRIDCOVERAGE.HIGHRES`
  - `CRS=EPSG:2154`
  - `BBOX=460940,6909280,462310,6910350`
  - `WIDTH=1370`, `HEIGHT=1070`
  - `FORMAT=image/x-bil;bits=32`
- **Local copy:** `_downloads/rgealti_box_1m.bil` (5,863,600 bytes, git-ignored, not committed). Little-endian float32, 1370 x 1070, rows from the north, heights in metres NGF-IGN69.
- **Derived here:** `heights_2m.csv` (bilinear resample to 2 m, local metres, z relative to `height_base`), `checkpoints.csv`, by `terrain.py`.

## Historic reference

- Positions of the photo spots come from the `StansGameV2 level maps.geojson` in the project vault (coordinates only). No ww2maps.net image, caption or photo name is stored in this folder.
- Everything in `features.json` tagged `estimate` is a placeholder, not a measurement.

## Canal and bridge dimensions (round 2)

- **Today's canal water width, 46 m (estimate):** inferred from the 1994 bridge total length of 45.70 m (main span 43.2 m, completed 1994, replacing an identical 1935 bridge), `https://structurae.net/en/structures/pegasus-bridge`, page read 2026-11-13. That page does not state a canal width. A further source for about 50 m, `https://www.donnees.normandie.developpement-durable.gouv.fr/pdf/SITES/14130f.pdf`, was passed on by EOGHAN; its text could not be read here, so it is not verified.
- **1944 moving span, 28.4 m:** `https://www.lieux-insolites.fr/cicatrice/39-45/pegasusbridge/pegasusbridge.htm`, read 2026-11-13: the 1935 Scherzer bascule bridge, 28.40 m span. Fixed approach spans to the water edges and the deck ends are estimates.
- **The DEM cannot give the water edges:** every cell of the flat band reads 3.40 m to within 0.02 m, so water and flat bank ground are not separable. The band (about 99 m across) is kept as bank ground around the 46 m water.

Owner: [[ROISIN|RÓISÍN]] - ask RÓISÍN first about anything in this note.
