#!/usr/bin/env python3
"""Publish saved battle/campaign builds; ordinary development never updates this copy."""
from pathlib import Path
import argparse
import datetime
import json
import plistlib
import shutil
import subprocess
import tempfile
from playtest_launcher import create_launcher, copy_snapshot

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--refresh', action='store_true', help='Explicitly release the unified playtest')
parser.add_argument('--source', type=Path, help='Validated immutable unified application to publish')
parser.add_argument('--launcher-dir', type=Path, default=Path('/Applications'), help='Destination for the launcher app')
options = parser.parse_args()
if not options.refresh:
    raise SystemExit("Publication is on hold. Use --refresh only when the user explicitly requests a playtest release.")
options.launcher_dir.mkdir(parents=True, exist_ok=True)
root = Path(__file__).resolve().parent.parent
source = (options.source or root / 'Builds/ETW2.app').resolve()
if not (source / 'Contents/Info.plist').is_file():
    raise SystemExit('Build the unified ETW2 app before publishing a playtest.')
with (source / 'Contents/Info.plist').open('rb') as f:
    executable = plistlib.load(f)['CFBundleExecutable']
if not (source / 'Contents/MacOS' / executable).is_file():
    raise SystemExit('Unified game executable is missing.')
stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
snapshot = root / 'Builds/Playtests' / stamp
saved = snapshot / source.name
snapshot.mkdir(parents=True, exist_ok=False)
copy_snapshot(source, saved)
(snapshot / 'snapshot.json').write_text(json.dumps({
    'published_at': datetime.datetime.now().astimezone().isoformat(),
    'game': str(saved), 'battle': str(saved), 'campaign_map': str(saved),
    'notes': 'One application with campaign, battle scenario selection and campaign tactical battles.',
}, indent=2) + '\n')
launcher = options.launcher_dir / 'ETW2 Playtest.app'
with tempfile.TemporaryDirectory(prefix='etw2-launcher-') as folder:
    staged = Path(folder) / launcher.name
    create_launcher(staged, 'ETW2 Playtest', 'com.andrewcote.etw2.playtest.launcher', {'Battle': saved}, root / 'Temp/SwiftModuleCache')
    if launcher.exists():
        backup = root / 'Temp' / ('Playtest-launcher-' + stamp + '.app')
        backup.parent.mkdir(exist_ok=True)
        shutil.move(str(launcher), str(backup))
    shutil.copytree(staged, launcher, symlinks=True)
if options.launcher_dir == Path('/Applications'):
    subprocess.run(['/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister', '-f', str(launcher)], check=True, capture_output=True)
    subprocess.run(['/usr/bin/mdimport', '-i', str(launcher)], check=True, capture_output=True)
# Retire the old duplicate only after the unified launcher has been created and verified.
with (launcher / 'Contents/Info.plist').open('rb') as f:
    launcher_executable=plistlib.load(f)['CFBundleExecutable']
verified=subprocess.run([str(launcher / 'Contents/MacOS' / launcher_executable), '--verify-target', 'Battle'],check=True,capture_output=True,text=True)
if str(saved) not in verified.stdout:
    raise SystemExit('Unified launcher target verification failed; previous infantry launcher retained.')
legacy=options.launcher_dir / 'ETW2 Infantry Playtest.app'
if legacy.exists():
    backup=root / 'Temp' / ('Retired-infantry-launcher-' + stamp + '.app')
    backup.parent.mkdir(exist_ok=True);shutil.move(str(legacy),str(backup))
# Keep saved launcher copies on the same single application.
if options.launcher_dir.resolve() == Path('/Applications'):
    for directory in [Path.home()/'Applications', Path.home()/'Desktop', root/'Builds/Launchers']:
        mirror=directory/'ETW2 Playtest.app'
        if mirror.exists():
            backup=root/'Temp'/('Saved-launcher-'+directory.name+'-'+stamp+'.app')
            shutil.move(str(mirror),str(backup));shutil.copytree(launcher,mirror,symlinks=True)
            check=subprocess.run([str(mirror/'Contents/MacOS'/launcher_executable),'--verify-target','Battle'],check=True,capture_output=True,text=True)
            if check.stdout.strip()!=str(saved):raise SystemExit('Saved launcher verification failed: '+str(mirror))
        duplicate=directory/'ETW2 Infantry Playtest.app'
        if duplicate.exists():
            backup=root/'Temp'/('Retired-infantry-'+directory.name+'-'+stamp+'.app')
            backup.parent.mkdir(exist_ok=True);shutil.move(str(duplicate),str(backup))
print('Saved unified game:', saved)
print('Launcher:', launcher)
