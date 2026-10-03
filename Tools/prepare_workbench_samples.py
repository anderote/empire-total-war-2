"""Run in Blender: convert a bounded BRF equipment/vegetation review selection."""
import bpy,json
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parent.parent
OUT=ROOT/'Assets/Art/ModSamples'
OUT.mkdir(parents=True,exist_ok=True)
receipt=[]
for file in sorted((ROOT/'AssetSources/ModSamples/BRF').glob('*/data.json')):
    pack=file.parent.name.split('-napo_')[0] if '-napo_' in file.parent.name else '16th-century' if file.parent.name.startswith('16th-century') else 'gekokujo'
    sourcepack='Gekokujo' if pack=='gekokujo' else pack
    textures={p.stem.lower():p for p in (ROOT/'AssetSources/TroopPacks/Extracted'/sourcepack).rglob('*') if p.suffix.lower() in ('.dds','.png','.tga')}
    materials={}
    for meta in (ROOT/'AssetSources/TroopPacks/Converted'/pack).glob('*/data.json'):
        if meta.stat().st_size:
            for m in json.loads(meta.read_text()).get('materials',[]):materials[m['name']]=m
    data=json.loads(file.read_text());materials.update({m['name']:m for m in data['materials']})
    grouped={}
    for m in data['meshes']:
        name=m['name'].removesuffix('.8').removesuffix('.1')
        grouped.setdefault(name,[]).append(m)
    for name,parts in grouped.items():
        if name=="bamboo_1":
            print("SKIPPED bamboo_1: source pack does not contain referenced bark_bamboo/bamboo textures")
            continue
        bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
        matrows=[]
        for part in parts:
            rows=part['vertices'];triangles=[]
            axis=Matrix(((1,0,0),(0,0,-1),(0,1,0))) if any(x in file.parent.name for x in ('plants','interiors','japan_tree')) else Matrix.Identity(3)
            for face in part['triangles']:
                a,b,c=[Vector(rows[i]['position']) for i in face];n=sum((Vector(rows[i]['normal']) for i in face),Vector())
                triangles.append(list(reversed(face)) if (b-a).cross(c-a).dot(n)<0 else face)
            mesh=bpy.data.meshes.new(part['name']);mesh.from_pydata([axis@Vector(v['position']) for v in rows],[],triangles);mesh.update()
            for poly in mesh.polygons:poly.use_smooth=True
            mesh.normals_split_custom_set_from_vertices([axis@Vector(v['normal']) for v in rows])
            obj=bpy.data.objects.new(part['name'],mesh);bpy.context.collection.objects.link(obj)
            uv=mesh.uv_layers.new(name='UVMap')
            for l in mesh.loops:
                u,v=rows[l.vertex_index]['uv'];uv.data[l.index].uv=(u,1-v)
            key=part['material'];definition=materials.get(key,{});diffuse=definition.get('diffuseA',key);texture=textures.get(diffuse.lower())
            if not texture:raise RuntimeError(f'Missing texture {key}/{diffuse}')
            png=OUT/'Textures'/(pack+'-'+diffuse+'.png');png.parent.mkdir(exist_ok=True)
            if not png.exists():raise RuntimeError('Convert DDS to PNG with Pillow first: '+str(png))
            image=bpy.data.images.load(str(png),check_existing=True)
            material=bpy.data.materials.new(pack+'-'+key);material.use_nodes=True
            texnode=material.node_tree.nodes.new('ShaderNodeTexImage');texnode.image=image
            material.node_tree.links.new(texnode.outputs['Color'],material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
            mesh.materials.append(material)
            foliage=any(t in key for t in ['leaves','plant_wheat','tree_sakura']) or key=='bamboo'
            matrows.append(dict(name=pack+'-'+key,texture=str(png.relative_to(ROOT)),cutout=foliage))
        bpy.ops.object.select_all(action='SELECT')
        target=OUT/(pack+'-'+name+'.fbx')
        bpy.ops.export_scene.fbx(filepath=str(target),use_selection=True,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='RELATIVE')
        receipt.append(dict(name=name,fbx=str(target.relative_to(ROOT)),parts=len(parts),triangles=sum(len(m['triangles']) for m in parts),materials=matrows,source=str(file.relative_to(ROOT))))
(ROOT/'AssetSources/ModSamples/selection.json').write_text(json.dumps(receipt,indent=2))
print('WORKBENCH_SAMPLES',len(receipt))
