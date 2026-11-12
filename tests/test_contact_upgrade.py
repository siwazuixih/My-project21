"""20261023 adaptation contracts. Fake plant only; no hardware sockets."""
import ast
import contextlib
import io
import json
import math
from pathlib import Path
import sys
import threading
import time
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT/'ExternalCode'))
import cr10af_hybrid_x as base
import cr10af_exit_optimized as opt
from servo_contact_cycle import ContactCycle


def args(**changes):
    data = dict(execute=True, simulate=True, tool=1, distance_mm=.45, direction=-1.,
        speed_mm_s=1.2, position_step_mm=2., min_position_step_mm=.2,
        control_period_s=1.5, min_servo_duration_s=.2, lateral_trigger_n=24.,
        lateral_clear_n=18., lateral_hard_n=26., lateral_slow_n=18.,
        lateral_slow_ratio=.75, lateral_filter_samples=5, lateral_filter_alpha=.35,
        search_step_mm=.05, search_duration_s=.15, search_settle_s=.05,
        search_rounds=2, max_search_attempts=3, search_improvement_n=.2,
        hard_force_n=30., hard_moment_nm=10., max_runtime_s=90.)
    data.update(changes)
    return SimpleNamespace(**data)


class Plant:
    def __init__(self, force=lambda p: (0.,)*6, stall=False, rotate=False):
        self.p = [0.]*6
        self.force, self.stall, self.rotate = force, stall, rotate
        self.moves, self.stages = [], []
        self.stops, self.closes = 0, 0
        self.age = 0
    def status(self):
        return dict(time=time.monotonic()-self.age, mode=5, error=0, collision=0,
                    force=self.force(self.p), pose=tuple(self.p))
    def pose(self): return tuple(self.p)
    def wrench(self, tool): return self.force(self.p)
    def relative_target(self, x, y, z):
        delta = (-y, x, z) if self.rotate else (x, y, z)
        return tuple(self.p[i] + (delta[i] if i<3 else 0) for i in range(6))
    def move_original_interval(self, x, y, z, duration, stop, lateral):
        self.moves.append((x,y,z,duration,stop,lateral))
        if not self.stall: self.p[:] = self.relative_target(x,y,z)
    def finish_stage(self): self.stops += 1
    def close(self): self.closes += 1
    def begin_stage(self, phase, distance): self.stages.append((phase,distance))
    def end_stage(self): pass


class UpgradeTests(unittest.TestCase):
    def service_functions(self, names, **env):
        path=ROOT/'ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py'
        tree=ast.parse(path.read_text())
        env.setdefault('record_tool_io', lambda *a, **k: None)
        env.setdefault('tool_io_snapshot', lambda: {})
        env.setdefault('contact_cycle', SimpleNamespace(session=Mock(snapshot=Mock(return_value={'contact_session_blocked':False}))))
        env.update(lock=threading.RLock(), print=lambda *a,**kw:None)
        selected=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names]
        exec(compile(ast.Module(body=selected,type_ignores=[]),str(path),'exec'),env)
        return env
    def exit(self, plant, **changes):
        with patch.object(opt.time, 'sleep'):
            return opt.run_exit(args(**changes), base, plant)
    def test_exit_measured_negative_and_shared_lease(self):
        plant=Plant(); result=self.exit(plant)
        self.assertEqual(result['result'], 'completed', result)
        self.assertAlmostEqual(result['moved_mm'], .45)
        self.assertTrue(all(-.15 <= m[0] < 0 and m[4] for m in plant.moves))
        self.assertEqual((plant.stops, plant.closes), (1,0))
    def test_tool_axis_is_not_base_x(self):
        plant=Plant(rotate=True); result=self.exit(plant)
        self.assertEqual(result['result'],'completed',result)
        self.assertAlmostEqual(plant.p[0],0)
        self.assertAlmostEqual(plant.p[1],-.45)
    def test_stalled_pose_never_completes(self):
        plant=Plant(stall=True); result=self.exit(plant)
        self.assertEqual(result['result'],'tracking_error')
        self.assertEqual(len(plant.moves),1)
        self.assertEqual(result['moved_mm'],0)
    def test_axial_soft_stop_and_hard_stop(self):
        for force,expected in ((19,'axial_load_stop'),(31,'safety_stop')):
            plant=Plant(force=lambda p:(force,0,0,0,0,0))
            self.assertEqual(self.exit(plant)['result'],expected)
            self.assertFalse(plant.moves)
    def test_first_improvement_is_kept(self):
        plant=Plant(force=lambda p:(0,6 if p[1]>=.049 else 15,0,0,0,0))
        result=self.exit(plant)
        self.assertEqual(result['result'],'completed',result)
        self.assertEqual(plant.moves[0][:3],(0,.05,0))
        self.assertFalse(any(m[1]<0 for m in plant.moves))
    def test_no_improvement_never_pulls(self):
        plant=Plant(force=lambda p:(0,15,0,0,0,0)); result=self.exit(plant)
        self.assertEqual(result['result'],'lateral_jam')
        self.assertTrue(all(m[0]==0 for m in plant.moves))
        self.assertLessEqual(len(plant.moves),8)
    def test_loaded_steps_remain_small(self):
        plant=Plant(force=lambda p:(10,0,0,0,0,0))
        self.assertEqual(self.exit(plant,distance_mm=.15)['result'],'completed')
        self.assertTrue(all(abs(m[0])<=.05 for m in plant.moves))
    def test_stale_nonfinite_and_cancel_never_move(self):
        for plant in (Plant(force=lambda p:(float('nan'),0,0,0,0,0)),Plant()):
            if not math.isnan(plant.force(plant.p)[0]): plant.age=1
            self.assertNotEqual(self.exit(plant)['result'],'completed')
            self.assertFalse(plant.moves)
        plant=Plant(); event=threading.Event(); event.set()
        result=opt.run_exit(args(),base,plant,event)
        self.assertNotEqual(result['result'],'completed'); self.assertFalse(plant.moves)
    def test_stop_failure_is_not_success(self):
        plant=Plant(); plant.finish_stage=Mock(side_effect=RuntimeError('unconfirmed'))
        self.assertEqual(self.exit(plant)['result'],'safety_stop')
        self.assertEqual(plant.closes,0)
    def test_insert_has_vendor_adaptive_positive_steps(self):
        plant=Plant()
        result=base.HybridExit(args(direction=1,distance_mm=2),plant).run()
        self.assertEqual(result['result'],'completed',result)
        self.assertAlmostEqual(plant.moves[0][0],.1)
        self.assertTrue(all(0 < m[0] <= .1 and m[4] for m in plant.moves))
    def test_deadline_is_checked_inside_stage(self):
        plant=Plant(); controller=base.HybridExit(args(direction=1,max_runtime_s=-1),plant)
        self.assertEqual(controller.run()['result'],'safety_stop')
        self.assertFalse(plant.moves)
    def test_numerical_vendor_methods_are_unchanged(self):
        vendor=ROOT/'ExternalCode/程序和使用文档_X方向修订版_192.168.192.19/ubuntu20_04/cr10af_hybrid_x.py'
        def members(path):
            tree=ast.parse(path.read_text())
            cls=next(n for n in tree.body if isinstance(n,ast.ClassDef) and n.name=='HybridExit')
            return {n.name:ast.dump(n,include_attributes=False) for n in cls.body if isinstance(n,ast.FunctionDef)}
        a,b=members(vendor),members(ROOT/'ExternalCode/cr10af_hybrid_x.py')
        for name in ('lateral_state','lateral_search'): self.assertEqual(a[name],b[name])
        def exit_members(path):
            tree=ast.parse(path.read_text())
            cls=next(n for n in ast.walk(tree) if isinstance(n,ast.ClassDef) and n.name=='OptimizedExit')
            return {n.name:ast.dump(n,include_attributes=False) for n in cls.body if isinstance(n,ast.FunctionDef)}
        raw=ROOT/'ExternalCode/柔顺卡入与退出_V2完整程序及使用方法_20261029/ubuntu20_04_compliant_v2_20261029/cr10af_exit_optimized.py'
        a,b=exit_members(raw),exit_members(ROOT/'ExternalCode/cr10af_exit_optimized.py')
        for name in ('read_pose','wrench','sampled_cost','update_progress','move_checked','search_exit'):
            self.assertEqual(a[name],b[name],name)
    def test_configuration_and_direction_are_explicit(self):
        cycle=ContactCycle(None,None,None)
        cycle.configure(dict(cycle.settings,configuration_id='upgrade'))
        self.assertEqual(cycle.snapshot()['contact_api_version'],5)
        with self.assertRaisesRegex(RuntimeError,'direction'):
            cycle.start(False)
        for field,value in (('exitStageTimeoutSeconds',0),('contactLateralTrigger',25),('positiveXInsertionConfirmed','true')):
            with self.assertRaises(ValueError): cycle.configure(dict(cycle.settings,configuration_id='bad',**{field:value}))
    def test_cycle_routes_correct_directions_and_reports_success_evidence(self):
        cycle=ContactCycle(None,None,None); cycle.settings.update(insertionMm=.2,withdrawalMm=.15)
        plant=Plant()
        with patch.object(opt.time,'sleep'):
            cycle._stage(plant,1,True); cycle._stage(plant,-1,True)
        self.assertEqual(plant.stages,[('INSERTING',.2),('EXITING',.15)])
        evidence=json.loads(cycle.report()['cycle_report_json'])
        self.assertEqual(len(evidence['stages']),2)
    def test_stop_gate_requires_current_fresh_live_feedback(self):
        cycle=ContactCycle(None,lambda:dict(tool_connected=True,feedback_age=0,
             feedback_time=time.monotonic(),live_speed=0,device_error=0,state='OK',
             run_id='current',result_processing=False,tool_stop_confirmed=True),None)
        cycle._wait=lambda seconds, client: time.sleep(.01)
        cycle._tool_stopped(None,'current')
        cycle.read_tool=lambda:dict(state='EXIT_BLOCKED')
        with self.assertRaisesRegex(RuntimeError,'blocked'): cycle._tool_stopped(None,'current')

    def test_stop_gate_rejects_stale_previous_run_or_processing_result(self):
        for changes in ({'feedback_age':1},{'run_id':'old'},{'result_processing':True},
                        {'tool_stop_confirmed':False},{'live_speed':5},{'device_error':1},
                        {'feedback_time':0},{'tool_connected':False}):
            clock=[10.]
            cycle=ContactCycle(None,lambda:dict(tool_connected=True,feedback_age=0,
                feedback_time=clock[0],live_speed=0,device_error=0,state='OK',run_id='current',
                result_processing=False,tool_stop_confirmed=True,**{}),None)
            read=cycle.read_tool
            cycle.read_tool=lambda:dict(read(),**changes)
            cycle._wait=lambda s,c:clock.__setitem__(0,clock[0]+1)
            with patch('servo_contact_cycle.time',SimpleNamespace(monotonic=lambda:clock[0])):
                with self.assertRaisesRegex(RuntimeError,'EXIT_BLOCKED'): cycle._tool_stopped(None,'current')

    def test_result_worker_never_schedules_a_second_robot_cycle(self):
        for stopped in (True,False):
            env=self.service_functions({'_finish_result_worker'}, run_id='current',
                hard_stop_driver=Mock(return_value=True),send_cmd=Mock(return_value=True),
                CMD_STOP_WITH_FEEDBACK=b'stop',wait_tool_stopped=Mock(return_value=stopped),
                save_run_outputs=Mock(), stop_requested=threading.Event(),
                result_busy=threading.Event(),operation_done=threading.Event(),
                command_active=threading.Event(),operation_busy=threading.Event(),
                contact_cycle=SimpleNamespace(active=False),state='OK',tool_stop_confirmed=False)
            env['_finish_result_worker']('OK','message',None)
            self.assertEqual(env['tool_stop_confirmed'],stopped)
            self.assertEqual(env['state'],'OK' if stopped else 'EXIT_BLOCKED')
            env['save_run_outputs'].assert_called_once()
            self.assertTrue(env['operation_done'].is_set())

    def test_status_uses_live_stop_speed_and_monotonic_feedback_age(self):
        stamp=time.monotonic()
        env=self.service_functions({'unity_status','_latest_or_none'},time=time,
            state='OK',torque_raw_data=[28],torque_data=[28],speed_data=[20],angle_data=[99],
            time_data=[0],run_id='current',last_saved_csv='',last_saved_png='',
            last_feedback_time=stamp,last_feedback={'speed':0,'error_code':0},
            sock_lock=threading.Lock(),client_socket=object(),SIMULATION_ONLY=False,IP='fake',PORT=1200,
            result_busy=threading.Event(),tool_stop_confirmed=True,operation_busy=threading.Event(),
            stop_in_progress=threading.Event(),
            command_queue=SimpleNamespace(qsize=lambda:0))
        result=env['unity_status']()
        self.assertEqual((result['speed'],result['live_speed']),(20,0))
        self.assertLess(result['feedback_age'],.25)
        self.assertEqual(result['feedback_time'],stamp)

    def test_old_gateway_is_rejected_before_any_motion(self):
        from unity_robot_client import UnityRobotClient
        client=UnityRobotClient()
        client.request=Mock(side_effect=[dict(token='test-token'),dict(result='released')])
        with patch('unity_robot_client.socket.create_connection'):
            with self.assertRaisesRegex(RuntimeError,'version differs'): client.open()
        self.assertEqual([c.args[0] for c in client.request.call_args_list],['acquire','release'])
        # At actual electric-tool IO, stop blocks late Forward/Reverse, not Stop.
        event=threading.Event(); event.set(); transport=Mock()
        env=self.service_functions({'send_cmd'},SIMULATION_ONLY=False,
            stop_requested=event,sock_lock=threading.Lock(),client_socket=transport,
            CMD_FORWARD=b'f',CMD_FORWARD_NO_FEEDBACK=b'fn',CMD_REVERSE_WITH_FEEDBACK=b'r',
            CMD_REVERSE_NO_FEEDBACK=b'rn',hexstr=lambda x:str(x))
        self.assertFalse(env['send_cmd'](b'f'))
        self.assertFalse(env['send_cmd'](b'r'))
        transport.sendall.assert_not_called()
        self.assertTrue(env['send_cmd'](b'stop'))
        transport.sendall.assert_called_once_with(b'stop')
        # A rejected queue request is not reported as accepted to Unity.
        import queue
        env=self.service_functions({'enqueue_command'},SIMULATION_ONLY=False,
            cycle_lock=threading.RLock(),shutdown_event=threading.Event(),
            operation_busy=threading.Event(),result_busy=threading.Event(),
            stop_in_progress=threading.Event(),operation_done=threading.Event(),
            stop_requested=threading.Event(),command_queue=queue.Queue())
        self.assertTrue(env['enqueue_command']('FORWARD'))
        self.assertFalse(bool(env['enqueue_command']('REVERSE_HOME')))
        self.assertEqual(env['command_queue'].qsize(),1)

    def test_full_cycle_preserves_ng_and_never_exits_on_tool_fault(self):
        for outcome in ('OK','NG_SLIP','JAM','NG_DEVICE','COMM_LOST','FEEDBACK_STALE','TIGHTEN_TIMEOUT'):
            clock=[10.]; started=[False]
            def read():
                return dict(state=outcome if started[0] else 'MONITOR',run_id='current',
                    tool_connected=True,feedback_age=0,feedback_time=clock[0],live_speed=0,
                    device_error=0,result_processing=False,tool_stop_confirmed=started[0])
            cycle=ContactCycle(lambda:started.__setitem__(0,True),read,Mock())
            cycle.session=Mock()  # Interlock persistence covered with real temporary files in test_compliant_v2.
            cycle._wait=lambda seconds, client:clock.__setitem__(0,clock[0]+max(seconds,.1))
            client=Mock()
            with patch('servo_contact_cycle.time',SimpleNamespace(monotonic=lambda:clock[0])), \
                 patch('servo_contact_cycle.UnityRobotClient',return_value=Mock(open=Mock(return_value=client))), \
                 patch.object(cycle,'_stage') as stage:
                cycle._run(False)
            allowed=outcome in ('OK','NG_SLIP','JAM')
            self.assertEqual([c.args[1] for c in stage.call_args_list],[1,-1] if allowed else [1],outcome)
            self.assertEqual(cycle.result,outcome if allowed else 'FAILED')
            self.assertEqual(cycle.phase,'DONE' if outcome=='OK' else 'FAILED')
            if not allowed: cycle.stop_tool.assert_called_once()

    def test_pending_result_or_old_run_prevents_withdrawal(self):
        for bad in ({'result_processing':True},{'tool_stop_confirmed':False},{'run_id':'changed'}):
            clock=[10.]; polls=[0]; started=[False]
            def read():
                polls[0]+=1
                values=dict(state='OK' if started[0] else 'MONITOR',run_id='current',
                    tool_connected=True,feedback_age=0,feedback_time=clock[0],live_speed=0,
                    device_error=0,result_processing=False,tool_stop_confirmed=True)
                # first read following start establishes the fresh run id
                if started[0] and polls[0]>6: values.update(bad)
                return values
            def start(): started[0]=True; polls[0]=5
            cycle=ContactCycle(start,read,Mock())
            cycle.session=Mock()
            cycle._wait=lambda seconds, client:clock.__setitem__(0,clock[0]+max(seconds,.1))
            with patch('servo_contact_cycle.time',SimpleNamespace(monotonic=lambda:clock[0])), \
                 patch('servo_contact_cycle.UnityRobotClient',return_value=Mock(open=Mock(return_value=Mock()))), \
                 patch.object(cycle,'_stage') as stage:
                cycle._run(False)
            self.assertEqual([c.args[1] for c in stage.call_args_list],[1],bad)
            self.assertEqual(cycle.result,'FAILED')


if __name__=='__main__': unittest.main()
