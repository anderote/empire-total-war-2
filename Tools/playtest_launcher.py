"""Build a native macOS playtest launcher with explicit prototype buttons."""
from pathlib import Path
import json
import plistlib
import subprocess
import shutil
import sys
import tempfile


def copy_snapshot(source, destination):
    """Keep independent saved apps; APFS clones avoid duplicating unchanged build data."""
    source, destination = Path(source), Path(destination)
    if destination.exists():
        raise FileExistsError(destination)
    def source_state():
        return {str(p.relative_to(source)): (p.lstat().st_size, p.lstat().st_mtime_ns,
                    str(p.readlink()) if p.is_symlink() else None)
                for p in source.rglob('*')}
    before = source_state()
    if sys.platform == 'darwin':
        subprocess.run(['/bin/cp', '-cRp', str(source), str(destination)], check=True)
    else:
        shutil.copytree(source, destination, symlinks=True)
    if source_state() != before:
        shutil.rmtree(destination)
        raise RuntimeError('Build changed while saving the playtest; retry after its build finishes.')


def create_launcher(destination, name, identifier, targets, cache):
    destination = Path(destination)
    binary = destination / 'Contents/MacOS/ETW2Launcher'
    binary.parent.mkdir(parents=True, exist_ok=True)
    (destination / 'Contents/Resources').mkdir(exist_ok=True)
    cache = Path(cache)
    cache.mkdir(parents=True, exist_ok=True)
    values = ', '.join(json.dumps(key) + ': ' + json.dumps(str(value)) for key, value in targets.items())
    source = '''import AppKit
import Foundation
let targets: [String: String] = [%s]
let args = CommandLine.arguments
if args.count == 3 && args[1] == "--verify-target" {
    guard let target = targets[args[2]], FileManager.default.fileExists(atPath: target) else { exit(1) }
    print(target)
    exit(0)
}
let app = NSApplication.shared
app.setActivationPolicy(.accessory)
var chosen = "Battle"
if targets.count > 1 {
    let alert = NSAlert()
    alert.messageText = "ETW2 Playtest"
    alert.informativeText = "Choose a prototype to play."
    alert.addButton(withTitle: "Battle")
    alert.addButton(withTitle: "Campaign map")
    alert.addButton(withTitle: "Cancel")
    app.activate(ignoringOtherApps: true)
    let response = alert.runModal()
    if response == .alertThirdButtonReturn { exit(0) }
    chosen = response == .alertSecondButtonReturn ? "Campaign map" : "Battle"
}
guard let target = targets[chosen] else { exit(1) }
let config = NSWorkspace.OpenConfiguration()
NSWorkspace.shared.openApplication(at: URL(fileURLWithPath: target), configuration: config) { _, error in
    if let error = error {
        let alert = NSAlert()
        alert.messageText = "Unable to open playtest"
        alert.informativeText = error.localizedDescription
        alert.runModal()
    }
    app.terminate(nil)
}
app.run()
''' % values
    with tempfile.TemporaryDirectory(prefix='etw2-native-launcher-') as folder:
        swift_file = Path(folder) / 'main.swift'
        swift_file.write_text(source)
        subprocess.run(['/usr/bin/swiftc', '-module-cache-path', str(cache), '-O',
                        str(swift_file), '-o', str(binary)], check=True)
    with (destination / 'Contents/Info.plist').open('wb') as handle:
        plistlib.dump({
            'CFBundleName': name, 'CFBundleDisplayName': name,
            'CFBundleIdentifier': identifier, 'CFBundleExecutable': 'ETW2Launcher',
            'CFBundlePackageType': 'APPL', 'CFBundleVersion': '2',
            'CFBundleShortVersionString': '2.0', 'LSMinimumSystemVersion': '13.0',
            'LSApplicationCategoryType': 'public.app-category.games',
        }, handle)
    subprocess.run(['/usr/bin/codesign', '--force', '--sign', '-', str(destination)], check=True, capture_output=True)
