"""JSON-lines adapter. Only Unity owns the robot network connection."""
import json
import os
import socket
import threading
import time
import uuid


class UnityRobotClient:
    def __init__(self, simulate=False, host=None, port=None):
        self.host = host or os.environ.get('UNITY_ROBOT_HOST', '127.0.0.1')
        if self.host not in ('127.0.0.1', 'localhost', '::1'):
            raise ValueError('Unity robot gateway must be a loopback address')
        self.port = int(port or os.environ.get('UNITY_ROBOT_PORT', '19120'))
        self.simulate = simulate
        self.token = ''
        self.sock = None
        self.reader = None
        self.lock = threading.Lock()
        self.last_status = {}
        self.preview = None

    def open(self):
        self.sock = socket.create_connection((self.host, self.port), timeout=3)
        self.sock.settimeout(12)
        self.reader = self.sock.makefile('rb')
        try:
            acquired = self.request('acquire', simulate=self.simulate)
            self.token = acquired['token']
            if acquired.get('contact_profile') != 'contact-compliant-v2-20261029':
                raise RuntimeError('Unity gateway version differs from contact-compliant-v2-20261029; deploy complete build')
            return self
        except Exception:
            self.close()
            raise

    def request(self, op, **kwargs):
        with self.lock:
            request_id = uuid.uuid4().hex
            payload = dict(id=request_id, op=op, token=self.token, **kwargs)
            traced = op in ('acquire', 'release', 'stop', 'finish_stage', 'begin_stage', 'end_stage', 'move_relative', 'pose', 'wrench')
            started = time.monotonic()
            mode = 'SIM' if self.simulate else 'REAL_REQUEST'
            if op == 'stop':
                mode = 'CURRENT_OWNER'
            if traced:
                safe = {k: v for k, v in kwargs.items()
                        if k in ('simulate', 'delta', 'duration', 'tool', 'enforce_lateral', 'original_timing', 'stop_at_end', 'stage', 'planned_distance_mm', 'expected_target')}
                print('[RobotTrace][Python][TX] id=%s op=%s mode=%s params=%s units=mm,s,N,Nm,deg'
                      % (request_id, op, mode, json.dumps(safe, ensure_ascii=False)), flush=True)
            try:
                return self._exchange(payload, request_id, op, traced, started)
            except Exception as exc:
                if traced:
                    print('[RobotTrace][Python][FAIL] id=%s op=%s error=%s elapsed_ms=%.1f'
                          % (request_id, op, str(exc).replace(self.token, '[redacted]') if self.token else str(exc),
                             (time.monotonic() - started) * 1000), flush=True)
                raise

    def _exchange(self, payload, request_id, op, traced, started):
        self.sock.sendall((json.dumps(payload, allow_nan=False) + '\n').encode())
        line = self.reader.readline(65537)
        if not line or len(line) > 65536:
            raise RuntimeError('Unity gateway disconnected or oversized reply')
        result = json.loads(line)
        if result.get('id') != request_id:
            raise RuntimeError('Unity gateway response id mismatch')
        if not result.get('ok'):
            raise RuntimeError(result.get('error') or 'Unity rejected request')
        if traced:
            safe = {k: result[k] for k in ('ok', 'result', 'values') if k in result}
            print('[RobotTrace][Python][RX] id=%s op=%s reply=%s roundtrip_ms=%.1f'
                  % (request_id, op, json.dumps(safe, ensure_ascii=False),
                     (time.monotonic() - started) * 1000), flush=True)
        return result

    def status(self):
        result = self.request('status')
        self.last_status = dict(result)
        if result.get('simulated', False) != self.simulate:
            raise RuntimeError('Feedback source does not match requested execution mode')
        if not self.simulate and result.get('six_force_online') != 1:
            raise RuntimeError('Six-axis force sensor offline or abnormal')
        result['time'] = time.monotonic() - result.get('age_s', 1e9)
        result['error'] = result.get('robot_error', 0)
        self.last_status = dict(result)
        return result

    def pose(self):
        return tuple(self.request('pose')['values'])

    def relative_target(self, dx, dy, dz):
        delta = [dx, dy, dz]
        reply = self.request('relative_target', delta=delta)
        self.preview = (delta, reply['values'])
        return tuple(reply['values'])

    def wrench(self, tool):
        return tuple(self.request('wrench', tool=tool)['values'])

    def move_relative(self, dx, dy, dz, duration, enforce_lateral=False):
        result = self.request('move_relative', delta=[dx, dy, dz], duration=duration,
                              enforce_lateral=enforce_lateral)
        if result.get('result') != 'completed':
            raise RuntimeError('Unity did not confirm movement completion')
        return result

    def stop(self):
        return self.request('stop')

    def move_original_interval(self, dx, dy, dz, duration, stop_at_end, enforce_lateral):
        delta = [dx, dy, dz]
        expected = self.preview[1] if self.preview and self.preview[0] == delta else None
        self.preview = None
        # Unity JsonUtility turns an explicit null array into []. Omit an absent
        # preview; real six-value previews still get the gateway's strict check.
        preview = {} if expected is None else {'expected_target': expected}
        result = self.request('move_relative', delta=[dx, dy, dz], duration=duration,
                              original_timing=True, stop_at_end=stop_at_end,
                              enforce_lateral=enforce_lateral, **preview)
        if result.get('result') != 'interval_elapsed':
            raise RuntimeError('Gateway does not support original interval semantics')
        return result

    def finish_stage(self):
        result = self.request('finish_stage')
        if result.get('result') != 'stage_stopped':
            raise RuntimeError('Stage stop not confirmed')
        return result

    def begin_stage(self, stage, distance):
        return self.request('begin_stage', stage=stage, planned_distance_mm=distance)

    def end_stage(self):
        return self.request('end_stage')

    def close(self):
        try:
            if self.token:
                self.request('release')
        finally:
            self.token = ''
            reader, sock = self.reader, self.sock
            self.sock = self.reader = None
            try:
                if reader:
                    reader.close()
            finally:
                if sock:
                    sock.close()

    @classmethod
    def read_only_probe(cls, tool, user):
        client = cls()
        try:
            client.sock = socket.create_connection((client.host, client.port), timeout=3)
            client.sock.settimeout(8)
            client.reader = client.sock.makefile('rb')
            return client.request('contact_probe', tool=tool, user=user)
        finally:
            client.close()

    @classmethod
    def emergency_stop(cls):
        """Separate local connection: never wait behind a pending motion reply."""
        client = cls()
        try:
            client.sock = socket.create_connection((client.host, client.port), timeout=3)
            client.sock.settimeout(8)
            client.reader = client.sock.makefile('rb')
            return client.request('stop')
        finally:
            client.close()
