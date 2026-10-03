#!/usr/bin/env python3
"""Quick C# compile of actual Unity sources without opening another editor."""
import argparse, subprocess
from pathlib import Path

root=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser();parser.add_argument('--editor',action='store_true');args=parser.parse_args()
unity=Path('/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/Resources')
sdk=unity/'Scripting/DotNetSdk';out=root/'Artifacts/CampaignBattleIntegration';out.mkdir(parents=True,exist_ok=True)
references={}
for folder in [sdk/'packs/Microsoft.NETCore.App.Ref/8.0.21/ref/net8.0',unity/'Managed',unity/'Scripting/Managed',root/'Library/ScriptAssemblies']:
    for path in folder.rglob('*.dll'):
        if path.name.startswith('Assembly-CSharp'):continue
        references[path.name]=path
sources=[p for p in (root/'Assets').rglob('*.cs') if args.editor or 'Editor' not in p.parts]
arguments=['-nologo','-unsafe','-langversion:latest','-target:library','-nowarn:1701',
           '-out:"'+str(out/('EditorCompile.dll' if args.editor else 'RuntimeCompile.dll'))+'"',
           '-define:UNITY_6000_0_OR_NEWER,UNITY_6000_2_OR_NEWER,UNITY_6000_3_OR_NEWER'+(',UNITY_EDITOR' if args.editor else '')]
arguments+=['-r:"'+str(p)+'"' for p in references.values()]
arguments+=['"'+str(p)+'"' for p in sources]
rsp=out/('editor-compile.rsp' if args.editor else 'runtime-compile.rsp');rsp.write_text('\n'.join(arguments))
result=subprocess.run([str(sdk/'dotnet'),str(sdk/'sdk/8.0.318/Roslyn/bincore/csc.dll'),'@'+str(rsp)],cwd=root,text=True,capture_output=True)
(out/('editor-compile.log' if args.editor else 'runtime-compile.log')).write_text(result.stdout+result.stderr)
print(result.stdout+result.stderr,end='');print('C# source compile', 'passed' if result.returncode==0 else 'failed',len(sources),'files')
raise SystemExit(result.returncode)
