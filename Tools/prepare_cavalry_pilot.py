"""Blender: stage only LOD0 of the three selected Napoleonic pilot assets."""
import bpy,json
from pathlib import Path
ROOT=Path(__file__).resolve().parent.parent
archive=ROOT/'AssetSources/TroopPacks/ConvertedFBX/napoleonic-warfare'
out=ROOT/'Assets/Art/TroopSources/napoleonic-warfare'
for name in ['gap_french_cuirassier','gap_hussar_french','gap_cavalry_helmet_french_cuirassier']:
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False);bpy.ops.import_scene.fbx(filepath=str(archive/(name+'.FBX')))
 for ob in list(bpy.context.scene.objects):
  if ob.type=='MESH' and ('.LOD' in ob.name or name.startswith('gap_cavalry_helmet') and not ob.name.startswith(name)):
   bpy.data.objects.remove(ob,do_unlink=True)
 for ob in bpy.context.scene.objects:ob.select_set(True)
 bpy.ops.export_scene.fbx(filepath=str(out/(name+'.FBX')),use_selection=True,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='RELATIVE')
print('CAVALRY_PILOT_STAGED_LOD0')
