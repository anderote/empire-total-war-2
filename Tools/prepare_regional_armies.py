"""Blender: fit six selected regional outfits to the existing animated human rig."""
import bpy,json
from pathlib import Path
from mathutils import Vector,Quaternion
ROOT=Path(__file__).resolve().parent.parent
archive=ROOT/'AssetSources/TroopPacks/ConvertedFBX/16th-century'
out=ROOT/'Assets/Art/TroopSources/RegionalArmies';out.mkdir(parents=True,exist_ok=True)
recipes={'ming-line':('DaMing_armor_01','DaMing_helmet_01'),'ming-light':('BuKuai','MWuShamao01'),'ming-sword':('DaMing_armor_02','DaMing_helmet_02'),'india-line':('armor_vijaynagri_tunici','beige_turban'),'india-light':('armor_vijaynagri_tunicii','beige_turban'),'india-sword':('armor_vijaynagri_maili','helm_vijaynagri_royal')}
bone_map={'abdomen':'pelvis','spine':'spine','thorax':'spine2','head':'head','thigh.L':'l_thigh','calf.L':'l_calf','foot.L':'l_foot','thigh.R':'r_thigh','calf.R':'r_calf','foot.R':'r_foot','shoulder.L':'l_clavicle','upperarm.L':'l_upperarm_twist','forearm.L':'l_foretwist','hand.L':'l_hand','item.L':'l_hand','shoulder.R':'r_clavicle','upperarm.R':'r_upperarm_twist','forearm.R':'r_foretwist','hand.R':'r_hand','item.R':'r_hand'}
links={'abdomen':'spine','spine':'thorax','thorax':'head','thigh.L':'calf.L','calf.L':'foot.L','thigh.R':'calf.R','calf.R':'foot.R','shoulder.L':'upperarm.L','upperarm.L':'forearm.L','forearm.L':'hand.L','shoulder.R':'upperarm.R','upperarm.R':'forearm.R','forearm.R':'hand.R'}
receipt=[]
for key,parts in recipes.items():
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
 bpy.ops.import_scene.fbx(filepath=str(ROOT/'AssetSources/Bannerlord/AssetSources/copyright/real_uniforms/gap_french_cuirassier.FBX'))
 target=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
 for ob in list(bpy.context.scene.objects):
  if ob!=target:bpy.data.objects.remove(ob,do_unlink=True)
 material_maps=[];mesh_receipt=[]
 for part_index,part in enumerate(parts):
  before=set(bpy.context.scene.objects);bpy.ops.import_scene.fbx(filepath=str(archive/(part+'.fbx')));imported=set(bpy.context.scene.objects)-before
  source_arms=[o for o in imported if o.type=='ARMATURE']
  for obj in [o for o in imported if o.type=='MESH']:
   arm=obj.find_armature();source_names={g.index:g.name for g in obj.vertex_groups};mapped_weights=[];mapped_normals=[]
   source_normals=[Vector() for _ in obj.data.vertices]
   for loop in obj.data.loops:source_normals[loop.vertex_index]+=obj.data.corner_normals[loop.index].vector
   for vertex in obj.data.vertices:
    p=obj.matrix_world@vertex.co;normal=(obj.matrix_world.to_3x3().inverted().transposed()@source_normals[vertex.index]).normalized();weights={}
    if arm and vertex.groups:
     result=Vector((0,0,0));normal_result=Vector();total=0
     for g in vertex.groups:
      if g.weight<=0:continue
      source_name=source_names[g.group];name=bone_map[source_name]
      source_bind=arm.matrix_world@arm.data.bones[source_name].matrix_local
      dest_bind=target.matrix_world@target.data.bones[name].matrix_local
      source_pos=source_bind.translation;dest_pos=dest_bind.translation
      rotation=Quaternion()
      if source_name in links:
       nxt=links[source_name];a=(arm.matrix_world@arm.data.bones[nxt].head_local)-source_pos;b=(target.matrix_world@target.data.bones[bone_map[nxt]].head_local)-dest_pos
       if a.length>1e-6 and b.length>1e-6:rotation=a.rotation_difference(b)
      result+=(dest_pos+rotation@(p-source_pos))*g.weight;normal_result+=(rotation@normal)*g.weight;total+=g.weight;weights[name]=weights.get(name,0)+g.weight
     if total: p=result/total;normal=normal_result.normalized();weights={n:w/total for n,w in weights.items()}
    else:
     # Rigid Warband headgear uses Z-up attachment coordinates; skinned clothing uses Y-up.
     p=Vector((p.x,p.z,-p.y))+(target.matrix_world@target.data.bones['head'].head_local);normal=Vector((normal.x,normal.z,-normal.y));weights={'head':1.}
    vertex.co=target.matrix_world.inverted()@p;mapped_weights.append(weights);mapped_normals.append((target.matrix_world.to_3x3().transposed()@normal).normalized())
   obj.data.normals_split_custom_set_from_vertices(mapped_normals)
   obj.matrix_world=target.matrix_world.copy()
   obj.vertex_groups.clear();groups={name:obj.vertex_groups.new(name=name) for name in target.data.bones.keys()}
   for i,weights in enumerate(mapped_weights):
    for name,w in weights.items():groups[name].add([i],w,'REPLACE')
   obj.modifiers.clear();mod=obj.modifiers.new('Common rig','ARMATURE');mod.object=target;obj.parent=target
   for mat in obj.data.materials:
    if not mat:continue
    for node in mat.node_tree.nodes:
     if node.type=='TEX_IMAGE' and node.image:
      image=node.image;src=Path(bpy.path.abspath(image.filepath));dest=out/'Textures'/src.name;dest.parent.mkdir(exist_ok=True)
      import shutil
      if src.exists():shutil.copy2(src,dest);image.filepath=str(dest);material_maps.append({'name':mat.name,'texture':str(dest.relative_to(ROOT))})
   for level,budget in [(0,1800 if part_index==0 else 400),(1,650 if part_index==0 else 120)]:
    duplicate=obj.copy();duplicate.data=obj.data.copy();bpy.context.collection.objects.link(duplicate);duplicate.name='Regional_'+('Body' if part_index==0 else 'Headgear')+'.LOD'+str(level)
    bpy.context.view_layer.objects.active=duplicate;duplicate.select_set(True)
    decimate=duplicate.modifiers.new('Crowd detail','DECIMATE');bpy.ops.object.modifier_move_up(modifier=decimate.name);decimate.ratio=min(1,budget/max(1,len(duplicate.data.polygons)));bpy.ops.object.modifier_apply(modifier=decimate.name)
    mesh_receipt.append({'name':duplicate.name,'vertices':len(duplicate.data.vertices),'faces':len(duplicate.data.polygons)})
   bpy.data.objects.remove(obj,do_unlink=True)
  for ob in source_arms:bpy.data.objects.remove(ob,do_unlink=True)
 for o in bpy.context.scene.objects:o.select_set(True)
 bpy.context.view_layer.objects.active=target;bpy.ops.export_scene.fbx(filepath=str(out/(key+'.fbx')),use_selection=True,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='RELATIVE')
 receipt.append({'name':key,'source_parts':parts,'materials':material_maps,'meshes':mesh_receipt})
(out/'regional-receipt.json').write_text(json.dumps(receipt,indent=2));print('REGIONAL_OUTFITS',len(receipt))
