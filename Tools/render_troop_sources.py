"""Render actual imported source components for visual inspection."""
import bpy,json,math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent.parent
out=ROOT/'Artifacts/TroopSources/PilotPreviews';out.mkdir(parents=True,exist_ok=True)
items=[('Cuirassier body','napoleonic-warfare/gap_french_cuirassier.FBX'),('Hussar body','napoleonic-warfare/gap_hussar_french.FBX'),('Lancer body','laigle/polish_coat_uhlan.fbx'),('Samurai armor','shokuho/sho_nanban_1.fbx'),('Ming armor','RegionalArmies/ming-line.fbx'),('Indian tunic','RegionalArmies/india-line.fbx')]
manifest={row['path']:row['materials'] for row in json.loads((ROOT/'Assets/Art/TroopSources/import-manifest.json').read_text())['models']}
for i,(label,file) in enumerate(items):
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
 path=ROOT/'Assets/Art/TroopSources'/file;bpy.ops.import_scene.fbx(filepath=str(path))
 meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and (not file.startswith('RegionalArmies/') or '.LOD0' in o.name)]
 for o in bpy.context.scene.objects:
  if o.type=='MESH' and o not in meshes:o.hide_render=True
 for o in meshes:
  for mat in o.data.materials:
   if not mat:continue
   mat.use_nodes=True;bsdf=mat.node_tree.nodes.get('Principled BSDF')
   for link in list(mat.node_tree.links):
    if link.to_node==bsdf and link.to_socket.name in ['Base Color','Alpha']:mat.node_tree.links.remove(link)
   bsdf.inputs['Alpha'].default_value=1
   if mat.name.startswith('gap_french_epaulette_red'):bsdf.inputs['Base Color'].default_value=(.65,.035,.035,1)
   candidates=[ROOT/m['texture'] for m in manifest[str(path.relative_to(ROOT))] if m.get('texture') and (mat.name==m['name'] or mat.name.startswith(m['name']+'.'))]
   if candidates:
    node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=bpy.data.images.load(str(candidates[0]),check_existing=True);node.image.alpha_mode='NONE';mat.node_tree.links.new(node.outputs['Color'],bsdf.inputs['Base Color'])
   bsdf.inputs['Roughness'].default_value=.65
 points=[o.matrix_world@Vector(v) for o in meshes for v in o.bound_box]
 low=Vector([min(v[j] for v in points) for j in range(3)]);high=Vector([max(v[j] for v in points) for j in range(3)]);center=(low+high)/2;size=max(high-low)
 camera=bpy.data.objects.new('Camera',bpy.data.cameras.new('Camera'));bpy.context.collection.objects.link(camera);camera.location=center+Vector((3,-4,2))*size;camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=size*1.3;bpy.context.scene.camera=camera
 for pos,power in [((3,-4,5),700),((-4,-1,3),450),((0,4,4),700)]:
  light=bpy.data.objects.new('Light',bpy.data.lights.new('Light','AREA'));bpy.context.collection.objects.link(light);light.location=center+Vector(pos)*size;light.data.energy=power*size*size;light.data.shape='DISK';light.data.size=3*size;light.rotation_euler=(center-light.location).to_track_quat('-Z','Y').to_euler()
 scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.render.resolution_x=480;scene.render.resolution_y=480;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False;scene.world.color=(.12,.12,.12);scene.render.filepath=str(out/(str(i)+'.png'));bpy.ops.render.render(write_still=True)
(out/'labels.json').write_text(json.dumps([label for label,_ in items]));print('RENDERED',len(items))
