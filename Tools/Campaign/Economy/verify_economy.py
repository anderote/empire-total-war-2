#!/usr/bin/env python3
"""Compile and test the actual campaign economy without starting the Unity editor."""
import json
import shutil
import sys
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
UNITY = Path('/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/Resources/Scripting')
SDK = UNITY / 'DotNetSdk'
OUT = ROOT / 'Artifacts/CampaignValidation'


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    dotnet = SDK / 'dotnet'
    compiler = SDK / 'sdk/8.0.318/Roslyn/bincore/csc.dll'
    framework = SDK / 'packs/Microsoft.NETCore.App.Ref/8.0.21/ref/net8.0'
    modules = UNITY / 'Managed/UnityEngine'
    refs = ['-r:"' + str(p) + '"' for p in sorted(framework.glob('*.dll'))]
    unity_refs = ['-r:"' + str(p) + '"' for p in sorted(modules.glob('*.dll'))]
    sources = ['Assets/Scripts/Campaign/Geography/' + name for name in
               ['GeographyData.cs', 'CampaignRegions.cs', 'CampaignBuildingTiers.cs', 'CountryCampaignFacilities.cs', 'CampaignEconomy.cs', 'CountryCampaignModels.cs', 'CampaignLandscape.cs', 'CampaignMovementTerrain.cs', 'CountryCampaignMovement.cs', 'CountryCampaignOrders.cs', 'CountryCampaign.cs', 'CountryCampaignArmies.cs', 'CountryCampaignRepublic.cs', 'CampaignBattleReports.cs', 'CountryCampaignAutoresolve.cs', 'CountryCampaignNaval.cs', 'CountryCampaignOperations.cs', 'CampaignOperationsValidation.cs', 'CountryCampaignResearch.cs', 'CountryCampaignProgression.cs', 'CountryCampaignEquipmentAccess.cs', 'CountryCampaignProcurement.cs', 'CountryCampaignStrategy.cs', 'CountryCampaignGovernance.cs', 'CountryCampaignDiplomacy.cs', 'CountryCampaignStrategyAI.cs', 'CampaignStrategyValidation.cs', 'CountryCampaignRegimentLibrary.cs']]
    sources.extend(['Assets/Scripts/Battle/Core/MilitaryEquipment.cs', 'Assets/Scripts/Battle/Core/UnitProgression.cs', 'Assets/Scripts/Battle/Core/RegimentAbilities.cs', 'Assets/Scripts/Battle/Core/UnitEquipment.cs'])
    sources.extend('Assets/Scripts/Battle/Core/' + name for name in ['SavedRegiment.cs', 'UnitRoster.cs', 'RegionalArmyRoster.cs', 'BattleTypes.cs', 'BattleMapProfile.cs'])
    model = OUT / 'EconomyModel.dll'
    model_rsp = OUT / 'economy-model.rsp'
    model_rsp.write_text('\n'.join(['-nologo', '-target:library', '-out:"'+str(model)+'"'] + refs + unity_refs + sources))
    subprocess.run([str(dotnet), str(compiler), '@'+str(model_rsp)], cwd=ROOT, check=True)
    test_rsp = OUT / 'economy-checks.rsp'
    test_rsp.write_text('\n'.join(['-nologo', '-target:exe', '-out:"'+str(OUT/'EconomyChecks.dll')+'"',
        '-r:"'+str(model)+'"'] + refs + unity_refs + ['Tools/Campaign/Economy/EconomyChecks.cs']))
    subprocess.run([str(dotnet), str(compiler), '@'+str(test_rsp)], cwd=ROOT, check=True)
    country_rsp = OUT / 'country-checks.rsp'
    country_rsp.write_text(test_rsp.read_text().replace('EconomyChecks.dll', 'CountryCampaignChecks.dll').replace('EconomyChecks.cs', 'CountryCampaignChecks.cs')+'\nTools/Campaign/Economy/ResearchChecks.cs\nTools/Campaign/Economy/CampaignEncounterChecks.cs\nTools/Campaign/Economy/ProgressionChecks.cs\nTools/Campaign/Economy/ArmyChecks.cs\nTools/Campaign/Economy/RepublicChecks.cs\nTools/Campaign/Economy/AutoresolveChecks.cs\nTools/Campaign/Economy/OperationsChecks.cs\nTools/Campaign/Economy/StrategyChecks.cs\nTools/Campaign/Economy/OrderChecks.cs')
    subprocess.run([str(dotnet), str(compiler), '@'+str(country_rsp)], cwd=ROOT, check=True)
    for name in ['UnityEngine.CoreModule.dll', 'UnityEngine.ScriptingModule.dll', 'UnityEngine.JSONSerializeModule.dll']:
        shutil.copy2(modules/name, OUT/name)
    (OUT/'EconomyChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':
        {'tfm':'net8.0', 'framework':{'name':'Microsoft.NETCore.App','version':'8.0.21'}}}))
    (OUT/'CountryCampaignChecks.runtimeconfig.json').write_text((OUT/'EconomyChecks.runtimeconfig.json').read_text())
    country = subprocess.run([str(dotnet), str(OUT/'CountryCampaignChecks.dll'), *sys.argv[1:]], cwd=ROOT, text=True, capture_output=True)
    if country.returncode:
        print(country.stdout, country.stderr)
        raise SystemExit(country.returncode)
    print(country.stdout.strip())
    if any(flag in sys.argv for flag in ('--operations-only', '--rules-only', '--strategy-only', '--orders-only', '--adopt-save')):
        return
    result = subprocess.run([str(dotnet), str(OUT/'EconomyChecks.dll')], cwd=ROOT, text=True, capture_output=True, check=True)
    print(result.stdout.strip())
    (OUT/'economy-verification.json').write_text(json.dumps({'result':'passed',
        'method':'Compiled actual geography, economy and country campaign sources; managed execution',
        'countrySummary':country.stdout.strip(),
        'summary':result.stdout.strip(), 'nativeSaveMigration':'pending Unity verification',
        'visualQA':'pending campaign build'}, indent=2)+'\n')


if __name__ == '__main__':
    main()
