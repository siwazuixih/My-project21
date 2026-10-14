"""0910 sequence, using Unity as the only robot communication owner."""
import threading
import time
import math
import json
from datetime import datetime, timezone
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
        self.first_error = ''
        self.failure_detail = ''
        self.cleanup_errors = []
        self.workflow = 'full'
        self.last_robot = {}
        self.last_tool = {}
        self.run_id = 0
        self.simulated = True
        self.settings = dict(toolNumber=1, insertionMm=20, withdrawalMm=20,
            contactSpeedMmS=1.2, contactStepMm=2, lateralLimitN=20,
            forceLimitN=30, momentLimitNm=10, contactStageTimeoutSeconds=30,
            tightenTimeoutSeconds=60, beforeTightenSeconds=5, beforeExitSeconds=5)
        self.configuration_id = ''

    def configure(self, values):
        limits = dict(toolNumber=(0, 50), insertionMm=(0, 100), withdrawalMm=(0, 100),
            contactSpeedMmS=(0.1, 5), contactStepMm=(0.1, 5), lateralLimitN=(0.1, 30),
            forceLimitN=(0.1, 30), momentLimitNm=(0.1, 10), contactStageTimeoutSeconds=(1, 600),
            tightenTimeoutSeconds=(1, 300), beforeTightenSeconds=(0, 60), beforeExitSeconds=(0, 60))
        updated = {}
        timing = values.get('originalContactTiming', False)
        if not isinstance(timing, bool):
            raise ValueError('originalContactTiming must be boolean')
        updated['originalContactTiming'] = timing
        for key, (low, high) in limits.items():
            value = values.get(key)
            if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or not low <= value <= high:
                raise ValueError('Invalid contact setting: ' + key)
            updated[key] = value
        if int(updated['toolNumber']) != updated['toolNumber']:
            raise ValueError('toolNumber must be integer')
        updated['toolNumber'] = int(updated['toolNumber'])
        if updated['lateralLimitN'] > updated['forceLimitN']:
            raise ValueError('lateralLimitN exceeds forceLimitN')
        if updated['contactStepMm'] / updated['contactSpeedMmS'] > 5:
            raise ValueError('Contact step exceeds gateway duration limit')
        if updated['contactStageTimeoutSeconds'] <= max(updated['insertionMm'], updated['withdrawalMm']) / updated['contactSpeedMmS']:
            raise ValueError('Stage timeout shorter than requested movement')
        configuration_id = values.get('configuration_id', '')
        if not isinstance(configuration_id, str) or not configuration_id or len(configuration_id) > 80:
            raise ValueError('Missing configuration id')
        with self.lock:
            if self.active:
                raise RuntimeError('Cannot configure active cycle')
            self.settings = updated
            self.configuration_id = configuration_id

    def snapshot(self):
        with self.lock:
            return dict(cycle_active=self.active, cycle_phase=self.phase,
                        cycle_result=self.result, cycle_error=self.error,
                        cycle_id=self.run_id, cycle_simulated=self.simulated,
                        configuration_id=self.configuration_id, contact_api_version=2,
                        original_timing_supported=True,
                        motion_diagnostics_supported=True, robot_only_supported=True,
                        cycle_first_error=self.first_error, cycle_failure_json=self.failure_detail,
                        cycle_cleanup_errors='; '.join(self.cleanup_errors), cycle_workflow=self.workflow,
                        original_contact_timing=self.settings.get('originalContactTiming', False))

    def start(self, simulated=True, workflow='full'):
        if workflow not in ('full', 'insert', 'withdraw'):
            raise ValueError('Invalid cycle workflow')
        with self.lock:
            if self.active:
                raise RuntimeError('Contact cycle already active')
            if not simulated and not self.configuration_id:
                raise RuntimeError('Real cycle requires confirmed field configuration')
            self.active = True
            self.phase, self.result, self.error = 'STARTING', '', ''
            self.first_error, self.failure_detail, self.cleanup_errors = '', '', []
            self.last_robot, self.last_tool = {}, {}
            self.workflow = workflow
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
            if not self.simulated and self.workflow == 'full':
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
            status = client.status()  # keep lease alive even while the tool is tightening
            self.last_robot = {k: status.get(k) for k in ('mode', 'age_s', 'error', 'collision', 'force', 'pose', 'joints', 'user', 'tool', 'sequence')}
            if not self.simulated:
                if status['age_s'] > 0.25 or status['mode'] != 5 or status['error'] or status['collision']:
                    raise RuntimeError('Robot hold feedback invalid: age=%s mode=%s error=%s collision=%s' %
                                       (status['age_s'], status['mode'], status['error'], status['collision']))
                f = status['force']
                if len(f) != 6 or not all(math.isfinite(x) for x in f) or math.sqrt(sum(x*x for x in f[:3])) >= self.settings['forceLimitN'] or math.sqrt(sum(x*x for x in f[3:])) >= self.settings['momentLimitNm']:
                    raise RuntimeError('Robot hold force limit: force=%s forceLimitN=%s momentLimitNm=%s' %
                                       (f, self.settings['forceLimitN'], self.settings['momentLimitNm']))
            self.cancel.wait(0.1)

    def _stage(self, client, direction, simulated):
        s = self.settings
        args = SimpleNamespace(execute=True, simulate=simulated, tool=s['toolNumber'],
            direction=direction, distance_mm=s['insertionMm' if direction < 0 else 'withdrawalMm'], speed_mm_s=s['contactSpeedMmS'],
            position_step_mm=s['contactStepMm'], lateral_trigger_n=s['lateralLimitN'],
            search_step_mm=0.1, search_duration_s=0.25,
            search_improvement_n=0.02, hard_force_n=s['forceLimitN'],
            hard_moment_nm=s['momentLimitNm'], max_runtime_s=s['contactStageTimeoutSeconds'],
            original_contact_timing=s.get('originalContactTiming', False))
        self._check()
        client.begin_stage('INSERTING' if direction < 0 else 'EXITING', args.distance_mm)
        result = HybridExit(args, client).run()
        if result['result'] != 'completed':
            raise RuntimeError(str(result))
        client.end_stage()

    def _failure(self, exc):
        # Freeze the first cause before stop/release can cancel the outer Unity task.
        def clean(value):
            if isinstance(value, float) and not math.isfinite(value):
                return str(value)
            if isinstance(value, dict):
                return {str(k): clean(v) for k, v in value.items()}
            if isinstance(value, (list, tuple)):
                return [clean(v) for v in value]
            return value
        with self.lock:
            if not self.first_error:
                self.first_error = str(exc)
                self.failure_detail = json.dumps(clean(dict(timeUtc=datetime.now(timezone.utc).isoformat(), cycle_id=self.run_id, phase=self.phase,
                    workflow=self.workflow, error_type=type(exc).__name__, error=str(exc),
                    robot=self.last_robot, tool=self.last_tool, robot_is_last_observation=True)), ensure_ascii=False, allow_nan=False)
            self.error = self.first_error
            detail = self.failure_detail
        try:
            print('[ContactCycle][FIRST_FAILURE] ' + detail, flush=True)
        except OSError:
            pass  # A broken stdout pipe must not prevent stop cleanup.

    def _cleanup_failure(self, exc):
        with self.lock:
            self.cleanup_errors.append(str(exc))
        try:
            print('[ContactCycle][CLEANUP_FAILURE] ' + str(exc), flush=True)
        except OSError:
            pass

    def _run(self, simulated):
        client = None
        outcome = ''
        try:
            client = UnityRobotClient(simulate=simulated).open()
            if self.workflow != 'full':
                self._phase('INSERTING' if self.workflow == 'insert' else 'EXITING')
                self._stage(client, -1.0 if self.workflow == 'insert' else 1.0, simulated)
                with self.lock:
                    self.result = 'ROBOT_ONLY_DONE'
                return
            self._phase('INSERTING')
            self._stage(client, -1.0, simulated)
            self._phase('BEFORE_TIGHTEN')
            self._wait(self.settings['beforeTightenSeconds'], client)
            self._phase('TIGHTENING')
            if simulated:
                self._wait(2, client)
                outcome = 'SIMULATED_OK'
            else:
                self._check()
                self.start_tool()  # clears old run before starting, never accepts previous OK
                end = time.monotonic() + self.settings['tightenTimeoutSeconds']
                while time.monotonic() < end:
                    self._check()
                    status = self.read_tool()
                    self.last_tool = dict(status)
                    outcome = status['state']
                    if outcome in ('OK', 'NG_SLIP', 'JAM', 'NG_DEVICE', 'STOP'):
                        break
                    self._wait(0.1, client)
                else:
                    raise RuntimeError('Tightening result timeout')
                if outcome not in ('OK', 'NG_SLIP', 'JAM'):
                    raise RuntimeError('Tightening failed: ' + outcome)
            self._phase('BEFORE_EXIT')
            self._wait(self.settings['beforeExitSeconds'], client)
            self._phase('EXITING')
            self._stage(client, 1.0, simulated)
            # A completed withdrawal must not turn NG into a successful assembly.
            with self.lock:
                self.result = outcome
        except Exception as exc:
            cached = getattr(client, 'last_status', None)
            if isinstance(cached, dict) and cached:
                self.last_robot = {k: cached.get(k) for k in ('mode', 'age_s', 'robot_error', 'collision',
                    'force', 'pose', 'joints', 'user', 'tool', 'sequence', 'timestamp_s')}
            self._failure(exc)
            with self.lock:
                self.result = 'CANCELLED' if self.cancel.is_set() else 'FAILED'
            if client:
                try:
                    client.stop()
                except Exception as cleanup:
                    self._cleanup_failure(cleanup)
            if not simulated and self.workflow == 'full':
                try:
                    self.stop_tool()
                except Exception as cleanup:
                    self._cleanup_failure(cleanup)
        finally:
            if client:
                try:
                    client.close()
                except Exception as exc:
                    self._failure(exc)
                    self._cleanup_failure(exc)
                    with self.lock:
                        self.result = 'FAILED'
            with self.lock:
                self.phase = 'DONE' if self.result in ('OK', 'SIMULATED_OK', 'ROBOT_ONLY_DONE') else 'FAILED'
                self.active = False
