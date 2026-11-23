# Joan's Lee-Enfield SMLE Mk III (lee-enfield-smle-mkiii, 43 parts, one texture set) made a drop-in for the game's
# LeeEnfield.fbx: joined into one mesh with one material ("SMLE"), turned, centred and scaled to lie in exactly the same
# frame as the old model (length along Y, height along Z, muzzle and magazine the same way round - found by matching
# the two rifles' side profiles), so the weapon prefab, its grip and its hold carry on unchanged. 1132 mm long.
# usage: python3 -I lee_smle.py -- <new obj> <old LeeEnfield.fbx> <out fbx>
import bpy, sys, numpy as np
from mathutils import Matrix, Vector
NEW, OLD, OUT = sys.argv[-3:]

def profile(V, ax_len, ax_up, n=40):
    L = V[:, ax_len]; U = V[:, ax_up]
    lo, hi = L.min(), L.max(); prof = []
    for i in range(n):
        m = (L >= lo + (hi - lo) * i / n) & (L <= lo + (hi - lo) * (i + 1) / n)
        prof.append((U[m].min(), U[m].max()) if m.any() else (np.nan, np.nan))
    p = np.array(prof); p = (p - np.nanmean(p)) / (np.nanstd(p) + 1e-9)
    return np.nan_to_num(p)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=OLD); bpy.context.view_layer.update()
Vo = np.array([(o.matrix_world @ v.co)[:] for o in bpy.data.objects if o.type == 'MESH' for v in o.data.vertices])
po = profile(Vo, 1, 2)
lo_old, hi_old = Vo.min(0), Vo.max(0)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=NEW); bpy.context.view_layer.update()
ms = [o for o in bpy.data.objects if o.type == 'MESH']
for o in ms: o.data.transform(o.matrix_world); o.matrix_world = Matrix()
bpy.ops.object.select_all(action='DESELECT')
for o in ms: o.select_set(True)
bpy.context.view_layer.objects.active = ms[0]; bpy.ops.object.join()
o = bpy.context.view_layer.objects.active; o.name = 'LeeEnfield'; o.data.name = 'LeeEnfield'
# length X -> Y
o.data.transform(Matrix.Rotation(np.pi / 2, 4, 'Z'))
V = np.array([v.co[:] for v in o.data.vertices])
best = None
for fl in (1, -1):          # muzzle end
    for fu in (1, -1):      # which way is up
        W = V * np.array([fl * fu, fl, fu])   # flipping length (and keeping it right-handed)
        d = np.abs(profile(W, 1, 2) - po).mean()
        print("TRY", fl, fu, round(d, 3))
        if best is None or d < best[0]: best = (d, fl, fu)
_, fl, fu = best
o.data.transform(Matrix.Diagonal((fl * fu, fl, fu, 1)))
V = np.array([v.co[:] for v in o.data.vertices]); lo, hi = V.min(0), V.max(0)
s = (hi_old[1] - lo_old[1]) / (hi[1] - lo[1])
o.data.transform(Matrix.Translation(Vector(((lo_old + hi_old) / 2).tolist())) @ Matrix.Scale(s, 4) @ Matrix.Translation(Vector((-(lo + hi) / 2).tolist())))
V = np.array([v.co[:] for v in o.data.vertices])
print('MATCH', 'flip length', fl, 'flip up', fu, 'profile error', round(best[0], 3), 'size', (V.max(0) - V.min(0)).round(3), 'old', (hi_old - lo_old).round(3))
o.data.materials.clear(); o.data.materials.append(bpy.data.materials.new('SMLE'))
for p in o.data.polygons: p.use_smooth = True
print('tris', sum(len(p.vertices) - 2 for p in o.data.polygons), 'uv', len(o.data.uv_layers))
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True, mesh_smooth_type='FACE')
