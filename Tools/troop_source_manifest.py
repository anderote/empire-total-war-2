#!/usr/bin/env python3
"""Create an explicit Unity import manifest from converted meshes and source materials."""
from pathlib import Path
import json
ROOT=Path(__file__).resolve().parent.parent
models=[]
selection=json.loads((ROOT/'AssetSources/TroopPacks/pilot-selection.json').read_text())
allowed=set(selection['models'])
for folder in sorted((ROOT/'Assets/Art/TroopSources').iterdir()):
 if not folder.is_dir():continue
 pack=folder.name; mappings={}
 converted=ROOT/'AssetSources/TroopPacks/Converted'/pack
 mats={}
 for p in converted.glob('*/data.json'):
  if not p.stat().st_size:continue
  for m in json.loads(p.read_text()).get('materials',[]):mats[m['name']]=m
 for p in converted.glob('*-export/data.json'):
  for m in json.loads(p.read_text())['meshes']:
   mat=m['material'];d=mats.get(mat,{}).get('diffuseA',mat);tex=folder/'Textures'/(Path(d).stem+'.png')
   mappings[m['name']]=[dict(name=mat,texture=str(tex.relative_to(ROOT)) if tex.exists() else '')]
 for p in converted.glob('*.json'):
  d=json.loads(p.read_text())
  if not isinstance(d,dict) or 'meshes' not in d:continue
  mappings[d['name']]=[dict(name=m['material'] or 'Default',texture=str((folder/'Textures'/(Path(m['diffuse']).stem+'.png')).relative_to(ROOT)) if m.get('diffuse') else '') for m in d['meshes']]
 if pack=='RegionalArmies':
  for row in json.loads((folder/'regional-receipt.json').read_text()):mappings[row['name']]=row['materials']
 for p in sorted(folder.iterdir()):
  if p.suffix.lower()!='.fbx':continue
  if str(p.relative_to(ROOT/'Assets/Art/TroopSources')) not in allowed:raise ValueError('Model outside pilot selection: '+str(p))
  models.append(dict(path=str(p.relative_to(ROOT)),pack=pack,name=p.stem,materials=mappings.get(p.stem,[])))
audit=ROOT/'Artifacts/TroopSources/pilot-materials.json'
if audit.exists():
 for row in json.loads(audit.read_text()):
  model=next((m for m in models if m['pack']=='napoleonic-warfare' and m['name']==Path(row['file']).stem),None)
  if model is None:continue
  model['materials']=[]
  for name in row['materials']:
   tex=ROOT/'Assets/Art/TroopSources/napoleonic-warfare/Textures'/(name+'_d.png')
   if tex.exists():model['materials'].append(dict(name=name,texture=str(tex.relative_to(ROOT))))
out=ROOT/'Assets/Art/TroopSources/import-manifest.json';out.write_text(json.dumps(dict(models=models),indent=2));print('Manifest',len(models),'models')
