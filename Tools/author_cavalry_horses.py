"""Blender batch: period cavalry mounts with original, ground-contact gait clips.

Run with Blender --background --factory-startup --python Tools/author_cavalry_horses.py.
Sources stay untouched. Outputs are staged outside Assets; HorseGaitBuild imports them.
The old BRF conversion has reflected neck/limb rest positions. Rebuild the bind rig
against the existing mesh and weights before authoring any motion.
"""
import bpy
import json
import math
import shutil
from pathlib import Path
from mathutils import Matrix, Vector, Quaternion

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'AssetSources/CavalryHorses'
REVIEW = ROOT / 'Artifacts/CavalryAssetResearch'
OUT.mkdir(parents=True, exist_ok=True)
REVIEW.mkdir(parents=True, exist_ok=True)
C = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
BONES = next(s['bones'] for s in json.loads((ROOT / 'AssetSources/TroopPacks/Converted/laigle/reference-export/data.json').read_text())['skeletons'] if s['name'] == 'skel_horse')
SPECS = [
    ('HussarBay', 'laigle', 'napo_horses-export', ['horse_french_hussar', 'horse_french_hussar.2']),
    ('CuirassierChestnut', 'laigle', 'napo_horses-export', ['horse_french_cuirassier', 'horse_french_cuirassier.2']),
    ('ArabianA', 'india1782', 'arabian_horses-export', ['arabian_horse_a']),
    ('ArabianB', 'india1782', 'arabian_horses-export', ['arabian_horse_b']),
]
# Contact phases are [fore left, fore right, hind left, hind right].
# Left lead: right hind, left hind, right fore, left fore. Canter pairs LH/RF.
# Sources: horses.extension.org/horse-natural-gaits/ and /horse-gallop/.
GAITS = {
    'Idle': dict(duration=3.0, stride=0, duty=1, lift=0, contacts=[0]*4, bob=.006),
    'Walk': dict(duration=1.12, stride=1.5, duty=.64, lift=.13, contacts=[.25, .75, 0, .5], bob=.010),
    'Trot': dict(duration=.72, stride=2.6, duty=.38, lift=.23, contacts=[0, .5, .5, 0], bob=.025),
    'CanterLeft': dict(duration=.66, stride=3.2, duty=.28, lift=.28, contacts=[.52, .26, .26, 0], bob=.035),
    'CanterRight': dict(duration=.66, stride=3.2, duty=.28, lift=.28, contacts=[.26, .52, 0, .26], bob=.035),
    'GallopLeft': dict(duration=.56, stride=4.0, duty=.23, lift=.34, contacts=[.54, .38, .16, 0], bob=.045),
    'GallopRight': dict(duration=.56, stride=4.0, duty=.23, lift=.34, contacts=[.38, .54, 0, .16], bob=.045),
}
CHAINS = [
    ['uarm.L', 'forearm.L', 'f_foot.L', 'f_hoof.L', 'clavicle.L'],
    ['uarm.R', 'forearm.R', 'f_foot.R', 'f_hoof.R', 'clavicle.R'],
    ['thigh.L', 'calf.L', 'h_foot.L', 'h_hoof.L'],
    ['thigh.R', 'calf.R', 'h_foot.R', 'h_hoof.R'],
]


def rest_heads():
    worlds = []
    heads = {}
    for b in BONES:
        local = Matrix(((b['x'][0], b['y'][0], b['z'][0], b['translation'][0]),
                        (b['x'][1], b['y'][1], b['z'][1], b['translation'][1]),
                        (b['x'][2], b['y'][2], b['z'][2], b['translation'][2]), (0, 0, 0, 1)))
        world = local if b['parent'] < 0 else worlds[b['parent']] @ local
        worlds.append(world)
        heads[b['name']] = (C @ world).translation.copy()
    # Construct Blender bones explicitly from evaluated BRF joint positions.
    # The earlier FBX conversion's bind joints do not match the leg weights.
    return heads


HEADS = rest_heads()


def build_mount(spec):
    name, pack, folder, parts = spec
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    data = json.loads((ROOT / f'AssetSources/TroopPacks/Converted/{pack}/{folder}/data.json').read_text())
    arm_data = bpy.data.armatures.new('CavalryHorseRig')
    arm = bpy.data.objects.new('HorseRig', arm_data)
    bpy.context.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    for b in BONES:
        bone = arm_data.edit_bones.new(b['name'])
        bone.head = HEADS[b['name']]
        children = [c for c in BONES if c['parent'] == BONES.index(b)]
        bone.tail = HEADS[children[0]['name']] if children else bone.head + Vector((0, -.10, -.05))
        if b['parent'] >= 0:
            bone.parent = arm_data.edit_bones[BONES[b['parent']]['name']]
        bone.use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')
    meshes, materials = [], []
    for part in parts:
        source = next(m for m in data['meshes'] if m['name'] == part)
        rows = source['vertices']
        triangles = []
        for face in source['triangles']:
            a, b, c = [Vector(rows[i]['position']) for i in face]
            normal = sum((Vector(rows[i]['normal']) for i in face), Vector())
            triangles.append(list(reversed(face)) if (b-a).cross(c-a).dot(normal) < 0 else face)
        mesh = bpy.data.meshes.new(part)
        mesh.from_pydata([C.to_3x3() @ Vector(v['position']) for v in rows], [], triangles)
        mesh.update()
        for poly in mesh.polygons:
            poly.use_smooth = True
        mesh.normals_split_custom_set_from_vertices([(C.to_3x3() @ Vector(v['normal'])).normalized() for v in rows])
        uv = mesh.uv_layers.new(name='UVMap')
        for loop in mesh.loops:
            u, v = rows[loop.vertex_index]['uv']
            uv.data[loop.index].uv = (u, 1-v)
        obj = bpy.data.objects.new(part, mesh)
        bpy.context.collection.objects.link(obj)
        groups = [obj.vertex_groups.new(name=b['name']) for b in BONES]
        for i, v in enumerate(rows):
            for bi, weight in zip(v['bones'], v['weights']):
                if weight > 0:
                    groups[bi].add([i], weight, 'REPLACE')
        modifier = obj.modifiers.new('Horse skin', 'ARMATURE')
        modifier.object = arm
        obj.parent = arm
        mat_name = source['material']
        tex_path = ROOT / f'AssetSources/TroopPacks/ConvertedFBX/{pack}/Textures/{mat_name}.png'
        if not tex_path.exists():
            raise RuntimeError(f'Missing coat/tack texture: {tex_path}')
        (OUT / 'Textures').mkdir(exist_ok=True)
        shutil.copy2(tex_path, OUT / 'Textures' / tex_path.name)
        mat = bpy.data.materials.new(mat_name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get('Principled BSDF')
        tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
        tex.image = bpy.data.images.load(str(OUT / 'Textures' / tex_path.name), check_existing=True)
        tex.image.alpha_mode = 'NONE'
        mat.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
        bsdf.inputs['Roughness'].default_value = .72
        mesh.materials.append(mat)
        meshes.append(obj)
        materials.append(dict(name=mat_name, texture='Textures/'+tex_path.name))
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    arm.location.z = -min(v.co.z for obj in meshes for v in obj.data.vertices)
    return arm, meshes, materials


def solve_chain(points, target, lengths):
    """Deterministic FABRIK seeded from anatomical joints; no frame-history drift."""
    root = points[0].copy()
    if (target-root).length >= sum(lengths)-.00001:
        direction = (target-root).normalized()
        for i, length in enumerate(lengths):
            points[i+1] = points[i] + direction*length
        return points
    for _ in range(48):
        points[-1] = target.copy()
        for i in range(len(points)-2, -1, -1):
            points[i] = points[i+1] + (points[i]-points[i+1]).normalized()*lengths[i]
        points[0] = root.copy()
        for i, length in enumerate(lengths):
            points[i+1] = points[i] + (points[i+1]-points[i]).normalized()*length
        if (points[-1]-target).length < .00005:
            break
    return points


def around(pivot, rotation):
    return Matrix.Translation(pivot) @ rotation.to_matrix().to_4x4() @ Matrix.Translation(-pivot)


def sample(arm, gait, phase):
    phase %= 1
    cfg = GAITS[gait]
    cycle = phase*math.tau
    moving = cfg['stride'] > 0
    # Leave flexion room in the standing legs, including at maximum reach.
    bob = cfg['bob'] * math.sin(cycle*(2 if gait in ('Walk', 'Trot') else 1))
    pitch = math.radians(1.6 if gait.startswith('Gallop') else .8 if gait.startswith('Canter') else .25)*math.sin(cycle)
    body = Matrix.Translation((0, 0, -.09+bob)) @ around(HEADS['abdomen'], Quaternion((1, 0, 0), pitch))
    desired = {b.name: body @ b.matrix_local for b in arm.data.bones}
    nod = math.radians(2.2 if moving else .7)*math.sin(cycle + .5)
    neck_matrix = body @ around(HEADS['neck'], Quaternion((1, 0, 0), nod))
    for name in ['neck', 'neck2', 'neck3', 'head']:
        desired[name] = neck_matrix @ arm.data.bones[name].matrix_local
    tail_matrix = body @ around(HEADS['tail1'], Quaternion((0, 1, 0), math.radians(3)*math.sin(cycle)))
    for name in ['tail1', 'tail2']:
        desired[name] = tail_matrix @ arm.data.bones[name].matrix_local
    errors, contacts = [], []
    for leg, chain in enumerate(CHAINS):
        p = (phase-cfg['contacts'][leg]) % 1
        stance = not moving or p < cfg['duty']
        reach = cfg['stride']*cfg['duty']
        target = HEADS[chain[-1]].copy()
        if leg >= 2:
            target.y -= .10  # Bring the hind contact under the hip for symmetric reach.
        swing = 0
        if moving and stance:
            target.y += reach*(p/cfg['duty']-.5)
        elif moving:
            t = (p-cfg['duty'])/(1-cfg['duty'])
            # Short toe-off extension then a cubic recovery arc. Limiting the
            # endpoint tangent keeps the hoof inside this mesh's anatomical reach.
            velocity = .2*cfg['stride']*(1-cfg['duty'])
            offset = (2*t**3-3*t**2+1)*reach*.5 + (t**3-2*t**2+t)*velocity + (-2*t**3+3*t**2)*(-reach*.5) + (t**3-t**2)*velocity
            target.y += offset
            swing = math.sin(math.pi*t)**2
            target.z += cfg['lift']*swing
        points = [body @ HEADS[n] for n in chain]
        # Seed the fore knee toward the belly; seed the hind stifle forward.
        if leg < 2:
            points[2].y += .23*swing
        else:
            points[1].y -= .20*swing
        lengths = [(HEADS[b]-HEADS[a]).length for a, b in zip(chain, chain[1:])]
        points = solve_chain(points, target, lengths)
        errors.append((points[-1]-target).length)
        contacts.append(dict(stance=stance, target=list(target), actual=list(points[-1])))
        for i, name in enumerate(chain):
            rest = arm.data.bones[name].matrix_local
            if i < len(chain)-1:
                before = HEADS[chain[i+1]]-HEADS[name]
                after = points[i+1]-points[i]
                rotation = before.rotation_difference(after) @ rest.to_quaternion()
            else:
                # Keep the hoof level during support; modest toe fold in flight.
                rotation = Quaternion((1, 0, 0), math.radians(-18)*swing) @ rest.to_quaternion()
            desired[name] = Matrix.LocRotScale(points[i], rotation, Vector((1, 1, 1)))
    # Derive local matrices directly to avoid dependency-graph update ordering.
    for b in BONES:
        name = b['name']
        pb = arm.pose.bones[name]
        rest = arm.data.bones[name].matrix_local
        if b['parent'] < 0:
            pb.matrix_basis = rest.inverted() @ desired[name]
        else:
            parent = BONES[b['parent']]['name']
            local_rest = arm.data.bones[parent].matrix_local.inverted() @ rest
            pb.matrix_basis = local_rest.inverted() @ desired[parent].inverted() @ desired[name]
    bpy.context.view_layer.update()
    return max(errors), contacts


def author(spec):
    name = spec[0]
    arm, meshes, materials = build_mount(spec)
    scene = bpy.context.scene
    scene.render.fps = 50
    clips = []
    for gait, cfg in GAITS.items():
        arm.animation_data_clear()
        steps = round(cfg['duration']*50)
        max_error = 0
        first = None
        for frame in range(steps+1):
            error, contacts = sample(arm, gait, frame/steps)
            max_error = max(max_error, error)
            matrices = [pb.matrix.copy() for pb in arm.pose.bones]
            if first is None:
                first = matrices
            for pb in arm.pose.bones:
                pb.keyframe_insert('location', frame=frame)
                pb.keyframe_insert('rotation_quaternion', frame=frame)
                pb.keyframe_insert('scale', frame=frame)
        loop_error = max(abs(a[i][j]-b[i][j]) for a,b in zip(first, matrices) for i in range(4) for j in range(4))
        action = arm.animation_data.action
        action.name = gait
        action.use_fake_user = True
        # Export one action at a time through NLA so every mount has the same names.
        track = arm.animation_data.nla_tracks.new()
        track.name = gait
        strip = track.strips.new(gait, 0, action)
        strip.name = gait
        strip.action_frame_start = 0
        strip.action_frame_end = steps
        strip.frame_end = steps
        # Keep actions across animation_data_clear, then attach all tracks below.
        clips.append(dict(name=gait, duration=steps/50, stride=cfg['stride'], duty=cfg['duty'], contacts=cfg['contacts'], frames=steps+1, max_target_error=max_error, loop_error=loop_error))
        if max_error > .01 or loop_error > .00001:
            raise RuntimeError(f'{name}/{gait}: contact {max_error:.4f}, loop {loop_error:.6f}')
    arm.animation_data_clear()
    arm.animation_data_create()
    for clip in clips:
        action = bpy.data.actions[clip['name']]
        track = arm.animation_data.nla_tracks.new()
        track.name = clip['name']
        strip = track.strips.new(clip['name'], 0, action)
        strip.name = clip['name']
        strip.action_frame_start = 0
        strip.action_frame_end = clip['frames']-1
        strip.frame_end = clip['frames']-1
        track.mute = True
    scene.frame_start = 0
    scene.frame_end = 150
    sample(arm, 'Idle', 0)
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = arm
    # NLA exporter skips muted tracks. Unmute for export, never evaluate combined tracks.
    for track in arm.animation_data.nla_tracks:
        track.mute = False
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')), use_selection=True, add_leaf_bones=False,
        bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
        bake_anim_simplify_factor=0, axis_forward='-Z', axis_up='Y', path_mode='RELATIVE',
        apply_scale_options='FBX_SCALE_UNITS')
    for track in arm.animation_data.nla_tracks:
        track.mute = True
    sample(arm, 'Idle', 0)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/(name+'.blend')))
    return dict(name=name, source_pack=spec[1], source_parts=spec[3], fbx=name+'.fbx', materials=materials,
                triangles=sum(len(m.data.polygons) for m in meshes), bones=len(BONES), clips=clips)


if __name__ == '__main__':
    receipt = []
    for spec in SPECS:
        receipt.append(author(spec))
        print('CAVALRY_AUTHORED', spec[0], flush=True)
    (OUT/'manifest.json').write_text(json.dumps(dict(models=receipt), indent=2)+'\n')
    (REVIEW/'authored-gait-receipt.json').write_text(json.dumps(dict(models=receipt), indent=2)+'\n')
    print('CAVALRY_GAITS_AUTHORED', len(receipt), 'mounts', len(GAITS), 'clips each', flush=True)
