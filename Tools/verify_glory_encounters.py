#!/usr/bin/env python3
"""Compile actual sources and check Glory offers without launching Unity or a game."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parent.parent
UNITY = Path('/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/Resources')
SDK = UNITY / 'Scripting/DotNetSdk'


def verify_visual_references():
    def guid(path):
        return re.search(r'guid: ([0-9a-f]{32})', path.read_text())[1]

    source = (ROOT / 'Assets/Scripts/RegimentalGlory/GloryEncounters.cs').read_text()
    factions = set(re.findall(r'new Encounter\("[^"]+","([^"]+)"', source))
    script = guid(ROOT / 'Assets/Scripts/Battle/Art/UnitVisualReference.cs.meta')
    count = 0
    for faction in sorted(factions):
        regional = faction in ('ming', 'india')
        for family in ('line', 'light', 'sword') if regional else ('line', 'light'):
            if faction in ('france', 'britain') and family == 'line':
                assert (ROOT / 'Assets/Resources/BakedInfantry/InfantryCrowdLibrary.asset').is_file()
                continue
            key = faction + '-' + family
            if regional:
                library = ROOT / 'Assets/Resources/RegionalRoster' / key / 'Library.asset'
            else:
                library = ROOT / 'Assets/Generated/UnitRoster' / key / 'Library.asset'
                entry = (ROOT / 'Assets/Resources/UnitRoster' / key / 'Reference.asset').read_text()
                assert f'm_Script: {{fileID: 11500000, guid: {script}, type: 3}}' in entry, key
                assert f'Library: {{fileID: 11400000, guid: {guid(Path(str(library) + ".meta"))}, type: 2}}' in entry, key
            data = library.read_text()
            assert f'  Faction: {faction}\n' in data and f'  Family: {family}\n' in data, key
            assert int(re.search(r'  BakeVersion: (\d+)', data)[1]) > 0, key
            assert '    Lods:\n    - Idle:\n' in data, key
            count += 1
        if regional:
            assert (ROOT / 'Assets/Resources/RegionalCavalry' / faction / 'CavalryCrowdLibrary.asset').is_file()
    print(f'Visual resource references passed: {count} specialist libraries across {len(factions)} factions', flush=True)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--runtime-only",action="store_true",help="Compile all runtime sources and the Glory verifier, excluding unrelated editor checks")
    args=parser.parse_args()
    verify_visual_references()
    references = {}
    for folder in [SDK / 'packs/Microsoft.NETCore.App.Ref/8.0.21/ref/net8.0',
                   UNITY / 'Managed', UNITY / 'Scripting/Managed', ROOT / 'Library/ScriptAssemblies']:
        for path in folder.rglob('*.dll'):
            if not path.name.startswith('Assembly-CSharp'):
                references[path.name] = path
    sources = sorted(p for p in (ROOT / 'Assets').rglob('*.cs')
                     if not args.runtime_only or 'Editor' not in p.parts or p.name=='RegimentalGloryVerification.cs')
    # Disposable managed assemblies only; no shared build outputs or Unity Library writes.
    with tempfile.TemporaryDirectory(prefix='etw2-glory-checks-') as directory:
        out = Path(directory)
        compiler = [str(SDK / 'dotnet'), str(SDK / 'sdk/8.0.318/Roslyn/bincore/csc.dll')]
        refs = ['-r:"' + str(p) + '"' for p in references.values()]
        rsp = out / 'model.rsp'
        rsp.write_text('\n'.join(['-nologo', '-unsafe', '-langversion:latest', '-target:library',
            '-nowarn:1701', '-define:UNITY_EDITOR,UNITY_6000_0_OR_NEWER,UNITY_6000_2_OR_NEWER,UNITY_6000_3_OR_NEWER',
            '-out:"' + str(out / 'GloryModel.dll') + '"'] + refs + ['"' + str(p) + '"' for p in sources]))
        subprocess.run(compiler + ['@' + str(rsp)], cwd=ROOT, check=True, timeout=120)
        print(f'C# source compile passed: {len(sources)} source files', flush=True)
        paths = ','.join(json.dumps(str(p.parent)) for p in sorted(set(references.values())))
        runner = out / 'Check.cs'
        runner.write_text('''using System; using System.IO; using System.Reflection; using System.Runtime.Loader;
class Check {
    static void Main() {
        string[] paths = new string[] {''' + paths + '''};
        AssemblyLoadContext.Default.Resolving += (context, name) => {
            foreach (var dir in paths) {
                var path = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        var model = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "GloryModel.dll"));
        var checks = model.GetType("RegimentalGloryVerification", true);
        foreach (var method in new[] {"VerifyBattlePower", "VerifyMassEncounters", "VerifyEncounterOffers", "VerifyFieldSizing", "VerifyEncounterVariety", "VerifyDefeatRecovery", "VerifyRecoveryBonus"}) {
            checks.GetMethod(method).Invoke(null, null);
            Console.WriteLine(method + " passed");
        }
    }
}''')
        rsp = out / 'checks.rsp'
        rsp.write_text('\n'.join(['-nologo', '-target:exe', '-out:"' + str(out / 'Check.dll') + '"']
            + refs + ['"' + str(runner) + '"']))
        subprocess.run(compiler + ['@' + str(rsp)], cwd=ROOT, check=True, timeout=60)
        (out / 'Check.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':
            {'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': '8.0.21'}}}))
        subprocess.run([str(SDK / 'dotnet'), str(out / 'Check.dll')], cwd=ROOT, check=True, timeout=60)
    print('Managed encounter verification passed; temporary outputs removed. No game launched.')


if __name__ == '__main__':
    main()
