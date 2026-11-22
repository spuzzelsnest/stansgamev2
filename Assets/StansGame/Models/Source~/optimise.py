# Lighter copies of the heaviest meshes in use (22 Nov): every object keeps its name, pivot, materials and place; the
# polygons are collapsed to about `ratio`, the textures are no longer packed inside the FBX (Unity already has them
# extracted beside the model, its materials remapped by name), so the files shrink from megabytes to a fraction.
# usage: python3 -I optimise.py -- <in.fbx> <out.fbx> <ratio> [min_tris]
import bpy, sys
a = sys.argv[sys.argv.index('--') + 1:]
src, dst, ratio = a[0], a[1], float(a[2]); floor = int(a[3]) if len(a) > 3 else 1500
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
before = after = 0
for o in bpy.data.objects:
    if o.type != 'MESH': continue
    n = sum(len(p.vertices) - 2 for p in o.data.polygons); before += n
    if n > floor:
        bpy.context.view_layer.objects.active = o
        if o.data.shape_keys: o.shape_key_clear()
        d = o.modifiers.new('d', 'DECIMATE'); d.ratio = max(ratio, floor / n); d.use_collapse_triangulate = True
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
        bpy.ops.object.modifier_apply(modifier='d')
    after += sum(len(p.vertices) - 2 for p in o.data.polygons)
for im in list(bpy.data.images):
    if im.packed_file: im.unpack(method='REMOVE') if False else None
print('TRIS', src.split('/')[-1], before, '->', after)
bpy.ops.export_scene.fbx(filepath=dst, use_selection=False, embed_textures=False, path_mode='STRIP',
    axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', bake_space_transform=False,
    use_custom_props=False, mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False)
