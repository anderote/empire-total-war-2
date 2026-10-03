#!/usr/bin/env python3
"""Validate development builds without modifying installed or saved playtest launchers."""
from playtest_guard import run_qa, popen_qa
import argparse
import hashlib
import json
from pathlib import Path
import plistlib
import subprocess

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'Artifacts/FranceCampaignQA/Final'
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--campaign-only', action='store_true', help='Review a presentation-only campaign change after the full native suite')
parser.add_argument('--build-dir', type=Path, default=ROOT/'Builds')
options = parser.parse_args()
OUT.mkdir(parents=True, exist_ok=True)


def assembly(app):
    return app / 'Contents/Resources/Data/Managed/Assembly-CSharp.dll'


def digest(app):
    return hashlib.sha256(assembly(app).read_bytes()).hexdigest()


def run(app, name, flags, required):
    folder = OUT / name
    folder.mkdir(exist_ok=True)
    with (app / 'Contents/Info.plist').open('rb') as handle:
        executable = plistlib.load(handle)['CFBundleExecutable']
    log = folder / 'player.log'
    before = digest(app)
    command = [str(app / 'Contents/MacOS' / executable), '-screen-fullscreen', '0',
               '-screen-width', '1440', '-screen-height', '900', '-logFile', str(log), *flags(folder)]
    result = run_qa(command, cwd=ROOT, capture_output=True, text=True, timeout=180)
    content = log.read_text()
    if result.returncode or required not in content or 'Exception:' in content or before != digest(app):
        raise SystemExit('Native check failed or app changed: ' + name + '; inspect ' + str(log))
    print('PASS', name, flush=True)
    return dict(app=str(app), check=name, sha256=before, log=str(log))


campaign = options.build_dir.resolve() / 'ETW2 Campaign.app'
main = options.build_dir.resolve() / 'Empire Battle.app'
infantry = options.build_dir.resolve() / 'Infantry Battle.app'
if options.campaign_only:
    row=run(campaign, 'France-Labels', lambda folder: ['-franceCampaignQA', str(folder), '-qaExit'], 'FRANCE_CAMPAIGN_QA_PASSED')
    (OUT / 'campaign-presentation-verification.json').write_text(json.dumps(dict(passed=True, check=row), indent=2) + '\n')

else:
    rows = [run(campaign, 'France', lambda folder: ['-franceCampaignQA', str(folder), '-qaExit'], 'FRANCE_CAMPAIGN_QA_PASSED')]
    for mode in ['attack', 'defend', 'cancel']:
        rows.append(run(campaign, 'Transition-' + mode, lambda folder, mode=mode: ['--campaign-battle-probe', mode,
            '--campaign-probe-output', str(folder / 'result.json')], 'CAMPAIGN_BATTLE_PROBE_PASSED'))
    for app in [main, infantry]:
        rows.append(run(app, app.stem.replace(' ', '-'), lambda folder: ['--inspect', '85', '--ui-review', str(folder)], 'BATTLE_UI_REVIEW_COMPLETE'))
    (OUT / 'native-verification.json').write_text(json.dumps(dict(passed=True, checks=rows), indent=2) + '\n')
