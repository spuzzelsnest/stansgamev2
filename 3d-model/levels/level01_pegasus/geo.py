"""Lambert-93 (EPSG:2154) <-> WGS84 (EPSG:4326), plain Python, no packages.

Constants (IGN / decree 2000-1276, RGF93):
  ellipsoid GRS80: a = 6378137, 1/f = 298.257222101
  Lambert conformal conic, 2 standard parallels 44 and 49 N,
  origin latitude 46.5 N, central meridian 3 E, false easting 700000, false northing 6600000.
RGF93 and WGS84 differ by well under 1 m here, so no datum shift is applied.

Local frame (see origin.json): local = L93 - origin, grid north (not true north).
Run `python geo.py` for the self-check.
"""
import json
import math
import os

A = 6378137.0
INV_F = 298.257222101
F = 1.0 / INV_F
E2 = F * (2 - F)
E = math.sqrt(E2)
LAT0 = math.radians(46.5)
LON0 = math.radians(3.0)
LAT1 = math.radians(44.0)
LAT2 = math.radians(49.0)
X0 = 700000.0
Y0 = 6600000.0


def _m(phi):
    s = math.sin(phi)
    return math.cos(phi) / math.sqrt(1 - E2 * s * s)


def _t(phi):
    s = math.sin(phi)
    return math.tan(math.pi / 4 - phi / 2) / ((1 - E * s) / (1 + E * s)) ** (E / 2)


_N = (math.log(_m(LAT1)) - math.log(_m(LAT2))) / (math.log(_t(LAT1)) - math.log(_t(LAT2)))
_G = _m(LAT1) / (_N * _t(LAT1) ** _N)
_R0 = A * _G * _t(LAT0) ** _N


def wgs84_to_l93(lat_deg, lng_deg):
    phi = math.radians(lat_deg)
    lam = math.radians(lng_deg)
    r = A * _G * _t(phi) ** _N
    gamma = _N * (lam - LON0)
    return X0 + r * math.sin(gamma), Y0 + _R0 - r * math.cos(gamma)


def l93_to_wgs84(x, y):
    dx = x - X0
    dy = _R0 - (y - Y0)
    r = math.copysign(math.hypot(dx, dy), _N)
    gamma = math.atan2(dx / r, dy / r)
    t = (r / (A * _G)) ** (1 / _N)
    phi = math.pi / 2 - 2 * math.atan(t)
    for _ in range(20):
        s = math.sin(phi)
        phi_new = math.pi / 2 - 2 * math.atan(t * ((1 - E * s) / (1 + E * s)) ** (E / 2))
        if abs(phi_new - phi) < 1e-14:
            phi = phi_new
            break
        phi = phi_new
    return math.degrees(phi), math.degrees(LON0 + gamma / _N)


def meridian_convergence_deg(lng_deg):
    """Angle grid north is rotated from true north (positive = grid north is east of true)."""
    return math.degrees(_N * (math.radians(lng_deg) - LON0))


_HERE = os.path.dirname(os.path.abspath(__file__))


def load_origin(path=None):
    with open(path or os.path.join(_HERE, "origin.json"), encoding="utf-8") as fh:
        return json.load(fh)


def l93_to_local(x, y, origin=None):
    o = origin or load_origin()
    return x - o["l93_x"], y - o["l93_y"]


def local_to_l93(lx, ly, origin=None):
    o = origin or load_origin()
    return lx + o["l93_x"], ly + o["l93_y"]


def wgs84_to_local(lat, lng, origin=None):
    x, y = wgs84_to_l93(lat, lng)
    return l93_to_local(x, y, origin)


def _selfcheck():
    # Official IGN example (NTG_71): Paris-ish check point, lat 49 N lon 3 E -> X 700000 Y 6619...
    x, y = wgs84_to_l93(46.5, 3.0)
    assert abs(x - 700000) < 1e-6 and abs(y - 6600000) < 1e-6, (x, y)
    # round trip on every spot + box corners
    pts = [(49.242462, -0.274578), (49.241669, -0.273638), (49.23850, -0.28300), (49.24850, -0.26500)]
    worst = 0.0
    for lat, lng in pts:
        x, y = wgs84_to_l93(lat, lng)
        lat2, lng2 = l93_to_wgs84(x, y)
        x2, y2 = wgs84_to_l93(lat2, lng2)
        worst = max(worst, math.hypot(x - x2, y - y2))
        print(f"  ({lat:.6f}, {lng:.6f}) -> L93 X {x:,.2f} Y {y:,.2f} -> back ({lat2:.6f}, {lng2:.6f})")
    print(f"round-trip worst error: {worst:.2e} m")
    assert worst < 1e-3
    # CATHAL figures
    bx, by = wgs84_to_l93(49.242462, -0.274578)   # site 995, bridge
    gx, gy = wgs84_to_l93(49.241669, -0.273638)   # site 193, glider 1
    for name, got, want in (("bridge X", bx, 461580), ("bridge Y", by, 6909698),
                            ("glider1 X", gx, 461645), ("glider1 Y", gy, 6909608)):
        d = got - want
        print(f"{name}: got {got:,.2f} want {want:,} diff {d:+.2f} m")
        assert abs(d) <= 1.0, name
    print(f"meridian convergence at -0.2746: {meridian_convergence_deg(-0.2746):+.3f} deg")
    print("geo.py self-check OK")


if __name__ == "__main__":
    _selfcheck()
