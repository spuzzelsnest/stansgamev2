import bpy, bmesh, sys, os, math, addon_utils
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index("--") + 1]
os.makedirs(OUT, exist_ok=True)
DATA = r"C:\Users\32477\AppData\Roaming\Blender Foundation\Blender\5.3\extensions\.user\user_default\mpfb\data"
ARM_DROP = float(os.environ.get("ARM_DROP", "40"))

bpy.ops.wm.read_factory_settings(use_empty=True)
addon_utils.enable("bl_ext.user_default.mpfb", default_set=False)
from bl_ext.user_default.mpfb.services.humanservice import HumanService
from bl_ext.user_default.mpfb.services.targetservice import TargetService

scene = bpy.context.scene


def p(*a):
    return os.path.join(DATA, *a)


# ============ body ============
macro = TargetService.get_default_macro_info_dict()
macro.update({"gender": 1.0, "age": 0.5, "muscle": 0.55, "weight": 0.47, "proportions": 0.6, "height": 0.55})
macro["race"] = {"asian": 0.0, "caucasian": 1.0, "african": 0.0}
body = HumanService.create_human(mask_helpers=True, detailed_helpers=False, extra_vertex_groups=False,
                                 feet_on_ground=True, scale=0.1, macro_detail_dict=macro)
body.name = "Stan_Body"

# face sliders pushed toward the reference photos: oval face, defined jaw, thin lips, high forehead, straight nose
FACE = [
    ("head-oval", 0.45), ("head-scale-vert-incr", 0.12),
    ("forehead-scale-vert-incr", 0.25), ("forehead-temple-decr", 0.2),
    ("chin-bones-incr", 0.35), ("chin-prominent-incr", 0.2), ("chin-width-decr", 0.2),
    ("l-cheek-bones-incr", 0.25), ("r-cheek-bones-incr", 0.25),
    ("l-cheek-volume-decr", 0.3), ("r-cheek-volume-decr", 0.3),
    ("nose-width1-decr", 0.5), ("nose-width2-decr", 0.45), ("nose-flaring-decr", 0.35),
    ("nose-scale-vert-decr", 0.4), ("nose-point-up", 0.25), ("nose-scale-depth-decr", 0.2),
    ("mouth-upperlip-volume-decr", 0.6), ("mouth-lowerlip-volume-decr", 0.45), ("mouth-scale-horiz-decr", 0.25),
    ("mouth-lowerlip-height-decr", 0.2), ("head-fat-decr", 0.2),
    ("l-ear-wing-incr", 0.25), ("r-ear-wing-incr", 0.25),
    ("l-eye-bag-decr", 0.2), ("r-eye-bag-decr", 0.2),
]
try:
    TargetService.bulk_load_targets(body, [{"target": n, "value": v} for n, v in FACE])
    print("FACE_TARGETS", len(FACE))
except Exception as e:
    print("FACE_TARGETS_FAILED", e)

HumanService.set_character_skin(p("skins", "young_caucasian_male", "young_caucasian_male.mhmat"), body, skin_type="ENHANCED_SSS")
rig = HumanService.add_builtin_rig(body, "default", import_weights=True)
parts = {}
for atype, folder, sub in (("Eyes", "eyes", "high-poly"), ("Eyebrows", "eyebrows", "eyebrow001"),
                           ("Teeth", "teeth", "teeth_base"), ("Hair", "hair", os.environ.get("HAIR", "short04"))):
    parts[atype] = HumanService.add_mhclo_asset(p(folder, sub, sub + ".mhclo"), body, asset_type=atype,
                                                subdiv_levels=1, material_type="MAKESKIN")

bpy.context.view_layer.update()


# ---- tints (hair, brows, eyes, skin) ----
def tint_base_color(obj, rgba, fac=1.0, mode="MULTIPLY"):
    for m in obj.data.materials:
        if not (m and m.use_nodes):
            continue
        nt = m.node_tree
        for bsdf in [n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"]:
            sock = bsdf.inputs["Base Color"]
            if not sock.is_linked:
                sock.default_value = rgba
                continue
            src = sock.links[0].from_socket
            mix = nt.nodes.new("ShaderNodeMix")
            mix.data_type = "RGBA"
            mix.blend_type = mode
            mix.inputs[0].default_value = fac
            mix.inputs[7].default_value = rgba
            nt.links.new(src, mix.inputs[6])
            nt.links.new(mix.outputs[2], sock)


def set_eye_texture(obj, png):
    img = bpy.data.images.load(png)
    for m in obj.data.materials:
        if m and m.use_nodes:
            for n in m.node_tree.nodes:
                if n.type == "TEX_IMAGE":
                    n.image = img


set_eye_texture(parts["Eyes"], p("eyes", "materials", "blue_eye.png"))
tint_base_color(parts["Hair"], (0.07, 0.045, 0.025, 1.0), 0.85, mode="MIX")
tint_base_color(parts["Eyebrows"], (0.16, 0.10, 0.05, 1.0), 0.85, mode="MIX")
tint_base_color(body, (1.06, 0.97, 0.93, 1.0), 0.6)
for _m in parts["Hair"].data.materials:
    if _m:
        for _a, _v in (("surface_render_method", "BLENDED"), ("blend_method", "BLEND")):
            try:
                setattr(_m, _a, _v)
            except Exception:
                pass

# ============ measure the posed body, classify every vertex by its dominant bone ============
for _mod in body.modifiers:
    if _mod.type == "MASK":
        _mod.show_viewport = False     # keep every vertex so indices match the original weights
        _mod.show_render = False
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
ev = body.evaluated_get(dg)
body_me = bpy.data.meshes.new_from_object(ev)
orig_groups = [list(v.groups) for v in body.data.vertices]
vg_names = [vg.name for vg in body.vertex_groups]
BONE_NAMES = set(rig.pose.bones.keys())
print("VG_SAMPLE", vg_names[:12], len(vg_names), "verts", len(body_me.vertices), len(orig_groups))
for _mod in body.modifiers:
    if _mod.type == "MASK":
        _mod.show_viewport = True
        _mod.show_render = True


BODY_IDX = vg_names.index("body") if "body" in vg_names else -1


def vclass(vtx):
    if BODY_IDX >= 0 and not any(g.group == BODY_IDX and g.weight > 0.5 for g in orig_groups[vtx.index]):
        return "other"          # helper geometry, not skin
    best, bw = None, 0.0
    for g in orig_groups[vtx.index]:
        if g.weight > bw and g.group < len(vg_names) and vg_names[g.group] in BONE_NAMES and vg_names[g.group] != "root":
            best, bw = vg_names[g.group], g.weight
    n = (best or "").lower()
    if n.startswith(("clavicle", "shoulder", "upperarm", "lowerarm")):
        return "arm"
    if n.startswith(("wrist", "hand", "finger", "metacarpal")):
        return "hand"
    if n.startswith("spine") or n.startswith("breast"):
        return "torso"
    if n.startswith("pelvis"):
        return "pelvis"
    if n.startswith(("upperleg", "lowerleg")):
        return "leg"
    if n.startswith(("foot", "toe")):
        return "foot"
    if n.startswith(("neck", "head", "jaw", "special", "eye", "tongue")):
        return "head"
    return "other"


body_me.transform(body.matrix_world)       # mesh now lives in world space
mwb = Matrix.Identity(4)
VCLS = [vclass(v) for v in body_me.vertices]

# ---- natural stance: swing both arms down about the shoulder (weights come from the rig) ----
ARM_PREFIX = ("upperarm", "lowerarm", "wrist", "finger", "metacarpal", "hand")


def arm_weight(i):
    return min(1.0, sum(g.weight for g in orig_groups[i]
                        if g.group < len(vg_names) and vg_names[g.group].lower().startswith(ARM_PREFIX)))


AW = [arm_weight(i) for i in range(len(body_me.vertices))]
rest = [v.co.copy() for v in body_me.vertices]
for sgn in (-1, 1):
    up = [rest[i] for i in range(len(rest)) if AW[i] > 0.5 and rest[i].x * sgn > 0.1 and
          any(vg_names[g.group].lower().startswith("upperarm01") for g in orig_groups[i] if g.group < len(vg_names))]
    zmax = max(q.z for q in up)
    top = [q for q in up if q.z > zmax - 0.03]
    pivot = Vector((sum(q.x for q in top) / len(top), sum(q.y for q in top) / len(top), zmax - 0.02))
    wr = [rest[i] for i in range(len(rest)) if AW[i] > 0.5 and rest[i].x * sgn > 0.1 and
          any(vg_names[g.group].lower().startswith("wrist") for g in orig_groups[i] if g.group < len(vg_names))]
    wc = Vector((sum(q.x for q in wr) / len(wr), sum(q.y for q in wr) / len(wr), sum(q.z for q in wr) / len(wr)))
    cur = math.degrees(math.atan2(abs(wc.x - pivot.x), pivot.z - wc.z))     # angle from vertical now
    drop = cur - float(os.environ.get("ARM_FINAL", "13"))                   # end ~13 deg off vertical
    R = Matrix.Rotation(math.radians(drop) * sgn, 3, "Y")
    print("ARM", sgn, "pivot", tuple(round(c, 3) for c in pivot), "wrist", tuple(round(c, 3) for c in wc), "cur", round(cur, 1), "drop", round(drop, 1))
    for i, v in enumerate(body_me.vertices):
        if AW[i] > 0.01 and rest[i].x * sgn > 0.0:
            new = pivot + R @ (rest[i] - pivot)
            v.co = rest[i].lerp(new, AW[i])
body_me.update()
WPTS = [v.co.copy() for v in body_me.vertices]
from collections import Counter
print("CLASSES", dict(Counter(VCLS)))
H = max(q.z for q in WPTS)
print("HEIGHT", round(H, 3))

# bake the posed skin as a real object (helpers removed), hide the unposed original
_bm = bmesh.new()
_bm.from_mesh(body_me)
_helpers = [v for v in _bm.verts if BODY_IDX >= 0 and not any(g.group == BODY_IDX and g.weight > 0.5 for g in orig_groups[v.index])]
bmesh.ops.delete(_bm, geom=_helpers, context="VERTS")
_skin_mesh = bpy.data.meshes.new("Stan_Skin")
_bm.to_mesh(_skin_mesh)
_bm.free()
for _mt in body_me.materials:
    _skin_mesh.materials.append(_mt)
for _pl in _skin_mesh.polygons:
    _pl.use_smooth = True
skin_obj = bpy.data.objects.new("Stan_Skin", _skin_mesh)
scene.collection.objects.link(skin_obj)
body.hide_render = True
body.hide_viewport = True
rig.hide_render = True
rig.hide_viewport = True

# ============ real face: landmark-driven reshape + photo projection ============
PHOTO = r"C:\Projects\AIassistant\3d-model\photos\reference_sheet.webp"
LMFILE = r"C:\Projects\AIassistant\3d-model\photos\landmarks.json"
if os.path.exists(PHOTO) and os.path.exists(LMFILE) and os.environ.get("NOPHOTO") != "1":
    import json
    import numpy as np

    _lm = json.load(open(LMFILE))
    P = np.array(_lm["photo_px"], dtype=float)       # 468 face landmarks in photo pixels
    W = np.array(_lm["world_xz"], dtype=float)       # the same landmarks on the model, world x/z in metres
    img = bpy.data.images.load(PHOTO)
    IW, IH = img.size

    def smooth(a, b, v):
        t = max(0.0, min(1.0, (v - a) / (b - a)))
        return t * t * (3 - 2 * t)

    # similarity transform photo -> world, anchored on the two eye centres (the eyeballs are separate objects)
    Q = np.stack([P[:, 0], -P[:, 1]], axis=1)
    EYE_A, EYE_B = [33, 133, 159, 145], [362, 263, 386, 374]
    qa, qb = Q[EYE_A].mean(0), Q[EYE_B].mean(0)
    wa, wb = W[EYE_A].mean(0), W[EYE_B].mean(0)
    A = complex(*(wb - wa)) / complex(*(qb - qa))
    T0 = complex(*((wa + wb) / 2)) - A * complex(*((qa + qb) / 2))

    def to_world(q):
        z = A * (q[:, 0] + 1j * q[:, 1]) + T0
        return np.stack([z.real, z.imag], axis=1)

    def to_photo(w):
        z = ((w[:, 0] + 1j * w[:, 1]) - T0) / A
        return np.stack([z.real, -z.imag], axis=1)

    D = to_world(Q) - W                               # where each model landmark should move to
    _dm = np.linalg.norm(D, axis=1) * 1000
    print("FACE_FIT px/m", round(1 / abs(A), 1), "roll", round(math.degrees(math.atan2(A.imag, A.real)), 2),
          "mean mm", round(float(_dm.mean()), 2), "max mm", round(float(_dm.max()), 2))

    # thin-plate spline through the landmark displacements
    SC = 20.0

    def _U(d):
        return np.where(d > 1e-9, d * d * np.log(d + 1e-12), 0.0)

    _src = W * SC
    _n = len(_src)
    _A = np.zeros((_n + 3, _n + 3))
    _A[:_n, :_n] = _U(np.linalg.norm(_src[:, None] - _src[None], axis=2)) + 5e-3 * np.eye(_n)
    _Pm = np.hstack([np.ones((_n, 1)), _src])
    _A[:_n, _n:] = _Pm
    _A[_n:, :_n] = _Pm.T
    _rhs = np.zeros((_n + 3, 2))
    _rhs[:_n] = D
    _coef = np.linalg.solve(_A, _rhs)

    def warp(xz):
        p = np.asarray(xz, dtype=float) * SC
        Kp = _U(np.linalg.norm(p[:, None] - _src[None], axis=2))
        return Kp @ _coef[:_n] + np.hstack([np.ones((len(p), 1)), p]) @ _coef[_n:]

    xc = float((wa[0] + wb[0]) / 2)
    ztop, zbot = float(W[:, 1].max()), float(W[:, 1].min())
    ynose = min(q.y for q, c in zip(WPTS, VCLS) if c == "head" and abs(q.x) < 0.02 and zbot < q.z < ztop)
    EYES = [(float(wa[0]), float(wa[1])), (float(wb[0]), float(wb[1]))]

    def geo_weight(co):
        w = 1.0 - smooth(0.065, 0.105, co.y - ynose)          # only the front of the head
        w *= 1.0 - smooth(0.062, 0.088, abs(co.x - xc))       # leave the ears alone
        w *= 1.0 - smooth(ztop - 0.005, ztop + 0.03, co.z)    # fade out towards the hairline
        w *= smooth(zbot - 0.03, zbot, co.z)                  # and under the chin
        return w

    def in_zone(co):
        return zbot - 0.05 < co.z < ztop + 0.06 and co.y < ynose + 0.12

    def reshape(mesh):
        vs = [v for v in mesh.vertices if in_zone(v.co)]
        d = warp(np.array([[v.co.x, v.co.z] for v in vs]))
        mag = np.linalg.norm(d, axis=1, keepdims=True)
        d = d * np.minimum(1.0, 0.016 / np.maximum(mag, 1e-9))    # never move anything more than 16 mm
        for v, (dx, dz) in zip(vs, d):
            w = geo_weight(v.co)
            if w <= 0.0:
                continue
            we = min(smooth(0.010, 0.026, math.hypot(v.co.x - ex, v.co.z - ez)) for ex, ez in EYES)
            k = w * (0.78 + 0.22 * we)                             # gentler on the eyelids
            v.co.x += dx * k
            v.co.z += dz * k
            v.co.y -= 0.7 * abs(dz) * w * (1.0 - we)               # keep moved lids in front of the eyeball
        mesh.update()

    # photo UVs and blend mask, from the un-reshaped positions so every feature lands on its own landmark
    me = skin_obj.data
    sel = [v.index for v in me.vertices if in_zone(v.co)]
    _xz = np.array([[me.vertices[i].co.x, me.vertices[i].co.z] for i in sel])
    _pp = to_photo(_xz + warp(_xz))
    uvl = me.uv_layers.new(name="photoUV")
    maskattr = me.attributes.new("photomask", "FLOAT", "POINT")
    pxy = {}
    for j, i in enumerate(sel):
        v = me.vertices[i]
        px, py = float(_pp[j][0]), float(_pp[j][1])
        m = 1.0 - smooth(0.05, 0.085, v.co.y - ynose)
        m *= 1.0 - smooth(74, 96, abs(px - 1140))                 # cheeks, stop before the ears
        m *= smooth(172, 196, py) * (1.0 - smooth(416, 436, py))   # below the hairline, above the scarf
        m *= max(0.0, min(1.0, (-v.normal.y + 0.15) / 0.25))
        maskattr.data[i].value = m
        pxy[i] = (px, py)
    for li, loop in enumerate(me.loops):
        px, py = pxy.get(loop.vertex_index, (0.0, 0.0))
        uvl.data[li].uv = (px / IW, 1.0 - py / IH)

    reshape(me)
    reshape(body_me)
    WPTS[:] = [v.co.copy() for v in body_me.vertices]

    # shader: photo over the generic skin, generic skin tinted towards the photo's skin tone
    _grp = [n for n in me.materials[0].node_tree.nodes if n.type == "GROUP"][0]
    nt = _grp.node_tree
    bsdf = [n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"][0]
    base = bsdf.inputs["Base Color"]
    src = base.links[0].from_socket
    GAIN = 1.6
    tint = np.ones(3)
    try:
        _px = np.empty(IW * IH * 4, dtype=np.float32)
        img.pixels.foreach_get(_px)
        _px = _px.reshape(IH, IW, 4)

        def _patch(x0, x1, y0, y1):
            return _px[IH - y1:IH - y0, x0:x1, :3].reshape(-1, 3).mean(0)

        pm = (_patch(1075, 1105, 300, 335) + _patch(1175, 1205, 300, 335)) / 2
        dimg = [n for n in nt.nodes if n.type == "TEX_IMAGE" and n.name == "DiffuseTexture"][0].image
        _gp = np.empty(len(dimg.pixels), dtype=np.float32)
        dimg.pixels.foreach_get(_gp)
        _gp = _gp.reshape(-1, 4)[:, :3]
        gm = _gp[_gp.sum(1) > 0.6].mean(0)
        t = GAIN * (pm ** 2.2) / (gm ** 2.2)
        tint = np.clip(t * 0.72, 0.4, 1.25)
        print("SKIN_TINT", [round(float(c), 3) for c in tint])
    except Exception as _e:
        print("SKIN_TINT_SKIPPED", _e)
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.extension = "EXTEND"
    uvn = nt.nodes.new("ShaderNodeUVMap")
    uvn.uv_map = "photoUV"
    nt.links.new(uvn.outputs["UV"], tex.inputs["Vector"])
    bright = nt.nodes.new("ShaderNodeMix")
    bright.data_type = "RGBA"
    bright.blend_type = "MULTIPLY"
    bright.inputs[0].default_value = 1.0
    bright.inputs[7].default_value = (GAIN, GAIN, GAIN, 1.0)       # the photo is moody and dark: lift it
    nt.links.new(tex.outputs["Color"], bright.inputs[6])
    tintmix = nt.nodes.new("ShaderNodeMix")
    tintmix.data_type = "RGBA"
    tintmix.blend_type = "MULTIPLY"
    tintmix.inputs[0].default_value = 1.0
    tintmix.inputs[7].default_value = (float(tint[0]), float(tint[1]), float(tint[2]), 1.0)
    nt.links.new(src, tintmix.inputs[6])
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "photomask"
    attr.attribute_type = "GEOMETRY"
    blend = nt.nodes.new("ShaderNodeMix")
    blend.data_type = "RGBA"
    blend.blend_type = "MIX"
    nt.links.new(attr.outputs["Fac"], blend.inputs[0])
    nt.links.new(tintmix.outputs[2], blend.inputs[6])
    nt.links.new(bright.outputs[2], blend.inputs[7])
    nt.links.new(blend.outputs[2], base)
    for _k in ("Eyebrows", "Teeth"):                # the photo carries the real brows; mouth is closed
        parts[_k].hide_render = True
        parts[_k].hide_viewport = True


def pts_of(classes, zlo=-9, zhi=9, side=0):
    out = []
    for q, c in zip(WPTS, VCLS):
        if c in classes and zlo <= q.z <= zhi and (side == 0 or q.x * side > 0):
            out.append(q)
    return out


def stats(pts):
    xs = [q.x for q in pts]
    ys = [q.y for q in pts]
    zs = [q.z for q in pts]
    return (min(xs), max(xs)), (min(ys), max(ys)), (min(zs), max(zs))


def torso_ring(z):
    band = pts_of(("torso", "pelvis"), z - 0.02, z + 0.02)
    if len(band) < 12 or z < 1.04:   # at hip height the surface belongs to the leg bones
        band = pts_of(("torso", "pelvis", "leg"), z - 0.02, z + 0.02)
    (x0, x1), (y0, y1), _ = stats(band)
    return Vector(((x0 + x1) / 2, (y0 + y1) / 2, z)), (x1 - x0) / 2, (y1 - y0) / 2


def leg_ring(z, s):
    band = pts_of(("leg", "foot"), z - 0.025, z + 0.025, side=s)
    (x0, x1), (y0, y1), _ = stats(band)
    return Vector(((x0 + x1) / 2, (y0 + y1) / 2, z)), (x1 - x0) / 2, (y1 - y0) / 2


# ============ materials ============
def mat(name, color, rough=0.85, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = color
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    return m


def fabric(name, c1, c2, c3, streak=False, scale=7):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = 0.95
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (4, 1.2, 4) if streak else (1, 1, 1)
    n1 = nt.nodes.new("ShaderNodeTexNoise")
    n1.inputs["Scale"].default_value = scale
    n1.inputs["Detail"].default_value = 4
    n1.inputs["Distortion"].default_value = 1.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = "CONSTANT" if streak else "LINEAR"
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = c1
    ramp.color_ramp.elements[1].position = 0.5
    ramp.color_ramp.elements[1].color = c2
    e = ramp.color_ramp.elements.new(0.72)
    e.color = c3
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.2
    fine = nt.nodes.new("ShaderNodeTexNoise")
    fine.inputs["Scale"].default_value = 900
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    nt.links.new(mp.outputs["Vector"], n1.inputs["Vector"])
    nt.links.new(n1.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], b.inputs["Base Color"])
    nt.links.new(tc.outputs["Object"], fine.inputs["Vector"])
    nt.links.new(fine.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], b.inputs["Normal"])
    return m


M = {
    "smock": fabric("Denison", (0.12, 0.13, 0.06, 1), (0.04, 0.06, 0.03, 1), (0.11, 0.07, 0.04, 1), streak=True, scale=5),
    "bdress": fabric("Battledress", (0.14, 0.13, 0.07, 1), (0.12, 0.11, 0.06, 1), (0.13, 0.12, 0.065, 1), scale=40),
    "web": fabric("Webbing", (0.2, 0.18, 0.09, 1), (0.17, 0.15, 0.08, 1), (0.19, 0.17, 0.085, 1), scale=60),
    "beret": fabric("Beret", (0.22, 0.012, 0.025, 1), (0.18, 0.01, 0.02, 1), (0.2, 0.011, 0.022, 1), scale=80),
    "leather": mat("Leather", (0.06, 0.035, 0.02, 1), 0.55),
    "boot": mat("Boot", (0.035, 0.025, 0.018, 1), 0.5),
    "black": mat("Black", (0.012, 0.012, 0.012, 1), 0.5),
    "brass": mat("Brass", (0.5, 0.38, 0.12, 1), 0.35, 1.0),
    "green": mat("Shamrock", (0.02, 0.35, 0.12, 1), 0.7),
    "steel": mat("Steel", (0.03, 0.035, 0.025, 1), 0.5, 0.6),
}
kit = bpy.data.collections.new("AirborneKit")
scene.collection.children.link(kit)


def put(o, name, mname, smooth=True):
    o.name = name
    o.data.materials.append(M[mname])
    if smooth:
        for pl in o.data.polygons:
            pl.use_smooth = True
    for c in list(o.users_collection):
        c.objects.unlink(o)
    kit.objects.link(o)
    return o


def shell(name, classes, offset, mname, zlo=-9, zhi=9, thickness=0.006, pred=None, clamp_z=None):
    """Clothing = the body's own surface (posed), pushed outwards: fits exactly, no gaps."""
    bm = bmesh.new()
    bm.from_mesh(body_me)
    for v in bm.verts:
        v.co = mwb @ v.co
    vc = {v.index: VCLS[v.index] for v in bm.verts}
    drop = []
    for f in bm.faces:
        cen = f.calc_center_median()
        if not (all(vc[v.index] in classes for v in f.verts) and zlo <= cen.z <= zhi and (pred is None or pred(cen))):
            drop.append(f)
    bmesh.ops.delete(bm, geom=drop, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.normal_update()
    for v in bm.verts:
        v.co = v.co + v.normal * (offset(v.co) if callable(offset) else offset)
        if clamp_z is not None and v.co.z < clamp_z:
            v.co.z = clamp_z                 # level rim instead of a ragged one
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    o = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(o)
    put(o, name, mname)
    sm = o.modifiers.new("solid", "SOLIDIFY")
    sm.thickness = thickness
    sm.offset = 1.0
    return o


def loft(name, rings, mname, n=32, cap=True, margin=0.0, subdiv=True):
    bm = bmesh.new()
    loops = []
    for (c, u, rx, v, ry) in rings:
        loops.append([bm.verts.new(c + u * (math.cos(2 * math.pi * i / n) * (rx + margin)) +
                                   v * (math.sin(2 * math.pi * i / n) * (ry + margin))) for i in range(n)])
    for r0, r1 in zip(loops, loops[1:]):
        for i in range(n):
            bm.faces.new((r0[i], r0[(i + 1) % n], r1[(i + 1) % n], r1[i]))
    if cap:
        bm.faces.new(loops[0][::-1])
        bm.faces.new(loops[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    o = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(o)
    put(o, name, mname)
    if subdiv:
        o.modifiers.new("sub", "SUBSURF").levels = 1
    return o


X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))


def box(name, loc, size, mname, rot=(0, 0, 0), bevel=0.008):
    bpy.ops.mesh.primitive_cube_add(location=loc, rotation=rot)
    o = bpy.context.object
    o.scale = (size[0] / 2, size[1] / 2, size[2] / 2)
    bpy.ops.object.transform_apply(scale=True)
    if bevel:
        bv = o.modifiers.new("bevel", "BEVEL")
        bv.width = bevel
        bv.segments = 3
    return put(o, name, mname)


def sphere(name, loc, scale, mname, rot=(0, 0, 0), seg=32):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, location=loc, rotation=rot)
    o = bpy.context.object
    o.scale = scale
    return put(o, name, mname)


def cyl(name, loc, r, depth, mname, rot=(0, 0, 0), seg=32):
    bpy.ops.mesh.primitive_cylinder_add(vertices=seg, radius=r, depth=depth, location=loc, rotation=rot)
    return put(bpy.context.object, name, mname)


def shamrock(prefix, loc, size, face_axis="-Y"):
    made = []
    x, y, z = loc
    for i, (dx, dz) in enumerate(((-0.55, 0.45), (0.55, 0.45), (0, 1.05))):
        made.append(sphere(f"{prefix}Leaf{i}", (x + dx * size, y, z + dz * size), (size * 0.55, size * 0.12, size * 0.55), "green", seg=16))
    made.append(cyl(f"{prefix}Stem", (x + 0.1 * size, y, z - 0.35 * size), size * 0.1, size * 1.2, "green", seg=8))
    if face_axis == "-X":
        R = Matrix.Translation(Vector(loc)) @ Matrix.Rotation(math.radians(-90), 4, "Z") @ Matrix.Translation(-Vector(loc))
        for o in made:
            o.matrix_world = R @ o.matrix_world
    return made


# ============ Denison smock: fitted shell + skirt + collar ============
shell("SmockShell", ("arm", "torso"), 0.026, "smock", zlo=0.98, zhi=1.62)
hem = []
for i in range(0, 9):   # A-line skirt, hip length: z 0.72 .. 1.04, flaring out towards the hem
    z = 0.72 + 0.04 * i
    c, rx, ry = torso_ring(max(z, 0.78))
    e = 0.055 + 0.07 * (1.0 - i / 8.0)
    hem.append((c, X, rx + e, Y, ry + e))
loft("SmockSkirt", hem, "smock", margin=0.0, cap=False)
# collar cut from the neck surface, so it sits snug at the throat instead of floating as a band
col = []
for zr, e in ((1.50, 0.038), (1.535, 0.046), (1.565, 0.040)):
    _w, _np = 0.012, []
    while len(_np) < 8 and _w < 0.12:
        _np = [q for q in pts_of(("head", "torso"), zr - _w, zr + _w) if abs(q.x) < 0.1]
        _w += 0.012
    (a0, a1), (b0, b1), _ = stats(_np)
    col.append((Vector(((a0 + a1) / 2, (b0 + b1) / 2, zr)), X, (a1 - a0) / 2 + e, Y, (b1 - b0) / 2 + e))
loft("SmockCollar", col, "smock", cap=False)

# chest / skirt pockets, zip
c13, rx13, ry13 = torso_ring(1.3)
fy = c13.y - ry13
cz, rxz, ryz = torso_ring(1.02)
for s, n in ((-1, "L"), (1, "R")):
    box(f"BreastPocket{n}", (0.09 * s, fy - 0.04, 1.30), (0.1, 0.03, 0.11), "smock", bevel=0.008)
    box(f"SkirtPocket{n}", (0.12 * s, cz.y - ryz - 0.06, 0.93), (0.12, 0.04, 0.16), "smock", bevel=0.01)
box("ZipFront", (0, fy - 0.034, 1.2), (0.012, 0.012, 0.5), "leather", bevel=0.004)

# Irish touch: shamrock patch on the outer left upper arm
la = pts_of(("arm",), 1.28, 1.34, side=-1)
(ax0, ax1), (ay0, ay1), _ = stats(la)
px = ax0 - 0.026 - 0.006
py = (ay0 + ay1) / 2
box("PatchL", (px, py, 1.31), (0.01, 0.075, 0.095), "black", bevel=0.003)

# knitted cuffs at the wrists and shoulder epaulettes
for s, n in ((-1, "L"), (1, "R")):
    arm_pts = pts_of(("arm",), side=s)
    zmin = min(q.z for q in arm_pts)
    shell(f"SleeveCuff{n}", ("arm",), 0.036, "bdress", zlo=zmin, zhi=zmin + 0.06, thickness=0.006,
          pred=lambda cen, s=s: cen.x * s > 0.1)
    sh = [q for q in arm_pts if 0.14 < q.x * s < 0.22]
    sz = max(sh, key=lambda q: q.z)
    box(f"Epaulette{n}", (0.185 * s, sz.y, sz.z + 0.03), (0.07, 0.11, 0.012), "smock", rot=(0, math.radians(-14 * s), 0), bevel=0.004)
    box(f"EpauletteButton{n}", (0.2 * s, sz.y, sz.z + 0.04), (0.012, 0.012, 0.006), "brass", bevel=0.002)
shamrock("SleeveShamrock", (px - 0.008, py, 1.295), 0.019, face_axis="-X")

# ============ battledress trousers (fitted shell), gaiters, boots ============
for s, n in ((-1, "L"), (1, "R")):
    # loose straight-cut battledress trousers: hip -> ankle, fuller than the leg, gathered into the gaiter
    rings = []
    zs_t = [1.02 - 0.07 * i for i in range(0, 13)]       # 1.02 .. 0.18
    for i, zt in enumerate(zs_t):
        c, rx, ry = leg_ring(zt, s)
        e = 0.05 - 0.012 * (i / 12.0)
        rings.append((c, X, rx + e, Y, ry + e))
    loft(f"Trouser{n}", rings, "bdress", margin=0.0, cap=True)
    c, rx, ry = leg_ring(0.12, s)
    c2, rx2, ry2 = leg_ring(0.23, s)
    loft(f"Gaiter{n}", [(c, X, rx, Y, ry), (c2, X, rx2, Y, ry2)], "web", margin=0.032)
    foot = pts_of(("foot",), -9, 0.12, side=s)
    (fx0, fx1), (fy0, fy1), (fz0, fz1) = stats(foot)
    box(f"BootFoot{n}", ((fx0 + fx1) / 2, (fy0 + fy1) / 2 - 0.01, 0.045), (fx1 - fx0 + 0.04, fy1 - fy0 + 0.05, 0.09), "boot", bevel=0.035)
    box(f"BootSole{n}", ((fx0 + fx1) / 2, (fy0 + fy1) / 2 - 0.01, 0.012), (fx1 - fx0 + 0.045, fy1 - fy0 + 0.055, 0.028), "black", bevel=0.01)
    c3, rx3, ry3 = leg_ring(0.09, s)
    loft(f"BootShaft{n}", [(Vector((c3.x, c3.y, 0.02)), X, rx3, Y, ry3), (Vector((c3.x, c3.y, 0.14)), X, rx3, Y, ry3)], "boot", margin=0.032)
cL, rxL, ryL = leg_ring(0.72, -1)
box("MapPocket", (cL.x - rxL - 0.03, cL.y, 0.72), (0.03, 0.14, 0.17), "bdress", bevel=0.008)

# ============ 37-pattern webbing ============
cw, rxw, ryw = torso_ring(1.0)
loft("Belt", [(Vector((cw.x, cw.y, 0.97)), X, rxw + 0.075, Y, ryw + 0.07), (Vector((cw.x, cw.y, 1.035)), X, rxw + 0.075, Y, ryw + 0.07)], "web", margin=0.0, subdiv=False)
by = cw.y - ryw - 0.075
box("BuckleFrame", (0, by - 0.005, 1.0), (0.058, 0.012, 0.042), "brass", bevel=0.003)
box("BuckleInner", (0, by - 0.012, 1.0), (0.038, 0.006, 0.024), "web", bevel=0.002)
box("BeltTab", (0.05, by - 0.003, 1.0), (0.05, 0.01, 0.034), "web", bevel=0.003)
for s, n in ((-1, "L"), (1, "R")):
    # braces cut from the body's own surface so they follow the chest and back exactly; narrower, ending below the shoulder
    shell(f"Brace{n}", ("torso",), 0.038, "web", zlo=1.04, zhi=1.45, thickness=0.004,
          pred=lambda cen, s=s: abs(cen.x - 0.085 * s) < 0.021)
    cb, rxb, ryb = torso_ring(1.24)
    box(f"BraceAdjuster{n}", (0.085 * s, cb.y - ryb - 0.046, 1.24), (0.046, 0.008, 0.022), "brass", bevel=0.002)
    box(f"BraceAdjusterBar{n}", (0.085 * s, cb.y - ryb - 0.052, 1.24), (0.04, 0.006, 0.006), "steel", bevel=0.001)
    # 37-pattern ammunition pouch: body, deep lipped flap, centre strap, stud
    px_ = 0.115 * s
    box(f"Pouch{n}", (px_, by - 0.022, 1.045), (0.092, 0.062, 0.118), "web", bevel=0.01)
    box(f"PouchFlap{n}", (px_, by - 0.058, 1.07), (0.098, 0.014, 0.078), "web", bevel=0.005)
    box(f"PouchLip{n}", (px_, by - 0.066, 1.036), (0.098, 0.012, 0.014), "web", bevel=0.003)
    box(f"PouchStrap{n}", (px_, by - 0.068, 1.055), (0.022, 0.008, 0.07), "leather", bevel=0.002)
    sphere(f"PouchStud{n}", (px_, by - 0.075, 1.032), (0.008, 0.005, 0.008), "brass", seg=12)
box("Holster", (rxw * 0.78, cw.y - ryw * 0.6, 0.93), (0.05, 0.1, 0.22), "leather", rot=(0, 0, math.radians(-25)), bevel=0.012)
box("HolsterFlap", (rxw * 0.78, cw.y - ryw * 0.6 - 0.045, 1.02), (0.054, 0.012, 0.05), "leather", rot=(0, 0, math.radians(-25)), bevel=0.004)
box("Bayonet", (-rxw * 0.78, cw.y - ryw * 0.6, 0.93), (0.03, 0.03, 0.26), "leather", rot=(0, 0, math.radians(25)), bevel=0.006)
box("BayonetHilt", (-rxw * 0.78, cw.y - ryw * 0.6, 1.07), (0.026, 0.026, 0.05), "black", rot=(0, 0, math.radians(25)), bevel=0.004)
# water bottle in its carrier, cap and strap
cyl("WaterBottle", (-0.11, cw.y + ryw + 0.07, 0.97), 0.058, 0.2, "web", seg=24)
cyl("BottleCap", (-0.11, cw.y + ryw + 0.07, 1.082), 0.03, 0.03, "black", seg=16)
box("BottleStrap", (-0.11, cw.y + ryw + 0.07 - 0.052, 1.0), (0.016, 0.008, 0.14), "web", bevel=0.002)
# entrenching tool helve strapped to the right rear hip
box("EntrenchingTool", (0.12, cw.y + ryw + 0.065, 0.86), (0.03, 0.026, 0.3), "leather", rot=(math.radians(4), 0, math.radians(-3)), bevel=0.006)
cp, rxp, ryp = torso_ring(1.2)
box("SmallPack", (0, cp.y + ryp + 0.09, 1.2), (0.28, 0.09, 0.22), "web", bevel=0.02)
box("PackFlap", (0, cp.y + ryp + 0.138, 1.27), (0.28, 0.014, 0.1), "web", bevel=0.005)
for s2 in (-1, 1):
    box(f"PackStrap{s2}", (0.07 * s2, cp.y + ryp + 0.146, 1.22), (0.024, 0.01, 0.2), "leather", bevel=0.002)
    box(f"PackBuckle{s2}", (0.07 * s2, cp.y + ryp + 0.153, 1.185), (0.024, 0.008, 0.02), "brass", bevel=0.002)

# ============ beret ============
hair = parts["Hair"]
corners = [hair.matrix_world @ Vector(c) for c in hair.bound_box]
(hx0, hx1), (hy0, hy1), (hz0, hz1) = stats(corners)
hcx, hcy = (hx0 + hx1) / 2, (hy0 + hy1) / 2
hw = hx1 - hx0
# Para beret measured off the head: thin band, crown pulled down over the wearer's right (-X), badge over the left eye
_skull = [q for q, c in zip(WPTS, VCLS) if c == "head"]
ztop_sk = max(q.z for q in _skull)
zb = ztop_sk - 0.096                  # band line on the forehead


def head_ring(z):
    zz = min(z, ztop_sk - 0.03)
    w, pts = 0.012, []
    while len(pts) < 10 and w < 0.1:
        pts = [q for q in _skull if abs(q.z - zz) < w and abs(q.x) < 0.088]
        w += 0.012
    (a0, a1), (b0, b1), _ = stats(pts)
    return (a0 + a1) / 2, (b0 + b1) / 2, (a1 - a0) / 2, (b1 - b0) / 2


# short back and sides: no hair under the beret, none down the neck
_hb = bmesh.new()
_hb.from_mesh(hair.data)
_mwh = hair.matrix_world
_kill = [f for f in _hb.faces
         if (_mwh @ f.calc_center_median()).z > zb - 0.006 or (_mwh @ f.calc_center_median()).z < zb - 0.105]
bmesh.ops.delete(_hb, geom=_kill, context="FACES")
_hb.to_mesh(hair.data)
_hb.free()

SHIFT = 0.032
span = (ztop_sk + 0.002) - zb
rings, ring_info = [], []
#        height  flare  shift  tilt(deg): +X side rises, -X side drops
for f, e, sh, tilt in ((0.00, 0.011, 0.00, 0), (0.14, 0.014, 0.00, 0), (0.32, 0.024, 0.30, 3), (0.54, 0.032, 0.70, 6),
                       (0.78, 0.030, 0.95, 8), (0.93, 0.018, 1.00, 8), (1.00, 0.004, 1.00, 8)):
    z = zb + f * span
    cx, cy, rx, ry = head_ring(z)
    th = math.radians(tilt)
    c = Vector((cx - SHIFT * sh, cy + 0.004 * sh, z))
    u = Vector((math.cos(th), 0.0, math.sin(th)))
    rings.append((c, u, rx + e, Y, ry + e * 0.9))
    ring_info.append((c, rx + e, ry + e * 0.9, th))
beret = loft("Beret", rings, "beret", n=72, cap=True)
_wool = bpy.data.textures.new("woolfold", "CLOUDS")
_wool.noise_scale = 0.05
_dsp = beret.modifiers.new("wool", "DISPLACE")
_dsp.texture = _wool
_dsp.strength = 0.003
_dsp.mid_level = 0.5
# thin black leather band at the rim
cb0 = head_ring(zb - 0.006)
cb1 = head_ring(zb + 0.006)
loft("BeretBand", [(Vector((cb0[0], cb0[1], zb - 0.006)), X, cb0[2] + 0.0108, Y, cb0[3] + 0.0100),
                   (Vector((cb1[0], cb1[1], zb + 0.006)), X, cb1[2] + 0.0108, Y, cb1[3] + 0.0100)],
     "black", n=72, cap=False, subdiv=False)
# badge on a black backing over the LEFT eye
c2, rx2, ry2, th2 = ring_info[1]
bx = 0.036
by_ = c2.y - ry2 * math.sqrt(max(0.0, 1 - ((bx - c2.x) / rx2) ** 2))
bz = c2.z + 0.012
box("BadgeBacking", (bx, by_ - 0.0005, bz), (0.030, 0.004, 0.038), "black", rot=(math.radians(-10), 0, math.radians(8)), bevel=0.002)
sphere("BadgeBase", (bx, by_ - 0.0035, bz), (0.010, 0.003, 0.014), "brass", seg=16)
shamrock("BeretShamrock", (bx, by_ - 0.0068, bz - 0.005), 0.0052)

# ============ scene + renders ============
bpy.ops.mesh.primitive_plane_add(size=20)
f = bpy.context.object
fm = bpy.data.materials.new("Floor")
fm.use_nodes = True
fm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.01, 0.01, 0.01, 1)
f.data.materials.append(fm)
world = bpy.data.worlds.new("W")
scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.01, 0.01, 0.012, 1)
for nm, loc, en in (("Key", (-2.5, -3.5, 3.0), 260), ("Fill", (3.0, -2.5, 1.5), 100), ("Rim", (0, 3.5, 2.5), 180)):
    ld = bpy.data.lights.new(nm, "AREA")
    ld.energy = en
    ld.size = 3
    o = bpy.data.objects.new(nm, ld)
    scene.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = (Vector((0, 0, 1)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
cd = bpy.data.cameras.new("Cam")
cd.type = "ORTHO"
cam = bpy.data.objects.new("Cam", cd)
scene.collection.objects.link(cam)
scene.camera = cam
for e in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
    try:
        scene.render.engine = e
        break
    except TypeError:
        continue
scene.render.resolution_x = 1000
scene.render.resolution_y = 1300
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "stan_airborne.blend"))

shots = {
    "full_front": ((0, -6, H / 2), 2.2),
    "full_side": ((-6, 0, H / 2), 2.2),
    "full_back": ((0, 6, H / 2), 2.2),
    "full_34": ((-4.2, -4.2, H / 2), 2.2),
    "face_front": ((0, -6, H - 0.14), 0.5),
    "face_34": ((-3.5, -3.5, H - 0.14), 0.5),
}
for name, (loc, scale) in shots.items():
    cd.ortho_scale = scale
    cam.location = loc
    tgt = Vector((0, 0, loc[2]))
    cam.rotation_euler = (tgt - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
print("DONE")

