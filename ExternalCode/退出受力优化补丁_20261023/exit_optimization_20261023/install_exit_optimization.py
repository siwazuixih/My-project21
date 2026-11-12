"""Install exit-only changes into the existing, working Ubuntu folder. No network."""
import argparse
import ast
from datetime import datetime
import hashlib
import os
from pathlib import Path
import shutil

MARKER = '# EXIT_OPTIMIZATION_20261023'
SERVO = 'servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py'


def function_span(text, name):
    tree = ast.parse(text)
    nodes = [n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == name]
    if len(nodes) != 1:
        raise ValueError('Cannot locate function: ' + name)
    node = nodes[0]
    lines = text.splitlines(keepends=True)
    return ''.join(lines[:node.lineno-1]), ''.join(lines[node.lineno-1:node.end_lineno]), ''.join(lines[node.end_lineno:])


def patch_robot(text):
    if MARKER in text: return text
    prefix, body, suffix = function_span(text, 'main')
    old = '    result = HybridExit(args).run()'
    if body.count(old) != 1:
        raise ValueError('Unrecognized robot entry. Original file has not been modified.')
    new = '''    # EXIT_OPTIMIZATION_20261023
    if args.direction == -1.0:
        import sys as _exit_sys
        from cr10af_exit_optimized import run_exit
        if not any(x == '--max-runtime-s' or x.startswith('--max-runtime-s=') for x in _exit_sys.argv[1:]):
            args.max_runtime_s = max(args.max_runtime_s, 90.0)
        result = run_exit(args, _exit_sys.modules[__name__])
    else:
        result = HybridExit(args).run()'''
    result = prefix + body.replace(old,new) + suffix
    ast.parse(result)
    return result


def patch_servo(text):
    if MARKER in text: return text
    prefix, body, suffix = function_span(text, 'run_robot_stage')
    if body.count('str(ROBOT_MAX_RUNTIME_S)') != 1 or 'ROBOT_TIMEOUT_S' not in body or '    cmd = [' not in body:
        raise ValueError('Unrecognized main controller. Original file has not been modified.')
    body = body.replace('str(ROBOT_MAX_RUNTIME_S)', 'str(stage_runtime)')
    body = body.replace('ROBOT_TIMEOUT_S', 'stage_timeout')
    body = body.replace('    cmd = [', '''    # EXIT_OPTIMIZATION_20261023
    stage_runtime = max(ROBOT_MAX_RUNTIME_S, 90.0) if direction < 0 else ROBOT_MAX_RUNTIME_S
    stage_timeout = max(ROBOT_TIMEOUT_S, stage_runtime + 15.0) if direction < 0 else ROBOT_TIMEOUT_S
    cmd = [''', 1)
    result = prefix + body + suffix
    ast.parse(result)
    return result


def install(folder, source):
    robot = folder/'cr10af_hybrid_x.py'
    servo = folder/SERVO
    module = folder/'cr10af_exit_optimized.py'
    files = {robot:robot.read_text(encoding='utf-8-sig'), servo:servo.read_text(encoding='utf-8-sig')}
    updated = {robot:patch_robot(files[robot]), servo:patch_servo(files[servo])}
    module_bytes = (source/'cr10af_exit_optimized.py').read_bytes()
    compile(module_bytes, str(module), 'exec')
    if all(files[p] == updated[p] for p in files):
        if module.is_file() and module.read_bytes() == module_bytes:
            print('退出优化已安装，无需重复安装。'); return
        raise ValueError('Installed module differs or is missing; restore backup before reinstalling.')
    if any(MARKER in files[p] for p in files):
        raise ValueError('Partial previous installation; restore backup before reinstalling.')
    backup = folder/('exit_backup_'+datetime.now().strftime('%Y%m%d_%H%M%S_%f'))
    backup.mkdir()
    targets = list(files) + [module]
    existed = {p:p.exists() for p in targets}
    for p in targets:
        if existed[p]: shutil.copy2(p,backup/p.name)
    try:
        for p,data in [(module,module_bytes)] + [(p,updated[p].encode('utf-8')) for p in files]:
            tmp = p.with_name(p.name+'.exit_tmp')
            tmp.write_bytes(data); os.replace(tmp,p)
    except Exception:
        for p in targets:
            if existed[p]: shutil.copy2(backup/p.name,p)
            elif p.exists(): p.unlink()
        raise
    print('已安装：仅 -X 退出使用优化策略；+X 卡入沿用现有逻辑。')
    print('备份目录：'+str(backup))
    print('安装不连接机器人，不发送运动指令。')
    print('机器人文件 SHA256：'+hashlib.sha256(robot.read_bytes()).hexdigest())


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target',type=Path,default=Path.cwd())
    args=parser.parse_args()
    install(args.target.resolve(),Path(__file__).resolve().parent)


if __name__=='__main__':
    main()
