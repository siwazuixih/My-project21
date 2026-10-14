"""Integration tests against the actual C# gateway harness, without robot TCP."""
import os
from pathlib import Path
import subprocess
import sys
import time
import threading
import socket
import json
import io
from contextlib import redirect_stdout
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'ExternalCode'))
from unity_robot_client import UnityRobotClient
from servo_contact_cycle import ContactCycle
from communication_latency_test import Probe


class GatewayTests(unittest.TestCase):
    def test_latency_echo_without_hardware_or_motion_lease(self):
        with Probe('127.0.0.1', 19129, 2, True) as probe:
            result = probe.query('pose', 0, 1, echo=True)
            self.assertEqual(result['robot_endpoint'], 'not_connected')
            self.assertEqual(result['result'], 'latency_echo')
            with self.assertRaisesRegex(IOError, '机械臂未连接'):
                probe.query('pose', 0, 1)

    def test_latency_probe_cannot_disturb_current_owner(self):
        client = UnityRobotClient(simulate=True).open()
        try:
            with Probe('127.0.0.1', 19129, 2, True) as probe:
                with self.assertRaisesRegex(IOError, '先结束'):
                    probe.query('pose', 0, 1, echo=True)
            self.assertTrue(client.status()['simulated'])
            client.move_relative(.1, 0, 0, .05)
        finally:
            client.close()

    def test_robot_only_runs_real_algorithm_without_tool_callbacks(self):
        for workflow in ('insert', 'withdraw'):
            def forbidden():
                self.fail('Robot-only test must not invoke a tool callback')
            cycle = ContactCycle(forbidden, forbidden, forbidden)
            cycle.configure(dict(cycle.settings, configuration_id='robot-only-' + workflow,
                originalContactTiming=True, insertionMm=.1, withdrawalMm=.1, contactStepMm=.1))
            cycle.start(True, workflow=workflow)
            deadline = time.monotonic()+5
            while cycle.active and time.monotonic()<deadline:
                time.sleep(.02)
            self.assertFalse(cycle.active)
            self.assertEqual(cycle.result, 'ROBOT_ONLY_DONE', cycle.snapshot())
            self.assertEqual(cycle.phase, 'DONE')

    def test_original_timing_cycle(self):
        cycle = ContactCycle(lambda: None, lambda: {'state': 'OK'}, lambda: None)
        settings = dict(cycle.settings, configuration_id='timing-sim', originalContactTiming=True,
                        insertionMm=.1, withdrawalMm=.1, contactStepMm=.1,
                        beforeTightenSeconds=0, beforeExitSeconds=0)
        cycle.configure(settings)
        cycle.start(True)
        deadline = time.monotonic()+10
        while cycle.active and time.monotonic()<deadline:
            time.sleep(.05)
        self.assertFalse(cycle.active)
        self.assertEqual(cycle.snapshot()['cycle_result'], 'SIMULATED_OK', cycle.snapshot())

    @classmethod
    def setUpClass(cls):
        os.environ['UNITY_ROBOT_PORT'] = '19129'
        dll = ROOT / 'tests/RobotGatewayHarness/bin/Debug/net8.0/RobotGatewayHarness.dll'
        cls.process = subprocess.Popen(['dotnet', str(dll)], stdin=subprocess.PIPE,
                                       stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        for _ in range(20):
            line = cls.process.stdout.readline()
            if 'HARNESS_READY' in line:
                cls.gateway_lines = []
                cls.drain = threading.Thread(target=lambda: cls.gateway_lines.extend(cls.process.stdout), daemon=True)
                cls.drain.start()
                return
            if not line:
                break
        raise RuntimeError('Gateway harness failed to start')

    @classmethod
    def tearDownClass(cls):
        cls.process.stdin.write('quit\n')
        cls.process.stdin.flush()
        cls.process.wait(timeout=5)
        cls.drain.join(2)
        cls.process.stdin.close()
        cls.process.stdout.close()

    def test_trace_matches_request_and_hides_token(self):
        output = io.StringIO()
        with redirect_stdout(output):
            client = UnityRobotClient(simulate=True).open()
            token = client.token
            try:
                client.move_relative(-0.2, 0, 0, 0.05)
                client.wrench(1)
                client.status()
            finally:
                client.close()
        log = output.getvalue()
        self.assertIn('[Python][TX]', log)
        self.assertIn('[Python][RX]', log)
        self.assertIn('"delta": [-0.2, 0, 0]', log)
        self.assertIn('"result": "completed"', log)
        self.assertNotIn(token, log)
        self.assertNotIn('op=status', log)
        import re
        sent = re.findall(r'\[TX\] id=(\w+)', log)
        received = re.findall(r'\[RX\] id=(\w+)', log)
        self.assertEqual(sent, received)

    def test_real_motion_is_not_authorized_by_python(self):
        with self.assertRaisesRegex(RuntimeError, '真实运动许可'):
            UnityRobotClient(simulate=False).open()

    def test_exclusive_lease_and_relative_completion(self):
        client = UnityRobotClient(simulate=True).open()
        try:
            with self.assertRaisesRegex(RuntimeError, '占用'):
                UnityRobotClient(simulate=True).open()
            before = client.pose()
            response = client.move_relative(0.2, 0, 0, 0.05)
            self.assertEqual(response['result'], 'completed')
            self.assertAlmostEqual(client.pose()[0] - before[0], 0.2)
            self.assertTrue(client.status()['simulated'])
        finally:
            client.close()

    def test_invalid_motion_and_cancel_latch(self):
        client = UnityRobotClient(simulate=True).open()
        try:
            with self.assertRaisesRegex(RuntimeError, '位移/速度'):
                client.move_relative(10, 0, 0, 0.5)
            with self.assertRaisesRegex(RuntimeError, '会话已停止'):
                client.move_relative(0.1, 0, 0, 0.05)
        finally:
            client.close()

    def test_stop_from_separate_connection(self):
        client = UnityRobotClient(simulate=True).open()
        try:
            UnityRobotClient.emergency_stop()
            with self.assertRaisesRegex(RuntimeError, '会话已停止'):
                client.move_relative(0.1, 0, 0, 0.05)
        finally:
            client.close()

    def test_cycle_simulation_uses_gateway_without_tool_commands(self):
        def forbidden():
            self.fail('Simulation must not call real tool callback')
        cycle = ContactCycle(forbidden, forbidden, forbidden)
        # Keep real HybridExit search/feed code and gateway protocol; shorten total
        # distance in this test to avoid a 45-second repeated timing test.
        from cr10af_hybrid_x import HybridExit
        original_run = HybridExit.run
        def short_run(obj):
            obj.args.distance_mm = 0.1
            return original_run(obj)
        with patch.object(HybridExit, 'run', short_run), patch.object(cycle, '_wait', side_effect=lambda seconds, client: client.status()):
            cycle.start(True)
            deadline = time.monotonic() + 10
            while cycle.snapshot()['cycle_active'] and time.monotonic() < deadline:
                time.sleep(0.02)
        status = cycle.snapshot()
        self.assertFalse(status['cycle_active'])
        self.assertEqual(status['cycle_phase'], 'DONE', status)
        self.assertEqual(status['cycle_result'], 'SIMULATED_OK', status)

    def test_stop_interrupts_inflight_move(self):
        client = UnityRobotClient(simulate=True).open()
        before = client.pose()
        errors = []
        def move():
            try:
                client.move_relative(0.5, 0, 0, 0.5)
            except RuntimeError as exc:
                errors.append(str(exc))
        worker = threading.Thread(target=move)
        worker.start()
        time.sleep(0.1)
        UnityRobotClient.emergency_stop()
        worker.join(3)
        try:
            self.assertFalse(worker.is_alive())
            self.assertTrue(errors)
            self.assertEqual(client.status()['pose'], list(before))
        finally:
            client.close()

    def test_fragmented_and_coalesced_json_lines(self):
        with socket.create_connection(('127.0.0.1', 19129), timeout=3) as sock:
            sock.settimeout(3)
            with sock.makefile('rb') as reader:
                sock.sendall(b'{"id":"split",')
                time.sleep(0.05)
                sock.sendall(b'"op":"status"}\n{"id":"next","op":"status"}\n')
                self.assertEqual(json.loads(reader.readline())['id'], 'split')
                self.assertEqual(json.loads(reader.readline())['id'], 'next')

    def test_oversized_line_is_rejected(self):
        with socket.create_connection(('127.0.0.1', 19129), timeout=3) as sock:
            sock.settimeout(3)
            sock.sendall(b'x' * 8193 + b'\n')
            self.assertEqual(sock.recv(1), b'')

    @unittest.skipUnless(os.environ.get('RUN_FULL_CYCLE') == '1', '45秒完整时长测试需RUN_FULL_CYCLE=1')
    def test_full_duration_cycle_without_shortcuts(self):
        def forbidden():
            raise AssertionError('模拟不得调用真实电批')
        cycle = ContactCycle(forbidden, forbidden, forbidden)
        cycle.start(True)
        deadline = time.monotonic() + 75
        while cycle.snapshot()['cycle_active'] and time.monotonic() < deadline:
            time.sleep(0.1)
        status = cycle.snapshot()
        self.assertFalse(status['cycle_active'], status)
        self.assertEqual(status['cycle_result'], 'SIMULATED_OK', status)


if __name__ == '__main__':
    unittest.main()
