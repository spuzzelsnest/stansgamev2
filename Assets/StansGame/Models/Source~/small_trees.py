# Splits _downloads/trees small/source/Small Trees.fbx into three trees for Unity:
# SmallTree_A (6.2 m), SmallTree_B (3.8 m), SmallTree_C (7.4 m); base at the origin; leaves texture with alpha.
import bpy, sys, os
from PIL import Image
src, texdir, out = sys.argv[-3:]
# textures: leaf albedo + opacity -> RGBA png, bark -> 1k jpg
a = Image.open(os.path.join(texdir, "qimhv_2K_Albedo.jpg")).convert("RGB").resize((1024, 1024), Image.LANCZOS)
o = Image.open(os.path.join(texdir, "qimhv_2K_Opacity.jpg")).convert("L").resize((1024, 1024), Image.LANCZOS)
a.putalpha(o); a.save(os.path.join(out, "SmallTree_Leaves.png"))
Image.open(os.path.join(texdir, "vmbibijg_2K_Albedo.jpg")).convert("RGB").resize((1024, 1024), Image.LANCZOS).save(os.path.join(out, "SmallTree_Bark.jpg"), quality=90)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
leafmat = bpy.data.materials.new("SmallTree_Leaves"); barkmat = bpy.data.materials.new("SmallTree_Bark")
for ob in bpy.data.objects:
    if ob.type == 'MESH':
        for i, m in enumerate(ob.data.materials):
            ob.data.materials[i] = leafmat if m and "Leaf" in m.name else barkmat
names = {"tree": "SmallTree_A", "tree.001": "SmallTree_B", "tree.002": "SmallTree_C"}
for trunk, out_name in names.items():
    t = bpy.data.objects[trunk]
    parts = [t] + [c for c in t.children if c.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts: p.select_set(True)
    bpy.context.view_layer.objects.active = t
    # bake transforms, join, put the base of the trunk at the origin
    bpy.ops.object.duplicate()
    dup = list(bpy.context.selected_objects)
    for d in dup:
        mw = d.matrix_world.copy(); d.parent = None; d.matrix_world = mw
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.context.view_layer.objects.active = dup[0]
    bpy.ops.object.join()
    j = bpy.context.active_object; j.name = out_name
    import mathutils
    vs = [v.co for v in j.data.vertices]
    minz = min(v.z for v in vs)
    low = [v for v in vs if v.z < minz + 0.3]
    cx = sum(v.x for v in low) / len(low); cy = sum(v.y for v in low) / len(low)
    for v in j.data.vertices: v.co -= mathutils.Vector((cx, cy, minz))
    print(out_name, len(j.data.polygons), tuple(round(x, 2) for x in j.dimensions))
    bpy.ops.object.select_all(action='DESELECT'); j.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, out_name + ".fbx"), use_selection=True, axis_forward='-Z', axis_up='Y',
                             apply_scale_options='FBX_SCALE_UNITS', mesh_smooth_type='FACE', path_mode='STRIP', bake_space_transform=True)
    bpy.data.objects.remove(j)
