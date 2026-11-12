"""Fixed-path contact interlock. A restart/config change is not a recovery."""
import json
import os
from pathlib import Path
import tempfile
import threading
import time
import uuid


def _invalid_number(value):
    raise ValueError('Non-finite number in contact state: ' + value)


class ContactSession:
    def __init__(self, path=None):
        requested = path or os.environ.get('SERVO_CONTACT_STATE_PATH')
        if requested and not Path(requested).is_absolute():
            raise ValueError('Contact state must use a fixed absolute persistent-data path')
        # Keep the same absolute spelling Unity provided (its data directory may
        # contain a symlink); resolving it here would fail the endpoint handshake.
        self.path = Path(os.path.abspath(requested)) if requested else None
        self.lock = threading.RLock()
        self.owner = None
        self.last_write = 0.

    def read(self):
        with self.lock:
            if self.path is None:
                raise RuntimeError('Persistent contact state path not configured; deploy complete Unity build')
            if not self.path.exists():
                return None
            if self.path.stat().st_size > 65536:
                raise RuntimeError('Contact state is oversized; keep blocked and inspect storage')
            with self.path.open(encoding='utf-8') as stream:
                value = json.load(stream, parse_constant=_invalid_number)
            if not isinstance(value, dict) or value.get('schema') != 1 or not value.get('session_id'):
                raise RuntimeError('Contact state is invalid; keep blocked and inspect storage')
            return value

    def snapshot(self):
        try:
            value = self.read()
            return dict(contact_session_blocked=value is not None,
                        contact_session_json=json.dumps(value, ensure_ascii=False) if value else '',
                        contact_session_path=str(self.path or 'not_configured'))
        except Exception as exc:
            return dict(contact_session_blocked=True, contact_session_json=str(exc),
                        contact_session_path=str(self.path or 'not_configured'))

    def begin(self, identity, configuration_id, workflow):
        with self.lock:
            if self.path is None:
                self.read()  # Fail before any real robot IO.
            self.path.parent.mkdir(parents=True, exist_ok=True)
            record = dict(schema=1, session_id=uuid.uuid4().hex, pid=os.getpid(),
                          identity=identity, configuration_id=configuration_id, workflow=workflow,
                          phase='STARTING', status='incomplete', started_unix=time.time())
            try:
                fd = os.open(str(self.path), os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
            except FileExistsError:
                raise RuntimeError('contact_session_blocked: inspect contact and use explicit field recovery')
            with os.fdopen(fd, 'w', encoding='utf-8') as stream:
                json.dump(record, stream, ensure_ascii=False, allow_nan=False)
                stream.flush()
                os.fsync(stream.fileno())
            self.owner = record['session_id']
            self.last_write = time.monotonic()

    def update(self, force=False, **values):
        with self.lock:
            if not force and time.monotonic() - self.last_write < 1:
                return
            record = self.read()
            if not self.owner or record is None or record['session_id'] != self.owner:
                raise RuntimeError('Contact state ownership lost; stop and keep interlock')
            record.update(values, updated_unix=time.time())
            temporary = None
            try:
                with tempfile.NamedTemporaryFile(mode='w', encoding='utf-8', dir=str(self.path.parent),
                                                 prefix='.contact-', delete=False) as stream:
                    temporary = Path(stream.name)
                    json.dump(record, stream, ensure_ascii=False, allow_nan=False)
                    stream.flush()
                    os.fsync(stream.fileno())
                os.replace(str(temporary), str(self.path))
                self.last_write = time.monotonic()
            finally:
                if temporary is not None and temporary.exists():
                    temporary.unlink()

    def complete(self):
        with self.lock:
            record = self.read()
            if not self.owner or record is None or record['session_id'] != self.owner:
                raise RuntimeError('Cannot clear an unowned contact state')
            self.path.unlink()
            self.owner = None

    def reset(self, identity):
        """Caller holds the cycle lock and has verified human + physical stops."""
        with self.lock:
            record = self.read()
            if record is None:
                return
            if record.get('identity') != identity:
                raise RuntimeError('Contact recovery device/User/Tool differs from recorded operation')
            pid = record.get('pid')
            if not isinstance(pid, int) or pid <= 0:
                raise RuntimeError('Previous contact process cannot be identified')
            if pid != os.getpid():
                try:
                    os.kill(pid, 0)
                except ProcessLookupError:
                    pass
                else:
                    raise RuntimeError('Previous contact process still exists; stop it before recovery')
            self.path.unlink()
            self.owner = None
