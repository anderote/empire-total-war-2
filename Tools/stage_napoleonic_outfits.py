#!/usr/bin/env python3
"""Stage curated outfits, preserving the download and avoiding duplicate source imports."""
import json, shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parent.parent
SRC=ROOT/'AssetSources/Bannerlord/AssetSources'
rows=json.loads((ROOT/'AssetSources/bannerlord-fbx-audit.json').read_text())
byname={Path(r['path']).name.lower():r for r in rows}
recipes=[]
def add(faction,key,label,role,coat,pants,hat,equipment,weapon='musket',automatic=False):
    names=[coat,pants,hat,equipment]
    parts=[]
    for name in names:
        if not name:continue
        matches=[r for n,r in byname.items() if n==name.lower()+'.fbx']
        if not matches:raise ValueError(name)
        parts.append(Path(matches[0]['path']).name)
    recipes.append(dict(Id=faction+'-'+key,Faction=faction,Name=label,Role=role,Weapon=weapon,Automatic=automatic,Parts=parts))
F='gap_infantry_pants_french_infantry'; FE='gap_french_equipment_beige'
B='gap_british_infantry_pants'; BE='gap_british_equipment_infantry'
A='gap_infantry_pants_austrian'; AE='gap_austrian_equipment_infantry'
P='gap_infantry_pants_prussian'; PE='gap_prussian_equipment_infantry'
R='gap_russian_musketeer_pants'; RE='gap_infantry_equipment_russian'
add('france','guard','Imperial Guard','Guard','gap_french_guard','gap_infantry_pants_french_guard','gap_french_guard_bearskin','gap_french_equipment_guard',automatic=True)
add('france','grenadier','French grenadiers','Guard','gap_french_grenadier',F,'gap_french_shako_grenadier',FE)
add('france','national-guard','National Guard','Militia','gap_french_natguard',F,'gap_french_shako_line',FE,automatic=True)
add('france','vistula','Vistula Legion','Line','gap_french_infantry_vistula','gap_infantry_pants_french_vistula','gap_french_shako_vistula',FE)
add('britain','guard','Coldstream Guards','Guard','gap_british_coldstream','gap_british_coldstream_pants','british_coldstream_shako_ranker','gap_british_equipment_guard',automatic=True)
add('britain','highlander','Highland regiment','Line','gap_british_highlander','gap_british_highlander_kilt','highlander_bonnet',BE)
add('britain','highland-light','Highland light infantry','Skirmisher','gap_british_highland_light','gap_british_highlander_kilt_2','highlander_bonnet',BE)
add('prussia','guard','Prussian Guard','Guard','gap_prussian_guard',P,'gap_prussian_shako_guard',PE,automatic=True)
add('prussia','landwehr','Landwehr','Militia','gap_prussian_landwehr',P,'prussian_landwehr_cap',PE,automatic=True)
add('prussia','freikorps','Freikorps','Skirmisher','gap_prussian_freikorps','gap_infantry_pants_prussian_jaeger','prussian_freikorps_officer_cap','gap_prussian_equipment_light')
add('prussia','rhine','Rhine infantry','Line','gap_prussian_rhine',P,'gap_prussian_shako_line_2',PE)
add('russia','guard','Russian Guard','Guard','gap_russian_guard',R,'gap_russian_guard_shako',RE,automatic=True)
add('russia','pavlovsky','Pavlovsky grenadiers','Guard','gap_russian_pavlovskiy','gap_russian_musketeer_pants_2','gap_russian_mitre_cap',RE)
add('russia','militia','Russian militia','Militia','gap_russian_militia',R,'gap_russian_militia_cap',RE,automatic=True)
for i,suffix in enumerate(['','_2','_3']):add('russia','partisan'+str(i+1),'Russian partisans '+str(i+1),'Militia','gap_russian_partizan'+suffix,R,'gap_russian_militia_cap',None)
add('austria','grenadier','Austrian grenadiers','Guard','gap_austrian_infantry',A,'gap_austrian_bearskin',AE,automatic=True)
for n in [1,4,14]:add('austria','regiment-'+str(n),'Austrian regiment '+str(n),'Line','gap_austrian_infantry_'+str(n),A,'gap_austrian_shako',AE)
add('austria','recruit','Austrian recruits','Militia','gap_recruit_austrian',A,'gap_austrian_shako_insurrection',AE,automatic=True)
for n in [2,60,62]:add('hungary','regiment-'+str(n),'Hungarian regiment '+str(n),'Line','gap_hungarian_infantry_'+str(n),'gap_austrian_hungarian_pants','gap_austrian_shako',AE)
add('hungary','insurrection','Hungarian insurrection','Militia','gap_hungarian_insurrection','gap_austrian_hungarian_pants','gap_austrian_shako_insurrection',AE,automatic=True)
add('hungary','grenadier','Hungarian grenadiers','Guard','gap_hungarian_infantry_19','gap_austrian_hungarian_pants','gap_austrian_bearskin',AE,automatic=True)
add('italy','guard','Italian Guard','Guard','gap_italian_guard',F,'gap_french_guard_bearskin','gap_french_equipment_guard',automatic=True)
for n in [2,3]:add('italy','line-'+str(n),'Italian line '+str(n),'Line','gap_italian_line'+str(n),F,'gap_italian_shako',FE)
for color in ['brown','dark','green','orange','white']:add('ottoman','greatcoat-'+color,'Ottoman greatcoat / '+color,'Line','gap_ottoman_greatcoat_'+color,'gap_ottoman_trousers_white','gap_janissary_hat',None)
for faction,pants,hat,equip in [('france',F,'gap_french_shako_line',FE),('britain',B,'gap_british_stovepipe_line',BE),('prussia',P,'gap_prussian_shako_line',PE)]:
 add(faction,'recruit',faction.title()+' recruits','Militia','gap_recruit_'+('british' if faction=='britain' else 'prussian' if faction=='prussia' else 'french'),pants,hat,equip,automatic=faction=='britain')
# Mounted roles share the horse library but carry the weapon actually issued.
for faction,prefix,pants,helmet in [('france','french',F,'gap_cavalry_helmet_french_cuirassier'),('britain','british','gap_british_cavalry_pants','gap_cavalry_helmet_british_horseguard'),('austria','austrian',A,'gap_cavalry_helmet_austrian'),('prussia','prussian',P,'gap_prussian_shako_line'),('russia','russian','gap_russian_dragoon_pants','gap_cavalry_helmet_russian')]:
 coat='gap_'+prefix+'_cuirassier' if faction!='britain' else 'gap_british_blues'
 add(faction,'cuirassier',faction.title()+' heavy cavalry','Cavalry',coat,pants,helmet,'gap_cavalry_equipment_lite','sabre')
 coat='gap_'+prefix+'_dragoon' if faction!='russia' else 'gap_russian_infantry'
 add(faction,'dragoon',faction.title()+' dragoons','Cavalry',coat,pants,helmet,'gap_cavalry_equipment_lite','carbine')
 hhat={'france':'gap_french_shako_hussar','britain':'gap_british_hussar_colpack','austria':'gap_austrian_hussar_shako','prussia':'gap_prussian_shako_hussar','russia':'gap_russian_shako'}[faction]
 add(faction,'hussar',faction.title()+' hussars','Cavalry','gap_hussar_'+prefix,pants,hhat,'gap_hussar_equipment_'+faction,'sabre',True)
 add(faction,'lancer',faction.title()+' lancer prototype','Cavalry','gap_hussar_'+prefix,pants,hhat,'gap_hussar_equipment_'+faction,'lance')
# Keep regional identity; these are clothing variants, not automatic stat bonuses.
add('hungary','hussar','Hungarian hussars','Cavalry','gap_hussar_hungarian','gap_hungarian_hussar_boots','gap_austrian_hussar_shako','gap_hussar_equipment_austria','sabre',True)
commands=[]
for faction,coat,pants,hat,colonel,colonelhat in [
 ('france','gap_french_officer_line',F,'gap_french_shako_officer','gap_french_guard_officer','gap_bicorne_french'),
 ('britain','gap_british_officer',B,'british_kgl_shako_officer','gap_british_guard_officer','gap_bicorne_default_officer'),
 ('austria','gap_austrian_infantry_officer',A,'gap_austrian_shako_officer','gap_austrian_general','gap_bicorne_austrian_general'),
 ('prussia','gap_prussian_guard',P,'gap_prussian_shako_guard_officer','gap_prussian_guard','gap_bicorne_default_officer'),
 ('russia','gap_russian_guard',R,'gap_russian_shako_officer','gap_russian_guard','gap_russian_guard_shako_officer'),
 ('italy','gap_italian_guard',F,'gap_italian_shako','gap_italian_guard','gap_bicorne_default_officer'),
 ('hungary','gap_hungarian_infantry_19_officer','gap_austrian_hungarian_pants','gap_austrian_shako_officer','gap_austrian_general','gap_bicorne_austrian_general'),
 ('ottoman','gap_ottoman_officer','gap_ottoman_trousers_white','gap_janissary_hat_officer','gap_ottoman_officer','gap_janissary_hat_officer')]:
 def filenames(names):return [Path(byname[(n+'.fbx').lower()]['path']).name for n in names]
 commands.append(dict(Faction=faction,CaptainParts=filenames([coat,pants,hat]),ColonelParts=filenames([colonel,pants,colonelhat])))
models=ROOT/'Assets/Art/NapoleonicInfantry/RosterModels';textures=ROOT/'Assets/Art/NapoleonicInfantry/Textures'
models.mkdir(parents=True,exist_ok=True);textures.mkdir(parents=True,exist_ok=True)
needed={p for r in recipes for p in r['Parts']}
needed.update(p for c in commands for p in c['CaptainParts']+c['ColonelParts'])
needed.update(['gap_infantry_sabre.FBX','gap_cuirassiersword.FBX','new_flintlock_pistol.FBX'])
texture_index={p.name:p for p in SRC.rglob('*.png')}
copied=[]
for name in sorted(needed):
 row=byname[name.lower()];source=ROOT/row['path'];dest=models/name
 if not dest.exists():shutil.copy2(source,dest);copied.append(str(dest.relative_to(ROOT)))
# Exact material maps plus available faction textures; embedded author-machine paths are not authoritative.
for name,path in texture_index.items():
 if name.endswith('_d.png') and not (textures/name).exists():shutil.copy2(path,textures/name);copied.append(str((textures/name).relative_to(ROOT)))
for name in ['gp_pistol_idle.FBX','gp_pistol_readyrelease.FBX','gp_pistol_reload.FBX']:
 dst=ROOT/'Assets/Art/NapoleonicInfantry/Animations'/name
 if not dst.exists():shutil.copy2(ROOT/byname[name.lower()]['path'],dst);copied.append(str(dst.relative_to(ROOT)))
out=ROOT/'Assets/Resources/NapoleonicOutfits';out.mkdir(parents=True,exist_ok=True)
(out/'Catalog.json').write_text(json.dumps({'Outfits':recipes,'Commands':commands},indent=2)+'\n')
receipt=ROOT/'Artifacts/NapoleonicOutfits';receipt.mkdir(parents=True,exist_ok=True)
previous=json.loads((receipt/'staging.json').read_text()) if (receipt/'staging.json').exists() else {}
owned=sorted(set(previous.get('knownCopiedFiles',previous.get('copied',[])))|set(copied))
(receipt/'staging.json').write_text(json.dumps({'outfits':len(recipes),'sourceModels':len(needed),'copied':copied,'knownCopiedFiles':owned},indent=2)+'\n')
print(f'Staged {len(recipes)} outfits, {len(needed)} source models; {len(copied)} new files')
