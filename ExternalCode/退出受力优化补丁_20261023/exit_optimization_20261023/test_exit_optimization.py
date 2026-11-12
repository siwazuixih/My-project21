"""Offline simulation; EXIT_TEST_TARGET selects the installed program folder."""
import ast
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from types import SimpleNamespace
from unittest.mock import patch

import cr10af_exit_optimized as opt
import install_exit_optimization as installer

TARGET = Path(os.environ.get('EXIT_TEST_TARGET', Path.cwd()))
spec = importlib.util.spec_from_file_location('exit_test_base', TARGET/'cr10af_hybrid_x.py')
base = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = base
spec.loader.exec_module(base)


def plan(direction=-1):
    output=subprocess.check_output([sys.executable,str(TARGET/'cr10af_hybrid_x.py'),
                                    '--direction',str(direction)],cwd=TARGET,text=True)
    return json.loads(output)['plan']


class Plant:
    def __init__(self, force=None, stall=False):
        self.p=[0.0]*6
        self.force=force or (lambda p:(0.0,)*6)
        self.moves=[]
        self.stall=stall
        self.commands=[]

    def command(self,cmd):
        self.commands.append(cmd)
        if cmd!='Stop()': raise AssertionError('Unexpected real command '+cmd)
        return '0,{},Stop();'

    def get(self):
        return {'time':time.monotonic(),'mode':5,'error':0,'collision':0,
                'force':tuple(self.force(self.p)),'pose':tuple(self.p),'tool':1}

    def wait(self): return self.get()
    def close(self): pass


def simulate(plant, distance=0.45):
    args=SimpleNamespace(**dict(plan(),distance_mm=distance,execute=True))
    cls=opt.make_controller(base)
    class Sim(cls):
        def relative_target(self,dx,dy,dz):
            return tuple(plant.p[i]+v for i,v in enumerate((dx,dy,dz,0,0,0)))
        def servo_relative(self,dx,dy,dz,duration,**kwargs):
            # Keep inherited hard-safety checks in the simulated move path.
            self.safety_ok(allow_running=True,enforce_lateral=True)
            plant.moves.append((dx,dy,dz))
            if not plant.stall:
                for i,v in enumerate((dx,dy,dz)): plant.p[i]+=v
            self.safety_ok(allow_running=True,enforce_lateral=True)
    with patch.object(base,'Dashboard',return_value=plant), \
         patch.object(base,'Feedback',return_value=plant), \
         patch.object(base,'pose',side_effect=lambda d:tuple(plant.p)), \
         patch.object(base,'tool_wrench',side_effect=lambda *a:tuple(plant.force(plant.p))), \
         patch.object(opt.time,'sleep'):
        return Sim(args).run()


class ExitTests(unittest.TestCase):
    def test_unloaded_exit_uses_small_negative_segments_and_measured_progress(self):
        p=Plant(); r=simulate(p)
        self.assertEqual(r['result'],'completed')
        self.assertAlmostEqual(r['moved_mm'],0.45)
        self.assertTrue(all(-0.15<=x<0 and y==z==0 for x,y,z in p.moves))

    def test_contact_uses_first_improving_lateral_step_and_stays_there(self):
        p=Plant(force=lambda p:(0,6 if p[1]>=0.049 else 15,0,0,0,0))
        r=simulate(p)
        self.assertEqual(r['result'],'completed')
        self.assertEqual(p.moves[0],(0,0.05,0))
        self.assertFalse(any(y<0 for x,y,z in p.moves))
        self.assertTrue(any(h['phase']=='exit_search' for h in r['history']))

    def test_no_improvement_stops_without_axial_pulling(self):
        p=Plant(force=lambda p:(0,15,0,0,0,0)); r=simulate(p)
        self.assertEqual(r['result'],'lateral_jam')
        self.assertTrue(all(x==0 for x,y,z in p.moves))
        self.assertLessEqual(len(p.moves),8)

    def test_axial_load_stops_before_any_motion(self):
        p=Plant(force=lambda p:(19,0,0,0,0,0)); r=simulate(p)
        self.assertEqual(r['result'],'axial_load_stop')
        self.assertEqual(p.moves,[])

    def test_rising_axial_load_reduces_step_before_protection(self):
        p=Plant(force=lambda p:(10,0,0,0,0,0)); r=simulate(p,distance=0.15)
        self.assertEqual(r['result'],'completed')
        self.assertTrue(all(abs(x)<=0.05 for x,y,z in p.moves))

    def test_stale_feedback_stops_without_motion(self):
        p=Plant()
        original=p.get
        p.get=lambda:dict(original(),time=time.monotonic()-1)
        r=simulate(p)
        self.assertEqual(r['result'],'safety_stop')
        self.assertIn('stale',r['reason'])
        self.assertEqual(p.moves,[])

    def test_hard_force_limit_is_not_relaxed(self):
        p=Plant(force=lambda p:(31,0,0,0,0,0)); r=simulate(p)
        self.assertEqual(r['result'],'safety_stop')
        self.assertEqual(p.moves,[])

    def test_hard_lateral_jump_during_segment_still_stops(self):
        p=Plant(force=lambda p:(0,27 if p[0]<-0.1 else 0,0,0,0,0)); r=simulate(p)
        self.assertEqual(r['result'],'safety_stop')
        self.assertEqual(len(p.moves),1)

    def test_stalled_pose_is_not_reported_as_completed(self):
        p=Plant(stall=True); r=simulate(p)
        self.assertEqual(r['result'],'tracking_error')
        self.assertEqual(r['moved_mm'],0)
        self.assertEqual(len(p.moves),1)

    def test_search_is_bounded_in_total_lateral_distance(self):
        # A shallow reduction never clears contact before the 0.3 mm budget.
        p=Plant(force=lambda p:(0,16-5*p[1],0,0,0,0)); r=simulate(p)
        self.assertNotEqual(r['result'],'completed')
        self.assertLessEqual(abs(p.p[1]),0.3)
        self.assertTrue(all(x==0 for x,y,z in p.moves))

    def test_nonfinite_force_never_causes_motion(self):
        p=Plant(force=lambda p:(float('nan'),0,0,0,0,0)); r=simulate(p)
        self.assertNotEqual(r['result'],'completed')
        self.assertEqual(p.moves,[])

    def test_dry_run_reports_exit_profile_but_insertion_keeps_original_parameters(self):
        exit_plan,insert_plan=plan(-1),plan(1)
        self.assertEqual(exit_plan['exit_strategy'],opt.VERSION)
        self.assertLessEqual(exit_plan['position_step_mm'],0.15)
        self.assertEqual(exit_plan['max_runtime_s'],90.0)
        self.assertNotIn('exit_strategy',insert_plan)
        self.assertEqual(insert_plan['position_step_mm'],2.0)
        self.assertEqual(insert_plan['max_runtime_s'],30.0)

    def test_explicit_runtime_budget_is_honored(self):
        output=subprocess.check_output([sys.executable,str(TARGET/'cr10af_hybrid_x.py'),
                                        '--direction','-1','--max-runtime-s=12'],text=True)
        self.assertEqual(json.loads(output)['plan']['max_runtime_s'],12)


class InstallerTests(unittest.TestCase):
    def test_unrecognized_source_fails_before_edit(self):
        with self.assertRaises(ValueError): installer.patch_robot('def main():\n    pass\n')

    def test_installed_sources_are_idempotent(self):
        r=(TARGET/'cr10af_hybrid_x.py').read_text(encoding='utf-8')
        s=(TARGET/installer.SERVO).read_text(encoding='utf-8')
        self.assertEqual(installer.patch_robot(r),r)
        self.assertEqual(installer.patch_servo(s),s)


if __name__=='__main__':
    unittest.main()
