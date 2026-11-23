# Hetzer (printscreen 42): the muzzle marker sat at the back of the hull, so the game turned the model round and it
# drove backwards with its gun behind. The 7.5 cm PaK 39's barrel is the thin part at the other end (offset to the right
# of the centre line, as on the real Jagdpanzer 38(t)): the marker is moved to the barrel tip.
import bpy, sys
SRC, OUT = sys.argv[-2], sys.argv[-1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC); bpy.context.view_layer.update()
hull = bpy.data.objects['Hetzer_Hull']; mz = bpy.data.objects['Hetzer_Muzzle']
P = [hull.matrix_world @ v.co for v in hull.data.vertices]
ymin = min(p.y for p in P)
tip = [p for p in P if p.y < ymin + 0.04]
c = sum((p for p in tip), start=type(P[0])((0, 0, 0))) / len(tip)
print('old', [round(x, 3) for x in mz.matrix_world.translation], 'new', [round(x, 3) for x in c])
mw = mz.matrix_world.copy(); mw.translation = c; mz.matrix_world = mw
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, axis_forward='-Z', axis_up='Y', apply_unit_scale=True, bake_space_transform=True, mesh_smooth_type='FACE', path_mode='STRIP', embed_textures=False, add_leaf_bones=False, bake_anim=False)
