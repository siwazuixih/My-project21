import importlib.util
import json
import os
import subprocess
import sys
import time
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SERVO_PATH = ROOT / "servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py"
ROBOT_PATH = ROOT / "cr10af_hybrid_x.py"


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


servo = load_module("servo_under_test", SERVO_PATH)
robot = load_module("robot_under_test", ROBOT_PATH)


def feedback_frame(torque_raw=1000, speed_raw=20, status=0, error=0,
                   torque_unit=1, direction=0):
    """Create one valid 41-byte tool feedback frame for parser tests."""
    body = bytearray(37)
    body[0:4] = b"\x55\xAA\x25\x81"
    body[6] = torque_unit
    body[7:9] = int(torque_raw).to_bytes(2, "little", signed=False)
    body[9:11] = int(speed_raw).to_bytes(2, "little", signed=True)
    body[13:15] = (123).to_bytes(2, "little")
    body[15:17] = (250).to_bytes(2, "little")
    body[17] = direction
    body[21] = status
    body[22] = error
    crc = servo.crc16_modbus(bytes(body))
    return bytes(body) + crc.to_bytes(2, "little") + b"\r\n"


class ProtocolTests(unittest.TestCase):
    def test_crc_known_modbus_vector(self):
        self.assertEqual(servo.crc16_modbus(b"123456789"), 0x4B37)

    def test_command_frame_shape_and_crc(self):
        frame = servo.build_pc_cmd(1, 1)
        self.assertEqual(len(frame), 11)
        self.assertEqual(frame[:7], b"\x55\xAA\x07\x01\x00\x01\x01")
        self.assertEqual(frame[-2:], b"\r\n")
        self.assertEqual(
            int.from_bytes(frame[7:9], "little"),
            servo.crc16_modbus(frame[:7]),
        )

    def test_feedback_parser_and_split_frames(self):
        frame = feedback_frame(torque_raw=1000, speed_raw=12)
        parsed = servo.parse_feedback_frame(frame)
        self.assertIsNotNone(parsed)
        self.assertAlmostEqual(parsed["torque"], 1.0)
        self.assertEqual(parsed["speed"], 12)

        buf = bytearray(b"\x55")
        self.assertEqual(servo.feed_receive_buffer(buf), [])
        buf.extend(frame[1:])
        self.assertEqual(servo.feed_receive_buffer(buf), [frame])
        self.assertEqual(buf, bytearray())

    def test_bad_crc_is_discarded_and_next_frame_survives(self):
        bad = bytearray(feedback_frame())
        bad[7] ^= 0x01
        good = feedback_frame(torque_raw=2000)
        buf = bytearray(bad + good)
        frames = servo.feed_receive_buffer(buf)
        self.assertEqual(frames, [good])
        self.assertEqual(buf, bytearray())


class SafetyLogicTests(unittest.TestCase):
    def test_lateral_hysteresis(self):
        args = type("Args", (), {
            "lateral_filter_samples": 3,
            "lateral_filter_alpha": 0.5,
            "lateral_trigger_n": 24.0,
            "lateral_clear_n": 18.0,
        })()
        controller = robot.HybridExit(args)
        self.assertEqual(controller.lateral_state((1, 1, 1, 0, 0, 0))[1], False)
        for _ in range(4):
            filtered, active = controller.lateral_state((0, 25, 0, 0, 0, 0))
        self.assertGreaterEqual(filtered, 24.0)
        self.assertTrue(active)
        controller.lateral_state((0, 0, 0, 0, 0, 0))
        _, active = controller.lateral_state((0, 0, 0, 0, 0, 0))
        self.assertFalse(active)

    def test_robot_dry_run_does_not_open_network(self):
        argv = [
            sys.executable, str(ROBOT_PATH),
            "--ip", "192.0.2.1", "--distance-mm", "20",
            "--direction", "1", "--speed-mm-s", "1.2",
        ]
        completed = subprocess.run(argv, cwd=ROOT, capture_output=True,
                                   text=True, timeout=10, check=False)
        self.assertEqual(completed.returncode, 0, completed.stderr)
        result = json.loads(completed.stdout)
        self.assertEqual(result["result"], "dry_run")

    def test_servo_check_does_not_open_network(self):
        argv = [
            sys.executable, str(SERVO_PATH), "--check",
        ]
        env = os.environ.copy()
        env.pop("DISPLAY", None)
        completed = subprocess.run(argv, cwd=ROOT, capture_output=True,
                                   text=True, timeout=10, check=False, env=env)
        self.assertEqual(completed.returncode, 0, completed.stderr)
        result = json.loads(completed.stdout)
        self.assertEqual(result["mode"], "offline_check")
        self.assertEqual(result["buffer_seconds"], 5.0)

    def test_ok_slip_and_jam_all_request_robot_exit(self):
        original = {
            "hard_stop_driver": servo.hard_stop_driver,
            "send_cmd": servo.send_cmd,
            "wait_tool_stopped": servo.wait_tool_stopped,
            "save_run_outputs": servo.save_run_outputs,
            "_cycle_exit_after_tighten": servo._cycle_exit_after_tighten,
        }
        requested = []
        try:
            servo.hard_stop_driver = lambda *a, **k: True
            servo.send_cmd = lambda *a, **k: True
            servo.wait_tool_stopped = lambda: True
            servo.save_run_outputs = lambda *a, **k: None
            servo._cycle_exit_after_tighten = lambda result: requested.append(result)
            for result in ("OK", "NG_SLIP", "JAM"):
                servo.stop_requested.clear()
                servo.shutdown_event.clear()
                servo.result_busy.set()
                with servo.cycle_lock:
                    servo.cycle_active = True
                servo._finish_result_worker(result, result, None)
            self.assertEqual(requested, ["OK", "NG_SLIP", "JAM"])
        finally:
            for name, value in original.items():
                setattr(servo, name, value)
            servo.result_busy.clear()
            servo.stop_requested.clear()
            servo.shutdown_event.clear()
            with servo.cycle_lock:
                servo.cycle_active = False


if __name__ == "__main__":
    unittest.main()
