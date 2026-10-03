#!/usr/bin/env python3
"""Run one reproducible native player capture; fail on a completed-log exception."""
from playtest_guard import run_qa, popen_qa
import argparse, hashlib, json, plistlib, re, subprocess
from pathlib import Path
root=Path(__file__).resolve().parent.parent
p=argparse.ArgumentParser()
p.add_argument('label');p.add_argument('--seconds',type=int,default=30)
p.add_argument('--case',choices=['combat','overview','march','close','endurance'],default='combat')
p.add_argument('--app',default='Builds/Infantry Performance.app');p.add_argument('--raw',action='store_true')
a,extra=p.parse_known_args();app=(root/a.app).resolve()
folder=root/'Artifacts/InfantryOptimization'/a.label;folder.mkdir(parents=True,exist_ok=True)
with (app/'Contents/Info.plist').open('rb') as f:exe=plistlib.load(f)['CFBundleExecutable']
phase='combat' if a.case in ('combat','overview','endurance') else 'march' if a.case=='march' else 'idle'
close=a.case in ('combat','close','endurance')
args=[str(app/'Contents/MacOS'/exe),'--benchmark-seconds',str(a.seconds),'--benchmark-phase',phase,'--inspect','80' if a.case in ('combat','endurance') else '6' if close else '900','--center','0,-240' if a.case=='close' else '0,0','--pitch','20' if a.case=='close' else '40' if close else '65','--ai','on' if phase=='combat' else 'off','--frame-cap','-1','--benchmark-output',str(folder/'report.json'),'--telemetry',str(folder/'frames.csv'),'-screen-width','1440','-screen-height','900','-screen-fullscreen','0','-logFile',str(folder/'player.log')]
if a.raw:args+=['--profile-log',str(folder/'timeline.raw')]
if a.case=='endurance':args+=['--benchmark-speed','4']
args+=extra[1:] if extra[:1]==['--'] else extra
assembly=app/'Contents/Resources/Data/Managed/Assembly-CSharp.dll'
(folder/'run.json').write_text(json.dumps({'command':args,'assembly_sha256':hashlib.sha256(assembly.read_bytes()).hexdigest()},indent=2))
with (folder/'stdout.log').open('w') as f:result=run_qa(args,cwd=root,stdout=f,stderr=subprocess.STDOUT,timeout=a.seconds+180)
log=(folder/'player.log').read_text()
issues=re.findall(r'^.*(?:\w+Exception:|Shader error|has been stripped from the build|_FAILED).*$' ,log,re.M)
if result.returncode or issues or not (folder/'report.json').exists():raise SystemExit(f'FAILED exit={result.returncode} issues={issues}')
report=json.loads((folder/'report.json').read_text())
print(json.dumps({k:report.get(k) for k in ['medianFrameMs','p95FrameMs','medianSimulationMs','p95SimulationMs','survivors','maxSmokePuffs','maxRenderedSoldiers']},indent=2))
