"""Actual V26 subprocess + actual Unity gateway harness. Loopback only; never uses hardware IPs.
Run separately: python3 tests/smoke_offline_service.py (requires Matplotlib/NumPy and built RobotGatewayHarness).
"""
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'ExternalCode'))
from servo_contact_cycle import ContactCycle


def port():
    with socket.socket() as probe:
        probe.bind(('127.0.0.1', 0))
        return probe.getsockname()[1]


def command(number, value):
    with socket.create_connection(('127.0.0.1', number), timeout=1) as client:
        client.settimeout(2)
        client.sendall((json.dumps({'cmd': value}) + '\n').encode())
        with client.makefile('rb') as reader:
            return json.loads(reader.readline())


def wait_until(probe, seconds=20):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        try:
            value = probe()
            if value:
                return value
        except (OSError, ValueError):
            pass
        time.sleep(.05)
    raise AssertionError('Loopback checkpoint timeout')


def main():
    with tempfile.TemporaryDirectory(prefix='servo-offline-smoke-') as directory, socket.socket() as fake_tool:
        fake_tool.bind(('127.0.0.1', 0))
        fake_tool.listen()
        fake_tool.setblocking(False)
        temporary = Path(directory)
        for name in ('servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py',
                     'servo_contact_cycle.py', 'unity_robot_client.py', 'cr10af_hybrid_x.py'):
            shutil.copy2(ROOT / 'ExternalCode' / name, temporary / name)
        bridge, curve, gateway_port = port(), port(), port()
        env = dict(os.environ, SERVO_SIMULATION_ONLY='1', SERVO_UNITY_EMBEDDED_MODE='1',
                   SERVO_TOOL_IP='127.0.0.1', SERVO_TOOL_PORT=str(fake_tool.getsockname()[1]),
                   SERVO_BRIDGE_HOST='127.0.0.1', SERVO_BRIDGE_PORT=str(bridge),
                   SERVO_CURVE_HOST='127.0.0.1', SERVO_CURVE_PORT=str(curve),
                   UNITY_ROBOT_HOST='127.0.0.1', UNITY_ROBOT_PORT=str(gateway_port),
                   MPLCONFIGDIR=str(temporary / 'mpl'), PYTHONUNBUFFERED='1')
        dll = ROOT / 'tests/RobotGatewayHarness/bin/Debug/net8.0/RobotGatewayHarness.dll'
        with (temporary / 'gateway.log').open('w') as gateway_log, (temporary / 'python.log').open('w') as python_log:
            gateway = subprocess.Popen(['dotnet', str(dll), str(gateway_port)], stdin=subprocess.PIPE,
                                       stdout=gateway_log, stderr=subprocess.STDOUT, text=True)
            service = None
            try:
                service = subprocess.Popen([sys.executable, str(temporary / 'servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py')],
                                           cwd=directory, env=env, stdout=python_log, stderr=subprocess.STDOUT)
                info = wait_until(lambda: command(bridge, 'status'))
                assert info['simulation_only'] and not info['tool_connected'] and info['contact_api_version'] == 2, info
                for value in ('connect', 'forward', 'reverse', 'auto_cycle', 'robot_insert', 'robot_withdraw'):
                    assert not command(bridge, value)['ok'], value
                settings = dict(ContactCycle(None, None, None).settings, configuration_id='offline-smoke',
                                insertionMm=.1, withdrawalMm=.1, contactStepMm=.1,
                                beforeTightenSeconds=0, beforeExitSeconds=0)
                assert command(bridge, 'configure_contact:' + json.dumps(settings))['ok']
                started = command(bridge, 'auto_cycle_sim')
                assert started['ok'] and started['cycle_simulated']
                result = wait_until(lambda: (lambda r: r if not r['cycle_active'] else None)(command(bridge, 'status')))
                assert result['cycle_id'] == started['cycle_id'] and result['cycle_result'] == 'SIMULATED_OK', result
                assert command(bridge, 'auto_cycle_sim')['ok']
                assert command(bridge, 'stop')['ok']
                cancelled = wait_until(lambda: (lambda r: r if not r['cycle_active'] else None)(command(bridge, 'status')))
                assert cancelled['cycle_result'] == 'CANCELLED', cancelled
                try:
                    client, _ = fake_tool.accept()
                    client.close()
                    raise AssertionError('Simulation unexpectedly connected to fake tool')
                except BlockingIOError:
                    pass
                print('OFFLINE_SERVICE_SMOKE_PASS: actual process, mode flag, 6 real commands rejected, SIMULATED_OK, cancellation, zero tool connections')
            except Exception:
                print((temporary / 'python.log').read_text()[-5000:])
                raise
            finally:
                if service is not None:
                    service.terminate()
                    try:
                        service.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        service.kill()
                        service.wait(timeout=5)
                if gateway.poll() is None:
                    gateway.stdin.write('quit\n')
                    gateway.stdin.flush()
                    try:
                        gateway.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        gateway.kill()
                        gateway.wait(timeout=5)
                gateway.stdin.close()


if __name__ == '__main__':
    main()
