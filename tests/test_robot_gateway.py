"""Integration tests against the actual C# gateway harness, without robot TCP."""
import os
from pathlib import Path
import subprocess
import sys
import time
import threading
import io
from contextlib import redirect_stdout
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'ExternalCode'))
from unity_robot_client import UnityRobotClient
from servo_contact_cycle import ContactCycle


class GatewayTests(unittest.TestCase):
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


if __name__ == '__main__':
    unittest.main()
