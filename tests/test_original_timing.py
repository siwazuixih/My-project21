"""No devices: compare original algorithm structure and adapter semantics."""
import ast
from pathlib import Path
import sys
import unittest
from types import SimpleNamespace
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'ExternalCode'))
from cr10af_hybrid_x import HybridExit
from servo_contact_cycle import ContactCycle
from unity_robot_client import UnityRobotClient


class OriginalTimingTests(unittest.TestCase):
    def test_search_algorithm_unchanged(self):
        def method(path):
            tree = ast.parse(path.read_text())
            cls = next(n for n in tree.body if isinstance(n, ast.ClassDef) and n.name == 'HybridExit')
            return ast.dump(next(n for n in cls.body if n.name == 'lateral_search'), include_attributes=False)
        self.assertEqual(method(ROOT/'ExternalCode/original/cr10af_hybrid_x.py'),
                         method(ROOT/'ExternalCode/cr10af_hybrid_x.py'))

    def test_stop_flag_and_running_state_preserved(self):
        algorithm = HybridExit(SimpleNamespace(original_contact_timing=True))
        algorithm.dashboard = Mock()
        algorithm.safety_ok = Mock()
        for stop in (False, True):
            algorithm.servo_relative(-2, 0, 0, 1.667, stop_at_end=stop, enforce_lateral=True)
            algorithm.dashboard.move_original_interval.assert_called_with(-2, 0, 0, 1.667, stop, True)
            algorithm.safety_ok.assert_called_with(allow_running=not stop, enforce_lateral=True)
        algorithm.dashboard.move_relative.assert_not_called()

    def test_old_mode_preserved(self):
        algorithm = HybridExit(SimpleNamespace())
        algorithm.dashboard = Mock(); algorithm.safety_ok = Mock()
        algorithm.servo_relative(0, .1, 0, .25)
        algorithm.dashboard.move_relative.assert_called_once()
        algorithm.dashboard.move_original_interval.assert_not_called()

    def test_interval_is_not_arrival(self):
        client = UnityRobotClient(simulate=True)
        client.request = Mock(return_value={'result': 'completed'})
        with self.assertRaises(RuntimeError):
            client.move_original_interval(1, 0, 0, 1, False, True)
        client.request.return_value = {'result': 'interval_elapsed'}
        client.move_original_interval(1, 0, 0, 1, False, True)

    def test_config_echo_and_type(self):
        cycle = ContactCycle(Mock(), Mock(), Mock())
        settings = dict(cycle.settings, configuration_id='original-test', originalContactTiming=True)
        cycle.configure(settings)
        self.assertTrue(cycle.snapshot()['original_contact_timing'])
        with self.assertRaises(ValueError): cycle.configure(dict(settings, originalContactTiming='true'))

    def test_stage_stop_failure_prevents_tightening(self):
        client = Mock()
        client.finish_stage.side_effect = RuntimeError('stop unconfirmed')
        cycle = ContactCycle(Mock(), Mock(), Mock())
        cycle.settings.update(originalContactTiming=True, insertionMm=0)
        with patch('servo_contact_cycle.UnityRobotClient', return_value=Mock(open=Mock(return_value=client))), \
             patch.object(HybridExit, 'safety_ok'):
            cycle._run(False)
        self.assertEqual(cycle.result, 'FAILED')
        cycle.start_tool.assert_not_called()
        client.finish_stage.assert_called_once()

if __name__ == '__main__': unittest.main()
