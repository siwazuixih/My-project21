#!/usr/bin/env python3
"""One entry for offline checks, read-only diagnostics, robot stages and GUI."""
import argparse
from datetime import datetime
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
ROBOT = ROOT/'cr10af_hybrid_x.py'
SERVO = ROOT/'servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py'
VERSION = 'compliant-v2-20261029'


def command(args):
    if args.action in ('check', 'gui'):
        return [sys.executable, str(SERVO), '--execute' if args.execute else '--check']
    if args.action == 'test':
        return [sys.executable, '-m', 'unittest', 'discover', '-s', str(ROOT/'tests'), '-v']
    cmd=[sys.executable,str(ROBOT),'--ip',args.ip,'--tool',str(args.tool)]
    if args.action == 'probe':
        return cmd+['--probe']
    cmd += ['--direction','1.0' if args.action=='insert' else '-1.0',
            '--distance-mm',str(args.distance_mm)]
    if args.execute: cmd.append('--execute')
    return cmd


def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action',choices=('check','test','probe','insert','exit','gui'))
    parser.add_argument('--ip',default='192.168.192.19')
    parser.add_argument('--tool',type=int,default=1)
    parser.add_argument('--distance-mm',type=float,default=2.0,
                        help='Standalone stage defaults to 2 mm; GUI defaults to its configured 20 mm')
    parser.add_argument('--execute',action='store_true')
    args=parser.parse_args(argv)
    if args.execute and args.action in ('check','test','probe'):
        parser.error('check/test/probe do not accept --execute')
    env=os.environ.copy()
    env.update(CR10AF_SCRIPT=str(ROBOT),CR10AF_IP=args.ip,CR10AF_TOOL=str(args.tool),PYTHONIOENCODING='utf-8')
    print('版本：%s\n运行目录：%s' % (VERSION,ROOT),file=sys.stderr)
    cmd=command(args)
    if args.action in ('gui','check','test'):
        return subprocess.call(cmd,cwd=str(ROOT),env=env)
    logs=ROOT/'robot_logs'
    logs.mkdir(exist_ok=True)
    path=logs/('%s_%s.json' % (datetime.now().strftime('%Y%m%d_%H%M%S_%f'),args.action))
    # Child receives Ctrl+C too and its controller's finally block sends Stop.
    child=subprocess.Popen(cmd,cwd=str(ROOT),env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    try:
        with path.open('wb') as f:
            for chunk in iter(lambda:child.stdout.read(4096),b''):
                f.write(chunk); f.flush()
                sys.stdout.buffer.write(chunk); sys.stdout.buffer.flush()
        return child.wait()
    except KeyboardInterrupt:
        try: child.wait(timeout=5)
        except subprocess.TimeoutExpired:
            # Keep the controller alive to run its stop/finally path.
            print('控制进程仍未结束；请检查现场停止状态。',file=sys.stderr)
        return 130
    finally:
        print('机器人诊断/结果日志：'+str(path),file=sys.stderr)


if __name__=='__main__':
    sys.exit(main())
