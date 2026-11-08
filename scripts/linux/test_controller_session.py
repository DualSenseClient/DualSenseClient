import importlib.util
from pathlib import Path
import unittest
import sys
from unittest.mock import Mock, patch

spec = importlib.util.spec_from_file_location('session', Path(__file__).with_name('controller-session.py'))
session = importlib.util.module_from_spec(spec)
spec.loader.exec_module(session)


class ConnectionLauncherTests(unittest.TestCase):
    @patch.object(session.subprocess, 'Popen')
    def test_disconnect_and_reconnect_preserve_child(self, popen):
        child = popen.return_value
        child.poll.return_value = None
        watcher = session.ConnectionLauncher(['/Apps/My Client', '--controller-connect'], {'DISPLAY': ':0'})
        watcher.update({'AA'})
        watcher.update(set())
        watcher.update({'AA'})
        watcher.update({'AA', 'BB'})
        popen.assert_called_once_with(['/Apps/My Client', '--controller-connect'], env={'DISPLAY': ':0'})
        child.terminate.assert_not_called()
        child.kill.assert_not_called()

    @patch.object(session.subprocess, 'Popen')
    def test_explicit_exit_waits_for_next_connection(self, popen):
        popen.return_value.poll.return_value = 0
        watcher = session.ConnectionLauncher(['client'], {})
        watcher.update({'AA'})
        watcher.update({'AA'})
        self.assertEqual(popen.call_count, 1)
        watcher.update(set())
        watcher.update({'AA'})
        self.assertEqual(popen.call_count, 2)

    @patch.object(session.subprocess, 'run')
    def test_query_failure_is_not_disconnection(self, run):
        run.return_value = Mock(returncode=1, stdout='')
        with self.assertRaises(RuntimeError):
            session.connected('AA')
        run.return_value = Mock(returncode=0, stdout='\tConnected: yes\n')
        self.assertTrue(session.connected('AA'))

    @patch.object(session, 'connected')
    def test_unavailable_address_does_not_block_another_controller(self, connected):
        connected.side_effect = [RuntimeError('forgotten pairing'), True]
        self.assertEqual(session.sample_connections(['old', 'new'], set()), {'new'})
        connected.side_effect = [RuntimeError('temporary failure'), False]
        self.assertEqual(session.sample_connections(['old', 'new'], {'old'}), {'old'})

    @patch.object(session.subprocess, 'run')
    def test_session_start_replaces_stale_display_environment(self, run):
        with patch.object(sys, 'argv', ['watcher', '--session-start']), patch.dict(session.os.environ, {'DISPLAY': ':9'}, clear=True):
            session.main()
        self.assertIn('unset-environment', run.call_args_list[0].args[0])
        self.assertEqual(run.call_args_list[1].args[0], ['systemctl', '--user', 'import-environment', 'DISPLAY'])
        self.assertEqual(run.call_args_list[2].args[0], ['systemctl', '--user', 'restart', 'dualsense-controller-launch.service'])


if __name__ == '__main__':
    unittest.main()
