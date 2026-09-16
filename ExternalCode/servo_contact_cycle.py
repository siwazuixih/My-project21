"""0910 sequence, using Unity as the only robot communication owner."""
import threading
import time
from types import SimpleNamespace
from unity_robot_client import UnityRobotClient
from cr10af_hybrid_x import HybridExit


class ContactCycle:
    def __init__(self, start_tool, read_tool, stop_tool):
        self.start_tool, self.read_tool, self.stop_tool = start_tool, read_tool, stop_tool
        self.lock = threading.Lock()
        self.cancel = threading.Event()
        self.active = False
        self.phase = 'IDLE'
        self.result = ''
        self.error = ''
        self.run_id = 0
        self.simulated = True

    def snapshot(self):
        with self.lock:
            return dict(cycle_active=self.active, cycle_phase=self.phase,
                        cycle_result=self.result, cycle_error=self.error,
                        cycle_id=self.run_id, cycle_simulated=self.simulated)

    def start(self, simulated=True):
        with self.lock:
            if self.active:
                raise RuntimeError('Contact cycle already active')
            self.active = True
            self.phase, self.result, self.error = 'STARTING', '', ''
            self.simulated = simulated
            self.run_id += 1
            self.cancel.clear()
        threading.Thread(target=self._run, args=(simulated,), daemon=True).start()

    def stop(self):
        self.cancel.set()
        # Must interrupt an outstanding move through a different local connection.
        try:
            UnityRobotClient.emergency_stop()
        finally:
            if not self.simulated:
                self.stop_tool()

    def _phase(self, value):
        with self.lock:
            self.phase = value
        print('[ContactCycle] ' + value, flush=True)

    def _check(self):
        if self.cancel.is_set():
            raise RuntimeError('Cycle cancelled')

    def _wait(self, seconds, client):
        end = time.monotonic() + seconds
        while time.monotonic() < end:
            self._check()
            client.status()  # keep lease alive even while the tool is tightening
            self.cancel.wait(0.1)

    def _stage(self, client, direction, simulated):
        args = SimpleNamespace(execute=True, simulate=simulated, tool=1,
            direction=direction, distance_mm=20.0, speed_mm_s=1.2,
            position_step_mm=2.0, lateral_trigger_n=20.0,
            search_step_mm=0.1, search_duration_s=0.25,
            search_improvement_n=0.02, hard_force_n=30.0,
            hard_moment_nm=10.0, max_runtime_s=30.0)
        self._check()
        result = HybridExit(args, client).run()
        if result['result'] != 'completed':
            raise RuntimeError(str(result))

    def _run(self, simulated):
        client = None
        outcome = ''
        try:
            client = UnityRobotClient(simulate=simulated).open()
            self._phase('INSERTING')
            self._stage(client, -1.0, simulated)
            self._phase('BEFORE_TIGHTEN')
            self._wait(5, client)
            self._phase('TIGHTENING')
            if simulated:
                self._wait(2, client)
                outcome = 'SIMULATED_OK'
            else:
                self._check()
                self.start_tool()  # clears old run before starting, never accepts previous OK
                end = time.monotonic() + 60
                while time.monotonic() < end:
                    self._check()
                    status = self.read_tool()
                    outcome = status['state']
                    if outcome in ('OK', 'NG_SLIP', 'JAM', 'NG_DEVICE', 'STOP'):
                        break
                    self._wait(0.1, client)
                else:
                    raise RuntimeError('Tightening result timeout')
                if outcome not in ('OK', 'NG_SLIP', 'JAM'):
                    raise RuntimeError('Tightening failed: ' + outcome)
            self._phase('BEFORE_EXIT')
            self._wait(5, client)
            self._phase('EXITING')
            self._stage(client, 1.0, simulated)
            # A completed withdrawal must not turn NG into a successful assembly.
            with self.lock:
                self.result = outcome
        except Exception as exc:
            with self.lock:
                self.error = str(exc)
                self.result = 'CANCELLED' if self.cancel.is_set() else 'FAILED'
            if client:
                try:
                    client.stop()
                except Exception:
                    pass
            if not simulated:
                self.stop_tool()
        finally:
            if client:
                try:
                    client.close()
                except Exception as exc:
                    with self.lock:
                        self.error = 'Release failed: ' + str(exc)
                        self.result = 'FAILED'
            with self.lock:
                self.phase = 'DONE' if self.result in ('OK', 'SIMULATED_OK') else 'FAILED'
                self.active = False
