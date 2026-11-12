"""Lifecycle regressions using AST-selected production functions and fake IO only."""
import ast
import importlib.util
from pathlib import Path
import socket
import threading
import time
from types import SimpleNamespace
import unittest
from unittest.mock import Mock

ROOT = Path(__file__).resolve().parents[1]
V26 = ROOT / 'ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py'
TREE = ast.parse(V26.read_text())


def functions(names, env):
    nodes = [node for node in TREE.body if isinstance(node, ast.FunctionDef) and node.name in names]
    env.setdefault('lock', threading.RLock())
    env.setdefault('state', 'IDLE')
    env.setdefault('contact_cycle', SimpleNamespace(active=False))
    env.setdefault('record_tool_io', lambda *a, **k: None)
    env.setdefault('trace_tool_io', lambda *a, **k: None)
    exec(compile(ast.Module(body=nodes, type_ignores=[]), str(V26), 'exec'), env)
    return env


class Finished(Exception):
    pass


class LifecycleTests(unittest.TestCase):
    def test_bridge_bind_failure_closes_listener(self):
        listener = Mock()
        listener.bind.side_effect = OSError('occupied')
        network = Mock()
        network.socket.return_value = listener
        env = functions({'create_unity_command_listener'}, dict(socket=network,
            UNITY_BRIDGE_HOST='127.0.0.1', UNITY_BRIDGE_PORT=9100))
        with self.assertRaises(OSError):
            env['create_unity_command_listener']()
        listener.close.assert_called_once()
        listener.listen.assert_not_called()

    def test_listener_is_bound_before_device_threads(self):
        bind_index = next(i for i, n in enumerate(TREE.body) if isinstance(n, ast.Assign) and
                          any(isinstance(t, ast.Name) and t.id == 'unity_listener' for t in n.targets))
        start_index = next(i for i, n in enumerate(TREE.body) if isinstance(n, ast.If) and
                           isinstance(n.test, ast.UnaryOp) and isinstance(n.test.operand, ast.Name) and
                           n.test.operand.id == 'SIMULATION_ONLY')
        self.assertLess(bind_index, start_index)

    def test_tool_failed_connect_releases_local_socket(self):
        client = Mock()
        client.connect.side_effect = OSError('unreachable')
        def stop(_):
            raise Finished()
        network = Mock()
        network.socket.return_value = client
        env = functions({'tcp_receive'}, dict(SIMULATION_ONLY=False, client_socket=None,
            recv_frame_count=0, last_print_recv_time=0, socket=network,
            CONNECT_TIMEOUT=3, IP='fake', PORT=1200, sock_lock=threading.Lock(),
            time=SimpleNamespace(sleep=stop), RECONNECT_DELAY=0, print=lambda *a: None))
        with self.assertRaises(Finished):
            env['tcp_receive']()
        client.close.assert_called_once()
        self.assertIsNone(env['client_socket'])

    def test_idle_receive_timeout_does_not_reconnect(self):
        class Client:
            calls = 0
            closed = False
            timeout = None
            def settimeout(self, value): self.timeout = value
            def connect(self, _): pass
            def recv(self, _):
                self.calls += 1
                if self.calls == 1: raise socket.timeout()
                if self.calls == 2: return b'frame'
                raise OSError('test complete')
            def close(self): self.closed = True
        client = Client()
        network = SimpleNamespace(AF_INET=socket.AF_INET, SOCK_STREAM=socket.SOCK_STREAM,
                                  timeout=socket.timeout, socket=Mock(return_value=client))
        sleeps = []
        def sleep(value):
            sleeps.append(value)
            if len(sleeps) == 2: raise Finished()
        env = functions({'tcp_receive'}, dict(SIMULATION_ONLY=False, client_socket=None,
            recv_frame_count=0, last_print_recv_time=0, socket=network, CONNECT_TIMEOUT=3,
            IP='fake', PORT=1200, sock_lock=threading.Lock(), time=SimpleNamespace(sleep=sleep, monotonic=time.monotonic),
            RECONNECT_DELAY=0, print=lambda *a: None, reset_driver=Mock(),
            ENABLE_PHYSICAL_SWITCH_CAPTURE=False, feed_receive_buffer=Mock(return_value=[])))
        with self.assertRaises(Finished): env['tcp_receive']()
        self.assertEqual(client.calls, 3)
        self.assertEqual(network.socket.call_count, 1)
        self.assertEqual(client.timeout, 1.0)
        self.assertTrue(client.closed)

    def test_reader_error_still_closes_socket_and_clears_fields(self):
        spec = importlib.util.spec_from_file_location('client_for_audit', ROOT / 'ExternalCode/unity_robot_client.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        client = module.UnityRobotClient()
        reader, transport = Mock(), Mock()
        reader.close.side_effect = OSError('reader close')
        client.reader, client.sock = reader, transport
        with self.assertRaises(OSError): client.close()
        transport.close.assert_called_once()
        self.assertIsNone(client.reader)
        self.assertIsNone(client.sock)

    def image_source(self, age):
        source_path = ROOT / 'ExternalCode/realsense_image_server.py'
        parsed = ast.parse(source_path.read_text())
        node = next(n for n in parsed.body if isinstance(n, ast.ClassDef) and n.name == 'RealSenseVisionSource')
        method = next(n for n in node.body if isinstance(n, ast.FunctionDef) and n.name == 'get_image')
        env = dict(time=time)
        exec(compile(ast.Module(body=[method], type_ignores=[]), str(source_path), 'exec'), env)
        source = SimpleNamespace(_lock=threading.Lock(), _latest_raw_jpeg=b'jpeg',
            _latest_processed_jpeg=None, _serial='fake', _usb_type='fake',
            _last_frame_time=time.time() - age, _processor=SimpleNamespace(status=lambda: {'ready': False}))
        return env['get_image'], source

    def test_stale_camera_image_not_returned_as_live(self):
        image, source = self.image_source(10)
        with self.assertRaisesRegex(RuntimeError, 'stale'): image(source)

    def test_live_camera_raw_fallback_preserved(self):
        image, source = self.image_source(.05)
        self.assertEqual(image(source)[:2], (b'jpeg', 'raw_fallback'))


if __name__ == '__main__':
    unittest.main()
