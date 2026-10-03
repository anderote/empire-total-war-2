"""Blender-only visual review of the original rifle LOD exports; never launches a game.

Blender --background --python Tools/render_weapon_budget.py -- <project-root> <output.png>
"""
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

arguments = sys.argv[sys.argv.index('--') + 1:]
root = Path(arguments[0]).resolve()
output = Path(arguments[1]).resolve()
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
material = bpy.data.materials.new('Wood and steel')
material.use_nodes = True
principled = material.node_tree.nodes.get('Principled BSDF')
principled.inputs['Roughness'].default_value = .48
image = bpy.data.images.load(str(root / 'Assets/Art/CartridgeWeapons/WoodSteel.png'))
texture = material.node_tree.nodes.new('ShaderNodeTexImage')
texture.image = image
material.node_tree.links.new(texture.outputs['Color'], principled.inputs['Base Color'])
label_material = bpy.data.materials.new('Labels')
label_material.diffuse_color = (.08, .10, .13, 1)
label_material.use_nodes = True
label_material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value = (.08, .10, .13, 1)

for family, x in [('SingleShot', -.9), ('Magazine', .9)]:
    for level, y in enumerate([.8, 0, -.8]):
        path = root / 'Artifacts/WeaponBudget' / f'{family}_L{level}.obj'
        bpy.ops.wm.obj_import(filepath=str(path), forward_axis='Z', up_axis='Y')
        obj = bpy.context.selected_objects[0]
        obj.rotation_euler = (0, math.pi / 2, 0)
        obj.scale = (.0254,) * 3
        obj.location = (x + 11 * .0254, y, 0)
        obj.data.materials.clear()
        obj.data.materials.append(material)
        triangles = len(obj.data.polygons)
        bpy.ops.object.text_add(location=(x-.74, y+.26, .02))
        label = bpy.context.object
        label.data.body = f'{"Single-shot" if family == "SingleShot" else "Magazine"} / L{level} / {triangles} triangles'
        label.data.size = .078
        label.data.materials.append(label_material)

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 32
scene.world.color = (.65, .65, .65)
for position, energy, size in [((0, 2, 4), 450, 5), ((-3, -1, 3), 180, 3)]:
    bpy.ops.object.light_add(type='AREA', location=position)
    light = bpy.context.object
    light.data.energy = energy
    light.data.shape = 'DISK'
    light.data.size = size
    light.rotation_euler = (Vector((0,0,0)) - light.location).to_track_quat('-Z', 'Y').to_euler()
bpy.ops.mesh.primitive_plane_add(size=200, location=(0,0,-.2))
backdrop = bpy.context.object
backdrop_material = bpy.data.materials.new('Backdrop')
backdrop_material.diffuse_color = (.82, .84, .86, 1)
backdrop_material.use_nodes = True
backdrop_material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value = (.82, .84, .86, 1)
backdrop.data.materials.append(backdrop_material)
bpy.ops.object.camera_add(location=(0,.1,6))
camera = bpy.context.object
camera.rotation_euler = (Vector((0,.1,0))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 3.6
scene.camera = camera
scene.render.resolution_x = 1600
scene.render.resolution_y = 1120
scene.render.resolution_percentage = 100
scene.view_settings.view_transform = 'Standard'
output.parent.mkdir(parents=True,exist_ok=True)
scene.render.filepath = str(output)
bpy.ops.wm.save_as_mainfile(filepath=str(output.with_suffix('.blend')))
bpy.ops.render.render(write_still=True)
