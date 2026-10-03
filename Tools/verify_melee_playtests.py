#!/usr/bin/env python3
"""Native contact/charge acceptance on both matched battle players."""
from playtest_guard import run_qa, popen_qa
from pathlib import Path
import argparse, hashlib, json, plistlib, re, subprocess, time
root=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser();parser.add_argument('--app');parser.add_argument('--case');parser.add_argument('--output-dir',type=Path);options=parser.parse_args()
apps=[options.app] if options.app else ['Empire Battle.app','Infantry Battle.app']
cases=[options.case] if options.case else ['infantry','frontal','flank','skirmish','square','forming','routing','withdrawal']
report=[]
for name in apps:
    app=root/'Builds'/name
    with (app/'Contents/Info.plist').open('rb') as f:exe=plistlib.load(f)['CFBundleExecutable']
    assembly=app/'Contents/Resources/Data/Managed/Assembly-CSharp.dll';digest=hashlib.sha256(assembly.read_bytes()).hexdigest()
    for case in cases:
        folder=(options.output_dir or root/'Artifacts/MeleeCavalry')/app.name.removesuffix('.app').replace(' ','-')/case;folder.mkdir(parents=True,exist_ok=True)
        args=[str(app/'Contents/MacOS'/exe),'--melee-case',case,'--benchmark-seconds','17','--benchmark-phase','idle','--benchmark-output',str(folder/'report.json'),'--frame-cap','-1','--grass','off','--screenshot',str(folder/'encounter.png'),'--screenshot-delay','6' if case=='infantry' else '14' if case=='routing' else '8','-screen-width','1440','-screen-height','900','-screen-fullscreen','0','-logFile',str(folder/'player.log')]
        if case=='infantry':args+=['--inspect','20','--center','0,-2','--pitch','35','--yaw','150','--hour','14']
        started=time.time()
        with (folder/'stdout.log').open('w') as f:result=run_qa(args,cwd=root,stdout=f,stderr=subprocess.STDOUT,timeout=150)
        log=(folder/'player.log').read_text();issues=re.findall(r'^.*(?:\w+Exception:|Shader error|has been stripped from the build|_FAILED).*$' ,log,re.M)
        assert result.returncode==0 and not issues,(name,case,result.returncode,issues)
        match=re.search(r'MELEE_NATIVE_PASSED '+case+r' strikes=(\d+) impacts=(\d+) parries=(\d+) rendered=(\d+)',log)
        assert match,(name,case,'Native fixture did not finish')
        stats=json.loads((folder/'report.json').read_text());assert stats['maxRenderedSoldiers']>0 and (folder/'encounter.png').is_file()
        assert hashlib.sha256(assembly.read_bytes()).hexdigest()==digest,'Build changed during acceptance'
        row={'app':name,'case':case,'assembly_sha256':digest,'strikes':int(match[1]),'impacts':int(match[2]),'parries':int(match[3]),'rendered':int(match[4]),'seconds':time.time()-started,'medianFrameMs':stats['medianFrameMs'],'p95FrameMs':stats['p95FrameMs'],'p95SimulationMs':stats['p95SimulationMs']};report.append(row)
        print(json.dumps(row),flush=True)
(options.output_dir or root/'Artifacts/MeleeCavalry').mkdir(parents=True,exist_ok=True)
(options.output_dir or root/'Artifacts/MeleeCavalry').joinpath('native-validation.json').write_text(json.dumps(report,indent=2)+'\n')
