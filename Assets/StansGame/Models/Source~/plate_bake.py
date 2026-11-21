# Bakes a tileable chequer (studded) steel plate texture from the "carbon metal floor" model
# (_downloads/Textures/texture-studded-metal-floor): top-down orthographic renders (emission shaders writing height and
# normal) of a patch a whole number of stud repeats wide -> albedo and tangent-space normal map.
# Run: python3 -I plate_bake.py -- <fbx> <out dir> [cx cy size]
import bpy, sys, os, mathutils
args = sys.argv[sys.argv.index('--') + 1:]
FBX, OUT = args[:2]
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']
sc = bpy.context.scene
pts = [o.matrix_world @ mathutils.Vector(c) for o in ms for c in o.bound_box]
lo = mathutils.Vector([min(p[i] for p in pts) for i in range(3)]); hi = mathutils.Vector([max(p[i] for p in pts) for i in range(3)])
cx, cy = (lo.x + hi.x) / 2, (lo.y + hi.y) / 2
size = 2.0
if len(args) >= 5: cx, cy, size = float(args[2]), float(args[3]), float(args[4])
px = int(args[5]) if len(args) >= 6 else 512
def emit_mat(kind):
    m = bpy.data.materials.new(kind); m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial'); em = nt.nodes.new('ShaderNodeEmission')
    geo = nt.nodes.new('ShaderNodeNewGeometry')
    if kind == 'height':
        sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(geo.outputs['Position'], sep.inputs[0])
        mr = nt.nodes.new('ShaderNodeMapRange'); mr.inputs['From Min'].default_value = hi.z - 0.08; mr.inputs['From Max'].default_value = hi.z
        nt.links.new(sep.outputs['Z'], mr.inputs['Value']); nt.links.new(mr.outputs['Result'], em.inputs['Color'])
    else:
        vm = nt.nodes.new('ShaderNodeVectorMath'); vm.operation = 'MULTIPLY_ADD'
        vm.inputs[1].default_value = (0.5, 0.5, 0.5); vm.inputs[2].default_value = (0.5, 0.5, 0.5)
        nt.links.new(geo.outputs['Normal'], vm.inputs[0]); nt.links.new(vm.outputs['Vector'], em.inputs['Color'])
    nt.links.new(em.outputs['Emission'], out.inputs['Surface'])
    return m
cam = bpy.data.cameras.new('c'); cam.type = 'ORTHO'
co = bpy.data.objects.new('c', cam); sc.collection.objects.link(co); sc.camera = co
co.location = (cx, cy, hi.z + 1.0); co.rotation_euler = (0, 0, 0); cam.ortho_scale = size; cam.clip_start = 0.01; cam.clip_end = 10
sc.render.resolution_x = sc.render.resolution_y = px
sc.render.engine = 'CYCLES'; sc.cycles.samples = 8; sc.cycles.device = 'CPU'
sc.view_settings.view_transform = 'Standard'; sc.render.image_settings.color_depth = '16'
sc.world = bpy.data.worlds.new('w'); sc.world.color = (0, 0, 0)
for kind in ('height', 'normal'):
    m = emit_mat(kind)
    for o in ms:
        o.data.materials.clear(); o.data.materials.append(m)
    sc.render.filepath = os.path.join(OUT, f'{kind}.png'); bpy.ops.render.render(write_still=True)
print('baked', cx, cy, size, px, 'bounds', tuple(round(v, 2) for v in lo), tuple(round(v, 2) for v in hi))
