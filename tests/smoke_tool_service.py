"""Actual electric-tool service against a local fake driver; never robot hardware.
Run explicitly: PYTHONDONTWRITEBYTECODE=1 python3 tests/smoke_tool_service.py
"""
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import threading
import time

from smoke_offline_service import command, port, wait_until, ROOT


def crc(data):
    value=0xffff
    for byte in data:
        value ^= byte
        for _ in range(8): value=(value>>1)^0xa001 if value&1 else value>>1
    return value


class FakeDriver:
    def __init__(self):
        self.listener=socket.socket()
        self.listener.bind(('127.0.0.1',0));self.listener.listen();self.listener.settimeout(.1)
        self.port=self.listener.getsockname()[1]
        self.lock=threading.Lock();self.stop=threading.Event()
        self.commands=[];self.run=0;self.reverse_at=0.;self.torque=0.
    def __enter__(self):
        self.thread=threading.Thread(target=self.accept,daemon=True);self.thread.start();return self
    def __exit__(self,*args):
        self.stop.set();self.thread.join(2);self.listener.close()
    def accept(self):
        while not self.stop.is_set():
            try: client,_=self.listener.accept()
            except socket.timeout: continue
            threading.Thread(target=self.serve,args=(client,),daemon=True).start()
    def serve(self,client):
        client.settimeout(.02);buffer=bytearray();enabled=False;next_frame=0.;injected=False
        try:
            with client:
                while not self.stop.is_set():
                    try:
                        data=client.recv(512)
                        if not data: return
                        buffer.extend(data)
                    except socket.timeout: pass
                    while len(buffer)>=11:
                        packet=bytes(buffer[:11]);del buffer[:11]
                        if packet[:3]!=b'\x55\xaa\x07' or crc(packet[:-4])!=int.from_bytes(packet[-4:-2],'little'):
                            raise AssertionError('Unexpected command frame')
                        enabled=packet[5]==1
                        with self.lock:
                            self.commands.append((packet[5],packet[6]))
                            self.run=packet[6]
                            if self.run==2: self.reverse_at=time.monotonic()
                    now=time.monotonic()
                    if now<next_frame: continue
                    next_frame=now+.02
                    with self.lock:
                        target=28. if self.run==1 else 0.
                        self.torque+=max(-2.,min(2.,target-self.torque))
                        speed=12 if self.run==1 or (self.run==2 and now-self.reverse_at<.35) else 0
                        torque=int(round(self.torque*1000))
                    if enabled:
                        packet=bytearray(41);packet[:4]=b'\x55\xaa\x25\x81';packet[6]=1
                        packet[7:9]=torque.to_bytes(2,'little');packet[9:11]=speed.to_bytes(2,'little')
                        packet[-4:-2]=crc(packet[:-4]).to_bytes(2,'little');packet[-2:]=b'\r\n'
                        if not injected:
                            corrupt=bytearray(packet);corrupt[25]^=1;client.sendall(corrupt);injected=True
                        client.sendall(packet)
        except (OSError,ConnectionError): pass


def main():
    with tempfile.TemporaryDirectory(prefix='servo-driver-smoke-') as directory, FakeDriver() as driver:
        temporary=Path(directory)
        for name in ('servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py',
                     'servo_contact_cycle.py','contact_session.py','unity_robot_client.py','cr10af_hybrid_x.py','cr10af_exit_optimized.py'):
            shutil.copy2(ROOT/'ExternalCode'/name,temporary/name)
        bridge,curve=port(),port()
        env=dict(os.environ,SERVO_UNITY_EMBEDDED_MODE='1',SERVO_SIMULATION_ONLY='0',
            SERVO_TOOL_IP='127.0.0.1',SERVO_TOOL_PORT=str(driver.port),
            SERVO_BRIDGE_HOST='127.0.0.1',SERVO_BRIDGE_PORT=str(bridge),
            SERVO_CURVE_HOST='127.0.0.1',SERVO_CURVE_PORT=str(curve),
            SERVO_LOG_DIR=str(temporary/'outputs'),SERVO_CONTACT_STATE_PATH=str(temporary/'contact-session.json'),MPLCONFIGDIR=str(temporary/'mpl'),
            PYTHONUNBUFFERED='1',PYTHONDONTWRITEBYTECODE='1')
        with (temporary/'service.log').open('w') as log:
            service=subprocess.Popen([sys.executable,str(temporary/'servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py')],cwd=directory,env=env,stdin=subprocess.DEVNULL,stdout=log,stderr=subprocess.STDOUT)
            try:
                def status(): return command(bridge,'status')
                ready=wait_until(lambda:(lambda r:r if r.get('live_speed')==0 and r.get('tool_connected') else None)(status()))
                assert ready['contact_api_version']==5 and ready['tool_io']['feedback_frames']>0,ready
                assert ready['tool_io']['framing_rejects']>0,ready
                assert command(bridge,'forward')['ok']
                done=wait_until(lambda:(lambda r:r if r['state']=='OK' and not r['result_processing'] and not r['operation_busy'] else None)(status()),15)
                assert done['tool_stop_confirmed'] and done['live_speed']==0,done
                assert done['csv'] and Path(done['csv']).is_file(),done
                assert command(bridge,'reverse')['ok']
                returned=wait_until(lambda:(lambda r:r if r['state']=='RELEASE_CONFIRMED' and not r['operation_busy'] else None)(status()),8)
                assert returned['live_speed']==0,returned
                assert command(bridge,'stop')['ok']
                stopped=wait_until(lambda:(lambda r:r if not r['stop_in_progress'] and not r['operation_busy'] else None)(status()),8)
                with driver.lock:
                    assert (1,1) in driver.commands and (1,2) in driver.commands,driver.commands
                    before=len(driver.commands)
                time.sleep(.15)
                with driver.lock:
                    assert all(run==0 for _,run in driver.commands[before:]),driver.commands
                assert '[ToolTrace]' in (temporary/'service.log').read_text()
                print('TOOL_SERVICE_SMOKE_PASS: actual process, fake 41-byte driver feedback, forward OK + fresh stop + CSV, reverse RELEASE_CONFIRMED, manual stop; no robot/hardware')
            except Exception:
                print((temporary/'service.log').read_text()[-9000:]);raise
            finally:
                service.terminate()
                try: service.wait(timeout=5)
                except subprocess.TimeoutExpired: service.kill();service.wait()


if __name__=='__main__': main()
