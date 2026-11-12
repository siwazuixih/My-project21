import importlib.util
import json
from pathlib import Path
import subprocess
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from test_force_compat import robot, FakeDashboard, FakeFeedback

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('complete_runner',ROOT/'run.py')
runner=importlib.util.module_from_spec(spec); spec.loader.exec_module(runner)


class CompleteTests(unittest.TestCase):
    def test_high_feedback_force_is_reported_without_any_motion_command(self):
        d,f=FakeDashboard(),FakeFeedback()
        f.item['force']=(31.37,0,0,0,0,0)
        with patch.object(robot,'Dashboard',return_value=d), patch.object(robot,'Feedback',return_value=f):
            r=robot.probe_robot('192.0.2.1',1)
        self.assertEqual(r['result'],'probe_ok')
        self.assertFalse(r['precheck_passed'])
        self.assertAlmostEqual(r['feedback_force_norm_n'],31.37)
        self.assertTrue(any('31.37' in s for s in r['blocked_reasons']))
        self.assertEqual(d.commands,['GetForce()'])
        self.assertFalse(r['motion_sent'])

    def test_high_getforce_axial_load_is_reported_even_if_feedback_small(self):
        d,f=FakeDashboard(),FakeFeedback()
        d.reply='0,{-19,0,0,0,0,0},GetForce();'
        with patch.object(robot,'Dashboard',return_value=d), patch.object(robot,'Feedback',return_value=f):
            r=robot.probe_robot('192.0.2.1',1)
        self.assertFalse(r['precheck_passed'])
        self.assertEqual(r['getforce_force_norm_n'],19)
        self.assertEqual(d.commands,['GetForce()'])

    def test_probe_and_execute_cannot_be_combined(self):
        r=subprocess.run([sys.executable,str(ROOT/'cr10af_hybrid_x.py'),'--probe','--execute'],
                         capture_output=True,text=True)
        self.assertEqual(r.returncode,2)

    def test_launcher_actions_map_to_correct_direction_and_require_execute(self):
        for action,sign in [('insert','1.0'),('exit','-1.0')]:
            args=SimpleNamespace(action=action,ip='192.168.192.19',tool=1,distance_mm=2,execute=False)
            cmd=runner.command(args)
            self.assertEqual(cmd[cmd.index('--direction')+1],sign)
            self.assertNotIn('--execute',cmd)
            args.execute=True
            self.assertIn('--execute',runner.command(args))

    def test_launcher_probe_has_no_state_changing_flag(self):
        args=SimpleNamespace(action='probe',ip='192.168.192.19',tool=1,execute=False)
        self.assertEqual(runner.command(args)[-1],'--probe')
        self.assertNotIn('--execute',runner.command(args))

    def test_force_api_failure_keeps_feedback_diagnostics(self):
        d,f=FakeDashboard(),FakeFeedback()
        with patch.object(robot,'Dashboard',return_value=d), patch.object(robot,'Feedback',return_value=f), \
             patch.object(d,'command',side_effect=RuntimeError('Rejected')):
            r=robot.probe_robot('192.0.2.1',1)
        self.assertEqual(r['result'],'probe_failed')
        self.assertIn('feedback_wrench',r)
        self.assertFalse(r['motion_sent'])


if __name__=='__main__': unittest.main()
