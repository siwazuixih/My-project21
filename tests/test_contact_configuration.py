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
        self.assertEqual(self.cycle.snapshot()['contact_api_version'], 2)

    def test_invalid_numbers_rejected(self):
        for value in (float('nan'), float('inf'), True, -1, 101, '2'):
            with self.subTest(value=value), self.assertRaises(ValueError):
                self.cycle.configure(dict(self.settings, insertionMm=value))

    def test_inconsistent_limits_rejected(self):
        for changes in ({'forceLimitN': 10}, {'contactStageTimeoutSeconds': 1},
                        {'contactSpeedMmS': .1}, {'toolNumber': 1.5}, {'configuration_id': ''}):
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                self.cycle.configure(dict(self.settings, **changes))

    def test_active_settings_cannot_change(self):
        self.cycle.active = True
        with self.assertRaises(RuntimeError): self.cycle.configure(self.settings)

    def test_unconfigured_real_start_rejected_before_thread(self):
        with patch('servo_contact_cycle.threading.Thread') as thread:
            with self.assertRaises(RuntimeError): self.cycle.start(False)
            thread.assert_not_called()

    def test_stage_uses_confirmed_distances_and_limits(self):
        self.cycle.configure(dict(self.settings, insertionMm=4, withdrawalMm=6, toolNumber=2))
        with patch('servo_contact_cycle.HybridExit') as hybrid:
            hybrid.return_value.run.return_value={'result': 'completed'}
            self.cycle._stage(Mock(), -1, True)
            args=hybrid.call_args.args[0]
            self.assertEqual((args.distance_mm, args.tool), (4,2))
            self.cycle._stage(Mock(), 1, True)
            self.assertEqual(hybrid.call_args.args[0].distance_mm, 6)

if __name__ == '__main__': unittest.main()
