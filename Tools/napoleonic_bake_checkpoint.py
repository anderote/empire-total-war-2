"""Stop only this task's exact owned Unity child at an asset checkpoint for visual QA."""
import argparse, datetime, json, os, signal, subprocess, time
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--pid',type=int,required=True);p.add_argument('--parent',type=int,required=True);p.add_argument('--log',type=Path,required=True);p.add_argument('--marker',required=True);a=p.parse_args()
while True:
    result=subprocess.run(['ps','-p',str(a.pid),'-o','ppid=,args='],capture_output=True,text=True)
    if result.returncode:break
    command=result.stdout.strip()
    if not command.startswith(str(a.parent)+' ') or '-executeMethod InfantryAssetBuild.BakeNapoleonicExpansion' not in command:raise RuntimeError('Task ownership check failed; no process stopped')
    if a.marker in a.log.read_text(errors='replace'):
        os.kill(a.pid,signal.SIGTERM)
        receipt=Path('Artifacts/NapoleonicOutfits/owned-processes.jsonl')
        with receipt.open('a') as f:f.write(json.dumps({'pid':a.pid,'action':'stop own resumable bake at verified asset checkpoint for early visual review','marker':a.marker,'utc':datetime.datetime.now(datetime.timezone.utc).isoformat()})+'\n')
        print('Owned bake paused at',a.marker,flush=True);break
    time.sleep(10)
