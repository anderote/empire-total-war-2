#!/usr/bin/env python3
"""Review actual battle UI/cursors in both players and verify published launcher code."""
from playtest_guard import run_qa, popen_qa
import argparse
import hashlib
import json
from pathlib import Path
import plistlib
import subprocess

ROOT = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--published', action='store_true')
options = parser.parse_args()
output = ROOT / 'Artifacts/CommandUI'
output.mkdir(parents=True, exist_ok=True)
apps = [ROOT / 'Builds/Empire Battle.app', ROOT / 'Builds/Infantry Battle.app']
required = ['UI_COMPACT_PANEL_AND_CLEAR_DAWN_PASSED', 'UI_COMMAND_CURSOR_PREVIEW_PASSED',
            'UI_HUD_POINTER_EXCLUSION_PASSED', 'BATTLE_UI_REVIEW_COMPLETE']

def assembly(app):
    return app / 'Contents/Resources/Data/Managed/Assembly-CSharp.dll'

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

if options.published:
    runtime = json.loads((output/'runtime-verification.json').read_text())
    verified = {row['app']: row['sha256'] for row in runtime['players']}
    rows = []
    for launcher, targets in [('/Applications/ETW2 Playtest.app', {'Battle': apps[0], 'Campaign map': ROOT/'Builds/ETW2 Campaign.app'}),
                              ('/Applications/ETW2 Infantry Playtest.app', {'Battle': apps[1]})]:
        for mode, source in targets.items():
            saved = Path(subprocess.check_output([str(Path(launcher)/'Contents/MacOS/ETW2Launcher'), '--verify-target', mode], text=True).strip())
            saved_hash = digest(assembly(saved))
            if saved_hash != digest(assembly(source)):
                raise SystemExit('Saved launcher contains stale code: '+launcher+' '+mode)
            if mode=='Battle' and saved_hash!=verified[str(source)]:
                raise SystemExit('Build changed after runtime validation: '+str(source))
            # Compiled type/method names prove this is the cursor/dawn update, not an old matched pair.
            data = assembly(saved).read_bytes()
            if b'CommandCursorAt' not in data or b'DrawReviewCommandCursor' not in data:
                raise SystemExit('Cursor update missing from saved assembly: '+str(saved))
            rows.append(dict(launcher=launcher, target=mode, saved=str(saved), sha256=saved_hash))
            print('PASS saved snapshot:', launcher, mode, flush=True)
    (output/'published-verification.json').write_text(json.dumps(dict(passed=True, targets=rows), indent=2)+'\n')
else:
    rows = []
    for app in apps:
        with (app/'Contents/Info.plist').open('rb') as handle:
            executable = plistlib.load(handle)['CFBundleExecutable']
        folder = output/app.stem
        folder.mkdir(parents=True, exist_ok=True)
        log = folder/'player.log'
        process = popen_qa([str(app/'Contents/MacOS'/executable), '-screen-fullscreen', '0',
            '-screen-width', '1280', '-screen-height', '900', '--inspect', '85', '--ui-review', str(folder), '-logFile', str(log)], cwd=ROOT, stdout=subprocess.DEVNULL)
        try:
            code = process.wait(timeout=150)
        except subprocess.TimeoutExpired:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill(); process.wait()
            raise SystemExit('Battle UI review timed out: '+str(log))
        content = log.read_text()
        if code != 0 or any(marker not in content for marker in required) or 'Exception:' in content or 'Mismatched serialization' in content:
            raise SystemExit('Battle UI review failed: '+str(log))
        rows.append(dict(app=str(app), passed=True, sha256=digest(assembly(app)), checks=required))
        print('PASS runtime command UI:', app, flush=True)
    (output/'runtime-verification.json').write_text(json.dumps(dict(passed=True, players=rows), indent=2)+'\n')
