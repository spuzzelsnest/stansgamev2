"""S3: read the RGE ALTI 1 m box (.bil) and write a 2 m grid in local metres.

Input : _downloads/rgealti_box_1m.bil  (little-endian float32, 1370 x 1070, rows from the north,
        cell (r,c) covers X 460940+c..+1, Y 6910350-r..-1; EPSG:2154, NGF-IGN69 heights)
Output: heights_2m.csv, checkpoints.csv; sets height_base in origin.json.
Pure Python (no numpy). Run: python -I terrain.py
"""
import array
import csv
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import geo  # noqa: E402

BIL = os.path.join(HERE, "_downloads", "rgealti_box_1m.bil")
W, H = 1370, 1070
BX0, BY1 = 460940, 6910350          # bbox west edge X and north edge Y
CELL = 2                             # output cell size, metres

CHECKS = [  # name, site, local x, local y (taken from spots.csv positions)
    ("bridge_approach", 992, None, None),
    ("glider_field", 193, None, None),
    ("benouville_church", 1038, None, None),
]


def load():
    a = array.array("f")
    with open(BIL, "rb") as fh:
        a.fromfile(fh, W * H)
    if sys.byteorder != "little":
        a.byteswap()
    return a


def sample(a, X, Y):
    """Bilinear between 1 m cell centres at L93 (X, Y)."""
    c = X - BX0 - 0.5
    r = BY1 - Y - 0.5
    c0, r0 = int(math.floor(c)), int(math.floor(r))
    if c0 < 0 or r0 < 0 or c0 + 1 >= W or r0 + 1 >= H:
        raise ValueError(f"outside the grid: {X},{Y}")
    fc, fr = c - c0, r - r0
    v = lambda rr, cc: a[rr * W + cc]
    top = v(r0, c0) * (1 - fc) + v(r0, c0 + 1) * fc
    bot = v(r0 + 1, c0) * (1 - fc) + v(r0 + 1, c0 + 1) * fc
    return top * (1 - fr) + bot * fr


def main():
    a = load()
    o = geo.load_origin()
    ox, oy = o["l93_x"], o["l93_y"]
    mn, mx, nonfinite = min(a), max(a), sum(1 for v in a if v != v or v in (float("inf"), float("-inf")))
    nodata = sum(1 for v in a if v < -1000)
    print(f"source: {W}x{H} cells, min {mn:.2f} max {mx:.2f}, non-finite {nonfinite}, nodata(<-1000) {nodata}")

    base = round(sample(a, ox, oy), 2)
    o["height_base"]["value_m"] = base
    o["height_base"]["note"] = ("z_local = NGF-IGN69 height - base. Base = RGE ALTI 1 m height at the origin (bridge point), "
                                "bilinear, rounded to 0.01 m. Set by terrain.py in S3.")
    with open(os.path.join(HERE, "origin.json"), "w", encoding="utf-8") as fh:
        json.dump(o, fh, indent=2, ensure_ascii=False)
        fh.write("\n")

    box = o["playable_box_local"]
    x0 = int(math.floor(box["x_min"] / CELL)) * CELL
    x1 = int(math.ceil(box["x_max"] / CELL)) * CELL
    y0 = int(math.floor(box["y_min"] / CELL)) * CELL
    y1 = int(math.ceil(box["y_max"] / CELL)) * CELL
    cols = (x1 - x0) // CELL + 1
    rows = (y1 - y0) // CELL + 1
    holes = 0
    path = os.path.join(HERE, "heights_2m.csv")
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh, lineterminator="\n")
        w.writerow(["x0_local_m", "y_top_local_m", "cell_m", "rows", "cols", "height_base_m", "layout"])
        w.writerow([x0, y1, CELL, rows, cols, base,
                    "values are z_local_m = NGF-IGN69 - base; row 0 is the north row (y = y_top), rows step south, "
                    "columns step east from x0; point (r,c) is at x = x0 + c*cell, y = y_top - r*cell"])
        for r in range(rows):
            y = y1 - r * CELL
            line = []
            for c in range(cols):
                x = x0 + c * CELL
                v = sample(a, ox + x, oy + y)
                if v != v or v < -1000:
                    holes += 1
                line.append(f"{v - base:.3f}")
            w.writerow(line)
    print(f"grid: {rows} rows x {cols} cols = {rows * cols} points, cell {CELL} m, "
          f"local x {x0}..{x1}, y {y0}..{y1}, holes {holes}")
    print(f"height base {base} m (NGF-IGN69 at origin)")

    spots = {int(r["site_id"]): r for r in csv.DictReader(open(os.path.join(HERE, "spots.csv"), encoding="utf-8"))}
    with open(os.path.join(HERE, "checkpoints.csv"), "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh, lineterminator="\n")
        w.writerow(["name", "site_id", "local_x", "local_y", "l93_x", "l93_y",
                    "ngf_height_m_bilinear", "ngf_height_m_nearest_cell", "z_local_m", "source"])
        for name, site, _, _ in CHECKS:
            s = spots[site]
            lx, ly = float(s["local_x"]), float(s["local_y"])
            X, Y = ox + lx, oy + ly
            bil = sample(a, X, Y)
            near = a[int(BY1 - Y) * W + int(X - BX0)]
            w.writerow([name, site, f"{lx:.2f}", f"{ly:.2f}", f"{X:.2f}", f"{Y:.2f}",
                        f"{bil:.2f}", f"{near:.2f}", f"{bil - base:.2f}",
                        "RGE ALTI 1 m (WMS-R box 2026-11-13), position from spots.csv"])
            print(f"check {name} (site {site}) local ({lx:.2f}, {ly:.2f}): "
                  f"NGF {bil:.2f} m bilinear, {near:.2f} m nearest cell, z_local {bil - base:.2f}")
    assert holes == 0 and nodata == 0 and nonfinite == 0


if __name__ == "__main__":
    main()
