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

    def open(self):
        self.sock = socket.create_connection((self.host, self.port), timeout=3)
        self.sock.settimeout(12)
        self.reader = self.sock.makefile('rb')
        try:
            self.token = self.request('acquire', simulate=self.simulate)['token']
            return self
        except Exception:
            self.close()
            raise

    def request(self, op, **kwargs):
        with self.lock:
            request_id = uuid.uuid4().hex
            payload = dict(id=request_id, op=op, token=self.token, **kwargs)
            traced = op in ('acquire', 'release', 'stop', 'move_relative', 'pose', 'wrench')
            started = time.monotonic()
            mode = 'SIM' if self.simulate else 'REAL_REQUEST'
            if op == 'stop':
                mode = 'CURRENT_OWNER'
            if traced:
                safe = {k: v for k, v in kwargs.items()
                        if k in ('simulate', 'delta', 'duration', 'tool', 'enforce_lateral')}
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
        if result.get('simulated', False) != self.simulate:
            raise RuntimeError('Feedback source does not match requested execution mode')
        result['time'] = time.monotonic() - result.get('age_s', 1e9)
        result['error'] = result.get('robot_error', 0)
        return result

    def pose(self):
        return tuple(self.request('pose')['values'])

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

    def close(self):
        try:
            if self.token:
                self.request('release')
        finally:
            self.token = ''
            if self.reader:
                self.reader.close()
            if self.sock:
                self.sock.close()
            self.sock = self.reader = None

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
