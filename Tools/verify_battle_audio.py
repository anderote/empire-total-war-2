#!/usr/bin/env python3
"""Run the explicit audio acceptance fixture serially in each exact native playtest app."""
from playtest_guard import run_qa, popen_qa
import argparse
import hashlib
import json
from pathlib import Path
import plistlib
import subprocess
import time

ROOT=Path(__file__).resolve().parent.parent

def verify_published():
    report=ROOT/'Artifacts/BattleAudio/atmosphere-runtime-validation.json'
    tested=json.loads(report.read_text())
    if len(tested)!=2 or not all(row['passed'] for row in tested):
        raise RuntimeError('Both exact apps must pass audio acceptance before snapshot verification.')
    by_name={Path(row['app']).name:row for row in tested}
    rows=[]
    for folder in [Path('/Applications'),ROOT/'Builds/Launchers']:
        for name in ['ETW2 Playtest.app','ETW2 Infantry Playtest.app']:
            launcher=folder/name
            with (launcher/'Contents/Info.plist').open('rb') as handle:
                executable=plistlib.load(handle)['CFBundleExecutable']
            target=Path(subprocess.check_output([str(launcher/'Contents/MacOS'/executable),
                                               '--verify-target','Battle'],text=True).strip())
            expected=by_name['Infantry Battle.app' if 'Infantry' in name else 'Empire Battle.app']
            hashes={relative:hashlib.sha256((target/relative).read_bytes()).hexdigest()
                    for relative in expected['sha256']}
            if hashes!=expected['sha256']:
                raise RuntimeError(f'Published audio differs from tested app: {launcher} -> {target}')
            rows.append({'launcher':str(launcher),'snapshot':str(target),'passed':True,'sha256':hashes})
            print(name+': published audio matches tested code/resources',flush=True)
    (ROOT/'Artifacts/BattleAudio/atmosphere-published-validation.json').write_text(json.dumps(rows,indent=2)+'\n')
    return 0

def verify(app,timeout):
    with (app/'Contents/Info.plist').open('rb') as handle:
        executable=plistlib.load(handle)['CFBundleExecutable']
    log=ROOT/'Logs'/('audio-atmosphere-'+app.stem.lower().replace(' ','-')+'.log')
    log.parent.mkdir(exist_ok=True)
    start=time.monotonic()
    result=run_qa([str(app/'Contents/MacOS'/executable),'--audio-verify','-logFile',str(log)],
                          cwd=ROOT,timeout=timeout,capture_output=True,text=True)
    output=log.read_text(errors='replace')
    checks=[line.split('BATTLE_AUDIO_CHECK: ',1)[1] for line in output.splitlines() if 'BATTLE_AUDIO_CHECK: ' in line]
    success=result.returncode==0 and len(checks)>=42 and 'rain schedules audible distant thunder through spatial pool' in checks and 'BATTLE_AUDIO_RUNTIME_PASSED:' in output and 'BATTLE_AUDIO_RUNTIME_FAILED:' not in output
    data=app/'Contents/Resources/Data'
    files=[data/'Managed/Assembly-CSharp.dll',data/'resources.assets',data/'resources.assets.resS',data/'resources.resource']
    row={'app':str(app),'exit_code':result.returncode,'passed':success,'elapsed_seconds':round(time.monotonic()-start,2),
         'log':str(log),'checks':checks,'sha256':{str(p.relative_to(app)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files if p.is_file()}}
    print(app.name+': '+('PASSED' if success else 'FAILED')+f' ({len(checks)} checks)',flush=True)
    if not success:
        print('\n'.join(line for line in output.splitlines() if 'BATTLE_AUDIO_' in line),flush=True)
    return row

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app',type=Path,action='append')
    parser.add_argument('--timeout',type=int,default=150)
    parser.add_argument('--published',action='store_true',help='Verify all four saved launcher targets against the tested code/resources.')
    args=parser.parse_args()
    if args.published:return verify_published()
    apps=args.app or [ROOT/'Builds/Empire Battle.app',ROOT/'Builds/Infantry Battle.app']
    # GPU-heavy native apps must run serially so fixture deadlines are meaningful.
    rows=[verify(app.resolve(),args.timeout) for app in apps]
    path=ROOT/'Artifacts/BattleAudio/atmosphere-runtime-validation.json'
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(rows,indent=2)+'\n')
    return 0 if all(row['passed'] for row in rows) else 1

if __name__=='__main__':raise SystemExit(main())
