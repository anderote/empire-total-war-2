"""Check protection without launching or stopping any game."""
from pathlib import Path
import subprocess
import json
import os
import plistlib
import shutil
import tempfile
import unittest
from unittest.mock import patch, Mock
import playtest_guard as guard


class PlaytestGuardTests(unittest.TestCase):
    def test_saved_release_and_symlink_are_protected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            app = root/'Builds/Playtests/snapshot/ETW2.app'
            binary = app/'Contents/MacOS/ETW2'
            binary.parent.mkdir(parents=True)
            binary.touch()
            alias = root/'QA.app'
            alias.symlink_to(app, target_is_directory=True)
            with patch.object(guard, 'ROOT', root):
                for candidate in (binary, alias/'Contents/MacOS/ETW2'):
                    with self.assertRaisesRegex(RuntimeError, 'Protected playtest'):
                        guard.require_qa_command([str(candidate), '--benchmark-seconds', '6'])

    def test_launcher_target_outside_snapshot_folder_is_protected(self):
        app = Path('/tmp/custom-saved-release.app')
        with patch.object(guard, 'saved_targets', return_value={app}):
            with self.assertRaisesRegex(RuntimeError, 'Protected playtest'):
                guard.require_qa_command([str(app/'Contents/MacOS/ETW2')])

    def test_existing_user_instance_is_protected(self):
        binary = '/tmp/ETW2 Development.app/Contents/MacOS/ETW2'
        listing = subprocess.CompletedProcess([], 0, stdout=binary+' --battle\n')
        with patch.object(guard, 'saved_targets', return_value=set()), \
             patch.object(guard.subprocess, 'run', return_value=listing):
            with self.assertRaisesRegex(RuntimeError, 'already running'):
                guard.require_qa_command([binary, '--benchmark-seconds', '6'])

    def test_separate_development_app_is_allowed(self):
        listing = subprocess.CompletedProcess([], 0, stdout='/tmp/other.app/Contents/MacOS/ETW2 --battle\n')
        with patch.object(guard, 'saved_targets', return_value=set()), \
             patch.object(guard.subprocess, 'run', return_value=listing):
            guard.require_qa_command(['/tmp/development.app/Contents/MacOS/ETW2'])

    def test_refusal_never_spawns_child(self):
        with patch.object(guard, 'require_qa_command', side_effect=RuntimeError('protected')), \
             patch.object(guard.subprocess, 'Popen') as spawn:
            with self.assertRaises(RuntimeError):
                guard.popen_qa(['/tmp/development.app/Contents/MacOS/ETW2'])
            spawn.assert_not_called()


class OwnedCopyTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.root = Path(self.folder.name).resolve()
        self.root_patch = patch.object(guard, 'ROOT', self.root)
        self.root_patch.start()
        self.source = self.root/'Builds/Playtests/current/Game.app'
        binary = self.source/'Contents/MacOS/Game'
        binary.parent.mkdir(parents=True)
        binary.write_bytes(b'original test executable; never launched')
        binary.chmod(0o755)
        with (self.source/'Contents/Info.plist').open('wb') as handle:
            plistlib.dump({'CFBundleExecutable': 'Game'}, handle)
        self.receipt = guard.create_isolated_qa_copy(self.source, owner='guard-test',
                            authorization=guard.ISOLATED_AUTHORIZATION)
        self.app = self.receipt.parent/'Player.app'
        self.binary = self.app/'Contents/MacOS/Game'

    def tearDown(self):
        self.root_patch.stop()
        self.folder.cleanup()

    def validate(self, session, listing=''):
        with patch.object(guard, 'saved_targets', side_effect=AssertionError('Desktop inventory must not be read here')), \
             patch.object(guard, '_targets_in', return_value=set()) as targets, \
             patch.object(guard.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, stdout=listing)):
            guard.require_qa_command([str(self.binary)], isolated_qa=session)
            self.assertNotIn(Path.home()/'Desktop', targets.call_args.args[0])

    def test_fresh_independent_copy_without_desktop_but_other_checks_retained(self):
        self.assertFalse(os.path.samefile(self.binary, self.source/'Contents/MacOS/Game'))
        with guard.IsolatedQASession(self.receipt) as session:
            self.validate(session)
            with self.assertRaisesRegex(RuntimeError, 'Protected playtest'):
                guard.require_qa_command([str(self.source/'Contents/MacOS/Game')], isolated_qa=session)
        self.assertEqual((self.source/'Contents/MacOS/Game').read_bytes(), b'original test executable; never launched')

    def test_mutated_copy_refused_before_spawn(self):
        self.binary.write_bytes(b'changed')
        with guard.IsolatedQASession(self.receipt) as session, patch.object(guard.subprocess, 'Popen') as spawn:
            with self.assertRaisesRegex(RuntimeError, 'changed'):
                guard.popen_qa([str(self.binary)], isolated_qa=session)
            spawn.assert_not_called()

    def test_external_symlink_and_hardlink_are_refused(self):
        self.binary.unlink()
        self.binary.symlink_to(self.source/'Contents/MacOS/Game')
        with guard.IsolatedQASession(self.receipt) as session:
            with self.assertRaises(RuntimeError):self.validate(session)
        self.binary.unlink()
        os.link(self.source/'Contents/MacOS/Game', self.binary)
        with guard.IsolatedQASession(self.receipt) as session:
            with self.assertRaisesRegex(RuntimeError, 'hardlinked'):self.validate(session)

    def test_existing_instance_and_known_launcher_target_remain_protected(self):
        with guard.IsolatedQASession(self.receipt) as session:
            with self.assertRaisesRegex(RuntimeError, 'already running'):
                self.validate(session, str(self.binary)+' --battle\n')
            with patch.object(guard, '_targets_in', return_value={self.app}):
                with self.assertRaisesRegex(RuntimeError, 'Protected playtest'):
                    guard.require_qa_command([str(self.binary)], isolated_qa=session)

    def test_no_boolean_bypass_or_inactive_session(self):
        with self.assertRaisesRegex(RuntimeError, 'active receipt-scoped'):
            guard.require_qa_command([str(self.binary)], isolated_qa=True)
        with self.assertRaisesRegex(RuntimeError, 'not active'):
            guard.require_qa_command([str(self.binary)], isolated_qa=guard.IsolatedQASession(self.receipt))

    def test_same_receipt_cannot_be_leased_twice(self):
        with guard.IsolatedQASession(self.receipt):
            with self.assertRaises(BlockingIOError):
                with guard.IsolatedQASession(self.receipt):pass

    def test_cleanup_signals_only_session_child_and_records_pid(self):
        owned = Mock(pid=123456, returncode=0)
        owned.poll.return_value = None
        with guard.IsolatedQASession(self.receipt) as session:
            session.record(owned, [str(self.binary)])
        owned.terminate.assert_called_once_with()
        owned.wait.assert_called_once_with(timeout=5)
        owned.kill.assert_not_called()
        self.assertEqual(json.loads(session.event_path.read_text())[0]['pid'], 123456)

    def test_no_existing_app_can_be_registered_by_receipt_path(self):
        rogue = self.root/'receipt.json'
        shutil.copy2(self.receipt, rogue)
        with self.assertRaises(RuntimeError):
            with guard.IsolatedQASession(rogue):pass


if __name__ == '__main__':
    unittest.main()
