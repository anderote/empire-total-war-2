#!/usr/bin/env python3
"""Compile actual campaign sources; run only naming/recruitment/save checks, never Unity."""
import ast,json,subprocess,tempfile,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
UNITY=Path('/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/Resources/Scripting')
# Reuse the maintained managed model inventory without running its broad campaign suite.
tree=ast.parse((ROOT/'Tools/Campaign/Economy/verify_economy.py').read_text())
main=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='main')
inventory=[]
for node in main.body:
 if isinstance(node,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='sources' for t in node.targets): inventory.append(node)
 elif isinstance(node,ast.Expr) and isinstance(node.value,ast.Call) and isinstance(node.value.func,ast.Attribute) and isinstance(node.value.func.value,ast.Name) and node.value.func.value.id=='sources': inventory.append(node)
namespace={};exec(compile(ast.fix_missing_locations(ast.Module(body=inventory,type_ignores=[])),'inventory','exec'),namespace)
sources=namespace['sources']
extra=ROOT/'Assets/Scripts/Campaign/Geography/CountryCampaignEquipmentAccess.cs'
if extra.exists(): sources.append(str(extra))
sources.append('Assets/Scripts/Battle/Core/RegimentBaseStats.cs')
sources=list(dict.fromkeys(str(ROOT/p) if not Path(p).is_absolute() else p for p in sources))
sdk=UNITY/'DotNetSdk'; dotnet=sdk/'dotnet'; compiler=sdk/'sdk/8.0.318/Roslyn/bincore/csc.dll'
refs=['-r:'+str(p) for p in sorted((sdk/'packs/Microsoft.NETCore.App.Ref/8.0.21/ref/net8.0').glob('*.dll'))]
refs+=['-r:'+str(p) for p in sorted((UNITY/'Managed/UnityEngine').glob('*.dll'))]
with tempfile.TemporaryDirectory(prefix='etw-naming-') as temp:
 out=Path(temp);model=out/'NamingChecks.dll';rsp=out/'compile.rsp'
 # Compile the actual standalone order value type, excluding unrelated BattleWorld methods.
 orders=(ROOT/'Assets/Scripts/Battle/Core/BattleWorld.Orders.cs').read_text().split('    public sealed partial class BattleWorld')[0]+'}\n'
 (out/'BattleOrder.cs').write_text(orders);sources.append(str(out/'BattleOrder.cs'))
 rsp.write_text('\n'.join(['-nologo','-target:exe','-out:'+str(model)]+refs+sources+[str(Path(__file__).with_name('NamingChecks.cs'))]))
 subprocess.run([str(dotnet),str(compiler),'@'+str(rsp)],cwd=ROOT,check=True)
 for name in ['UnityEngine.CoreModule.dll','UnityEngine.ScriptingModule.dll','UnityEngine.JSONSerializeModule.dll']:shutil.copy2(UNITY/'Managed/UnityEngine'/name,out/name)
 (out/'NamingChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.21'}}}))
 subprocess.run([str(dotnet),str(model)],cwd=ROOT,check=True)
