# Face variety (Joan, 24 Nov: "add some variations to the faces, so nobody looks the same").
# For each character texture: three copies with the skin recoloured - 1 sunburnt/ruddy, 2 pale, 3 weathered/tanned -
# into Resources/Faces/<texture>_v1..3.jpg (2048 px at most). Skin is found by its colour (warm hue under 26 deg,
# moderate saturation), so uniforms, webbing, hair and helmets stay as they are. FaceVariety.cs picks one per soldier.
# usage (from the project folder): python3 face_variants.py Assets/StansGame/Models/German/German_Normandy_BaseColor.jpg ...
import sys, os
from PIL import Image, ImageFilter
import numpy as np

OUT = "Assets/StansGame/Resources/Faces"
V = {1: ((1.07, 0.93, 0.90), 1.0), 2: ((1.00, 1.01, 1.03), 1.12), 3: ((0.92, 0.84, 0.76), 0.86)}
os.makedirs(OUT, exist_ok=True)
for f in sys.argv[1:]:
    name = os.path.splitext(os.path.basename(f))[0]
    im = Image.open(f).convert('RGB')
    if im.size[0] > 2048: im = im.resize((2048, 2048 * im.size[1] // im.size[0]), Image.LANCZOS)
    hsv = np.asarray(im.convert('HSV')).astype(float) / 255
    h, s, v = hsv[..., 0] * 360, hsv[..., 1], hsv[..., 2]
    skin = (((h < 26) | (h > 348)) & (s > 0.18) & (s < 0.65) & (v > 0.30)).astype('uint8') * 255
    m = Image.fromarray(skin).filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(3))
    m = np.asarray(m).astype(float)[..., None] / 255
    a = np.asarray(im).astype(float)
    for k, (mul, br) in V.items():
        o = np.clip(a * (1 - m) + a * np.array(mul) * br * m, 0, 255).astype('uint8')
        Image.fromarray(o).save(f"{OUT}/{name}_v{k}.jpg", quality=85)
    print(name, im.size)
