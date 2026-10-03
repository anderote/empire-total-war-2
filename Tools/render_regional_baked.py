"""Render actual imported source components for visual inspection."""
import bpy,json,math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent.parent
out=ROOT/'Artifacts/RegionalArmies/BakedPreviews';out.mkdir(parents=True,exist_ok=True)
project=ROOT
items=[(key,key) for key in ['ming-line','ming-light','ming-sword','ming-cavalry','india-line','india-light','india-sword','india-cavalry']]
for i,(label,file) in enumerate(items):
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
 data=json.loads((project/'Artifacts/RegionalArmies/BakedPreviews'/(file+'.json')).read_text())
 mesh=bpy.data.meshes.new(label);verts=[(v['x'],-v['z'],v['y']) for v in data['vertices']];tri=data['triangles'];mesh.from_pydata(verts,[],[tri[j:j+3] for j in range(0,len(tri),3)]);mesh.update()
 for poly in mesh.polygons:poly.use_smooth=True
 mesh.normals_split_custom_set_from_vertices([(n['x'],-n['z'],n['y']) for n in data['normals']])
 obj=bpy.data.objects.new(label,mesh);bpy.context.collection.objects.link(obj);meshes=[obj]
 uv=mesh.uv_layers.new(name='UVMap')
 for loop in mesh.loops:
  v=data['uv'][loop.vertex_index];uv.data[loop.index].uv=(v['x'],v['y'])
 mat=bpy.data.materials.new(label);mat.use_nodes=True;bsdf=mat.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Roughness'].default_value=.65
 tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(project/data['texture']),check_existing=True);tex.image.alpha_mode='NONE';mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color']);mesh.materials.append(mat)
 points=[o.matrix_world@Vector(v) for o in meshes for v in o.bound_box]
 low=Vector([min(v[j] for v in points) for j in range(3)]);high=Vector([max(v[j] for v in points) for j in range(3)]);center=(low+high)/2;size=max(high-low)
 camera=bpy.data.objects.new('Camera',bpy.data.cameras.new('Camera'));bpy.context.collection.objects.link(camera);camera.location=center+Vector((3,-4,2))*size;camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=size*1.3;bpy.context.scene.camera=camera
 for pos,power in [((3,-4,5),700),((-4,-1,3),450),((0,4,4),700)]:
  light=bpy.data.objects.new('Light',bpy.data.lights.new('Light','AREA'));bpy.context.collection.objects.link(light);light.location=center+Vector(pos)*size;light.data.energy=power*size*size;light.data.shape='DISK';light.data.size=3*size;light.rotation_euler=(center-light.location).to_track_quat('-Z','Y').to_euler()
 scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.render.resolution_x=600;scene.render.resolution_y=600;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False;scene.world.color=(.12,.12,.12);scene.render.filepath=str(out/(str(i)+'.png'));bpy.ops.render.render(write_still=True)
(out/'labels.json').write_text(json.dumps([label for label,_ in items]));print('RENDERED',len(items))
