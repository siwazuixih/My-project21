"""No sockets/hardware: validate the actual Python contact configuration contract."""
import sys
from pathlib import Path
import unittest
from unittest.mock import patch, Mock
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'ExternalCode'))
from servo_contact_cycle import ContactCycle

class ConfigurationTests(unittest.TestCase):
    def setUp(self):
        self.cycle = ContactCycle(lambda: None, lambda: {}, lambda: None)
        self.settings = dict(self.cycle.settings, configuration_id='test-session')

    def test_accept_echo_and_copy(self):
        self.settings.update(insertionMm=4, withdrawalMm=6, toolNumber=2)
        self.cycle.configure(self.settings)
        self.settings['insertionMm']=9
        self.assertEqual(self.cycle.settings['insertionMm'], 4)
        self.assertEqual(self.cycle.snapshot()['configuration_id'], 'test-session')
        self.assertEqual(self.cycle.snapshot()['contact_api_version'], 5)

    def test_invalid_numbers_rejected(self):
        for value in (float('nan'), float('inf'), True, -1, 101, '2'):
            with self.subTest(value=value), self.assertRaises(ValueError):
                self.cycle.configure(dict(self.settings, insertionMm=value))

    def test_inconsistent_limits_rejected(self):
        for changes in ({'forceLimitN': 10}, {'contactStageTimeoutSeconds': 0},
                        {'contactSpeedMmS': 0}, {'toolNumber': 1.5}, {'configuration_id': ''}):
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                self.cycle.configure(dict(self.settings, **changes))

    def test_configuration_does_not_start_unused_stage_or_reject_recovery(self):
        self.cycle.configure(dict(self.settings, contactStageTimeoutSeconds=30))
        self.cycle.validate_stage_budgets('withdraw')
        with self.assertRaisesRegex(ValueError, 'No motion dispatched'):
            self.cycle.validate_stage_budgets('insert')
        with self.assertRaises(ValueError): self.cycle.validate_stage_budgets('full')
        self.cycle.configure(dict(self.settings, exitStageTimeoutSeconds=30))
        self.cycle.validate_stage_budgets('insert')
        with self.assertRaises(ValueError): self.cycle.validate_stage_budgets('withdraw')

    def test_real_start_budget_rejected_before_any_side_effect(self):
        self.cycle.configure(dict(self.settings, contactStageTimeoutSeconds=30,
                                  positiveXInsertionConfirmed=True))
        with patch('servo_contact_cycle.threading.Thread') as thread, \
             patch.object(self.cycle.session, 'snapshot') as snapshot, \
             patch.object(self.cycle, 'prepare_tool', Mock()) as prepare:
            for workflow in ('insert', 'full'):
                with self.assertRaisesRegex(ValueError, '30s'):
                    self.cycle.start(False, workflow)
            thread.assert_not_called(); snapshot.assert_not_called(); prepare.assert_not_called()
        self.assertFalse(self.cycle.active)

    def test_budget_uses_capped_speed_and_strict_boundary(self):
        for value in (30, 50):
            self.cycle.configure(dict(self.settings, contactStageTimeoutSeconds=value, contactSpeedMmS=5))
            with self.assertRaises(ValueError): self.cycle.validate_stage_budgets('insert')
        self.cycle.configure(dict(self.settings, contactStageTimeoutSeconds=51))
        self.cycle.validate_stage_budgets('insert')
        self.cycle.configure(dict(self.settings, contactSpeedMmS=.1))
        with self.assertRaises(ValueError): self.cycle.validate_stage_budgets('insert')

    def test_active_settings_cannot_change(self):
        self.cycle.active = True
        with self.assertRaises(RuntimeError): self.cycle.configure(self.settings)

    def test_unconfigured_real_start_rejected_before_thread(self):
        with patch('servo_contact_cycle.threading.Thread') as thread:
            with self.assertRaises(RuntimeError): self.cycle.start(False)
            thread.assert_not_called()

    def test_stage_uses_confirmed_distances_and_limits(self):
        self.cycle.configure(dict(self.settings, insertionMm=4, withdrawalMm=6, toolNumber=2))
        with patch('servo_contact_cycle.run_exit', return_value={'result': 'completed'}) as hybrid:
            self.cycle._stage(Mock(), 1, True)
            args=hybrid.call_args.args[0]
            self.assertEqual((args.distance_mm, args.tool), (4,2))
            self.assertEqual(args.search_rounds, 8)
            with patch('servo_contact_cycle.run_exit', return_value={'result': 'completed'}) as exit_algorithm:
                self.cycle._stage(Mock(), -1, True)
                self.assertEqual(exit_algorithm.call_args.args[0].distance_mm, 6)

if __name__ == '__main__': unittest.main()
