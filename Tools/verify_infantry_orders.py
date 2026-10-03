#!/usr/bin/env python3
"""Exercise interrupted musket fire, movement, reloads and reforming in both native players."""
from playtest_guard import run_qa, popen_qa
from pathlib import Path
import argparse
import hashlib
import json
import plistlib
import re
import subprocess

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--app', type=Path)
options = parser.parse_args()
apps = [options.app] if options.app else [root / 'Builds/Empire Battle.app', root / 'Builds/Infantry Battle.app']
results = []
for app in apps:
    app = app.resolve()
    folder = root / 'Artifacts/InfantryOrders' / app.stem.replace(' ', '-')
    folder.mkdir(parents=True, exist_ok=True)
    with (app / 'Contents/Info.plist').open('rb') as handle:
        executable = plistlib.load(handle)['CFBundleExecutable']
    assembly = app / 'Contents/Resources/Data/Managed/Assembly-CSharp.dll'
    digest = hashlib.sha256(assembly.read_bytes()).hexdigest()
    args = [str(app / 'Contents/MacOS' / executable), '--infantry-orders-review',
            '--benchmark-seconds', '60', '--benchmark-speed', '2', '--benchmark-phase', 'idle',
            '--benchmark-output', str(folder / 'report.json'), '--grass', 'off', '--frame-cap', '60',
            '--inspect', '60', '--center', '0,-35', '--pitch', '35',
            '--screenshot', str(folder / 'reload.png'), '--screenshot-delay', '2',
            '-screen-width', '1440', '-screen-height', '900', '-screen-fullscreen', '0',
            '-logFile', str(folder / 'player.log')]
    with (folder / 'stdout.log').open('w') as handle:
        run = run_qa(args, cwd=root, stdout=handle, stderr=subprocess.STDOUT, timeout=180)
    log = (folder / 'player.log').read_text()
    errors = re.findall(r'^.*(?:\w+Exception:|Shader error|has been stripped from the build|_FAILED).*$', log, re.M)
    assert run.returncode == 0 and not errors, (app.name, run.returncode, errors)
    assert 'INFANTRY_ORDERS_NATIVE_PASSED' in log, 'Native order sequence did not finish'
    assert hashlib.sha256(assembly.read_bytes()).hexdigest() == digest, 'App changed during acceptance'
    report = json.loads((folder / 'report.json').read_text())
    assert report['maxRenderedSoldiers'] > 0 and (folder / 'reload.png').is_file()
    results.append({'app': str(app), 'assembly_sha256': digest, 'passed': True,
                    'medianFrameMs': report['medianFrameMs'], 'p95FrameMs': report['p95FrameMs']})
    print(json.dumps(results[-1]), flush=True)
(root / 'Artifacts/InfantryOrders/native-validation.json').write_text(json.dumps(results, indent=2) + '\n')
