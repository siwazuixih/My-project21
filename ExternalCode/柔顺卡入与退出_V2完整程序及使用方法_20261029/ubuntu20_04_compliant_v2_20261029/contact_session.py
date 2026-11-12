"""Persist a failed/incomplete contact operation across process restarts."""
import json
import os
import sys
from pathlib import Path
from datetime import datetime, timezone

STATE = Path(__file__).resolve().with_name('contact_session.json')


def process_present(pid):
    pid = int(pid)
    if pid <= 0:
        return True
    if sys.platform == 'win32':
        # os.kill(pid, 0) is not a harmless existence check on Windows.
        import ctypes
        from ctypes import wintypes
        api = ctypes.WinDLL('kernel32', use_last_error=True)
        api.OpenProcess.argtypes = [wintypes.DWORD,wintypes.BOOL,wintypes.DWORD]
        api.OpenProcess.restype = wintypes.HANDLE
        handle = api.OpenProcess(0x00100000,False,pid)
        if not handle:
            return ctypes.get_last_error() != 87
        try:
            api.WaitForSingleObject.argtypes=[wintypes.HANDLE,wintypes.DWORD]
            return api.WaitForSingleObject(handle,0) != 0
        finally:
            api.CloseHandle.argtypes=[wintypes.HANDLE]
            api.CloseHandle(handle)
    try:
        os.kill(pid,0)
    except ProcessLookupError:
        return False
    return True


def guarded_run(args, callback, state=None):
    path = Path(state) if state is not None else STATE
    if not args.execute:
        return callback()
    record = {'ip':args.ip,'tool':args.tool,'direction':args.direction,'pid':os.getpid(),
              'started_utc':datetime.now(timezone.utc).isoformat(),
              'status':'incomplete'}
    try:
        fd = os.open(str(path), os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    except FileExistsError:
        return {'result':'contact_session_blocked','moved_mm':0.0,
                'reason':'Previous contact operation failed or is still running. Inspect the fixture, then use the documented reset procedure.',
                'state_file':str(path)}
    except OSError as exc:
        return {'result':'session_storage_error','reason':str(exc),'moved_mm':0.0}
    with os.fdopen(fd,'w',encoding='utf-8') as f:
        json.dump(record,f,ensure_ascii=False)
    result = callback()  # Ctrl+C/crash leaves the incomplete record in place.
    if result.get('result')=='completed':
        path.unlink()
    else:
        record.update(status='blocked',result=result.get('result'),reason=result.get('reason'),
                      moved_mm=result.get('moved_mm'),last_wrench=result.get('last_wrench'))
        path.write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
    return result


def reset_session(report, confirmed, state=None):
    path = Path(state) if state is not None else STATE
    if not confirmed:
        return {'result':'reset_refused','reason':'--confirm-tool-free is required after physical inspection','motion_sent':False}
    if report.get('result')!='probe_ok' or not report.get('precheck_passed'):
        return {'result':'reset_refused','reason':'Read-only precheck did not pass','probe':report,'motion_sent':False}
    if path.exists():
        record=json.loads(path.read_text(encoding='utf-8'))
        if record.get('status')=='incomplete':
            try:
                present = process_present(record['pid'])
            except (PermissionError, KeyError, ValueError):
                return {'result':'reset_refused','reason':'Cannot verify whether previous process stopped','motion_sent':False}
            else:
                if not present:
                    path.unlink()
                    return {'result':'session_reset','motion_sent':False}
                return {'result':'reset_refused','reason':'Previous process is still present; stop it before reset','motion_sent':False}
        path.unlink()
    return {'result':'session_reset','motion_sent':False,
            'note':'Human confirmed contact released; software checks passed. No motion sent.'}
