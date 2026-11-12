"""Actual Python + production C# gateway + fake robot/driver, loopback only."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

from smoke_offline_service import ROOT, command, port, wait_until
from smoke_tool_service import FakeDriver


def main():
    with tempfile.TemporaryDirectory(prefix='contact-v2-smoke-') as directory, FakeDriver() as driver:
        temporary = Path(directory)
        for name in ('servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py', 'servo_contact_cycle.py',
                     'contact_session.py', 'unity_robot_client.py', 'cr10af_hybrid_x.py', 'cr10af_exit_optimized.py'):
            shutil.copy2(ROOT/'ExternalCode'/name, temporary/name)
        bridge, curve, gateway_port = port(), port(), port()
        state = temporary/'contact-session.json'
        env = dict(os.environ, SERVO_UNITY_EMBEDDED_MODE='1', SERVO_SIMULATION_ONLY='0',
            SERVO_TOOL_IP='127.0.0.1', SERVO_TOOL_PORT=str(driver.port),
            SERVO_BRIDGE_HOST='127.0.0.1', SERVO_BRIDGE_PORT=str(bridge),
            SERVO_CURVE_HOST='127.0.0.1', SERVO_CURVE_PORT=str(curve),
            UNITY_ROBOT_HOST='127.0.0.1', UNITY_ROBOT_PORT=str(gateway_port),
            SERVO_LOG_DIR=str(temporary/'outputs'), SERVO_CONTACT_STATE_PATH=str(state),
            MPLCONFIGDIR=str(temporary/'mpl'), PYTHONDONTWRITEBYTECODE='1', PYTHONUNBUFFERED='1')
        dll = ROOT/'tests/FieldRobotHarness/bin/Debug/net8.0/FieldRobotHarness.dll'
        service = gateway = None
        with (temporary/'gateway.log').open('w') as robot_log, (temporary/'service.log').open('w') as log:
            try:
                gateway = subprocess.Popen(['dotnet', str(dll), '--contact-gateway', str(gateway_port)],
                    stdin=subprocess.PIPE, stdout=robot_log, stderr=subprocess.STDOUT, text=True)
                wait_until(lambda: 'CONTACT_GATEWAY_READY' in (temporary/'gateway.log').read_text())
                def launch():
                    return subprocess.Popen([sys.executable, str(temporary/'servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py')],
                        cwd=directory, env=env, stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT)
                service = launch()
                def status(): return command(bridge, 'status')
                wait_until(lambda: (lambda r: r if r.get('live_speed')==0 and r.get('tool_connected') else None)(status()))
                from servo_contact_cycle import ContactCycle
                settings = dict(ContactCycle(None,None,None).settings, configuration_id='contact-smoke',
                    robotIp='127.0.0.1', dashboardPort=29999, feedbackPort=30004, userNumber=0,
                    toolIp='127.0.0.1', toolPort=driver.port, positiveXInsertionConfirmed=True,
                    insertionMm=.2, withdrawalMm=.2, beforeTightenSeconds=0, beforeExitSeconds=0)
                # Old field JSON must remain configurable/recoverable, but an
                # impossible real insertion must fail before thread/lease/state file.
                legacy = dict(settings, insertionMm=20, contactStageTimeoutSeconds=30)
                assert command(bridge, 'configure_contact:'+json.dumps(legacy))['ok']
                before = status()['cycle_id']
                for request in ('robot_insert', 'auto_cycle'):
                    denied = command(bridge, request)
                    assert not denied['ok'], denied
                rejected = status()
                assert not rejected['cycle_active'] and rejected['cycle_id']==before and not state.exists(), rejected
                # A genuine exit-only request is not blocked by unused insertion30s.
                assert command(bridge, 'robot_withdraw')['ok']
                exited = wait_until(lambda: (lambda r: r if not r['cycle_active'] else None)(status()), 20)
                assert exited['cycle_result']=='ROBOT_ONLY_DONE' and not state.exists(), exited
                stages = json.loads(command(bridge,'cycle_report')['cycle_report_json'])['stages']
                assert len(stages)==1 and stages[0]['plan']['stage']=='exit', stages
                assert command(bridge, 'configure_contact:'+json.dumps(settings))['ok']
                assert command(bridge, 'auto_cycle')['ok']
                done = wait_until(lambda: (lambda r: r if not r['cycle_active'] else None)(status()), 35)
                assert done['cycle_result']=='OK', done
                assert not state.exists(), done
                evidence = json.loads(command(bridge,'cycle_report')['cycle_report_json'])
                assert [s['plan']['stage'] for s in evidence['stages']]==['insert','exit'], evidence
                assert all(s['plan']['compliance_strategy']=='bounded-compliance-v2-20261029' for s in evidence['stages'])
                assert command(bridge, 'robot_insert')['ok']
                inserted=wait_until(lambda: (lambda r: r if not r['cycle_active'] else None)(status()), 20)
                assert inserted['cycle_result']=='ROBOT_ONLY_DONE' and state.exists(), inserted
                assert not command(bridge,'robot_withdraw')['ok']
                assert not command(bridge,'forward')['ok']
                # Fake service is idle and tool stopped. Restart only this owned test process.
                service.terminate(); service.wait(timeout=5); service=launch()
                restarted=wait_until(status)
                assert restarted['contact_session_blocked'] and state.exists(), restarted
                assert not command(bridge,'auto_cycle')['ok']
                assert command(bridge,'configure_contact:'+json.dumps(legacy))['ok']
                gateway.stdin.write('end-field\n'); gateway.stdin.flush()
                wait_until(lambda:'FIELD_IDLE' in (temporary/'gateway.log').read_text())
                wait_until(lambda:status().get('live_speed')==0)
                reset=command(bridge,'reset_contact_session:{"confirm_tool_free":true}')
                assert reset['ok'] and not reset['contact_session_blocked'] and not state.exists(), reset
                print('CONTACT_V2_SMOKE_PASS: legacy budget configured, insert rejected before motion, independent exit, '
                      'full cycle, insert-only/restart interlock, read-only recovery with legacy budget; '
                      'actual service + production gateway/fake devices, no hardware')
            except Exception:
                print((temporary/'service.log').read_text()[-18000:])
                print((temporary/'gateway.log').read_text()[-8000:])
                raise
            finally:
                if service and service.poll() is None:
                    service.terminate(); service.wait(timeout=5)
                if gateway:
                    try: gateway.communicate('quit\n', timeout=8)
                    except subprocess.TimeoutExpired: gateway.kill(); gateway.wait()


if __name__ == '__main__': main()
