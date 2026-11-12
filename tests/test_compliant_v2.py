"""V2 + persistent contact integration; only fake plant and temporary files."""
import ast
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import time
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'ExternalCode'))
from contact_session import ContactSession
from servo_contact_cycle import ContactCycle
import cr10af_exit_optimized as opt
import cr10af_hybrid_x as base
from test_contact_upgrade import Plant, args

IDENTITY = dict(robotIp='192.0.2.1', dashboardPort=29999, feedbackPort=30004,
                userNumber=0, toolNumber=1, toolIp='192.0.2.2', toolPort=1200)


class V2Tests(unittest.TestCase):
    def run_plant(self, plant, **changes):
        with patch.object(opt.time, 'sleep'):
            return opt.run_exit(args(search_rounds=8, **changes), base, plant)

    def test_insert_uses_measured_positive_small_steps(self):
        p = Plant()
        result = self.run_plant(p, direction=1, distance_mm=.3)
        self.assertEqual(result['result'], 'completed')
        self.assertAlmostEqual(result['moved_mm'], .3)
        self.assertTrue(all(0 < m[0] <= .1 and m[4] for m in p.moves))
        self.assertEqual(result['plan']['compliance_strategy'], opt.VERSION)

    def test_stalled_insertion_never_finishes(self):
        p = Plant(stall=True)
        self.assertEqual(self.run_plant(p, direction=1)['result'], 'tracking_error')

    def test_insert_axial_soft_stop_before_hard_limit(self):
        p = Plant(force=lambda p: (13, 0, 0, 0, 0, 0))
        self.assertEqual(self.run_plant(p, direction=1)['result'], 'axial_load_stop')
        self.assertFalse(p.moves)

    def test_both_directions_stop_failure_is_not_completed(self):
        for direction in (1, -1):
            p = Plant(); p.finish_stage = Mock(side_effect=RuntimeError('Stop failed'))
            r = self.run_plant(p, direction=direction)
            self.assertEqual(r['result'], 'safety_stop')

    def test_gradual_contact_improves_more_than_two_rounds(self):
        p = Plant(force=lambda p: (0, max(0, 19-60*p[1]), 0, 0, 0, 0))
        r = self.run_plant(p)
        self.assertEqual(r['result'], 'completed', r)
        self.assertGreater(len([h for h in r['history'] if h['phase'] == 'contact_search']), 2)
        self.assertLessEqual(abs(p.p[1]), .3)

    def test_axial_load_transfer_is_rejected(self):
        p = Plant(force=lambda p: (5 if p[1] > .02 else 0, 6 if p[1] > .02 else 15, 0, 0, 0, 0))
        r = self.run_plant(p)
        self.assertEqual(r['result'], 'lateral_jam')
        self.assertFalse(any(h['phase'] == 'contact_search' for h in r['history']))

    def test_tiny_distance_still_moves(self):
        p = Plant(); r = self.run_plant(p, direction=1, distance_mm=.005)
        self.assertEqual(r['result'], 'completed'); self.assertEqual(len(p.moves), 1)
        self.assertAlmostEqual(r['moved_mm'], .005)

    def test_checkpoint_error_stops_and_shared_client_not_closed(self):
        p = Plant()
        r = opt.run_exit(args(direction=1), base, p, checkpoint=Mock(side_effect=OSError('disk full')))
        self.assertEqual(r['result'], 'safety_stop'); self.assertFalse(p.moves)
        self.assertEqual(p.stops, 1); self.assertEqual(p.closes, 0)


class SessionTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix='contact-v2-test-')
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name)/'contact-session.json'
        self.store = ContactSession(self.path)

    def begin(self): self.store.begin(IDENTITY, 'configuration', 'full')

    def cycle(self):
        c = ContactCycle(Mock(), Mock(), Mock(), session_path=self.path)
        c.configure(dict(c.settings, **IDENTITY, configuration_id='configuration', positiveXInsertionConfirmed=True))
        c.simulated = False
        return c

    def test_missing_or_relative_path_fails_closed(self):
        with patch.dict(os.environ, {}, clear=True):
            self.assertTrue(ContactSession().snapshot()['contact_session_blocked'])
        with self.assertRaises(ValueError): ContactSession('relative-state.json')

    def test_cross_instance_exclusive_and_restart_does_not_clear(self):
        self.begin(); other = ContactSession(self.path)
        self.assertTrue(other.snapshot()['contact_session_blocked'])
        with self.assertRaisesRegex(RuntimeError, 'blocked'): other.begin(IDENTITY, 'new', 'full')
        self.assertEqual(other.read()['configuration_id'], 'configuration')

    def test_corrupt_state_does_not_grant_motion(self):
        with self.path.open('w') as f: f.write('{bad')
        self.assertTrue(self.store.snapshot()['contact_session_blocked'])
        with self.assertRaises(Exception): self.store.reset(IDENTITY)
        self.assertTrue(self.path.exists())

    def test_owned_checkpoint_and_completion(self):
        self.begin(); self.store.update(force=True, phase='INSERTING', moved_mm=.1)
        self.assertEqual(self.store.read()['moved_mm'], .1)
        self.store.complete(); self.assertFalse(self.path.exists())

    def test_unowned_update_or_completion_refused(self):
        self.begin(); other = ContactSession(self.path)
        with self.assertRaises(RuntimeError): other.update(force=True, moved_mm=3)
        with self.assertRaises(RuntimeError): other.complete()

    def test_replace_failure_keeps_original_lock(self):
        self.begin()
        with patch('contact_session.os.replace', side_effect=OSError('storage error')):
            with self.assertRaises(OSError): self.store.update(force=True, status='blocked')
        self.assertEqual(self.store.read()['status'], 'incomplete')

    def test_reset_refuses_wrong_identity_and_other_live_pid_for_blocked_state(self):
        self.begin(); self.store.update(force=True, status='blocked', pid=12345)
        with self.assertRaises(RuntimeError): self.store.reset(dict(IDENTITY, toolNumber=2))
        with patch('contact_session.os.kill'):
            with self.assertRaisesRegex(RuntimeError, 'still exists'): self.store.reset(IDENTITY)
        self.assertTrue(self.path.exists())
        with patch('contact_session.os.kill', side_effect=ProcessLookupError): self.store.reset(IDENTITY)
        self.assertFalse(self.path.exists())

    def test_cycle_start_blocked_before_prepare_or_thread(self):
        c = self.cycle(); self.begin(); c.prepare_tool = Mock()
        with patch('servo_contact_cycle.threading.Thread') as thread:
            with self.assertRaisesRegex(RuntimeError, 'blocked'): c.start(False)
        c.prepare_tool.assert_not_called(); thread.assert_not_called()

    def test_insert_only_success_keeps_contact_record(self):
        c = self.cycle(); c.workflow = 'insert'; p = Plant()
        with patch('servo_contact_cycle.UnityRobotClient') as client, patch.object(opt.time, 'sleep'):
            client.return_value.open.return_value = p
            c._run(False)
        self.assertEqual(c.result, 'ROBOT_ONLY_DONE')
        self.assertEqual(c.session.read()['status'], 'blocked')
        self.assertEqual(p.closes, 1)

    def test_failed_release_never_clears_record(self):
        c = self.cycle(); c.workflow = 'withdraw'; p = Plant()
        p.close = Mock(side_effect=RuntimeError('release failed'))
        with patch('servo_contact_cycle.UnityRobotClient') as client, patch.object(c, '_stage'):
            client.return_value.open.return_value = p
            c._run(False)
        self.assertEqual(c.result, 'FAILED'); self.assertTrue(self.path.exists())

    def test_successful_withdrawal_clears_only_after_release(self):
        c = self.cycle(); c.workflow = 'withdraw'; p = Plant()
        def close(): self.assertTrue(self.path.exists())
        p.close = Mock(side_effect=close)
        with patch('servo_contact_cycle.UnityRobotClient') as client, patch.object(c, '_stage'):
            client.return_value.open.return_value = p
            c._run(False)
        self.assertEqual(c.result, 'ROBOT_ONLY_DONE'); self.assertFalse(self.path.exists())

    def test_simulation_never_reads_or_writes_real_contact_store(self):
        c = self.cycle(); c.workflow = 'insert'; c.simulated = True
        self.begin(); original = self.store.read()
        with patch('servo_contact_cycle.UnityRobotClient') as client, patch.object(c, '_stage'):
            client.return_value.open.return_value = Plant()
            c._run(True)
        self.assertEqual(self.store.read(), original)

    def test_human_confirm_and_idle_required_before_probe(self):
        c = self.cycle(); self.begin()
        with patch('servo_contact_cycle.UnityRobotClient.read_only_probe') as probe:
            for confirmed, active in ((False, False), (True, True)):
                c.active = active
                with self.assertRaises(RuntimeError): c.reset_contact_session(confirmed)
            probe.assert_not_called()

    def test_recovery_high_wrench_never_clears(self):
        c = self.cycle(); self.begin()
        with patch('servo_contact_cycle.UnityRobotClient.read_only_probe', return_value=dict(values=[13,0,0,0,0,0],force=[0]*6)):
            with self.assertRaisesRegex(RuntimeError, 'load precheck'): c.reset_contact_session(True)
        self.assertTrue(self.path.exists())

    def test_recovery_requires_fresh_live_stopped_tool_and_matching_identity(self):
        c = self.cycle(); self.begin()
        c.read_tool = lambda: dict(tool_connected=True, feedback_age=0, feedback_time=time.monotonic(),
            live_speed=0, device_error=0, result_processing=False, operation_busy=False, stop_in_progress=False)
        with patch('servo_contact_cycle.UnityRobotClient.read_only_probe', return_value=dict(values=[0]*6,force=[0]*6)):
            c.reset_contact_session(True)
        self.assertFalse(self.path.exists())

    def test_recovery_raw_force_hard_limit_not_hidden_by_global_zero(self):
        c = self.cycle(); self.begin(); c.settings['forceLimitN'] = 10
        with patch('servo_contact_cycle.UnityRobotClient.read_only_probe', return_value=dict(values=[0]*6,force=[20,0,0,0,0,0])):
            with self.assertRaisesRegex(RuntimeError, 'load precheck'): c.reset_contact_session(True)
        self.assertTrue(self.path.exists())

    def test_full_cycle_retains_record_through_wait_tighten_and_release(self):
        for outcome in ('OK', 'NG_SLIP', 'JAM'):
            c = self.cycle(); p = Plant()
            c._tool_stopped = Mock()
            c.start_tool = Mock()
            c.read_tool = lambda: dict(state=outcome, run_id='this-run')
            phases = []
            def wait(seconds, client):
                self.assertTrue(self.path.exists()); phases.append(c.session.read()['phase'])
            c._wait = wait
            c._stage = lambda *a: self.assertTrue(self.path.exists())
            c.finish_tool = lambda: self.assertTrue(self.path.exists())
            with patch('servo_contact_cycle.UnityRobotClient') as client:
                client.return_value.open.return_value = p
                c._run(False)
            self.assertEqual(c.result, outcome)
            self.assertEqual(phases, ['BEFORE_TIGHTEN', 'BEFORE_EXIT'])
            self.assertFalse(self.path.exists())

    def test_failure_or_cancel_between_stages_keeps_record(self):
        for cancelled in (False, True):
            c = self.cycle(); p = Plant(); c._tool_stopped = Mock(); c._stage = Mock()
            def wait(seconds, client):
                if cancelled: c.cancel.set()
                raise RuntimeError('interrupted after insertion')
            c._wait = wait
            with patch('servo_contact_cycle.UnityRobotClient') as client:
                client.return_value.open.return_value = p
                c._run(False)
            self.assertEqual(c.result, 'CANCELLED' if cancelled else 'FAILED')
            self.assertTrue(self.path.exists()); c.start_tool.assert_not_called()
            c.session.reset(c.identity)

    def test_cancel_during_successful_release_cannot_report_success(self):
        c = self.cycle(); c.workflow = 'withdraw'; p = Plant()
        p.close = c.cancel.set
        with patch('servo_contact_cycle.UnityRobotClient') as client, patch.object(c, '_stage'):
            client.return_value.open.return_value = p
            c._run(False)
        self.assertEqual(c.result, 'CANCELLED'); self.assertTrue(self.path.exists())

    def test_manual_queue_rejected_before_cancellation_is_cleared(self):
        from test_contact_upgrade import UpgradeTests
        cancel = threading.Event(); cancel.set()
        env = UpgradeTests().service_functions({'enqueue_command'}, SIMULATION_ONLY=False,
            contact_cycle=SimpleNamespace(session=Mock(snapshot=Mock(return_value={'contact_session_blocked':True}))),
            stop_requested=cancel, command_queue=Mock())
        self.assertFalse(env['enqueue_command']('FORWARD'))
        self.assertFalse(env['enqueue_command']('REVERSE_HOME'))
        self.assertTrue(cancel.is_set()); env['command_queue'].put_nowait.assert_not_called()

    def test_isolated_service_status_report_and_reset_never_access_real_store(self):
        c = self.cycle(); c.isolation_only = True
        with patch.object(c.session, 'read', side_effect=AssertionError('real store accessed')):
            self.assertEqual(c.snapshot()['contact_session_path'], 'isolated-no-real-state')
            self.assertFalse(json.loads(c.report()['cycle_report_json'])['contact_session']['contact_session_blocked'])
            with self.assertRaises(RuntimeError): c.reset_contact_session(True)
            with self.assertRaisesRegex(RuntimeError, 'Isolated'): c.start(False)


if __name__ == '__main__': unittest.main()
