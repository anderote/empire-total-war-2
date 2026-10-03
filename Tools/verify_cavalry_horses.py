"""Run the bounded horse-only Workbench review in a protected development app."""
import json
import subprocess
import time
from pathlib import Path
from playtest_guard import popen_qa

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'Artifacts/CavalryAssetResearch/WorkbenchQA'
APP = ROOT / 'Builds/Development/ETW2 Asset Workbench.app'


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    started = time.time()
    command = [str(APP / 'Contents/MacOS/ETW2 Asset Workbench'),
               '--horse-gaits-qa', str(OUT), '-screen-width', '1600',
               '-screen-height', '1000', '-screen-fullscreen', '0',
               '-logFile', str(OUT / 'player.log')]
    with (OUT / 'stdout.log').open('w') as stream:
        process = popen_qa(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT)
        (OUT / 'launch.json').write_text(json.dumps({'pid': process.pid, 'command': command}, indent=2)+'\n')
        print(f'Horse review child PID {process.pid}', flush=True)
        try:
            result = process.wait(timeout=90)
        except subprocess.TimeoutExpired:
            # Only the exact child created and recorded by this task is stopped.
            process.kill()
            process.wait()
            raise
    if result:
        raise RuntimeError(f'Horse Workbench review exited {result}; see {OUT / "player.log"}')
    if (OUT / 'result.json').stat().st_mtime < started:
        raise RuntimeError('Horse review did not produce a fresh result')
    receipt = json.loads((OUT / 'result.json').read_text())
    if receipt != {'mounts': 4, 'animatedClips': 28, 'passed': True}:
        raise RuntimeError(f'Incomplete horse review: {receipt}')
    for name in ('HussarBay-idle', 'CuirassierChestnut-idle', 'ArabianA-idle',
                 'ArabianB-idle', 'Walk', 'Trot', 'CanterLeft', 'GallopLeft'):
        screenshot = OUT / (name+'.png')
        if not screenshot.is_file() or screenshot.stat().st_mtime < started:
            raise RuntimeError('Missing horse screenshot: '+name)
    print('CAVALRY_WORKBENCH_QA_PASSED: four mounts, 28 clips, eight screenshots', flush=True)


if __name__ == '__main__':
    main()
