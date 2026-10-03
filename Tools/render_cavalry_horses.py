"""Render the actual authored mount meshes and gait poses for visual review."""
import bpy
import json
import sys
from pathlib import Path
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import author_cavalry_horses as horse


def scene_setup(center, scale, path, width=1200, height=800):
    scene = bpy.context.scene
    world = bpy.data.worlds.new('Review world')
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (.20, .23, .27, 1)
    world.node_tree.nodes['Background'].inputs[1].default_value = .5
    scene.world = world
    camera = bpy.data.objects.new('Review camera', bpy.data.cameras.new('Review camera'))
    scene.collection.objects.link(camera)
    camera.location = center + Vector((7, -1.7, 2.6))*scale
    camera.rotation_euler = (center-camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = scale
    scene.camera = camera
    for position, energy, size in [((4,-4,6), 1100, 5), ((-4,-1,4), 650, 4), ((1,5,5), 1000, 4)]:
        light = bpy.data.objects.new('Softbox', bpy.data.lights.new('Softbox', 'AREA'))
        scene.collection.objects.link(light)
        light.location = center+Vector(position)
        light.data.energy = energy
        light.data.size = size
        light.rotation_euler = (center-light.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0,0,0))
    floor = bpy.context.object
    material = bpy.data.materials.new('Neutral floor')
    material.diffuse_color = (.11,.13,.16,1)
    floor.data.materials.append(material)
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 16
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def snapshot(meshes, offset):
    graph = bpy.context.evaluated_depsgraph_get()
    result = []
    for source in meshes:
        mesh = bpy.data.meshes.new_from_object(source.evaluated_get(graph))
        mesh.transform(source.matrix_world)
        obj = bpy.data.objects.new('Pose '+source.name,mesh)
        bpy.context.collection.objects.link(obj)
        obj.location = offset
        result.append(obj)
    return result


if __name__ == '__main__':
    args = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    selected = args[0] if args else 'HussarBay'
    spec = next(s for s in horse.SPECS if s[0] == selected)
    arm, meshes, _ = horse.build_mount(spec)
    gait = args[1] if len(args)>1 else 'GallopLeft'
    if gait == 'Idle':
        horse.sample(arm,gait,0)
        scene_setup(Vector((0,-.15,1.05)),3.25,horse.REVIEW/(selected+'-idle.png'))
    else:
        for i, phase in enumerate([0,.25,.5,.75]):
            horse.sample(arm,gait,phase)
            snapshot(meshes,Vector((0,(i-1.5)*3.1,0)))
        for obj in meshes+[arm]:
            bpy.data.objects.remove(obj,do_unlink=True)
        scene_setup(Vector((0,0,.95)),13,horse.REVIEW/(selected+'-'+gait+'.png'),1800,460)
