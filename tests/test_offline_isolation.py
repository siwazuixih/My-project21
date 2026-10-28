"""Exercise actual V26 entry points via AST, without importing its GUI/server startup."""
import ast
from pathlib import Path
import sys
import threading
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'ExternalCode'))
from servo_contact_cycle import ContactCycle

SOURCE = ROOT / 'ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py'
TREE = ast.parse(SOURCE.read_text())


class OfflineIsolationTests(unittest.TestCase):
    def entry(self, simulation=True):
        names = {'run_unity_command', 'send_cmd', 'send_cmd_burst', 'send_cmd_new_connection', 'tcp_receive'}
        nodes = [n for n in TREE.body if isinstance(n, ast.FunctionDef) and n.name in names]
        cycle = Mock()
        cycle.snapshot.return_value = {'cycle_active': False}
        env = dict(SIMULATION_ONLY=simulation, socket=Mock(), client_socket=Mock(),
                   sock_lock=threading.Lock(), state='IDLE', contact_cycle=cycle,
                   command_queue=Mock(), unity_status=lambda ok=True, message='': {'ok': ok, 'message': message},
                   threading=Mock(), stop_driver=Mock(), reset_driver=Mock(return_value=True),
                   enqueue_command=Mock(), ENABLE_PHYSICAL_SWITCH_CAPTURE=False)
        env['command_queue'].empty.return_value = True
        exec(compile(ast.Module(body=nodes, type_ignores=[]), str(SOURCE), 'exec'), env)
        return env

    def test_simulated_service_rejects_all_real_commands(self):
        env = self.entry()
        for cmd in ('connect', 'forward', 'reverse', 'auto_cycle', 'robot_insert', 'robot_withdraw'):
            self.assertFalse(env['run_unity_command'](cmd)['ok'], cmd)
        for name in ('reset_driver', 'stop_driver', 'enqueue_command'):
            env[name].assert_not_called()
        env['contact_cycle'].start.assert_not_called()

    def test_simulated_service_allows_only_simulated_cycle(self):
        env = self.entry()
        self.assertTrue(env['run_unity_command']('auto_cycle_sim')['ok'])
        env['contact_cycle'].start.assert_called_once_with(simulated=True)

    def test_idle_simulated_stop_never_sends_tool_stop(self):
        env = self.entry()
        for cmd in ('stop', 'reset'):
            self.assertTrue(env['run_unity_command'](cmd)['ok'])
        env['stop_driver'].assert_not_called()

    def test_transport_functions_refuse_even_direct_calls_in_simulation(self):
        env = self.entry()
        self.assertFalse(env['send_cmd'](b'x'))
        self.assertFalse(env['send_cmd_burst']([(b'x', 'test')]))
        self.assertFalse(env['send_cmd_new_connection'](b'x'))
        env['tcp_receive']()
        env['socket'].socket.assert_not_called()
        env['socket'].create_connection.assert_not_called()
        env['client_socket'].sendall.assert_not_called()

    def test_startup_threads_are_isolated_and_real_default_is_preserved(self):
        node = next(n for n in TREE.body if isinstance(n, ast.If) and
                    isinstance(n.test, ast.UnaryOp) and isinstance(n.test.op, ast.Not) and
                    isinstance(n.test.operand, ast.Name) and n.test.operand.id == 'SIMULATION_ONLY')
        for simulated, expected in ((True, []), (False, ['command_worker', 'tcp_receive', 'terminal_keyboard_worker'])):
            started = []
            fake_threading = Mock()
            fake_threading.Thread.side_effect = lambda target, daemon: Mock(start=lambda: started.append(target))
            env = dict(SIMULATION_ONLY=simulated, threading=fake_threading,
                       command_worker='command_worker', tcp_receive='tcp_receive',
                       terminal_keyboard_worker='terminal_keyboard_worker')
            exec(compile(ast.Module(body=[node], type_ignores=[]), str(SOURCE), 'exec'), env)
            self.assertEqual(started, expected)

    def test_real_entry_still_forwards_tool_commands(self):
        env = self.entry(False)
        self.assertTrue(env['run_unity_command']('connect')['ok'])
        self.assertTrue(env['run_unity_command']('forward')['ok'])
        self.assertTrue(env['run_unity_command']('reverse')['ok'])
        self.assertTrue(env['run_unity_command']('stop')['ok'])
        env['reset_driver'].assert_called_once()
        self.assertEqual(env['enqueue_command'].call_args_list[0].args, ('FORWARD',))
        self.assertEqual(env['enqueue_command'].call_args_list[1].args, ('REVERSE_HOME',))
        env['stop_driver'].assert_called_once()

    def test_simulated_cycle_stop_does_not_send_unowned_gateway_stop(self):
        cycle = ContactCycle(Mock(), Mock(), Mock())
        cycle.simulated = True
        with patch('servo_contact_cycle.UnityRobotClient.emergency_stop') as stop:
            cycle.stop()
            stop.assert_not_called()
        self.assertTrue(cycle.cancel.is_set())
        cycle.stop_tool.assert_not_called()

    def test_real_cycle_stop_still_interrupts_gateway_and_tool(self):
        cycle = ContactCycle(Mock(), Mock(), Mock())
        cycle.simulated = False
        with patch('servo_contact_cycle.UnityRobotClient.emergency_stop') as stop:
            cycle.stop()
            stop.assert_called_once()
        cycle.stop_tool.assert_called_once()


if __name__ == '__main__':
    unittest.main()
