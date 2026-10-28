"""Contact diagnostics tests: no device connections and no motor commands."""
import ast
import io
import json
import sys
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'ExternalCode'))
from servo_contact_cycle import ContactCycle


class MotionDiagnosticsTests(unittest.TestCase):
    def cycle(self):
        cycle = ContactCycle(Mock(), Mock(), Mock())
        cycle.configure(dict(cycle.settings, configuration_id='measurement-test', originalContactTiming=True))
        cycle.simulated = False
        return cycle

    def test_robot_only_never_calls_tool(self):
        for workflow, direction in [('insert', -1), ('withdraw', 1)]:
            cycle = self.cycle()
            cycle.workflow = workflow
            client = Mock()
            with patch('servo_contact_cycle.UnityRobotClient', return_value=Mock(open=Mock(return_value=client))), \
                 patch.object(cycle, '_stage') as stage:
                cycle._run(False)
            stage.assert_called_once_with(client, direction, False)
            self.assertEqual(cycle.result, 'ROBOT_ONLY_DONE')
            self.assertEqual(cycle.phase, 'DONE')
            cycle.start_tool.assert_not_called()
            cycle.read_tool.assert_not_called()
            cycle.stop_tool.assert_not_called()
            client.close.assert_called_once()

    def test_robot_only_failure_does_not_start_tool(self):
        cycle = self.cycle()
        cycle.workflow = 'insert'
        client = Mock()
        with patch('servo_contact_cycle.UnityRobotClient', return_value=Mock(open=Mock(return_value=client))), \
             patch.object(cycle, '_stage', side_effect=RuntimeError('actual feedback stale')):
            cycle._run(False)
        cycle.start_tool.assert_not_called()
        cycle.stop_tool.assert_not_called()
        client.stop.assert_called_once()
        self.assertEqual(cycle.first_error, 'actual feedback stale')

    def test_failure_frozen_before_stop_and_cleanup(self):
        cycle = self.cycle()
        client = Mock()
        def stop():
            self.assertEqual(cycle.snapshot()['cycle_first_error'], 'Tightening failed: NG_DEVICE')
            raise RuntimeError('secondary stop failure')
        client.stop.side_effect = stop
        client.close.side_effect = RuntimeError('secondary release failure')
        cycle.read_tool.return_value = dict(state='NG_DEVICE', filtered_torque=7.5, speed=10)
        output = io.StringIO()
        with redirect_stdout(output), \
             patch('servo_contact_cycle.UnityRobotClient', return_value=Mock(open=Mock(return_value=client))), \
             patch.object(cycle, '_stage'), patch.object(cycle, '_wait'):
            cycle._run(False)
        status = cycle.snapshot()
        self.assertEqual(status['cycle_first_error'], 'Tightening failed: NG_DEVICE')
        details = json.loads(status['cycle_failure_json'])
        self.assertEqual(details['phase'], 'TIGHTENING')
        self.assertEqual(details['tool']['filtered_torque'], 7.5)
        self.assertIn('secondary release failure', status['cycle_cleanup_errors'])
        self.assertIn('[FIRST_FAILURE]', output.getvalue())

    def test_hold_failure_captures_force_and_age(self):
        cycle = self.cycle()
        client = Mock()
        client.status.return_value = dict(age_s=.01, mode=5, error=0, collision=0, force=[31, 0, 0, 0, 0, 0])
        with self.assertRaisesRegex(RuntimeError, 'force=.*31') as raised:
            cycle._wait(.1, client)
        cycle._failure(raised.exception)
        data = json.loads(cycle.snapshot()['cycle_failure_json'])
        self.assertEqual(data['robot']['force'][0], 31)
        self.assertEqual(data['robot']['age_s'], .01)

    def test_nonfinite_diagnostic_json_is_valid(self):
        cycle = self.cycle()
        cycle.last_robot = {'force': [float('nan')]}
        cycle._failure(RuntimeError('nonfinite input'))
        self.assertEqual(json.loads(cycle.failure_detail)['robot']['force'], ['nan'])

    def test_measurement_marks_both_stage_boundaries(self):
        cycle = self.cycle()
        client = Mock()
        with patch('servo_contact_cycle.HybridExit') as algorithm:
            algorithm.return_value.run.return_value = {'result': 'completed'}
            cycle._stage(client, -1, False)
        client.begin_stage.assert_called_once_with('INSERTING', 20)
        client.end_stage.assert_called_once()

    def test_service_dispatch_robot_only_and_busy_guard(self):
        # Extract the real dispatcher without importing the module (which starts hardware threads).
        source = (ROOT / 'ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py').read_text()
        node = next(n for n in ast.parse(source).body if isinstance(n, ast.FunctionDef) and n.name == 'run_unity_command')
        cycle = Mock()
        cycle.snapshot.return_value = {}
        namespace = dict(SIMULATION_ONLY=False, state='IDLE', command_queue=Mock(empty=Mock(return_value=True)), contact_cycle=cycle,
                         unity_status=lambda ok=True, message='': {'ok': ok, 'message': message})
        exec(compile(ast.Module(body=[node], type_ignores=[]), '<dispatcher>', 'exec'), namespace)
        self.assertTrue(namespace['run_unity_command']('robot_insert')['ok'])
        cycle.start.assert_called_once_with(simulated=False, workflow='insert')
        namespace['state'] = 'RUN_FORWARD'
        cycle.start.reset_mock()
        self.assertFalse(namespace['run_unity_command']('robot_withdraw')['ok'])
        cycle.start.assert_not_called()


if __name__ == '__main__':
    unittest.main()
