"""Regression of actual adapter functions in combination; no hardware imports/IO."""
import ast
import json
import queue
import socket
import threading
from pathlib import Path
from types import SimpleNamespace
import sys
import unittest
from unittest.mock import Mock

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'ExternalCode'))
from unity_robot_client import UnityRobotClient

SERVICE = ROOT / 'ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py'


def functions(names, **values):
    tree = ast.parse(SERVICE.read_text())
    selected = [n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name in names]
    env = dict(print=lambda *a, **k: None, record_tool_io=lambda *a, **k: None,
               trace_tool_io=lambda *a, **k: None, tool_io_snapshot=lambda: {})
    env.update(values)
    exec(compile(ast.Module(body=selected, type_ignores=[]), str(SERVICE), 'exec'), env)
    return env


class Clock:
    def __init__(self):
        self.now = 100.
        self.after_sleep = lambda: None
    def monotonic(self): return self.now
    def time(self): return self.now
    def sleep(self, seconds):
        self.now += max(seconds, .000001)
        self.after_sleep()


def tool_fixture(extra=()):
    clock = Clock()
    transport = SimpleNamespace(writes=[], close=Mock())
    transport.sendall = lambda packet: transport.writes.append(packet)
    names = {'send_cmd', 'hexstr', 'hard_stop_driver', '_sleep_interruptible',
             '_do_start_forward', '_launch_tightening', 'wait_tool_stopped',
             'prepare_cycle_tool', 'hold_cycle_tool', '_finish_result_worker',
             'quick_stop_for_home', 'fast_stop_after_home_reverse', 'send_cmd_burst',
             '_reverse_to_home'} | set(extra)
    env = functions(names, time=clock, SIMULATION_ONLY=False,
        stop_requested=threading.Event(), shutdown_event=threading.Event(),
        cycle_lock=threading.RLock(), lock=threading.RLock(), sock_lock=threading.Lock(),
        command_active=threading.Event(), result_busy=threading.Event(),
        stop_in_progress=threading.Event(), operation_busy=threading.Event(),
        operation_done=threading.Event(), command_queue=queue.Queue(), client_socket=transport,
        CMD_FORWARD=b'forward', CMD_FORWARD_NO_FEEDBACK=b'forward-no-feedback',
        CMD_REVERSE_WITH_FEEDBACK=b'reverse', CMD_REVERSE_NO_FEEDBACK=b'reverse-no-feedback',
        CMD_STOP_WITH_FEEDBACK=b'stop', CMD_RESET=b'reset', CMD_LOCK=b'lock',
        wait_for_tcp_connected=lambda **k: True, close_current_socket=Mock(),
        send_cmd_new_connection=Mock(return_value=True), state='MONITOR', fault_latched=False,
        last_feedback={'speed': 0., 'error_code': 0}, last_feedback_time=clock.now,
        EXIT_STOP_TIMEOUT_S=3., EXIT_STOP_DWELL_S=.3, run_id='current',
        contact_cycle=SimpleNamespace(active=False), tool_stop_confirmed=False,
        save_run_outputs=Mock(), REVERSE_HOME_POST_STOP_REPEAT=3, REVERSE_HOME_POST_STOP_GAP=.015,
        REVERSE_HOME_MAX_TIME=2., REVERSE_HOME_MIN_RUN_TIME=.15,
        REVERSE_HOME_MOTION_SPEED_TH=2., REVERSE_HOME_STOP_SPEED_TH=1.,
        REVERSE_HOME_RELEASE_TORQUE_TH=1.2, REVERSE_HOME_STABLE_DWELL=.25)
    env['clear_run'] = lambda value: env.update(state=value, fault_latched=False)
    clock.after_sleep = lambda: env.update(last_feedback_time=clock.now)
    return env, clock, transport


class OptionalTargetTests(unittest.TestCase):
    def client(self):
        client = UnityRobotClient(simulate=True)
        sent = []
        client._exchange = lambda payload, *a: sent.append(json.loads(json.dumps(payload))) or {'result': 'interval_elapsed'}
        return client, sent
    def test_insert_omits_optional_target_instead_of_json_null(self):
        client, sent = self.client()
        client.move_original_interval(1.8, 0, 0, 1.5, False, True)
        self.assertNotIn('expected_target', sent[-1])
    def test_exit_preserves_six_value_precomputed_target(self):
        client, sent = self.client()
        client.preview = ([-.15, 0, 0], [1., 2., 3., 4., 5., 6.])
        client.move_original_interval(-.15, 0, 0, .25, True, True)
        self.assertEqual(sent[-1]['expected_target'], [1., 2., 3., 4., 5., 6.])
        self.assertIsNone(client.preview)
    def test_mismatching_preview_is_not_reused_or_sent_as_null(self):
        client, sent = self.client()
        client.preview = ([.1, 0, 0], [1.] * 6)
        client.move_original_interval(.2, 0, 0, .25, False, True)
        self.assertNotIn('expected_target', sent[-1])
        self.assertIsNone(client.preview)


class StopCombinationTests(unittest.TestCase):
    def test_normal_preparation_then_start_uses_real_stop_and_launch(self):
        env, _, transport = tool_fixture()
        self.assertTrue(env['_do_start_forward']())
        self.assertEqual(transport.writes.count(b'forward'), 1)
        self.assertFalse(env['stop_requested'].is_set())
    def test_cycle_hold_then_start_is_not_a_user_cancel(self):
        env, _, transport = tool_fixture()
        env['prepare_cycle_tool']()
        env['hold_cycle_tool']()
        self.assertTrue(env['_do_start_forward']())
        self.assertEqual(transport.writes.count(b'forward'), 1)
    def test_normal_result_stop_then_fresh_feedback_can_confirm(self):
        env, _, _ = tool_fixture()
        env['state'] = 'OK'
        env['result_busy'].set()
        env['_finish_result_worker']('OK', 'done', None)
        self.assertTrue(env['tool_stop_confirmed'])
        self.assertEqual(env['state'], 'OK')
        self.assertFalse(env['stop_requested'].is_set())
        env['save_run_outputs'].assert_called_once()
    def test_manual_stop_during_preparation_prevents_late_forward(self):
        env, clock, transport = tool_fixture()
        clock.after_sleep = env['stop_requested'].set
        self.assertFalse(env['_do_start_forward']())
        self.assertNotIn(b'forward', transport.writes)
        self.assertTrue(env['stop_requested'].is_set())
    def test_ordinary_stop_never_clears_existing_user_cancel(self):
        env, _, transport = tool_fixture()
        env['stop_requested'].set()
        self.assertTrue(env['hard_stop_driver']('normal stop', repeat=1, delay=0))
        self.assertFalse(env['_do_start_forward']())
        self.assertTrue(env['stop_requested'].is_set())
        self.assertNotIn(b'forward', transport.writes)
    def test_cancelled_result_never_reports_stop_confirmation(self):
        env, _, _ = tool_fixture()
        env['state'] = 'OK'
        env['stop_requested'].set()
        env['_finish_result_worker']('OK', 'done', None)
        self.assertFalse(env['tool_stop_confirmed'])
        self.assertEqual(env['state'], 'EXIT_BLOCKED')
        self.assertTrue(env['stop_requested'].is_set())
    def test_no_feedback_never_reports_stopped(self):
        env, clock, _ = tool_fixture()
        env['last_feedback'] = None
        clock.after_sleep = lambda: None
        env['hard_stop_driver']('normal stop', repeat=1, delay=0)
        self.assertFalse(env['wait_tool_stopped']())
    def test_reverse_pre_and_post_stop_do_not_turn_success_into_cancel(self):
        env, clock, transport = tool_fixture()
        reverse_started = [None]
        def send(packet):
            transport.writes.append(packet)
            if packet == b'reverse': reverse_started[0] = clock.now
        transport.sendall = send
        def feedback():
            age = 0 if reverse_started[0] is None else clock.now - reverse_started[0]
            env.update(last_feedback_time=clock.now,
                       last_feedback={'speed': 10 if 0 < age < .25 else 0,
                                      'filtered_torque': 0., 'error_code': 0})
        clock.after_sleep = feedback
        env['_reverse_to_home']('MANUAL_REVERSE_HOME')
        self.assertEqual(transport.writes.count(b'reverse'), 1)
        self.assertEqual(env['state'], 'RELEASE_CONFIRMED')
        self.assertFalse(env['stop_requested'].is_set())


class ToolDiagnosticsTests(unittest.TestCase):
    def environment(self, extra=()):
        clock=Clock()
        env=functions({'record_tool_io','tool_io_snapshot','trace_tool_io',
                       'feed_receive_buffer','parse_feedback_frame','crc16_modbus','u16_le'} | set(extra),
            tool_io_lock=threading.Lock(), tool_io={'connection_id':0}, time=clock, json=json)
        env['record_tool_io']('connected')
        return env,clock
    def frame(self, env, opcode=.0):
        packet=bytearray(41)
        packet[:4]=bytes([0x55,0xAA,37,int(opcode) if opcode else 0x81])
        packet[6]=1
        packet[-4:-2]=env['crc16_modbus'](packet[:-4]).to_bytes(2,'little')
        packet[-2:]=b'\r\n'
        return bytes(packet)
    def test_no_bytes_is_distinct_from_received_but_invalid_frame(self):
        env,_=self.environment()
        self.assertEqual(env['tool_io_snapshot']()['rx_bytes'],0)
        packet=bytearray(self.frame(env));packet[24]^=1
        env['record_tool_io']('rx',raw=packet)
        self.assertEqual(env['feed_receive_buffer'](bytearray(packet)),[])
        value=env['tool_io_snapshot']()
        self.assertEqual((value['rx_bytes'],value['feedback_frames']),(41,0))
        self.assertGreater(value['framing_rejects'],0)
    def test_partial_header_and_valid_frame_resynchronize_without_losing_feedback(self):
        env,_=self.environment();packet=self.frame(env)
        buffer=bytearray(b'junk\x55')
        self.assertEqual(env['feed_receive_buffer'](buffer),[])
        buffer.extend(packet[1:17]);self.assertEqual(env['feed_receive_buffer'](buffer),[])
        buffer.extend(packet[17:]);frames=env['feed_receive_buffer'](buffer)
        self.assertEqual(frames,[packet])
        self.assertEqual(env['parse_feedback_frame'](frames[0])['speed'],0)
    def test_raw_hex_and_errors_are_bounded_and_new_connection_resets_counts(self):
        env,_=self.environment()
        env['record_tool_io']('rx',raw=b'x'*10000)
        env['record_tool_io']('tx',raw=b'y'*1000)
        env['record_tool_io']('error',error='z'*1000)
        value=env['tool_io_snapshot']()
        self.assertEqual(value['rx_bytes'],10000)
        self.assertEqual(len(value['last_rx_hex']),192)
        self.assertEqual(len(value['last_tx_hex']),64)
        self.assertEqual(len(value['last_error']),240)
        env['record_tool_io']('connected')
        value=env['tool_io_snapshot']()
        self.assertEqual((value['connection_id'],value['rx_bytes']),(2,0))
        self.assertIsNone(value['last_rx_age_s'])
    def test_actual_receive_pipeline_counts_raw_rejects_and_valid_feedback(self):
        env,clock=self.environment({'tcp_receive'})
        good=self.frame(env);bad=bytearray(good);bad[25]^=1
        chunks=[bytes(bad),self.frame(env,0x82),good[:1],good[1:],b'']
        transport=Mock();transport.recv=Mock(side_effect=chunks)
        class Done(Exception): pass
        sleep=clock.sleep
        def advance(seconds):
            if seconds==5: raise Done()
            sleep(seconds)
        clock.sleep=advance
        updates=Mock()
        env.update(SIMULATION_ONLY=False,client_socket=None,recv_frame_count=0,last_print_recv_time=0,
            socket=SimpleNamespace(AF_INET=socket.AF_INET,SOCK_STREAM=socket.SOCK_STREAM,
                                   timeout=socket.timeout,socket=lambda *a:transport),
            CONNECT_TIMEOUT=3,IP='fake',PORT=1200,sock_lock=threading.Lock(),lock=threading.RLock(),
            reset_driver=Mock(),ENABLE_PHYSICAL_SWITCH_CAPTURE=False,
            should_begin_physical_run=lambda p:False,filter_torque=lambda t:t,
            update_last_feedback=updates,start_time=100.,time_data=[],torque_raw_data=[],
            torque_data=[],speed_data=[],angle_data=[],status_data=[],state='MONITOR',
            detect_state=Mock(),contact_cycle=SimpleNamespace(active=False),RECONNECT_DELAY=5)
        with self.assertRaises(Done): env['tcp_receive']()
        values=env['tool_io_snapshot']()
        self.assertEqual(values['rx_bytes'],sum(map(len,chunks)))
        self.assertGreater(values['framing_rejects'],0)
        self.assertEqual(values['parse_rejects'],1)
        self.assertEqual(values['feedback_frames'],1)
        updates.assert_called_once()
        transport.close.assert_called_once()
        self.assertIn('receive/connect',values['last_error'])
    def test_timeout_error_keeps_specific_diagnostics_and_does_not_move(self):
        from servo_contact_cycle import ContactCycle
        from unittest.mock import patch
        clock=Clock()
        cycle=ContactCycle(Mock(),lambda:dict(state='MONITOR',tool_connected=True,
            feedback_age=None,live_speed=None,device_error=-1,tool_io={'rx_bytes':19,'framing_rejects':1}),Mock())
        cycle._wait=lambda *a:clock.sleep(.1)
        with patch('servo_contact_cycle.time',clock):
            with self.assertRaisesRegex(RuntimeError,'rx_bytes.*19'):
                cycle._tool_stopped(None)
        cycle.start_tool.assert_not_called()


if __name__ == '__main__': unittest.main()
