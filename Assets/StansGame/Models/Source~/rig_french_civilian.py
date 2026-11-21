# Rigging the French civilian (_downloads/French-resistent-character) for Unity. Run each part with: python3 -I <part>.py (bpy 5.x)

# ======== rig2.py
import bpy, mathutils, math
from mathutils import Vector
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath='/home/claude/civ/civ.glb')
civ=[o for o in bpy.data.objects if o.type=='MESH'][0]
civ.name='FrenchCivilian'
for o in list(bpy.data.objects):
    if o!=civ: bpy.data.objects.remove(o)
bpy.context.view_layer.objects.active=civ; civ.select_set(True)
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
def G(x,y,z): return Vector((x,-z,y))   # glTF (y up, z fwd) -> Blender
J={ # left side (+x) and centre, glTF metres
 'Hips':(0,0.93,0.0),'Spine':(0,1.01,0.0),'Spine1':(0,1.13,-0.005),'Spine2':(0,1.26,-0.01),
 'Neck':(0,1.46,-0.02),'Head':(0,1.56,0.0),'HeadTop_End':(0,1.79,0.0),
 'LeftShoulder':(0.035,1.43,-0.02),'LeftArm':(0.165,1.395,-0.03),'LeftForeArm':(0.32,1.235,-0.005),
 'LeftHand':(0.477,1.10,0.10),'LeftHandEnd':(0.56,0.99,0.18),
 'LeftUpLeg':(0.09,0.90,0.0),'LeftLeg':(0.145,0.50,0.012),'LeftFoot':(0.16,0.095,-0.05),
 'LeftToeBase':(0.21,0.025,0.085),'LeftToe_End':(0.235,0.02,0.165)}
def P(n):
    if n.startswith('Right'):
        x,y,z=J['Left'+n[5:]]; return G(-x,y,z)
    return G(*J[n])
chain=[('Hips',None,'Spine'),('Spine','Hips','Spine1'),('Spine1','Spine','Spine2'),('Spine2','Spine1','Neck'),
 ('Neck','Spine2','Head'),('Head','Neck','HeadTop_End'),('HeadTop_End','Head',None)]
for s in ('Left','Right'):
    chain+=[(s+'Shoulder','Spine2',s+'Arm'),(s+'Arm',s+'Shoulder',s+'ForeArm'),(s+'ForeArm',s+'Arm',s+'Hand'),(s+'Hand',s+'ForeArm',s+'HandEnd'),
            (s+'UpLeg','Hips',s+'Leg'),(s+'Leg',s+'UpLeg',s+'Foot'),(s+'Foot',s+'Leg',s+'ToeBase'),(s+'ToeBase',s+'Foot',s+'Toe_End'),(s+'Toe_End',s+'ToeBase',None)]
ad=bpy.data.armatures.new('Armature'); arm=bpy.data.objects.new('Armature',ad); bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active=arm; bpy.ops.object.mode_set(mode='EDIT')
eb={}
for n,par,tail in chain:
    b=ad.edit_bones.new('mixamorig:'+n); h=P(n)
    if tail: t=P(tail)
    else: t=h+(h-P(par)).normalized()*0.06
    b.head=h; b.tail=t; eb[n]=b
for n,par,tail in chain:
    if par: eb[n].parent=eb[par]; eb[n].use_connect=False
# rolls: z-axis forward-ish for a clean rig
for b in ad.edit_bones: b.align_roll(Vector((0,-1,0)) if abs((b.tail-b.head).normalized().y)<0.9 else Vector((0,0,1)))
bpy.ops.object.mode_set(mode='OBJECT')
# proxy: watertight voxel remesh, heat weights there, then transfer to the real mesh
proxy=civ.copy(); proxy.data=civ.data.copy(); bpy.context.scene.collection.objects.link(proxy); proxy.name='proxy'
m=proxy.modifiers.new('r','REMESH'); m.mode='VOXEL'; m.voxel_size=0.008
bpy.context.view_layer.objects.active=proxy; bpy.ops.object.modifier_apply(modifier='r')
import bmesh
bm=bmesh.new(); bm.from_mesh(proxy.data); bm.verts.ensure_lookup_table()
seen=set(); kill=[]
for v in bm.verts:
    if v.index in seen: continue
    st=[v]; comp=[v]; seen.add(v.index)
    while st:
        x=st.pop()
        for e in x.link_edges:
            o=e.other_vert(x)
            if o.index not in seen: seen.add(o.index); st.append(o); comp.append(o)
    if len(comp)<5000: kill+=comp
bmesh.ops.delete(bm,geom=kill,context='VERTS'); bm.to_mesh(proxy.data); bm.free()
print('proxy verts',len(proxy.data.vertices))
arm2=arm.copy(); arm2.data=arm.data.copy(); bpy.context.scene.collection.objects.link(arm2)
for o in (proxy,arm2): o.scale=(10,10,10)
for o in bpy.data.objects: o.select_set(o in (proxy,arm2))
bpy.context.view_layer.objects.active=arm2
bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
proxy.modifiers.clear(); proxy.parent=None; proxy.scale=(0.1,0.1,0.1)
for o in bpy.data.objects: o.select_set(o==proxy)
bpy.context.view_layer.objects.active=proxy
bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
bpy.data.objects.remove(arm2)
print('proxy groups',len(proxy.vertex_groups), sum(1 for v in proxy.data.vertices if len(v.groups)==0),'unweighted')
for vg in arm.data.bones: 
    if vg.name not in civ.vertex_groups: civ.vertex_groups.new(name=vg.name)
dt=civ.modifiers.new('dt','DATA_TRANSFER'); dt.object=proxy; dt.use_vert_data=True; dt.data_types_verts={'VGROUP_WEIGHTS'}
dt.vert_mapping='POLYINTERP_NEAREST'; dt.layers_vgroup_select_src='ALL'; dt.layers_vgroup_select_dst='NAME'
bpy.context.view_layer.objects.active=civ; bpy.ops.object.modifier_apply(modifier='dt')
bpy.data.objects.remove(proxy)
for o in bpy.data.objects: o.select_set(o==civ)
bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL',limit=4)
bpy.ops.object.vertex_group_normalize_all(lock_active=False)
civ.parent=arm; am=civ.modifiers.new('Armature','ARMATURE'); am.object=arm
print('unweighted civ',sum(1 for v in civ.data.vertices if len(v.groups)==0))
bpy.ops.wm.save_as_mainfile(filepath='/home/claude/civ/apose.blend')

# ======== rig3.py
import bpy, math, sys
from mathutils import Vector, Matrix, Quaternion
bpy.ops.wm.open_mainfile(filepath='/home/claude/civ/apose.blend')
arm=bpy.data.objects['Armature']; civ=bpy.data.objects['FrenchCivilian']
bpy.context.view_layer.objects.active=arm; bpy.ops.object.mode_set(mode='POSE')
def rot(pbn, frm, to):
    pb=arm.pose.bones[pbn]; h=pb.head.copy()
    R=frm.normalized().rotation_difference(to.normalized())
    pb.matrix = Matrix.Translation(h) @ R.to_matrix().to_4x4() @ Matrix.Translation(-h) @ pb.matrix
    bpy.context.view_layer.update()
P=arm.pose.bones
for s,sx in (('Left',1),('Right',-1)):
    a=P[f'mixamorig:{s}Arm']; f=P[f'mixamorig:{s}ForeArm']; h=P[f'mixamorig:{s}Hand']
    rot(a.name, f.head-a.head, Vector((sx,0,0)))
    rot(f.name, h.head-f.head, Vector((sx,0,0)))
    rot(h.name, h.tail-h.head, Vector((sx,0,0)))
bpy.ops.object.mode_set(mode='OBJECT')
bpy.context.view_layer.objects.active=civ
bpy.ops.object.modifier_apply(modifier='Armature')
bpy.context.view_layer.objects.active=arm; bpy.ops.object.mode_set(mode='POSE')
bpy.ops.pose.select_all(action='SELECT'); bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode='OBJECT')
am=civ.modifiers.new('Armature','ARMATURE'); am.object=arm
for b in arm.data.bones:
    if b.name.endswith('Arm') or b.name.endswith('Hand'):
        print(b.name,[round(v,3) for v in b.head_local],[round(v,3) for v in b.tail_local])
print('dims',civ.dimensions)
bpy.ops.wm.save_as_mainfile(filepath='/home/claude/civ/tpose.blend')

# ======== export.py
import bpy
# civilian
bpy.ops.wm.open_mainfile(filepath='/home/claude/civ/tpose.blend')
civ=bpy.data.objects['FrenchCivilian']
for m in list(civ.data.materials): pass
civ.data.materials.clear()
mat=bpy.data.materials.new('French_Civilian'); civ.data.materials.append(mat)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath='/home/claude/civ/out/French_Civilian.fbx', use_selection=True, axis_forward='-Z', axis_up='Y',
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True, add_leaf_bones=False,
    mesh_smooth_type='FACE', object_types={'ARMATURE','MESH'}, use_armature_deform_only=False, bake_anim=False, path_mode='STRIP')
# bottle: 0.31 m (a 75 cl bottle), origin at the base
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath='/mnt/user-data/uploads/stansGameV2/_downloads/bottle-of-champagne/source/A_BottleChampagne.fbx')
o=[x for x in bpy.data.objects if x.type=='MESH'][0]
for x in list(bpy.data.objects):
    if x!=o: bpy.data.objects.remove(x)
o.parent=None
bpy.context.view_layer.objects.active=o; o.select_set(True)
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
zs=[v.co.z for v in o.data.vertices]; xs=[v.co.x for v in o.data.vertices]; ys=[v.co.y for v in o.data.vertices]
s=0.31/(max(zs)-min(zs))
for v in o.data.vertices:
    v.co.x=(v.co.x-(max(xs)+min(xs))/2)*s; v.co.y=(v.co.y-(max(ys)+min(ys))/2)*s; v.co.z=(v.co.z-min(zs))*s
o.name='ChampagneBottle'
bpy.ops.export_scene.fbx(filepath='/home/claude/civ/out/ChampagneBottle.fbx', use_selection=True, axis_forward='-Z', axis_up='Y',
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True, mesh_smooth_type='FACE', path_mode='STRIP')
print('bottle', o.dimensions)
