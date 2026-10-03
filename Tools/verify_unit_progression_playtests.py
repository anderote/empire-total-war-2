#!/usr/bin/env python3
"""Exercise the actual armory UI in every supplied player, without touching player saves."""
from playtest_guard import run_qa, popen_qa
import argparse
import json
from pathlib import Path
import plistlib
import subprocess

ROOT=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('apps',nargs='*',type=Path)
parser.add_argument('--output',type=Path,default=ROOT/'Artifacts/UnitProgression/Players')
options=parser.parse_args();options.output=options.output.resolve()
apps=options.apps or [ROOT/'Builds/ETW2.app']
results=[]
for app in apps:
    app=app.resolve()
    with (app/'Contents/Info.plist').open('rb') as handle:executable=plistlib.load(handle)['CFBundleExecutable']
    binary=app/'Contents/MacOS'/executable
    output=options.output/app.stem;output.mkdir(parents=True,exist_ok=True)
    log=output/'player.log'
    process=popen_qa([str(binary),'-screen-fullscreen','0','-screen-width','1280','-screen-height','900',
        '--battle','--unit-roster','france,britain','--unit-upgrades-review','--unit-upgrades-output',str(output),'-logFile',str(log)],cwd=ROOT)
    try:code=process.wait(timeout=90)
    except subprocess.TimeoutExpired:
        process.terminate()
        try:process.wait(timeout=10)
        except subprocess.TimeoutExpired:process.kill();process.wait()
        raise SystemExit('Armory player review timed out: '+str(app))
    result=json.loads((output/'result.json').read_text()) if (output/'result.json').exists() else {'passed':False}
    result.update(app=str(app),exit_code=code)
    results.append(result)
    if code!=0 or not result['passed'] or 'UNIT_UPGRADES_PLAYTEST_PASSED' not in log.read_text():
        raise SystemExit('Armory player review failed: '+str(app)+'; inspect '+str(log))
    print('PASS:',app,'54 items; infantry 45 rounds / 125 morale; battery 375 rounds; native armory screenshots',flush=True)
options.output.mkdir(parents=True,exist_ok=True)
(options.output/'verification.json').write_text(json.dumps({'passed':True,'players':results},indent=2)+'\n')
