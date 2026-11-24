# Commando variants of British_Infantry (Tripo, rigged): the steel helmet pressed down into the skull and a green
# commando beret over it, worn pulled down to the right (badge over the left eye). PIPER=1 adds Bill Millin's kilt
# (Cameron of Erracht tartan) and his bagpipes. The texture is widened: the soldier's own on the left half, the beret,
# tartan and pipes on the right half. usage: python3 -I variant.py -- <in fbx> <in tex> <out fbx> <out tex> <piper 0/1>
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix
SRC, TEX, OUT, OUTTEX, PIPER = sys.argv[-5], sys.argv[-4], sys.argv[-3], sys.argv[-2], sys.argv[-1] == '1'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC); bpy.context.view_layer.update()
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
body = [o for o in bpy.data.objects if o.type == 'MESH'][0]
W = body.matrix_world; Wi = W.inverted()
def bone(n): return arm.matrix_world @ arm.data.bones['mixamorig:' + n].head_local
head, top = bone('Head'), arm.matrix_world @ arm.data.bones['mixamorig:HeadTop_End'].head_local
# ---- 1. the helmet pressed into the skull: everything above the brow line and outside the skull moves in
c = Vector((head.x, head.y + 0.005, 1.70)); rx, ry, rz = 0.088, 0.098, 0.085
moved = 0; hair = set()
for v in body.data.vertices:
    p = W @ v.co
    if p.z < 1.665:
        # the brim, lower down at the back and sides: folded in round the head
        if p.z < 1.62: continue
        dx, dy = p.x - c.x, p.y - c.y
        kh = (dx / 0.098) ** 2 + (dy / 0.108) ** 2
        if kh > 1.0:
            p2 = Vector((c.x + dx / math.sqrt(kh), c.y + dy / math.sqrt(kh), p.z))
            v.co = Wi @ p2; moved += 1
            if dy > -0.06: hair.add(v.index)
        continue
    d = p - c
    k = (d.x / rx) ** 2 + (d.y / ry) ** 2 + (max(d.z, 0) / rz) ** 2
    if k > 1.0:
        p2 = c + d / math.sqrt(k)
        v.co = Wi @ p2; moved += 1; hair.add(v.index)
    elif p.z > 1.735 or (p.z > 1.695 and not (d.y < -0.06 and p.z < 1.725)): hair.add(v.index)
print('helmet vertices pressed in', moved)
# ---- 2. widen the UVs: the soldier on the left half of a double-width texture
uvl = body.data.uv_layers.active
for l in body.data.loops:
    if l.vertex_index in hair: uvl.data[l.index].uv = (0.57, 0.20)     # short dark hair under the beret
    else: uvl.data[l.index].uv.x *= 0.5
# palette on the right half: u 0.5..1; (beret 0.52-0.62, tartan 0.65-0.95 x 0.05-0.95, black 0.52-0.62 v 0.3, bag 0.52-0.62 v 0.55)
def add_part(name, bm, uvbox, group_weights):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    uv = me.uv_layers.new()
    # planar-ish UVs squeezed into the box
    xs = [v.co.x for v in me.vertices]; zs = [v.co.z for v in me.vertices]
    x0, x1, z0, z1 = min(xs), max(xs) + 1e-6, min(zs), max(zs) + 1e-6
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            ang = math.atan2(co.y - sum(v.co.y for v in me.vertices) / len(me.vertices), co.x - (x0 + x1) / 2)
            u = uvbox[0] + (uvbox[2] - uvbox[0]) * ((ang / math.tau) % 1.0)
            vv = uvbox[1] + (uvbox[3] - uvbox[1]) * (co.z - z0) / (z1 - z0)
            uv.data[li].uv = (u, vv)
    for gname, wf in group_weights.items():
        g = o.vertex_groups.new(name='mixamorig:' + gname)
        for v in me.vertices:
            w = wf(v.co)
            if w > 1e-3: g.add([v.index], w, 'REPLACE')
    for p in me.polygons: p.use_smooth = True
    return o
parts = []
# ---- 3. the beret: a soft flattened disc over the head, tilted down to the right
bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=10, radius=1.0)
for v in bm.verts:
    x, y, z = v.co
    z = z * 0.32 if z > 0 else z * 0.12
    v.co = Vector((x * 0.122, y * 0.13, z * 0.11))
bm.transform(Matrix.Rotation(math.radians(-14), 4, 'Y'))          # pulled down on the right (the man's right is -x)
bm.transform(Matrix.Translation(c + Vector((-0.012, 0.0, 0.035))))
parts.append(add_part('Beret', bm, (0.52, 0.80, 0.62, 0.95), {'Head': lambda co: 1.0}))
# a cap badge on the left front
bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
bm.transform(Matrix.Diagonal(Vector((0.022, 0.006, 0.026, 1))))
bm.transform(Matrix.Translation(c + Vector((0.045, -0.105, 0.035))))
parts.append(add_part('Badge', bm, (0.52, 0.62, 0.53, 0.63), {'Head': lambda co: 1.0}))
if PIPER:
    hips, lul, rul, lleg = bone('Hips'), bone('LeftUpLeg'), bone('RightUpLeg'), bone('LeftLeg')
    zt, zb = hips.z + 0.03, lleg.z + 0.02                              # waist to the top of the knee
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, segments=32, radius1=0.25, radius2=0.185, depth=zt - zb)
    bm.transform(Matrix.Translation(Vector((hips.x, hips.y + 0.01, (zt + zb) / 2))))
    for v in bm.verts:                                                # pleats at the back
        a = math.atan2(v.co.y - hips.y, v.co.x - hips.x)
        if math.sin(a) > 0.2: r = 1 + 0.03 * math.sin(a * 14); v.co.x = hips.x + (v.co.x - hips.x) * r; v.co.y = hips.y + (v.co.y - hips.y) * r
    def side(co, s): # weight to a thigh grows towards the hem, on its own side
        t = max(0.0, min(1.0, (zt - co.z) / (zt - zb)))
        k = max(0.0, min(1.0, 0.5 + s * co.x / 0.12))
        return t * 0.7 * k
    kilt = add_part('Kilt', bm, (0.65, 0.05, 0.95, 0.75),
                    {'Hips': lambda co: 1.0 - side(co, 1) - side(co, -1), 'LeftUpLeg': lambda co: side(co, 1), 'RightUpLeg': lambda co: side(co, -1)})
    parts.append(kilt)
    # sporran
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bm.transform(Matrix.Diagonal(Vector((0.13, 0.05, 0.16, 1)))); bm.transform(Matrix.Translation(Vector((hips.x, hips.y - 0.215, zt - 0.17))))
    parts.append(add_part('Sporran', bm, (0.52, 0.30, 0.62, 0.40), {'Hips': lambda co: 1.0}))
    # the pipes, under the left arm: the bag against the left side of the chest, three drones up over the left
    # shoulder, the chanter down the front, the blowpipe up to the mouth (bind pose: arms out, so nothing collides)
    chest = bone('Spine2'); neck = bone('Neck')
    def tube(name, p0, p1, r0, r1, uvbox):
        d = p1 - p0; L = d.length
        bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=r0, radius2=r1, depth=L)
        bm.transform(Matrix.Translation(Vector((0, 0, L / 2))))
        bm.transform(d.normalized().to_track_quat('Z', 'Y').to_matrix().to_4x4())
        bm.transform(Matrix.Translation(p0))
        parts.append(add_part(name, bm, uvbox, {'Spine2': lambda co: 1.0}))
    bagc = Vector((chest.x + 0.15, chest.y - 0.10, chest.z - 0.02))
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=1.0)
    bm.transform(Matrix.Diagonal(Vector((0.08, 0.10, 0.12, 1)))); bm.transform(Matrix.Translation(bagc))
    parts.append(add_part('Pipe bag', bm, (0.52, 0.55, 0.62, 0.65), {'Spine2': lambda co: 1.0}))
    black = (0.52, 0.30, 0.62, 0.40)
    for i, (ln, sx) in enumerate([(0.55, 0.0), (0.42, 0.05), (0.40, 0.10)]):
        p0 = bagc + Vector((0.02 + sx * 0.3, 0.02 + sx, 0.08))
        tube(f'Drone {i + 1}', p0, p0 + Vector((0.18 + sx, 0.22, 0.92)).normalized() * ln, 0.016, 0.012, black)
    tube('Chanter', bagc + Vector((-0.08, -0.06, -0.06)), bagc + Vector((-0.12, -0.16, -0.38)), 0.017, 0.012, black)
    tube('Blowpipe', bagc + Vector((-0.05, -0.08, 0.06)), Vector((neck.x + 0.01, neck.y - 0.12, neck.z + 0.12)), 0.010, 0.008, black)
# ---- 4. one mesh again (the game gives the body one material), bound to the armature
for o in parts:
    o.matrix_world = Matrix()
bpy.ops.object.select_all(action='DESELECT')
for o in parts: o.select_set(True)
body.select_set(True); bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
mod = body.modifiers.get('Armature') or body.modifiers.new('Armature', 'ARMATURE'); mod.object = arm
print('faces', len(body.data.polygons))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                         add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False, mesh_smooth_type='FACE')
# ---- 5. the texture: soldier left, palette right
from PIL import Image, ImageDraw
src = Image.open(TEX).convert('RGB'); S = 1024
out = Image.new('RGB', (S * 2, S), (40, 40, 40)); out.paste(src.resize((S, S)), (0, 0))
d = ImageDraw.Draw(out)
def box(u0, v0, u1, v1, col): d.rectangle([int(u0 * 2 * S), int((1 - v1) * S), int(u1 * 2 * S), int((1 - v0) * S)], fill=col)
box(0.5, 0.78, 0.64, 0.97, (46, 66, 38))        # commando green beret (Bancroft green)
box(0.5, 0.62, 0.64, 0.64, (150, 120, 60))      # badge (brass)
box(0.5, 0.14, 0.64, 0.26, (38, 28, 20))        # hair
box(0.5, 0.28, 0.64, 0.42, (24, 20, 18))        # black wood / leather: drones, chanter, sporran
box(0.5, 0.53, 0.64, 0.67, (30, 52, 40))        # the bag in a dark tartan cover
# Cameron of Erracht tartan (approximate sett): dark green and dark blue with red, light blue and yellow lines
x0, x1, y0, y1 = int(0.65 * 2 * S), int(0.95 * 2 * S), int((1 - 0.75) * S), int((1 - 0.05) * S)
G, B, R, Y, LB = (28, 58, 38), (24, 34, 66), (150, 30, 32), (190, 160, 50), (90, 120, 160)
sett = [(G, 16), (B, 10), (R, 2), (B, 10), (G, 16), (Y, 2), (G, 16), (B, 10), (LB, 2), (B, 10)]
period = sum(w for _, w in sett); scale = max(1, (x1 - x0) // (period * 3))
def colour_at(i):
    i %= period * scale
    for c_, w in sett:
        if i < w * scale: return c_
        i -= w * scale
    return G
px = out.load()
for y in range(y0, y1):
    cy = colour_at(y - y0)
    for x in range(x0, x1):
        cx = colour_at(x - x0)
        # twill: warp and weft alternate in diagonal steps
        px[x, y] = cx if ((x + y) // max(1, scale // 2)) % 2 else cy
out.save(OUTTEX, quality=90)
print('texture', out.size)
