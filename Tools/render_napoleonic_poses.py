"""Render Napoleonic outfit and weapon-motion previews without launching a native game.

Blender --background --python Tools/render_weapon_poses.py -- <project-root> <output-folder>
"""
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector

arguments=sys.argv[sys.argv.index('--')+1:]
root=Path(arguments[0]).resolve()
output=Path(arguments[1]).resolve()
output.mkdir(parents=True,exist_ok=True)
for name in (root/'Artifacts/NapoleonicOutfits/Poses/index.txt').read_text().splitlines():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    data=json.loads((root/'Artifacts/NapoleonicOutfits/Poses'/f'{name}.json').read_text())
    mesh=bpy.data.meshes.new(name)
    vertices=[(p['x'],-p['z'],p['y']) for p in data['vertices']]
    indices=data['triangles']
    mesh.from_pydata(vertices,[],[indices[i:i+3] for i in range(0,len(indices),3)])
    mesh.update()
    for polygon in mesh.polygons:polygon.use_smooth=True
    mesh.normals_split_custom_set_from_vertices([(p['x'],-p['z'],p['y']) for p in data['normals']])
    uv=mesh.uv_layers.new(name='UVMap')
    for loop in mesh.loops:
        point=data['uv'][loop.vertex_index]
        uv.data[loop.index].uv=(point['x'],point['y'])
    obj=bpy.data.objects.new(name,mesh)
    bpy.context.collection.objects.link(obj)
    material=bpy.data.materials.new(name)
    material.use_nodes=True
    material.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.65
    texture=material.node_tree.nodes.new('ShaderNodeTexImage')
    texture.image=bpy.data.images.load(str(root/data['texture']),check_existing=True)
    texture.image.alpha_mode='NONE'
    material.node_tree.links.new(texture.outputs['Color'],material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
    mesh.materials.append(material)
    mounted=name.startswith(('Mounted','Pistol','Lance','Slash')) or name.endswith(('cuirassier','dragoon','hussar','lancer','colonel'))
    centre=Vector((0,-.25,1.8 if name.startswith("LanceReady") or name.endswith("lancer") else 1.35 if mounted else .95))
    bpy.ops.object.camera_add(location=centre+Vector((3,-3.5,1.5)))
    camera=bpy.context.object
    camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='ORTHO'
    camera.data.ortho_scale=5.2 if name.startswith("Lance") or name.endswith("lancer") else 4.2 if mounted else 2.7
    scene=bpy.context.scene
    scene.camera=camera
    for position,energy in [((3,-4,5),600),((-3,-1,3),300),((0,4,4),450)]:
        bpy.ops.object.light_add(type='AREA',location=position)
        light=bpy.context.object
        light.data.energy=energy
        light.data.size=3
        light.rotation_euler=(centre-light.location).to_track_quat('-Z','Y').to_euler()
    scene.render.engine='CYCLES'
    scene.cycles.samples=12
    scene.render.resolution_x=480
    scene.render.resolution_y=480
    scene.render.resolution_percentage=100
    scene.world.color=(.15,.15,.15)
    scene.render.filepath=str(output/f'{name}.png')
    bpy.ops.render.render(write_still=True)
