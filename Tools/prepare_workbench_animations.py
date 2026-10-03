"""Blender: preserve original Warband animations on a source-rig review mannequin.
These are candidates, not retargeted production animations. Action ranges/durations
come from each mod's actions.txt; translation is kept for checking root drift.
"""
import bpy,json,math,bisect
from pathlib import Path
from mathutils import Matrix,Vector
ROOT=Path(__file__).resolve().parent.parent
OUT=ROOT/'AssetSources/ModSamples/AnimationFBX';OUT.mkdir(parents=True,exist_ok=True)
C=Matrix(((1,0,0,0),(0,0,-1,0),(0,1,0,0),(0,0,0,1)))
bones=next(s['bones'] for s in json.loads((ROOT/'AssetSources/TroopPacks/Converted/laigle/reference-export/data.json').read_text())['skeletons'] if s['name']=='skel_human')
specs={
 'katana_slashright_a':[(60,70,.46),(70,98,.39)],
 'katana_slashleft_current':[(0,7,.45),(7,26,.50)],
 'katana_overswing_version3':[(4,10,.35),(10,21,.43),(21,24,.25)],
 'parry_right_twohands_light':[(1,4,.23)],
 'line_musket_reload_ani':[(0,599,7)],
 'rifle_reload':[(0,449,7)],
 'musket_stab_down':[(0,0,.35),(0,25,.6),(25,44,.3)],
 'pike_brace':[(1,13,1.3)]}
receipt=[]
for file in (ROOT/'AssetSources/ModSamples/Animations').glob('*/data.json'):
 for anim in json.loads(file.read_text())['animations']:
  name=anim['name'];pieces=specs[name];duration=sum(p[2] for p in pieces)
  bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
  armdata=bpy.data.armatures.new('Warband20');arm=bpy.data.objects.new('SourceRig',armdata);bpy.context.collection.objects.link(arm)
  bpy.context.view_layer.objects.active=arm;arm.select_set(True);bpy.ops.object.mode_set(mode='EDIT');rest=[]
  for i,b in enumerate(bones):
   local=Matrix(((b['x'][0],b['y'][0],b['z'][0],b['translation'][0]),(b['x'][1],b['y'][1],b['z'][1],b['translation'][1]),(b['x'][2],b['y'][2],b['z'][2],b['translation'][2]),(0,0,0,1)))
   world=local if b['parent']<0 else rest[b['parent']]@local;rest.append(world)
   eb=armdata.edit_bones.new(b['name']);eb.matrix=C@world;eb.length=.1
   if b['parent']>=0:eb.parent=armdata.edit_bones[bones[b['parent']]['name']]
  bpy.ops.object.mode_set(mode='OBJECT')
  mat=bpy.data.materials.new('SourceMotionMannequin');mat.diffuse_color=(.45,.62,.75,1)
  for i,b in enumerate(bones):
   if b['name'].startswith('item'):continue
   a=(C@rest[i]).translation
   children=[j for j,c in enumerate(bones) if c['parent']==i and not c['name'].startswith('item')]
   if children:
    end=(C@rest[children[0]]).translation;vec=end-a
    bpy.ops.mesh.primitive_cylinder_add(vertices=8,radius=.09 if b['name'] in ('abdomen','spine','thorax') else .045,depth=max(.06,vec.length),location=(a+end)*.5)
    obj=bpy.context.object;obj.rotation_mode='QUATERNION';obj.rotation_quaternion=Vector((0,0,1)).rotation_difference(vec.normalized())
   else:
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=6,radius=.11 if b['name']=='head' else .06,location=a)
    obj=bpy.context.object
   obj.name='Mannequin_'+b['name'];bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
   obj.data.materials.append(mat);vg=obj.vertex_groups.new(name=b['name']);vg.add(list(range(len(obj.data.vertices))),1,'REPLACE')
   mod=obj.modifiers.new('Source skin','ARMATURE');mod.object=arm;obj.parent=arm
  frames=anim['frames'];indices=[f['index'] for f in frames]
  for pb in arm.pose.bones:pb.rotation_mode='QUATERNION'
  bpy.context.scene.render.fps=30;bpy.context.scene.frame_start=1;bpy.context.scene.frame_end=1+round(duration*30)
  roots=[]
  for fi in range(1,bpy.context.scene.frame_end+1):
   time=min(duration,(fi-1)/30);elapsed=0
   for start,end,seconds in pieces:
    if time<=elapsed+seconds+1e-6:source=start+(end-start)*min(1,(time-elapsed)/seconds);break
    elapsed+=seconds
   left=max(0,bisect.bisect_right(indices,source)-1);right=min(len(frames)-1,left+1)
   f0=frames[left];f1=frames[right];blend=0 if indices[right]==indices[left] else (source-indices[left])/(indices[right]-indices[left])
   translation=Vector(f0['translation']).lerp(Vector(f1['translation']),blend);worlds=[]
   for bi,b in enumerate(bones):
    q0=Matrix(f0['rotations'][bi]).to_quaternion();q1=Matrix(f1['rotations'][bi]).to_quaternion();rot=q0.slerp(q1,blend).to_matrix().to_4x4()
    parent=worlds[b['parent']] if b['parent']>=0 else Matrix.Translation(translation)
    world=parent@Matrix.Translation(Vector(b['translation']))@rot;worlds.append(world)
    pb=arm.pose.bones[b['name']]
    bone_rest=C@rest[bi]
    if b['parent']>=0:
     local_rest=(C@rest[b['parent']]).inverted()@bone_rest
     pb.matrix_basis=local_rest.inverted()@(C@worlds[b['parent']]).inverted()@(C@world)
    else:pb.matrix_basis=bone_rest.inverted()@(C@world)
    pb.keyframe_insert('location',frame=fi);pb.keyframe_insert('rotation_quaternion',frame=fi);pb.keyframe_insert('scale',frame=fi)
   roots.append(list(translation));bpy.context.view_layer.update()
  action=arm.animation_data.action;action.name=name
  bpy.ops.object.select_all(action='SELECT');path=OUT/('source-'+name+'.fbx')
  bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',path_mode='RELATIVE')
  receipt.append(dict(name=name,fbx=str(path.relative_to(ROOT)),source=str(file.relative_to(ROOT)),duration=duration,segments=pieces,bones=20,frames=len(roots),root_span=[max(v[i] for v in roots)-min(v[i] for v in roots) for i in range(3)],status='source rig candidate; game retarget and weapon contact validation pending'))
(ROOT/'AssetSources/ModSamples/animation-selection.json').write_text(json.dumps(receipt,indent=2));print('SOURCE_ANIMATION_CANDIDATES',len(receipt))
