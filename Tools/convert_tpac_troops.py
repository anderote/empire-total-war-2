"""Convert exported Bannerlord mesh JSON into FBX using matching source-pack rigs."""
import bpy,json,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent.parent
pack=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'shokuho'
source=ROOT/'AssetSources/TroopPacks/Converted'/pack
out=ROOT/'AssetSources/TroopPacks/ConvertedFBX'/pack;out.mkdir(parents=True,exist_ok=True)
rig_files={'human':ROOT/'AssetSources/Bannerlord/AssetSources/copyright/real_uniforms/gap_french_cuirassier.FBX','horse':ROOT/'AssetSources/Bannerlord/AssetSources/copyright/real_saddles/gap_horse_saddles.FBX'}
receipt=[]
for record in json.loads((source/'export-receipt.json').read_text()):
 if not record['ok']:continue
 data=json.loads((source/(record['name']+'.json')).read_text())
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
 kind='horse' if 'horse' in data['name'].lower() else 'human'
 bpy.ops.import_scene.fbx(filepath=str(rig_files[kind]));arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
 for o in list(bpy.context.scene.objects):
  if o!=arm:bpy.data.objects.remove(o,do_unlink=True)
 names=[b.name for b in arm.data.bones];count=0
 for part in data['meshes']:
  rows=part['vertices'];mesh=bpy.data.meshes.new(part['name']);mesh.from_pydata([v['position'] for v in rows],[],part['triangles']);mesh.update()
  obj=bpy.data.objects.new(part['name'],mesh);bpy.context.collection.objects.link(obj)
  uv=mesh.uv_layers.new(name='UVMap')
  for loop in mesh.loops:
   u,v=rows[loop.vertex_index]['uv'];uv.data[loop.index].uv=(u,1-v)
  mat=bpy.data.materials.new(part['material'] or 'Default');mat.use_nodes=True
  if part['diffuse']:
   png=out/'Textures'/(Path(part['diffuse']).stem+'.png');image=bpy.data.images.load(str(png),check_existing=True);node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=image;mat.node_tree.links.new(node.outputs['Color'],mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
  obj.data.materials.append(mat)
  if any(v['weights'] for v in rows):
   groups=[obj.vertex_groups.new(name=n) for n in names]
   for i,v in enumerate(rows):
    for index,weight in zip(v['bones'],v['weights']):
     if weight>0:
      if index>=len(groups):raise ValueError(f'{part["name"]}: bone index {index} exceeds rig {len(groups)}')
      groups[index].add([i],weight,'REPLACE')
   mod=obj.modifiers.new('Source skinning','ARMATURE');mod.object=arm;obj.parent=arm
  count+=len(rows)
 for o in bpy.context.scene.objects:o.select_set(True)
 bpy.context.view_layer.objects.active=arm
 path=out/(data['name']+'.fbx');bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='RELATIVE')
 receipt.append(dict(name=data['name'],vertices=count,rig_source=str(rig_files[kind].relative_to(ROOT)),bones=len(names),fbx=str(path.relative_to(ROOT))))
(out/'conversion-receipt.json').write_text(json.dumps(receipt,indent=2));print('CONVERTED',len(receipt))
