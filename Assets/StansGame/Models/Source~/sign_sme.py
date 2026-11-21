# The "Sainte / Mère-Eglise" town sign of the 1944 photos (stMereEglise18: white painted planks, black border, black
# lettering, on posts), made from the Malaysian street sign download (LR_Signboard, "JALAN STESEN"):
# the texture is repainted (blue enamel and its lettering -> white paint, the rust kept), the plaque is made taller
# (1.17 x 0.67 m) and the posts become weathered wood.
# Usage: python3 -I sign_sme.py -- <LR_Signboard folder> <out.fbx> <tmp dir>
import sys, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
SRC, OUT, TMP = sys.argv[sys.argv.index('--') + 1:][:3]
os.makedirs(TMP, exist_ok=True)
FONT = '/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf'
if not os.path.exists(FONT): FONT = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'

a = np.asarray(Image.open(os.path.join(SRC, 'Signboard_MAT_Sign_BaseColor.png')).convert('RGB')).astype(np.float32)
H, W, _ = a.shape
r, g, b = a[..., 0], a[..., 1], a[..., 2]
rust = (r > b + 35) & (g > 0.42 * r) & (r > 70)
rng = np.random.default_rng(6)
def noise(scale):
    f = np.fft.fftfreq(W)[None, :] ** 2 + np.fft.fftfreq(H)[:, None] ** 2
    z = np.fft.ifft2(np.exp(-f * scale ** 2 * 40) * (f > 0) * np.exp(2j * np.pi * rng.random((H, W)))).real
    return (z - z.min()) / (z.max() - z.min())
grain = noise(1.5); blot = noise(25)
paint = np.array([226, 222, 206], np.float32) * (0.88 + 0.14 * grain[..., None]) * (0.9 + 0.12 * blot[..., None])
out = a.copy()
blue = (b > r + 20) & (b > 60)
out[blue] = paint[blue]

BANDS = [(62, 128, 948, 260), (62, 388, 948, 526)]     # front and back of the plaque in the texture
MPX_X = (948 - 62) / 1.17                              # texture pixels per metre across the board
for (x0, y0, x1, y1) in BANDS:
    w, h = x1 - x0, y1 - y0
    hm = int(round(0.67 * MPX_X))                      # draw at true proportions, squash into the band afterwards
    img = Image.new('RGB', (w, hm), (0, 0, 0)); d = ImageDraw.Draw(img)
    ink = Image.new('L', (w, hm), 0); di = ImageDraw.Draw(ink)
    bw = int(0.035 * MPX_X)                            # the black frame
    di.rectangle([0, 0, w - 1, hm - 1], fill=255); di.rectangle([bw, bw, w - 1 - bw, hm - 1 - bw], fill=0)
    for k in range(1, 5):                              # plank joints, faint
        y = bw + k * (hm - 2 * bw) // 5; di.line([(bw, y), (w - bw, y)], fill=70, width=2)
    def text(t, cy, size):
        f = ImageFont.truetype(FONT, size)
        x0t, y0t, x1t, y1t = di.textbbox((0, 0), t, font=f)
        di.text(((w - (x1t - x0t)) / 2 - x0t, cy - (y1t - y0t) / 2 - y0t), t, font=f, fill=255)
    text('Sainte', hm * 0.33, int(hm * 0.27))
    text('Mère-Eglise', hm * 0.68, int(hm * 0.27))
    ink = ink.filter(ImageFilter.GaussianBlur(0.8)).resize((w, h), Image.LANCZOS)
    m = np.asarray(ink, np.float32)[..., None] / 255.0
    region = out[y0:y1, x0:x1]
    # everything in the band that isn't rust becomes paint (wipes the old lettering), then the black lettering on top
    keep = rust[y0:y1, x0:x1][..., None]
    base = np.where(keep, region, paint[y0:y1, x0:x1])
    black = np.array([28, 27, 25], np.float32) * (0.8 + 0.4 * grain[y0:y1, x0:x1, None])
    worn = (grain[y0:y1, x0:x1, None] > 0.83)          # chipped paint shows through the black
    m = m * np.where(worn, 0.45, 1.0)
    out[y0:y1, x0:x1] = np.where(keep, region, base * (1 - m) + black * m)
# the posts: weathered wood instead of white and black stripes
posts = np.zeros((H, W), bool); posts[560:, :] = True; posts &= ~rust
lum = out[..., :3].mean(-1, keepdims=True) / 255.0
wood = np.array([118, 100, 80], np.float32) * (0.65 + 0.45 * lum) * (0.8 + 0.3 * noise(0.6)[..., None])
out[posts] = wood[posts]
tex = os.path.join(TMP, 'SME_TownSign_BaseColor.png'); Image.fromarray(np.clip(out, 0, 255).astype(np.uint8)).save(tex)
nrm = os.path.join(TMP, 'SME_TownSign_Normal.png'); Image.open(os.path.join(SRC, 'Signboard_MAT_Sign_Normal.png')).convert('RGB').save(nrm)
print('texture', tex)

import bpy, mathutils
bpy.ops.wm.read_factory_settings(use_empty=True)
fbx = [f for f in os.listdir(SRC) if f.lower().endswith('.fbx')][0]
bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, fbx))
bpy.context.view_layer.update()
o = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
for x in [x for x in bpy.context.scene.objects if x.type != 'MESH']: bpy.data.objects.remove(x)
bpy.context.view_layer.objects.active = o; o.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
me = o.data
S = 0.85; ZCUT = 0.85; K = 0.67 / (0.24 * S)
vs = [v.co.copy() for v in me.vertices]
cy = (min(v.y for v in vs) + max(v.y for v in vs)) / 2; cx = (min(v.x for v in vs) + max(v.x for v in vs)) / 2
for v in me.vertices:
    p = v.co; z = p.z
    zb = ZCUT * S
    z = z * S
    if p.z > ZCUT: z = zb + (z - zb) * K
    v.co = mathutils.Vector(((p.x - cx) * S, (p.y - cy) * S, z))
me.update()
# the board faces +X in the source: turn it so it faces Blender -Y ("south", the front in our convention is +Y; we want
# the lettering readable from the road, so the builder chooses the yaw). Here: front (band 1) faces +Y.
o.data.transform(mathutils.Matrix.Rotation(-1.5708, 4, 'Z') if True else mathutils.Matrix())
me.update()
m = me.materials[0]; m.name = 'SME_TownSign'; m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
outn = nt.nodes.new('ShaderNodeOutputMaterial'); bs = nt.nodes.new('ShaderNodeBsdfPrincipled'); nt.links.new(bs.outputs['BSDF'], outn.inputs['Surface'])
bs.inputs['Roughness'].default_value = 0.8
t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(tex); nt.links.new(t.outputs['Color'], bs.inputs['Base Color'])
tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = bpy.data.images.load(nrm); tn.image.colorspace_settings.name = 'Non-Color'
nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(tn.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], bs.inputs['Normal'])
o.name = me.name = 'TownSign'
vs = [v.co for v in me.vertices]
print('size', [round(max(v[i] for v in vs) - min(v[i] for v in vs), 2) for i in range(3)])
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, object_types={'MESH'}, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', path_mode='COPY', embed_textures=True, bake_anim=False, mesh_smooth_type='FACE')
print('exported', OUT)
