# Joan's downloads (22 Nov, batch 2) to real-size FBX for Unity: up is Z, the front (the gun) towards +Y, on the
# ground at z = 0, centred; heavy meshes decimated; moving parts kept apart with their pivots where they turn
# (<Name>_Hull, <Name>_Turret on its ring, <Name>_Gun on its trunnions, <Name>_Muzzle at the end of the barrel);
# every material renamed to a plain name whose texture is written beside it as <name>.jpg (1024 px).
# usage: python3 -I convert.py -- <key>
import bpy, sys, math, re, os
from mathutils import Vector, Matrix
from PIL import Image
D = '/mnt/user-data/uploads/stansGameV2/_downloads/'
OUT = '/home/claude/assets/out3/'

def dl(p):
    """A download wherever it is sorted: straight in _downloads, or in _Weapons, _Vehicles, _Characters, _Objects..."""
    if os.path.exists(p) or not p.startswith(D): return p
    rel = p[len(D):]
    for sub in sorted(os.listdir(D)):
        if sub.startswith('_') and os.path.exists(D + sub + '/' + rel): return D + sub + '/' + rel
    return p
W = D + 'flakpanzer-iv-wirbelwind/'
M16T = '/mnt/user-data/uploads/stansGameV2/_downloads/m16-mgmc-halftrack/textures/'
M16NUM = {'0': '1', '2': '5', '3': '9'}
SPEC = {
  # Flakpanzer IV Wirbelwind: Pz IV hull 5.92 m, open turret with the 2 cm Flakvierling 38 (four barrels)
  'Wirbelwind': dict(src='flakpanzer-iv-wirbelwind/source/Welberwind tank/Welberwind tank.obj', length=5.92, front=1, budget=30000,
                   turret_names=('turret_turret', 'turret_base'), gun_names=('gun_rot', 'gun1_', 'gun2_'), pivot_obj='gun_rot',
                   tex=lambda m: W + 'textures/' + {'wirbelwind': 'wirbelwind#2', 'wirbelwind_turret': 'wirbelwind_turret#2'}.get(m, m) + '.png'),
  # M16 MGMC: M3 half-track 6.49 m with the M45 Quadmount (four .50 Brownings); the mount rides facing aft, turned to face forward
  'M16Halftrack': dict(src='/tmp/m16/M16 halftrack.obj', length=6.49, front=1, budget=30000, turret_spin=True, drop_prefix=('at1_', 'at2_', 'at3_', 'at4_'),
                   turret_names=('turret_turret',), gun_names=('gun_rot', 'gun1_', 'gun2_', 'gun3_', 'gun4_'), pivot_obj='gun_rot',
                   tex=lambda m: M16T + M16NUM.get(m, m) + '.png'),
  # length: the real overall length (m) along the front axis; up: 'y' = the file is Y-up (stand it up)
  'Cromwell': dict(src='cromwell-iv/source/cromwell-iv/cromwell-iv.fbx', length=6.35, turret='turret.001', gun='mount.001', muzzle='weapon muzzle', budget=24000,
                   tex=lambda m: D + 'cromwell-iv/textures/' + m + '.png'),
  'MarderIIIM': dict(src='marder-iiim/source/marder-iii-m/marder-iii-m.fbx', length=4.95, turret='mount.001', gun='mount2', muzzle='weapon muzzle', budget=26000,
                   tex=lambda m: D + 'marder-iiim/textures/' + (m or 'marder-iiim-hull') + '.png'),
  'StuartM5A1': dict(src='m5a1-stuart/source/M5A1/M5A1.obj', length=4.84, turret_geo='bone_turret', gun_names=('bone_gun', 'gun_barrel'), budget=26000, flip=True,
                   tex=lambda m: D + 'm5a1-stuart/source/M5A1/textures/' + m + '.jpg'),
  'Hetzer': dict(src='jagdpanzer-38t-hetzer/source/hetzer/hetzer_rework.obj', length=6.38, budget=30000, muzzle_tip=True,
                   tex=lambda m: D + 'jagdpanzer-38t-hetzer/textures/hetzer_' + m + '_BaseColor.png'),
  'Flak88': dict(src='flak-88/source/itog_model/itog_model.obj', length=7.62, budget=24000, muzzle_tip=True,
                   tex=lambda m: D + 'flak-88/textures/DefaultMaterial_Base_Color.png'),
  'Pak40': dict(src='pak40-75mm-canon-ww2/source/Pak40.fbx', length=6.2, budget=16000, muzzle_tip=True,
                   tex=lambda m: D + 'pak40-75mm-canon-ww2/textures/' + m + '_BaseColor.jpg'),
  'Pak40Ammo': dict(src='75-cm-pak-40-munition/source/pak 40.fbx', scale=1.0, drop=('floor_grid',), keep_axes=True,
                   tex=lambda m: D + '75-cm-pak-40-munition/textures/Crate_color.jpg', matname='Pak40Ammo'),
  'SandbagWall': dict(src='sandbag-barrier/source/SM_SBFlat_UE/SM_SBFlat.fbx', scale=1.0, budget=5000, keep_axes=True, matname='SandbagWall',
                   tex=lambda m: D + 'sandbag-barrier/textures/T_SBFlat_new_D.jpeg'),
  'SandbagCorner': dict(src='sandbags-corner/source/SM_BarrierCorner_UE/T_SBCorner.fbx', scale=1.0, budget=6000, keep_axes=True, matname='SandbagCorner',
                   tex=lambda m: D + 'sandbags-corner/textures/T_SBCornerNew_D.jpeg'),
  'SandbagPit': dict(src='sand-bags/source/Sand Bags.fbx', scale=1.0, budget=8000, keep_axes=True, matname='SandbagPit',
                   tex=lambda m: D + 'sand-bags/textures/DefaultMaterial_Base_color.png'),
}
key = sys.argv[-1]; S = SPEC[key]
src = dl(S['src'] if S['src'].startswith('/') else D + S['src'])
bpy.ops.wm.read_factory_settings(use_empty=True)
if src.endswith('.obj'): bpy.ops.wm.obj_import(filepath=src)
else: bpy.ops.import_scene.fbx(filepath=src)
bpy.context.view_layer.update()
# obj: material names from the .mtl texture files
if src.endswith('.obj'):
    mtl = src[:-4] + '.mtl'; cur = None; mp = {}
    if os.path.exists(mtl):
        for line in open(mtl, errors='ignore'):
            p = line.split()
            if not p: continue
            if p[0] == 'newmtl': cur = ' '.join(p[1:])
            elif p[0] == 'map_Kd' and cur: mp[cur] = os.path.splitext(os.path.basename(' '.join(p[1:]).replace(chr(92), '/')))[0]
    for m in bpy.data.materials:
        if m.name in mp: m.name = mp[m.name]
# parents before flattening
parent = {o.name: (o.parent.name if o.parent else None) for o in bpy.data.objects}
def under(n, root):
    while n:
        if n == root: return True
        n = parent.get(n)
    return False
muzzle_pt = None
if S.get('muzzle') and S['muzzle'] in bpy.data.objects: muzzle_pt = bpy.data.objects[S['muzzle']].matrix_world.translation.copy()
for o in list(bpy.data.objects):
    if o.name.startswith(tuple(S.get('drop_prefix', ('\0',)))): bpy.data.objects.remove(o)
ms = [o for o in bpy.data.objects if o.type == 'MESH' and o.name not in S.get('drop', ())]
for o in list(bpy.data.objects):
    if o.type != 'MESH' or o.name in S.get('drop', ()): bpy.data.objects.remove(o)
for o in ms:
    o.data = o.data.copy(); o.data.transform(o.matrix_world); o.parent = None; o.matrix_world = Matrix()
def allv(): return [v.co for o in ms for v in o.data.vertices]
def T(M):
    global muzzle_pt
    for o in ms: o.data.transform(M)
    if muzzle_pt is not None: muzzle_pt = M @ muzzle_pt
if S.get('up') == 'y': T(Matrix.Rotation(math.pi / 2, 4, 'X'))
P = allv(); lo = Vector([min(p[i] for p in P) for i in range(3)]); hi = Vector([max(p[i] for p in P) for i in range(3)])
if not S.get('keep_axes'):
    if hi.x - lo.x > hi.y - lo.y: T(Matrix.Rotation(math.pi / 2, 4, 'Z'))
    P = allv(); lo = Vector([min(p[i] for p in P) for i in range(3)]); hi = Vector([max(p[i] for p in P) for i in range(3)])
    # the front: the muzzle; else the end where the barrel sticks out (the higher of the two thin ends in the top half)
    c = (lo + hi) / 2
    if muzzle_pt is None and S.get('muzzle_tip'):
        top = [p for p in P if p.z > lo.z + (hi.z - lo.z) * 0.3]
        ymax = max(p.y for p in top); ymin = min(p.y for p in top)
        front = 1 if (ymax - c.y) > (c.y - ymin) else -1
    elif muzzle_pt is not None: front = 1 if muzzle_pt.y > c.y else -1
    else: front = 1
    if 'front' in S: front = S['front']
    if S.get('flip'): front = -front
    if front < 0: T(Matrix.Rotation(math.pi, 4, 'Z'))
    P = allv(); lo = Vector([min(p[i] for p in P) for i in range(3)]); hi = Vector([max(p[i] for p in P) for i in range(3)])
c = (lo + hi) / 2
T(Matrix.Translation(Vector((-c.x, -c.y, -lo.z))))
P = allv(); lo = Vector([min(p[i] for p in P) for i in range(3)]); hi = Vector([max(p[i] for p in P) for i in range(3)])
s = S['length'] / (hi.y - lo.y) if 'length' in S else S.get('scale', 1.0)
T(Matrix.Scale(s, 4))
P = allv(); lo = Vector([min(p[i] for p in P) for i in range(3)]); hi = Vector([max(p[i] for p in P) for i in range(3)])
print(key, 'size', [round(x, 2) for x in hi - lo], 'scale', round(s, 5))
def bbox(objs):
    Q = [v.co for o in objs for v in o.data.vertices]
    return Vector([min(p[i] for p in Q) for i in range(3)]), Vector([max(p[i] for p in Q) for i in range(3)])
# groups: turret (a named object and what hangs under it, or what stands inside its footprint), gun, the rest
gun = [o for o in ms if (S.get('gun') and under(o.name, S['gun'])) or any(o.name.startswith(k) if k.endswith('_') or 'rot' in k else k in o.name for k in S.get('gun_names', ()))]
piv = bbox([o for o in ms if o.name.startswith(S['pivot_obj'])]) if S.get('pivot_obj') else None
tur = []
if S.get('turret_names'): tur = [o for o in ms if any(o.name.startswith(k) for k in S['turret_names']) and o not in gun]
elif S.get('turret'): tur = [o for o in ms if under(o.name, S['turret']) and o not in gun]
elif S.get('turret_geo'):
    core = [o for o in ms if S['turret_geo'] in o.name]
    tl, th = bbox(core)
    for o in ms:
        if o in gun or o in core: continue
        a, b = bbox([o]); m = (a + b) / 2
        if tl.x - 0.05 < m.x < th.x + 0.05 and tl.y - 0.05 < m.y < th.y + 0.05 and m.z > tl.z + 0.05: tur.append(o)
    tur += core
hull = [o for o in ms if o not in gun and o not in tur]
if S.get('turret_spin'):
    tl0, th0 = bbox(tur); cc = (tl0 + th0) / 2
    R = Matrix.Translation(Vector((cc.x, cc.y, 0))) @ Matrix.Rotation(math.pi, 4, 'Z') @ Matrix.Translation(Vector((-cc.x, -cc.y, 0)))
    for o in tur + gun: o.data.transform(R)
    if piv: piv = (R @ piv[0], R @ piv[1])
print('parts: hull', len(hull), 'turret', len(tur), 'gun', len(gun))
def join(objs, name):
    if not objs: return None
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1: bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = name; o.data.name = name
    return o
H = join(hull, key + '_Hull'); TU = join(tur, key + '_Turret'); G = join(gun, key + '_Gun')
def dec(o, target):
    if not o or len(o.data.polygons) <= target: return
    bpy.context.view_layer.objects.active = o
    if o.data.shape_keys: o.shape_key_clear()
    d = o.modifiers.new('d', 'DECIMATE'); d.ratio = target / len(o.data.polygons); bpy.ops.object.modifier_apply(modifier='d')
b = S.get('budget', 60000)
dec(H, int(b * 0.8)); dec(TU, int(b * 0.15)); dec(G, max(1500, int(b * 0.08)))
# the muzzle: the given marker, or the barrel's far end
if G:
    gl, gh = bbox([G])
    tip = max((v.co for v in G.data.vertices), key=lambda p: p.y)
    if muzzle_pt is None: muzzle_pt = Vector(((gl.x + gh.x) / 2, tip.y, (gl.z + gh.z) / 2))
elif S.get('muzzle_tip'):
    top = [v.co for v in H.data.vertices if v.co.z > (hi.z) * 0.3]
    tip = max(top, key=lambda p: p.y); near = [p for p in top if p.y > tip.y - 0.25]
    muzzle_pt = Vector((sum(p.x for p in near) / len(near), tip.y, sum(p.z for p in near) / len(near)))
# pivots: the turret on its ring (middle of its footprint, its lowest point), the gun on its trunnions (middle of the
# mount's box, or the back of the barrel)
if TU:
    tl, th = bbox([TU]); ring = Vector(((tl.x + th.x) / 2, (tl.y + th.y) / 2, tl.z))
    TU.data.transform(Matrix.Translation(-ring)); TU.location = ring; TU.parent = H; TU.matrix_parent_inverse = Matrix()
    TU.location = ring
if G:
    gl, gh = bbox([G]); trun = Vector(((gl.x + gh.x) / 2, gl.y + (gh.y - gl.y) * (0.15 if S.get('gun_names') else 0.5), (gl.z + gh.z) / 2))
    if S.get('gun') and S['gun'] + '' in ('mount.001', 'mount2'):
        trun = Vector(((gl.x + gh.x) / 2, gl.y + min(0.45, (gh.y - gl.y) * 0.25), (gl.z + gh.z) / 2))
    if piv: trun = (piv[0] + piv[1]) / 2
    G.data.transform(Matrix.Translation(-trun))
    par = TU if TU else H
    G.parent = par; G.matrix_parent_inverse = Matrix(); G.location = trun - (par.location if par is TU else Vector())
if muzzle_pt is not None:
    mz = bpy.data.objects.new(key + '_Muzzle', None); bpy.context.collection.objects.link(mz)
    host = G or H
    mz.parent = host; mz.matrix_parent_inverse = Matrix()
    bpy.context.view_layer.update()
    mz.location = host.matrix_world.inverted() @ muzzle_pt
    print('muzzle', [round(x, 2) for x in muzzle_pt])
# materials: plain names, textures beside the model
os.makedirs(OUT + 'Textures/' + key, exist_ok=True)
done = {}
for o in [x for x in (H, TU, G) if x]:
    for p in o.data.polygons: p.use_smooth = True
    if not o.data.materials: o.data.materials.append(bpy.data.materials.new(S.get('matname') or key))
    for i, m in enumerate(o.data.materials):
        if not m: continue
        raw = re.sub(r'_\d+$', '', m.name); raw = re.sub(r'\.\d+$', '', raw)
        clean = S.get('matname') or re.sub(r'[^A-Za-z0-9]+', '_', raw).strip('_') or key
        if clean not in done:
            f = dl(S['tex'](raw))
            if os.path.exists(f):
                im = Image.open(f).convert('RGB'); im.thumbnail((1024, 1024)); im.save(OUT + 'Textures/' + key + '/' + clean + '.jpg', quality=86)
            else: print('  no texture for', raw, f)
            nm = bpy.data.materials.new(clean); done[clean] = nm
        o.data.materials[i] = done[clean]
for o in [x for x in (H, TU, G) if x]: print(o.name, len(o.data.polygons), [round(x, 2) for x in o.location])
print('materials', list(done))
# flat: no parents in the file (nested parts came into Unity at 1/100 scale with the unit conversion); the game
# finds <key>_Turret, _Gun and _Muzzle by name and builds its own pivots at their origins
bpy.context.view_layer.update()
for o in list(bpy.data.objects):
    mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
bpy.context.view_layer.update()
bpy.ops.export_scene.fbx(filepath=OUT + key + '.fbx', use_selection=False, axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True, mesh_smooth_type='FACE')
