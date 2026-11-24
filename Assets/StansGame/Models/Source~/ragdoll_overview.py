# Ragdoll overview for Joan (24 Nov 2026): the British_Infantry body with the ragdoll the game builds on every
# humanoid (Editor/CharacterImporter.cs BuildRagdoll): one collider shape per physics body, named RD_<part>, with its
# mass and joint limits as custom properties (Object Properties > Custom Properties). Saved as a .blend to look at and
# adjust; and two pictures. usage: python3 -I ragdoll_overview.py -- <character fbx> <texture> <out.blend> <out png prefix>
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix
SRC, TEX, OUT, PNG = sys.argv[-4:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC); bpy.context.view_layer.update()
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
body = [o for o in bpy.data.objects if o.type == 'MESH'][0]
def B(n): return arm.matrix_world @ arm.data.bones['mixamorig:' + n].head_local
col = bpy.data.collections.new('Ragdoll (as the game builds it)'); bpy.context.scene.collection.children.link(col)
mats = {}
def mat(name, rgba):
    if name in mats: return mats[name]
    m = bpy.data.materials.new(name); m.diffuse_color = rgba; m.use_nodes = False; mats[name] = m; return m
def finish(o, part, kind, mass, parent, limits, colour):
    col.objects.link(o)
    o.data.materials.append(mat(kind, colour))
    o['mass_kg'] = mass; o['joint_to'] = parent or '-'
    if limits:
        o['twist_low_deg'], o['twist_high_deg'], o['swing1_deg'], o['swing2_deg'] = limits
    o['collider'] = kind
    return o
def box(part, centre, size, mass, parent, limits, colour):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0); bm.transform(Matrix.Diagonal(Vector((*size, 1)))); bm.transform(Matrix.Translation(centre))
    me = bpy.data.meshes.new('RD_' + part); bm.to_mesh(me); bm.free()
    return finish(bpy.data.objects.new('RD_' + part, me), part, 'Box', mass, parent, limits, colour)
def sphere(part, centre, r, mass, parent, limits, colour):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=r); bm.transform(Matrix.Translation(centre))
    me = bpy.data.meshes.new('RD_' + part); bm.to_mesh(me); bm.free()
    return finish(bpy.data.objects.new('RD_' + part, me), part, 'Sphere', mass, parent, limits, colour)
def capsule(part, a, b, r, mass, parent, limits, colour):
    d = b - a; L = d.length
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=r)
    for v in bm.verts: v.co.z += (L / 2) if v.co.z > 0 else -(L / 2)
    bm.transform(Matrix.Translation(Vector((0, 0, L / 2))))
    bm.transform(d.normalized().to_track_quat('Z', 'Y').to_matrix().to_4x4()); bm.transform(Matrix.Translation(a))
    me = bpy.data.meshes.new('RD_' + part); bm.to_mesh(me); bm.free()
    return finish(bpy.data.objects.new('RD_' + part, me), part, 'Capsule', mass, parent, limits, colour)
TORSO, HEAD, ARM, LEG = (0.9, 0.35, 0.2, 0.55), (0.95, 0.8, 0.2, 0.55), (0.25, 0.6, 0.95, 0.55), (0.3, 0.85, 0.35, 0.55)
hips, chest, head = B('Hips'), B('Spine1'), B('Head')
# (24 Nov) the torso and head shapes centred front-to-back on the body itself, as the game now does: on the Tripo
# rigs the spine bones run along the front of the body, and the shapes sat half outside it
P = [body.matrix_world @ v.co for v in body.data.vertices]
bodyY = (min(p.y for p in P) + max(p.y for p in P)) / 2
def C(p):
    d = max(-0.12, min(0.12, bodyY - p.y))
    return p if abs(d) < 0.03 else p + Vector((0, d, 0))
box('Hips', C(hips), (0.32, 0.22, 0.22), 12, None, None, TORSO)
L = (head - chest).length * 0.8
box('Chest', C(chest.lerp(head, 0.35)), (0.36, 0.24, L), 12, 'Hips', (-20, 20, 10, 10), TORSO)
sphere('Head', C(head + Vector((0, 0, 0.08))), 0.12, 5, 'Chest', (-40, 25, 25, 0), HEAD)
for s in ('Left', 'Right'):
    capsule(s + 'UpperArm', B(s + 'Arm'), B(s + 'ForeArm'), 0.06, 2.5, 'Chest', (-45, 45, 95, 70), ARM)
    capsule(s + 'LowerArm', B(s + 'ForeArm'), B(s + 'Hand'), 0.051, 2.0, s + 'UpperArm', (-10, 10, 75, 8), ARM)
    capsule(s + 'UpperLeg', B(s + 'UpLeg'), B(s + 'Leg'), 0.09, 7.5, 'Hips', (-20, 20, 75, 35), LEG)
    capsule(s + 'LowerLeg', B(s + 'Leg'), B(s + 'Foot'), 0.0765, 4.0, s + 'UpperLeg', (-10, 10, 80, 8), LEG)
total = sum(o['mass_kg'] for o in col.objects)
print('ragdoll mass', total, 'kg, bodies', len(col.objects))
# the body see-through, textured
m = bpy.data.materials.new('Body (see-through)'); m.use_nodes = True
t = m.node_tree.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(TEX)
m.node_tree.links.new(t.outputs[0], m.node_tree.nodes['Principled BSDF'].inputs[0])
body.data.materials.clear(); body.data.materials.append(m)
bpy.ops.wm.save_as_mainfile(filepath=OUT)
# pictures: front and side, body faint, shapes coloured
sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.color_type = 'MATERIAL'; sc.display.shading.show_xray = True; sc.display.shading.xray_alpha = 0.6
sc.render.resolution_x, sc.render.resolution_y = 700, 900
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.1
for name, loc, rot in (('front', (0, -5, 0.95), (90, 0, 0)), ('side', (5, -0.13, 0.95), (90, 0, 90))):
    cam.location = loc; cam.rotation_euler = [math.radians(x) for x in rot]
    sc.render.filepath = f'{PNG}_{name}.png'; bpy.ops.render.render(write_still=True)
