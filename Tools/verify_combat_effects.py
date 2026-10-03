#!/usr/bin/env python3
"""Run lethal artillery and survivor recovery fixtures against each matched native app."""
from playtest_guard import run_qa, popen_qa
import argparse
import json
import plistlib
import re
import subprocess
from pathlib import Path

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--app', required=True, type=Path)
parser.add_argument('--label', required=True)
options = parser.parse_args()
app = options.app.resolve()
with (app / 'Contents/Info.plist').open('rb') as handle:
    executable = plistlib.load(handle)['CFBundleExecutable']
folder = root / 'Artifacts' / 'CombatEffects' / options.label
folder.mkdir(parents=True, exist_ok=True)
for fixture, marker, delay in [('casualty', 'CASUALTY_PLAYTEST_PASSED', 10.5),
                                ('recovery', 'ARTILLERY_RECOVERY_PLAYTEST_PASSED', 3.7)]:
    log_path = folder / (fixture + '.log')
    report_path = folder / (fixture + '.json')
    command = [str(app / 'Contents/MacOS' / executable), '--' + fixture + '-inspect',
               '--benchmark-seconds', '14', '--benchmark-output', str(report_path),
               '--screenshot', str(folder / (fixture + '.png')), '--screenshot-delay', str(delay),
               '-screen-width', '1440', '-screen-height', '900', '-screen-fullscreen', '0',
               '-logFile', str(log_path)]
    result = run_qa(command, cwd=root, capture_output=True, text=True, timeout=194)
    log = log_path.read_text()
    issues = re.findall(r'^.*(?:\w+Exception:|Shader error|_FAILED|has been stripped from the build).*$' , log, re.M)
    if result.returncode or issues or marker not in log or not report_path.exists():
        raise SystemExit(f'{fixture} FAILED: exit={result.returncode} issues={issues} marker={marker in log}')
    print(f'{options.label}: {next(line for line in log.splitlines() if marker in line)}', flush=True)
(folder / 'verification.json').write_text(json.dumps({'app': str(app), 'fixtures': ['casualty', 'recovery'], 'passed': True}, indent=2) + '\n')
