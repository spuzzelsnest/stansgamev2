import bpy, sys, math, os
from mathutils import Vector, Matrix
SRC, OUT = sys.argv[-2:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
bpy.context.view_layer.update()
ms = [o for o in bpy.data.objects if o.type == 'MESH']
for o in ms:
    o.data = o.data.copy(); o.data.transform(o.matrix_world); o.parent = None; o.matrix_world = Matrix()
for o in [o for o in bpy.data.objects if o.type != 'MESH']: bpy.data.objects.remove(o)
bpy.ops.object.select_all(action='DESELECT')
for o in ms: o.select_set(True)
bpy.context.view_layer.objects.active = ms[0]; bpy.ops.object.join()
t = bpy.context.view_layer.objects.active; t.name = 'Sherman'
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.remove_doubles(threshold=0.0002); bpy.ops.object.mode_set(mode='OBJECT')
n0 = len(t.data.polygons)
d = t.modifiers.new('dec', 'DECIMATE'); d.ratio = 70000 / n0; bpy.ops.object.modifier_apply(modifier='dec')
print('faces', n0, '->', len(t.data.polygons))
# save the texture
for img in bpy.data.images:
    if img.size[0] > 0:
        img.filepath_raw = '/home/claude/assets/sherman_tex_' + img.name.replace(' ', '_') + '.png'; img.file_format = 'PNG'; img.save(); print('image', img.name, img.size[:])
bpy.ops.wm.save_as_mainfile(filepath=OUT)
