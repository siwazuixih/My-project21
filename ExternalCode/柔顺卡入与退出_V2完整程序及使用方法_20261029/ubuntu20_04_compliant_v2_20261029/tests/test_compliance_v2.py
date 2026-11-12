import tempfile
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import Mock
from unittest.mock import patch
import os
from test_offline import servo

from test_exit_optimization import Plant,simulate
from contact_session import guarded_run,reset_session


class ComplianceV2Tests(unittest.TestCase):
    def test_automatic_cycle_requires_explicitly_validated_stroke(self):
        with patch.dict(os.environ):
            os.environ.pop('CR10AF_DISTANCE_MM',None)
            with patch.object(servo,'run_robot_stage') as stage:
                servo._do_auto_cycle()
            stage.assert_not_called()

    def test_insertion_uses_actual_progress_and_positive_small_steps(self):
        p=Plant(); r=simulate(p,distance=0.3,direction=1)
        self.assertEqual(r['result'],'completed')
        self.assertAlmostEqual(r['moved_mm'],0.3)
        self.assertTrue(all(0<x<=0.1 for x,y,z in p.moves))

    def test_insertion_stops_at_soft_axial_limit_before_hard_limit(self):
        p=Plant(force=lambda p:(13,0,0,0,0,0))
        r=simulate(p,direction=1)
        self.assertEqual(r['result'],'axial_load_stop')
        self.assertEqual(p.moves,[])

    def test_gradual_contact_can_clear_after_more_than_two_rounds(self):
        p=Plant(force=lambda p:(0,max(0,19-60*p[1]),0,0,0,0))
        r=simulate(p)
        self.assertEqual(r['result'],'completed')
        accepted=[h for h in r['history'] if h['phase']=='contact_search']
        self.assertGreater(len(accepted),2)
        self.assertLessEqual(abs(p.p[1]),0.3)

    def test_lateral_improvement_that_transfers_axial_load_is_rejected(self):
        p=Plant(force=lambda p:(5 if p[1]>0.02 else 0,6 if p[1]>0.02 else 15,0,0,0,0))
        r=simulate(p)
        self.assertEqual(r['result'],'lateral_jam')
        self.assertFalse(any(h['phase']=='contact_search' for h in r['history']))

    def test_failure_latches_across_runs_without_calling_motion_again(self):
        args=SimpleNamespace(execute=True,ip='192.0.2.1',tool=1,direction=-1)
        with tempfile.TemporaryDirectory() as d:
            path=Path(d)/'state.json'
            first=guarded_run(args,lambda:{'result':'lateral_jam'},path)
            cb=Mock()
            second=guarded_run(args,cb,path)
            self.assertEqual(second['result'],'contact_session_blocked')
            cb.assert_not_called()
            self.assertEqual(reset_session({'result':'probe_ok','precheck_passed':True},False,path)['result'],'reset_refused')
            self.assertEqual(reset_session({'result':'probe_ok','precheck_passed':False},True,path)['result'],'reset_refused')
            self.assertTrue(path.exists())
            self.assertEqual(reset_session({'result':'probe_ok','precheck_passed':True},True,path)['result'],'session_reset')
            self.assertFalse(path.exists())

    def test_crash_keeps_record_and_dry_run_never_creates_record(self):
        args=SimpleNamespace(execute=False,ip='192.0.2.1',tool=1,direction=1)
        with tempfile.TemporaryDirectory() as d:
            path=Path(d)/'state.json'
            guarded_run(args,lambda:{'result':'dry_run'},path)
            self.assertFalse(path.exists())
            args.execute=True
            with self.assertRaises(RuntimeError):
                guarded_run(args,lambda:(_ for _ in ()).throw(RuntimeError('disconnect')),path)
            self.assertTrue(path.exists())
            self.assertEqual(reset_session({'result':'probe_ok','precheck_passed':True},True,path)['result'],'reset_refused')


if __name__=='__main__': unittest.main()
