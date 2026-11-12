import contextlib
import io
import json
import subprocess
import sys
import unittest
from types import SimpleNamespace
from unittest.mock import Mock, patch

from test_offline import ROOT, ROBOT_PATH, robot, servo


def dry_plan(*extra):
    completed = subprocess.run(
        [sys.executable, str(ROBOT_PATH), *extra], cwd=ROOT,
        capture_output=True, text=True, timeout=10, check=True,
    )
    result = json.loads(completed.stdout)
    assert result['result'] == 'dry_run'
    return result['plan']


class DirectionTests(unittest.TestCase):
    def test_default_is_exit_negative_x_and_explicit_directions_match(self):
        self.assertEqual(dry_plan()['direction'], -1.0)
        for sign in (1.0, -1.0):
            self.assertEqual(dry_plan('--direction', str(sign))['direction'], sign)

    def test_auto_cycle_passes_insert_positive_exit_negative_to_robot_process(self):
        launched = []

        def fake_popen(cmd, stdout, stderr):
            launched.append(cmd)
            stdout.write(b'{"result":"completed","moved_mm":20.0}')
            stdout.flush()
            return Mock(returncode=0, poll=Mock(return_value=0))

        servo.stop_requested.clear()
        servo.shutdown_event.clear()
        try:
            with contextlib.ExitStack() as stack:
                for name in ('wait_for_tcp_connected', 'hard_stop_driver',
                             'send_cmd', 'wait_tool_stopped', '_launch_tightening'):
                    stack.enter_context(patch.object(servo, name, return_value=True))
                stack.enter_context(patch.object(servo, '_sleep_interruptible', return_value=False))
                stack.enter_context(patch.object(servo, 'EXECUTION_ENABLED', True))
                stack.enter_context(patch.object(servo.subprocess, 'Popen', side_effect=fake_popen))
                stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
                for result in ('OK', 'NG_SLIP', 'JAM'):
                    servo.cycle_active = False
                    servo._do_auto_cycle()
                    self.assertTrue(servo.cycle_active)
                    servo._cycle_exit_after_tighten(result)
                    self.assertFalse(servo.cycle_active)
            self.assertEqual(len(launched), 6)
            self.assertEqual(
                [float(cmd[cmd.index('--direction') + 1]) for cmd in launched],
                [1.0, -1.0] * 3,
            )
        finally:
            servo.cycle_active = False
            servo.stop_requested.clear()
            servo.shutdown_event.clear()

    def test_axis_advance_keeps_requested_sign_without_double_inversion(self):
        plan = dry_plan('--distance-mm', '0.2')
        for sign in (1.0, -1.0):
            controller = robot.HybridExit(SimpleNamespace(**dict(plan, direction=sign, execute=True)))
            with patch.object(robot, 'Dashboard'), patch.object(robot, 'Feedback'), \
                    patch.object(robot, 'tool_wrench', return_value=(0.0,) * 6), \
                    patch.object(controller, 'safety_ok'), \
                    patch.object(controller, 'servo_relative') as move:
                result = controller.run()
            self.assertEqual(result['result'], 'completed')
            self.assertAlmostEqual(sum(c.args[0] for c in move.call_args_list), sign * 0.2)
            self.assertTrue(all(c.args[1:3] == (0, 0) for c in move.call_args_list))


if __name__ == '__main__':
    unittest.main()
