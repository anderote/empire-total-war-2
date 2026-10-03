#!/usr/bin/env python3
"""Review an isolated unified development preview without changing saved launchers."""
from playtest_guard import run_qa, popen_qa
import argparse
import hashlib
import json
from pathlib import Path
import plistlib
import subprocess

ROOT=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--app',type=Path,default=ROOT/'Builds/ETW2 Republic Campaign Preview.app')
parser.add_argument('--presentation-only',action='store_true')
parser.add_argument('--output',type=Path,default=ROOT/'Artifacts/RepublicCampaignQA')
options=parser.parse_args();app=options.app.resolve();out=options.output.resolve();out.mkdir(parents=True,exist_ok=True)
with (app/'Contents/Info.plist').open('rb') as handle: executable=plistlib.load(handle)['CFBundleExecutable']
assembly=app/'Contents/Resources/Data/Managed/Assembly-CSharp.dll'
digest=hashlib.sha256(assembly.read_bytes()).hexdigest()
rows=[]

def run(name,flags,required):
    folder=out/name;folder.mkdir(exist_ok=True);log=folder/'player.log'
    command=[str(app/'Contents/MacOS'/executable),'-screen-fullscreen','0','-screen-width','1440','-screen-height','900','-logFile',str(log),*flags(folder)]
    result=run_qa(command,cwd=ROOT,capture_output=True,text=True,timeout=180)
    content=log.read_text() if log.exists() else ''
    if result.returncode or required not in content or 'Exception:' in content or hashlib.sha256(assembly.read_bytes()).hexdigest()!=digest:
        raise SystemExit('Native check failed: '+name+'; inspect '+str(log))
    rows.append(dict(check=name,log=str(log),sha256=digest));print('PASS',name,flush=True)

run('Republic',lambda folder:['--campaign','-franceCampaignQA',str(folder),'-qaExit'],'FRANCE_CAMPAIGN_QA_PASSED')
if options.presentation_only:
    (out/'presentation-verification.json').write_text(json.dumps(dict(passed=True,app=str(app),sha256=digest,checks=rows,installed_or_saved_launchers_modified=False),indent=2)+'\n')
    raise SystemExit(0)
for mode in ['attack','defend','cancel']:
    run('Republic-'+mode,lambda folder,mode=mode:['--campaign','--republic-probe','--campaign-battle-probe',mode,'--campaign-probe-output',str(folder/'result.json')],'CAMPAIGN_BATTLE_PROBE_PASSED')
run('Standalone-battle',lambda folder:['--battle','--inspect','85','--ui-review',str(folder)],'BATTLE_UI_REVIEW_COMPLETE')
(out/'native-verification.json').write_text(json.dumps(dict(passed=True,app=str(app),sha256=digest,checks=rows,installed_or_saved_launchers_modified=False),indent=2)+'\n')
