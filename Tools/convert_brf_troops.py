"""Blender batch conversion of selected BRF JSON meshes; retains source bone weights."""
import bpy,json,sys,math,os,re
from pathlib import Path
from mathutils import Matrix,Vector
ROOT=Path(__file__).resolve().parent.parent
OUT=ROOT/'AssetSources/TroopPacks/ConvertedFBX'
REF=json.loads((ROOT/'AssetSources/TroopPacks/Converted/laigle/reference-export/data.json').read_text())
C=Matrix(((1,0,0,0),(0,0,-1,0),(0,1,0,0),(0,0,0,1)))
receipt=[]
for pack in ['laigle','gekokujo','india1782','16th-century']:
 source=ROOT/'AssetSources/TroopPacks/Extracted'/('Gekokujo' if pack=='gekokujo' else pack)
 texture_lookup={p.name.lower():p for p in source.rglob('*') if p.suffix.lower() in ['.dds','.tga','.png']}
 materials={}
 for p in (ROOT/'AssetSources/TroopPacks/Converted'/pack).glob('*/data.json'):
  if not p.stat().st_size:continue
  for m in json.loads(p.read_text()).get('materials',[]): materials[m['name']]=m
 for file in sorted((ROOT/'AssetSources/TroopPacks/Converted'/pack).glob('*-export/data.json')):
  if file.parent.name=='reference-export':continue
  data=json.loads(file.read_text())
  for model in data['meshes']:
   if not model.get('vertices'):continue
   if os.environ.get('BRF_CONVERT_FILTER') and not re.search(os.environ['BRF_CONVERT_FILTER'],model['name']):continue
   bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
   name=model['name']; rows=model['vertices']; dest=OUT/pack; dest.mkdir(parents=True,exist_ok=True)
   mesh=bpy.data.meshes.new(name)
   # BRF stores clockwise faces and explicit outward normals; Blender uses counterclockwise.
   triangles=[]
   for face in model['triangles']:
    a,b,c=[Vector(rows[i]['position']) for i in face];n=sum((Vector(rows[i]['normal']) for i in face),Vector())
    triangles.append(list(reversed(face)) if (b-a).cross(c-a).dot(n)<0 else face)
   mesh.from_pydata([C.to_3x3()@Vector(v['position']) for v in rows],[],triangles);mesh.update()
   for poly in mesh.polygons:poly.use_smooth=True
   mesh.normals_split_custom_set_from_vertices([(C.to_3x3()@Vector(v['normal'])).normalized() for v in rows])
   obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
   uv=mesh.uv_layers.new(name='UVMap')
   for loop in mesh.loops:
    u,v=rows[loop.vertex_index]['uv']; uv.data[loop.index].uv=(u,1-v)
   matdef=materials.get(model['material'],{});mat=bpy.data.materials.new(model['material']);mat.use_nodes=True
   diffuse=matdef.get('diffuseA',model['material']); tex=texture_lookup.get(diffuse.lower()) or texture_lookup.get((diffuse+'.dds').lower())
   if tex:
    png=dest/'Textures'/(Path(diffuse).stem+'.png')
    image=bpy.data.images.load(str(png),check_existing=True)
    node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=image;mat.node_tree.links.new(node.outputs['Color'],mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
   obj.data.materials.append(mat)
   bonecount=0
   if any(v.get('weights') and sum(v['weights'])>0 for v in rows):
    horse='horse' in name.lower() or name in ['burmaelephant']
    bones=next(s['bones'] for s in REF['skeletons'] if s['name']==('skel_horse' if horse else 'skel_human'))
    maxindex=max((i for v in rows for i,w in zip(v.get('bones',[]),v.get('weights',[])) if w>0),default=-1)
    if maxindex>=len(bones):raise ValueError(f'{name}: bone {maxindex} beyond skeleton')
    armdata=bpy.data.armatures.new('Warband_'+('Horse' if horse else 'Human'));arm=bpy.data.objects.new('Armature',armdata);bpy.context.collection.objects.link(arm)
    bpy.context.view_layer.objects.active=arm;arm.select_set(True);bpy.ops.object.mode_set(mode='EDIT');globals=[]
    for i,b in enumerate(bones):
     local=Matrix(((b['x'][0],b['y'][0],b['z'][0],b['translation'][0]),(b['x'][1],b['y'][1],b['z'][1],b['translation'][1]),(b['x'][2],b['y'][2],b['z'][2],b['translation'][2]),(0,0,0,1)))
     world=local if b['parent']<0 else globals[b['parent']]@local;globals.append(world)
     eb=armdata.edit_bones.new(b['name']);eb.matrix=C@world;eb.length=.12
     if b['parent']>=0:eb.parent=armdata.edit_bones[bones[b['parent']]['name']]
    bpy.ops.object.mode_set(mode='OBJECT')
    groups=[obj.vertex_groups.new(name=b['name']) for b in bones]
    for i,v in enumerate(rows):
     for index,weight in zip(v.get('bones',[]),v.get('weights',[])):
      if weight>0:groups[index].add([i],weight,'REPLACE')
    mod=obj.modifiers.new('Source skinning','ARMATURE');mod.object=arm;obj.parent=arm;bonecount=len(bones)
   for o in bpy.context.scene.objects:o.select_set(True)
   bpy.context.view_layer.objects.active=obj
   path=dest/(name+'.fbx');bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='RELATIVE')
   receipt.append(dict(pack=pack,name=name,fbx=str(path.relative_to(ROOT)),vertices=len(rows),triangles=len(model['triangles']),bones=bonecount,texture=str(tex.relative_to(ROOT)) if tex else None))
(OUT/('regional-conversion-receipt.json' if os.environ.get('BRF_CONVERT_FILTER') else 'conversion-receipt.json')).write_text(json.dumps(receipt,indent=2));print('CONVERTED',len(receipt))
