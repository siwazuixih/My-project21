"""Benchmarks: clock-domain validation and local fake robot/Unity endpoints only."""
from argparse import Namespace
from contextlib import redirect_stdout
import io
import json
from pathlib import Path
import socket
import sys
import tempfile
import threading
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'ExternalCode'))
from communication_latency_test import stats, frame_age, robot_command, parse_robot_reply, Probe, robot_benchmark, camera_benchmark


class BenchmarkTests(unittest.TestCase):
    def test_statistics_and_missing_values(self):
        self.assertEqual(stats([None, float('nan')])['count'], 0)
        data = stats([1, 2, 3])
        self.assertEqual(data['mean'], 2)
        self.assertAlmostEqual(data['p95'], 2.9)

    def test_camera_clock_domain_not_guessed(self):
        self.assertEqual(frame_age(1000, 'hardware_clock', 2000)[0], None)
        self.assertEqual(frame_age(2000, 'global_time', 2001)[0], 1)
        self.assertEqual(frame_age(2000, 'global_time', 1999)[0], None)
        self.assertEqual(frame_age(2000, 'system_time', 2001, True)[0], None)

    def test_only_read_queries_and_exact_reply(self):
        self.assertEqual(robot_command('angle'), 'GetAngle()')
        with self.assertRaises(ValueError): robot_command('Stop')
        with self.assertRaises(ValueError): robot_command('pose', 51)
        self.assertEqual(parse_robot_reply('0,{1,2,3,4,5,6},GetAngle();', 'GetAngle()')[0], 1)
        for reply in ('-1,{},GetAngle();', '0,{1,2,3,4,5,6},Wrong();', '0,{nan,2,3,4,5,6},GetAngle();'):
            with self.assertRaises(ValueError): parse_robot_reply(reply, 'GetAngle()')

    def test_direct_probe_assembles_fragmented_response(self):
        with FakeEndpoint() as endpoint:
            with Probe('127.0.0.1', endpoint.port, 1) as probe:
                self.assertEqual(probe.query('pose', 0, 1)['values'], [1, 2, 3, 4, 5, 6])
        self.assertEqual(endpoint.commands, ['GetPose(user=0,tool=1)'])

    def test_abba_summary_and_endpoint_match(self):
        with FakeEndpoint() as direct, FakeEndpoint(proxy=True) as unity, tempfile.TemporaryDirectory() as directory:
            unity.robot_endpoint = '127.0.0.1:%d' % direct.port
            args = Namespace(robot_ip='127.0.0.1', dashboard_port=direct.port, unity_host='127.0.0.1',
                unity_port=unity.port, query='pose', user=0, tool=1, samples=4, warmup=0, interval_ms=0, timeout=1)
            with redirect_stdout(io.StringIO()):
                result = robot_benchmark(args, Path(directory))
            self.assertTrue(result['valid_comparison'], result)
            self.assertEqual(result['direct_rtt_ms']['count'], 4)
            self.assertEqual(result['unity_rtt_ms']['count'], 4)
            self.assertIn('extra_mean_ms', result)
            rows = (Path(directory) / 'robot.csv').read_text()
            self.assertIn('dashboard_transport_ms', rows)
            unity.robot_endpoint = 'wrong:29999'
            result = robot_benchmark(args, Path(directory))
            self.assertFalse(result['valid_comparison'])
            self.assertEqual(result['direct_rtt_ms']['count'], 0)

    def test_fake_camera_keeps_wait_and_age_separate(self):
        import numpy as np
        from types import SimpleNamespace
        class Frame:
            def get_timestamp(self): return 1000
            def get_frame_number(self): return 1
            def get_frame_timestamp_domain(self): return 'timestamp_domain.hardware_clock'
            def get_data(self): return np.zeros((2, 2, 3), dtype=np.uint8)
        frames = SimpleNamespace(get_color_frame=lambda: Frame())
        pipeline = SimpleNamespace(start=lambda config: SimpleNamespace(get_device=lambda: SimpleNamespace(get_info=lambda _: 'fake')),
            wait_for_frames=lambda timeout: frames, stop=lambda: None)
        fake = SimpleNamespace(pipeline=lambda: pipeline,
            config=lambda: SimpleNamespace(enable_stream=lambda *a: None),
            stream=SimpleNamespace(color=1, depth=2), format=SimpleNamespace(bgr8=1, z16=2),
            camera_info=SimpleNamespace(name=1, serial_number=2))
        args = Namespace(serial='', width=2, height=2, fps=15, color_only=True, depth_width=2, depth_height=2,
            samples=2, warmup=0, global_time=False)
        with tempfile.TemporaryDirectory() as directory, patch.dict(sys.modules, pyrealsense2=fake):
            result = camera_benchmark(args, Path(directory))
        self.assertTrue(result['completed'])
        self.assertEqual(result['wait_for_frames_ms']['count'], 2)
        self.assertEqual(result['timestamp_to_python_ready_ms_estimate']['count'], 0)
        self.assertEqual(result['duplicate_frames'], 1)


class FakeEndpoint:
    def __init__(self, proxy=False):
        self.listener = socket.socket()
        self.listener.bind(('127.0.0.1', 0))
        self.listener.listen()
        self.listener.settimeout(.1)
        self.port = self.listener.getsockname()[1]
        self.proxy, self.robot_endpoint = proxy, ''
        self.commands = []
        self.stopped = threading.Event()
    def __enter__(self):
        self.thread = threading.Thread(target=self.serve, daemon=True)
        self.thread.start()
        return self
    def __exit__(self, *args):
        self.stopped.set()
        self.thread.join(2)
        self.listener.close()
    def serve(self):
        while not self.stopped.is_set():
            try:
                connection, _ = self.listener.accept()
            except socket.timeout:
                continue
            with connection, connection.makefile('rb') as reader:
                for line in reader:
                    if self.proxy:
                        request = json.loads(line)
                        response = json.dumps(dict(id=request['id'], ok=True, result=request['op'],
                            benchmark_schema='latency-v1-2026-10-08', robot_endpoint=self.robot_endpoint,
                            values=[1, 2, 3, 4, 5, 6], gateway_processing_ms=.5, dashboard_exchange_ms=.3,
                            dashboard_gate_ms=.1, dashboard_transport_ms=.2)) + '\n'
                    else:
                        command = line.decode().strip()
                        self.commands.append(command)
                        response = '0,{1,2,3,4,5,6},' + command + ';'
                    data = response.encode()
                    connection.sendall(data[:3])
                    connection.sendall(data[3:])


if __name__ == '__main__':
    unittest.main()
