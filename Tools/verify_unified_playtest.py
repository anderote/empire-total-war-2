#!/usr/bin/env python3
"""Validate campaign, standalone battle and combat effects in one immutable native app."""
from playtest_guard import run_qa, popen_qa
import argparse
import hashlib
import json
from pathlib import Path
import plistlib
import re
import subprocess

ROOT=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--app',required=True,type=Path)
parser.add_argument('--resume',action='store_true',help='Reuse completed checks for the identical application assembly')
parser.add_argument('--output',required=True,type=Path)
parser.add_argument('--checks',help='Comma-separated checks to run; default runs every check')
options=parser.parse_args()
app=options.app.resolve();out=options.output.resolve();out.mkdir(parents=True,exist_ok=True)
with (app/'Contents/Info.plist').open('rb') as handle:executable=plistlib.load(handle)['CFBundleExecutable']
binary=app/'Contents/MacOS'/executable
assembly=app/'Contents/Resources/Data/Managed/Assembly-CSharp.dll'
digest=lambda:hashlib.sha256(assembly.read_bytes()).hexdigest()
initial=digest();rows=[];completed={}
receipt=out/'native-verification.json'
if options.resume and receipt.exists():
    previous=json.loads(receipt.read_text())
    if previous.get('sha256')==initial:completed={r['check']:r for r in previous.get('checks',[]) if r.get('passed')}
cases=[('campaign',['--campaign'],'FRANCE_CAMPAIGN_QA_PASSED')]
for mode in ['attack','defend','cancel']:cases.append(('transition-'+mode,['--campaign','--campaign-battle-probe',mode],'CAMPAIGN_BATTLE_PROBE_PASSED'))
cases.extend([('standalone',['--battle','--inspect','85','--benchmark-seconds','6'],'UNIT_PROGRESSION_VERSION'),
              ('casualty',['--casualty-inspect','--benchmark-seconds','14'],'CASUALTY_PLAYTEST_PASSED'),
              ('recovery',['--recovery-inspect','--benchmark-seconds','14'],'ARTILLERY_RECOVERY_PLAYTEST_PASSED')])
if options.checks:
    selected=set(options.checks.split(','));unknown=selected-{name for name,_,_ in cases}
    if unknown:raise SystemExit('Unknown checks: '+', '.join(sorted(unknown)))
    cases=[case for case in cases if case[0] in selected]
for name,flags,marker in cases:
    if name in completed:
        rows.append(completed[name]);print('REUSE PASS '+name,flush=True);continue
    folder=out/name;folder.mkdir(exist_ok=True);log=folder/'player.log'
    if name=='campaign':flags+=['-franceCampaignQA',str(folder),'-qaExit']
    elif name.startswith('transition-'):flags+=['--campaign-probe-output',str(folder/'result.json')]
    elif name=='standalone':flags+=['--benchmark-output',str(folder/'result.json'),'--screenshot',str(folder/'frame.png'),'--screenshot-delay','4']
    else:flags+=['--benchmark-output',str(folder/'result.json'),'--screenshot',str(folder/'frame.png'),'--screenshot-delay','10.5' if name=='casualty' else '3.7']
    print('RUN '+name,flush=True)
    result=run_qa([str(binary),'-batchmode','-screen-fullscreen','0','-screen-width','1440','-screen-height','900','-logFile',str(log),*flags],cwd=ROOT,capture_output=True,text=True,timeout=240)
    content=log.read_text() if log.exists() else ''
    issues=re.findall(r'^.*(?:\w+Exception:|Shader error|_FAILED|has been stripped from the build).*$' ,content,re.M)
    if result.returncode or issues or marker not in content or digest()!=initial:
        raise SystemExit(f'{name} failed: exit={result.returncode}, issues={issues}, marker={marker in content}; {log}')
    evidence=next(line for line in content.splitlines() if marker in line)
    rows.append({'check':name,'passed':True,'log':str(log),'evidence':evidence})
    (out/'native-verification.json').write_text(json.dumps({'app':str(app),'sha256':initial,'passed':len(rows)==len(cases),'checks':rows},indent=2)+'\n')
    print('PASS '+name+': '+evidence,flush=True)
# Resumed checks at the end of the list also count toward the final receipt.
(out/'native-verification.json').write_text(json.dumps({'app':str(app),'sha256':initial,'passed':len(rows)==len(cases),'checks':rows},indent=2)+'\n')
