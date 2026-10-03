#!/usr/bin/env python3
"""Bounded managed campaign building tier/condition/migration checks; launches no Unity/game."""
import argparse
import json
import subprocess
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1])
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args()
root=args.root.resolve();out=args.output.resolve();out.mkdir(parents=True,exist_ok=True)
unity=Path('/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/Resources')
sdk=unity/'Scripting/DotNetSdk'
refs={}
for folder in [sdk/'packs/Microsoft.NETCore.App.Ref/8.0.21/ref/net8.0',unity/'Managed',unity/'Scripting/Managed',root/'Library/ScriptAssemblies']:
    for p in folder.rglob('*.dll'):
        if not p.name.startswith('Assembly-CSharp'):refs[p.name]=p
def compile(name,sources,extra=(),target='library'):
    rsp=out/(name+'.rsp')
    rsp.write_text('\n'.join(['-nologo','-unsafe','-langversion:latest','-nowarn:1701','-target:'+target,'-out:"'+str(out/(name+'.dll'))+'"','-define:UNITY_6000_0_OR_NEWER,UNITY_6000_2_OR_NEWER,UNITY_6000_3_OR_NEWER']+['-r:"'+str(p)+'"' for p in [*refs.values(),*extra]]+['"'+str(p)+'"' for p in sources]))
    result=subprocess.run([str(sdk/'dotnet'),str(sdk/'sdk/8.0.318/Roslyn/bincore/csc.dll'),'@'+str(rsp)],cwd=root,text=True,capture_output=True)
    (out/(name+'-compile.log')).write_text(result.stdout+result.stderr)
    print(result.stdout+result.stderr,end='')
    if result.returncode:raise SystemExit(result.returncode)
    print(name+': '+str(len(sources))+' actual source files compiled')
compile('BuildingRuntime',[p for p in (root/'Assets').rglob('*.cs') if 'Editor' not in p.parts])
compile('BuildingChecks',[root/'Tools/Campaign/Economy/BuildingTierChecks.cs'],[out/'BuildingRuntime.dll'])
boot=out/'Bootstrap.cs'
boot.write_text('''using System;using System.IO;using System.Linq;using System.Reflection;using System.Runtime.Loader;
class Bootstrap {static void Main(string[] args){
var paths=File.ReadAllLines(args[0]).Select(p=>new {Key=Path.GetFileNameWithoutExtension(p),Path=p}).ToDictionary(x=>x.Key,x=>x.Path);
AssemblyLoadContext.Default.Resolving+=(context,name)=>paths.TryGetValue(name.Name,out var path)?context.LoadFromAssemblyPath(path):null;
try {AssemblyLoadContext.Default.LoadFromAssemblyPath(args[1]).GetType("BuildingTierChecks").GetMethod("Run").Invoke(null,new object[]{args[2]});}
catch(TargetInvocationException ex){Console.Error.WriteLine(ex.InnerException);Environment.Exit(1);}
}}''')
compile('BuildingBootstrap',[boot],target='exe')
(out/'BuildingBootstrap.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.21'}}}))
paths=out/'assembly-paths.txt';paths.write_text('\n'.join(str(p) for p in [*refs.values(),out/'BuildingRuntime.dll']))
result=subprocess.run([str(sdk/'dotnet'),str(out/'BuildingBootstrap.dll'),str(paths),str(out/'BuildingChecks.dll'),str(root)],cwd=root,text=True,capture_output=True)
(out/'checks.log').write_text(result.stdout+result.stderr)
print(result.stdout+result.stderr,end='')
raise SystemExit(result.returncode)
