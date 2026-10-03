#!/usr/bin/env python3
"""Serialize Unity batches and wait for this project's editors launched outside the wrapper."""
from pathlib import Path
import fcntl
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent.parent
EDITOR = Path('/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity')


def project_editors():
    listing = subprocess.run(['ps', '-axo', 'pid=,args='], capture_output=True, text=True, check=True)
    matches = []
    for line in listing.stdout.splitlines():
        fields = line.strip().split(None, 1)
        if len(fields) != 2:
            continue
        pid, args = fields
        project_argument = '-projectPath ' + str(ROOT)
        if args.startswith(str(EDITOR)) and '-adb2' not in args and (
                project_argument + ' ' in args or args.endswith(project_argument)):
            matches.append(int(pid))
    return matches


def main():
    # Unity removes Temp during editor startup/shutdown; keep the shared lock persistent.
    lock_path = ROOT / 'Library' / 'codex-unity-editor.lock'
    lock_path.parent.mkdir(parents=True, exist_ok=True)
    with lock_path.open('a') as lock:
        try:
            fcntl.flock(lock.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            print('Waiting for another wrapped Unity operation.', flush=True)
            fcntl.flock(lock.fileno(), fcntl.LOCK_EX)
        reported = False
        while project_editors():
            if not reported:
                print('Waiting for this project’s existing Unity editor to close; no processes will be stopped.', flush=True)
                reported = True
            time.sleep(1)
        arguments = sys.argv[1:]
        graphics = '--graphics' in arguments
        if graphics:
            arguments.remove('--graphics')
        # Play-mode verification exits explicitly after asynchronous domain reload.
        self_exit = '--self-exit' in arguments
        if self_exit:
            arguments.remove('--self-exit')
        return subprocess.run([str(EDITOR), '-batchmode', *([] if graphics else ['-nographics']), *([] if self_exit else ['-quit']),
                               '-projectPath', str(ROOT), *arguments], cwd=ROOT).returncode


if __name__ == '__main__':
    sys.exit(main())
