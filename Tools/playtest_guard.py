"""Keep automated native QA away from saved releases and existing game sessions."""
from pathlib import Path
import datetime
import fcntl
import hashlib
import json
import os
import plistlib
import shutil
import subprocess
import uuid

ROOT = Path(__file__).resolve().parent.parent


def saved_targets():
    return _targets_in((Path('/Applications'), Path.home()/'Applications',
                        Path.home()/'Desktop', ROOT/'Builds/Launchers'))


def _targets_in(folders):
    targets = set()
    for folder in folders:
        for launcher in folder.glob('ETW2*Playtest.app'):
            verifier = launcher/'Contents/MacOS/ETW2Launcher'
            if not verifier.is_file():
                continue
            for mode in ('Battle', 'Campaign map'):
                result = subprocess.run([str(verifier), '--verify-target', mode],
                                        capture_output=True, text=True, timeout=10)
                if result.returncode == 0 and result.stdout.strip():
                    targets.add(Path(result.stdout.strip()).resolve())
    return targets


def require_qa_command(command, *, isolated_qa=None):
    binary = Path(command[0]).resolve()
    app = next((parent for parent in binary.parents if parent.suffix == '.app'), None)
    if app is None:
        raise RuntimeError('Native QA requires an explicit development .app executable.')
    protected_roots = (ROOT/'Builds/Playtests', ROOT/'Builds/InfantryPlaytests',
                       ROOT/'Builds/Launchers', Path('/Applications'),
                       Path.home()/'Applications', Path.home()/'Desktop')
    if any(app.is_relative_to(folder.resolve()) for folder in protected_roots):
        raise RuntimeError(f'Protected playtest: {app}. Use a separate development copy for QA.')
    if isolated_qa is not None:
        if not isinstance(isolated_qa, IsolatedQASession):
            raise RuntimeError('Isolated QA requires an active receipt-scoped session, not a bypass flag.')
        isolated_qa.check(binary, app)
        # Explicitly authorized owned-copy workflow; retain every other launcher check.
        targets = _targets_in((Path('/Applications'), Path.home()/'Applications', ROOT/'Builds/Launchers'))
    else:
        targets = saved_targets()
    if app in {target.resolve() for target in targets}:
        raise RuntimeError(f'Protected playtest: {app}. Use a separate development copy for QA.')
    listing = subprocess.run(['ps', '-axo', 'args='], check=True,
                             capture_output=True, text=True).stdout
    names = {str(binary), str(Path(command[0]).absolute())}
    # macOS may preserve /tmp in argv while resolve() canonicalizes it to /private/tmp.
    if str(binary).startswith('/private/'):
        names.add(str(binary).removeprefix('/private'))
    if any(line.strip() == name or line.strip().startswith(name+' ')
           for line in listing.splitlines() for name in names):
        raise RuntimeError(f'App already running: {app}. Leave it alone and use a separate QA copy.')


def run_qa(command, **kwargs):
    require_qa_command(command)
    return subprocess.run(command, **kwargs)


def popen_qa(command, *, isolated_qa=None, **kwargs):
    require_qa_command(command, isolated_qa=isolated_qa)
    process = subprocess.Popen(command, **kwargs)
    if isolated_qa is not None:
        isolated_qa.record(process, command)
    return process


# Andrew approved this narrower project workflow on 2026-10-02. This does not
# grant OS folder access. Normal run_qa/popen_qa still enumerate every launcher.
ISOLATED_AUTHORIZATION = 'Andrew 2026-10-02 isolated battle benchmark'


def _manifest(app, *, independent=False):
    result = {}
    for path in sorted(app.rglob('*')):
        relative = str(path.relative_to(app))
        if path.is_symlink():
            if not path.resolve().is_relative_to(app.resolve()):
                raise RuntimeError(f'External bundle symlink is not allowed: {path}')
            result[relative] = {'symlink': os.readlink(path)}
        elif path.is_file():
            stat = path.stat()
            if independent and stat.st_nlink != 1:
                raise RuntimeError(f'Owned copy must not contain hardlinked files: {path}')
            digest = hashlib.sha256()
            with path.open('rb') as handle:
                for chunk in iter(lambda: handle.read(1024 * 1024), b''):
                    digest.update(chunk)
            result[relative] = {'sha256': digest.hexdigest(), 'size': stat.st_size,
                                'executable': bool(stat.st_mode & 0o111)}
        elif not path.is_dir():
            raise RuntimeError(f'Unsupported bundle entry: {path}')
    return result


def _digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()


def create_isolated_qa_copy(source, *, owner, authorization):
    """Create a fresh private independent copy; never bless an existing app.

    Assumption explicitly accepted for this workflow: an unread Desktop launcher
    is not deliberately retargeted to the newly generated private random path.
    This is not a claim that Desktop's launcher inventory was inspected.
    """
    if authorization != ISOLATED_AUTHORIZATION or not owner:
        raise RuntimeError('An explicit approved task owner and authorization are required.')
    source = Path(source).resolve()
    if source.suffix != '.app' or not (source/'Contents/Info.plist').is_file():
        raise RuntimeError('Source must be a complete app bundle.')
    before = _manifest(source)
    base = ROOT/'Builds/Development/OwnedQA'
    base.mkdir(parents=True, exist_ok=True)
    if base.absolute() != base.resolve():
        raise RuntimeError('Owned QA base must not use symlink aliases.')
    directory = base/uuid.uuid4().hex
    directory.mkdir(mode=0o700, exist_ok=False)
    app = directory/'Player.app'
    try:
        subprocess.run(['/bin/cp', '-cRp', str(source), str(app)], check=True)
        copied = _manifest(app, independent=True)
        if before != copied or before != _manifest(source):
            raise RuntimeError('Source changed or isolated copy did not verify.')
        for relative, entry in copied.items():
            if 'sha256' in entry and os.path.samefile(source/relative, app/relative):
                raise RuntimeError('Isolated app contains a source-file alias.')
        with (app/'Contents/Info.plist').open('rb') as handle:
            executable = plistlib.load(handle)['CFBundleExecutable']
        binary = app/'Contents/MacOS'/executable
        if not binary.is_file() or not binary.resolve().is_relative_to(app):
            raise RuntimeError('Executable must be a file inside the isolated bundle.')
        receipt = {'schema': 1, 'owner': owner, 'authorization': authorization,
                   'created_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                   'source': str(source), 'app': str(app), 'executable': str(binary),
                   'source_manifest_sha256': _digest(before), 'manifest': copied,
                   'desktop_inventory': 'not read; explicit fresh-private-copy authorization'}
        path = directory/'receipt.json'
        path.write_text(json.dumps(receipt, indent=2))
        return path
    except BaseException:
        # This exclusive directory has never been returned to a launcher.
        shutil.rmtree(directory)
        raise


class IsolatedQASession:
    """Lease one verified receipt; terminate only children spawned in this scope."""
    def __init__(self, receipt):
        self.receipt_path = Path(receipt).absolute()
        self.lock = None
        self.children = []
        self.events = []
        self.event_path = self.receipt_path.parent/('owned-processes-'+uuid.uuid4().hex+'.json')

    def __enter__(self):
        path = self.receipt_path
        directory = path.parent
        base = (ROOT/'Builds/Development/OwnedQA').absolute()
        if (path.name != 'receipt.json' or directory.parent != base or
                path != path.resolve() or directory.name != uuid.UUID(directory.name).hex):
            raise RuntimeError('Receipt must belong to a fresh owned QA directory.')
        self.lock = (directory/'lease.lock').open('a')
        try:
            fcntl.flock(self.lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.receipt = json.loads(path.read_text())
            if (self.receipt.get('schema') != 1 or not self.receipt.get('owner') or
                    self.receipt.get('authorization') != ISOLATED_AUTHORIZATION or
                    self.receipt.get('app') != str(directory/'Player.app') or
                    _digest(self.receipt['manifest']) != self.receipt.get('source_manifest_sha256')):
                raise RuntimeError('Invalid owned QA receipt.')
        except BaseException:
            self.lock.close(); self.lock = None
            raise
        return self

    def check(self, binary, app):
        if self.lock is None or self.lock.closed:
            raise RuntimeError('Owned QA lease is not active.')
        if str(app) != self.receipt['app'] or str(binary) != self.receipt['executable']:
            raise RuntimeError('Command does not match the leased isolated copy.')
        if _manifest(app, independent=True) != self.receipt['manifest']:
            raise RuntimeError('Owned app changed after receipt creation.')

    def record(self, process, command):
        self.children.append(process)
        self.events.append({'pid': process.pid, 'command': list(command),
                            'spawned_utc': datetime.datetime.now(datetime.timezone.utc).isoformat()})
        self._write_events()

    def _write_events(self):
        self.event_path.write_text(json.dumps(self.events, indent=2))

    def __exit__(self, *exception):
        try:
            for process, event in zip(self.children, self.events):
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill(); process.wait(timeout=5)
                event['returncode'] = process.returncode
                event['ended_utc'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
            self._write_events()
        finally:
            if self.lock:
                self.lock.close(); self.lock = None
