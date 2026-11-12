import importlib.util
import struct
import time
import unittest
from pathlib import Path
from unittest.mock import patch

path = Path(__file__).resolve().parents[1] / "cr10af_hybrid_x.py"
spec = importlib.util.spec_from_file_location("force_compat_robot", path)
robot = importlib.util.module_from_spec(spec)
spec.loader.exec_module(robot)


class FakeFeedback:
    def __init__(self, tool=1):
        self.item = {"time": time.monotonic(), "tool": tool,
                     "mode": 5, "error": 0, "collision": 0,
                     "force": (0.0,) * 6, "pose": (0.0,) * 6}
        self.closed = False

    def get(self):
        return self.item

    def wait(self):
        return self.item

    def close(self):
        self.closed = True


class FakeDashboard:
    def __init__(self):
        self.commands = []
        self.closed = False
        self.reply = "0,{-0.2124,0.1357,0.5718,-0.0612,0.0058,0.0101},GetForce();"
        self.after_read = lambda: None

    def command(self, cmd):
        self.commands.append(cmd)
        if cmd != "GetForce()":
            raise RuntimeError("Controller rejected " + cmd)
        self.after_read()
        return self.reply

    def close(self):
        self.closed = True


class ForceCompatibilityTests(unittest.TestCase):
    def test_observed_no_argument_reply_is_preserved(self):
        dash = FakeDashboard()
        wrench = robot.tool_wrench(dash, 1, FakeFeedback())
        self.assertEqual(wrench, (-0.2124, 0.1357, 0.5718, -0.0612, 0.0058, 0.0101))
        self.assertEqual(dash.commands, ["GetForce()"])

    def test_wrong_tool_blocks_reading(self):
        dash = FakeDashboard()
        with self.assertRaisesRegex(RuntimeError, "Global tool mismatch"):
            robot.tool_wrench(dash, 1, FakeFeedback(tool=0))
        self.assertEqual(dash.commands, [])

    def test_tool_change_during_read_discards_sample(self):
        dash = FakeDashboard()
        feedback = FakeFeedback()
        dash.after_read = lambda: feedback.item.update(tool=0)
        with self.assertRaisesRegex(RuntimeError, "Global tool mismatch"):
            robot.tool_wrench(dash, 1, feedback)

    def test_stale_feedback_cannot_validate_tool(self):
        feedback = FakeFeedback()
        feedback.item["time"] -= 1
        dash = FakeDashboard()
        with self.assertRaisesRegex(RuntimeError, "stale"):
            robot.tool_wrench(dash, 1, feedback)
        self.assertEqual(dash.commands, [])

    def test_invalid_force_is_rejected(self):
        for payload in ("nan,0,0,0,0,0", "1,2,3"):
            dash = FakeDashboard()
            dash.reply = "0,{" + payload + "},GetForce();"
            with self.assertRaisesRegex(RuntimeError, "6 finite values"):
                robot.tool_wrench(dash, 1, FakeFeedback())

    def test_feedback_tool_offset(self):
        packet = bytearray(robot.PACKET_SIZE)
        struct.pack_into("<H", packet, 0, robot.PACKET_SIZE)
        struct.pack_into("<Q", packet, 48, robot.FEEDBACK_MAGIC)
        packet[1013] = 7
        self.assertEqual(robot.Feedback.decode(packet)["tool"], 7)

    def test_probe_is_read_only_for_matching_and_mismatching_tool(self):
        for tool, expected in ((1, "probe_ok"), (0, "tool_mismatch")):
            dash, feedback = FakeDashboard(), FakeFeedback(tool)
            with patch.object(robot, "Dashboard", return_value=dash), \
                    patch.object(robot, "Feedback", return_value=feedback):
                result = robot.probe_robot("192.0.2.1", 1)
            self.assertEqual(result["result"], expected)
            self.assertEqual(result["active_tool"], tool)
            self.assertFalse(result["motion_sent"])
            self.assertEqual(dash.commands, ["GetForce()"])
            self.assertTrue(dash.closed and feedback.closed)

    def test_motion_guard_rejects_tool_change_before_pose_or_motion(self):
        args = type("Args", (), {"tool": 1})()
        controller = robot.HybridExit(args)
        controller.feedback = FakeFeedback(tool=0)
        controller.dashboard = FakeDashboard()
        with self.assertRaisesRegex(RuntimeError, "Global tool mismatch"):
            controller.servo_relative(1, 0, 0, 1)
        self.assertEqual(controller.dashboard.commands, [])


if __name__ == "__main__":
    unittest.main()
