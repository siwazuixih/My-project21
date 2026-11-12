"""V2 +X insertion / -X exit; Unity owns robot IO and one cycle owns contact."""
import threading
import time
import math
import json
from datetime import datetime, timezone
from types import SimpleNamespace
from unity_robot_client import UnityRobotClient
import cr10af_hybrid_x as algorithm
from cr10af_hybrid_x import HybridExit, PROFILE
from cr10af_exit_optimized import run_exit
from contact_session import ContactSession


class ContactCycle:
    def __init__(self, start_tool, read_tool, stop_tool, prepare_tool=None, finish_tool=None, hold_tool=None, session_path=None):
        self.start_tool, self.read_tool, self.stop_tool = start_tool, read_tool, stop_tool
        self.prepare_tool, self.finish_tool = prepare_tool, finish_tool
        self.hold_tool = hold_tool
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
        self.stage_records = []
        self.session = ContactSession(session_path)
        self.isolation_only = False
        self.identity = {}
        self.settings = dict(toolNumber=1, insertionMm=20, withdrawalMm=20,
            contactSpeedMmS=1.2, contactStepMm=2, lateralLimitN=20,
            forceLimitN=30, momentLimitNm=10, contactStageTimeoutSeconds=90,
            tightenTimeoutSeconds=60, beforeTightenSeconds=5, beforeExitSeconds=5)
        self.settings.update(exitStageTimeoutSeconds=90, contactLateralTrigger=18,
            contactLateralClear=12, contactLateralSlow=12, positiveXInsertionConfirmed=False)
        self.configuration_id = ''

    def configure(self, values):
        limits = dict(toolNumber=(0, 50), insertionMm=(0, 100), withdrawalMm=(0, 100),
            contactSpeedMmS=(0.1, 5), contactStepMm=(0.1, 5), lateralLimitN=(0.1, 30),
            forceLimitN=(0.1, 30), momentLimitNm=(0.1, 10), contactStageTimeoutSeconds=(1, 600),
            tightenTimeoutSeconds=(1, 300), beforeTightenSeconds=(0, 60), beforeExitSeconds=(0, 60))
        limits.update(exitStageTimeoutSeconds=(1, 600), contactLateralTrigger=(0.01, 30),
                      contactLateralClear=(0, 30), contactLateralSlow=(0, 30))
        updated = {}
        direction_confirmed = values.get('positiveXInsertionConfirmed', False)
        if not isinstance(direction_confirmed, bool):
            raise ValueError('positiveXInsertionConfirmed must be boolean')
        updated['positiveXInsertionConfirmed'] = direction_confirmed
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
        if not (updated['contactLateralClear'] < updated['contactLateralTrigger'] < min(26, updated['lateralLimitN'])) or updated['contactLateralSlow'] > updated['contactLateralTrigger']:
            raise ValueError('Require clear < search trigger < lateral hard limit; slow <= trigger')
        # V2 caps effective segments by speed*0.25s; the uncapped requested
        # step/speed ratio no longer describes the duration sent to the gateway.
        configuration_id = values.get('configuration_id', '')
        if not isinstance(configuration_id, str) or not configuration_id or len(configuration_id) > 80:
            raise ValueError('Missing configuration id')
        with self.lock:
            if self.active:
                raise RuntimeError('Cannot configure active cycle')
            self.settings = updated
            self.configuration_id = configuration_id
            self.identity = {key: values.get(key) for key in
                ('robotIp', 'dashboardPort', 'feedbackPort', 'userNumber', 'toolNumber', 'toolIp', 'toolPort')}

    def snapshot(self):
        with self.lock:
            return dict(cycle_active=self.active, cycle_phase=self.phase,
                        cycle_result=self.result, cycle_error=self.error,
                        cycle_id=self.run_id, cycle_simulated=self.simulated,
                        configuration_id=self.configuration_id, contact_api_version=5,
                        contact_profile=PROFILE, exit_optimization_supported=True,
                        positive_x_insertion=self.settings['positiveXInsertionConfirmed'],
                        tool_stop_confirmation_supported=True,
                        original_timing_supported=True,
                        motion_diagnostics_supported=True, robot_only_supported=True,
                        cycle_first_error=self.first_error, cycle_failure_json=self.failure_detail,
                        cycle_cleanup_errors='; '.join(self.cleanup_errors), cycle_workflow=self.workflow,
                        original_contact_timing=self.settings.get('originalContactTiming', False),
                        contact_session_supported=True, **self.contact_state())

    def contact_state(self):
        if self.isolation_only:
            return dict(contact_session_blocked=False, contact_session_json='', contact_session_path='isolated-no-real-state')
        return self.session.snapshot()

    def start(self, simulated=True, workflow='full'):
        if workflow not in ('full', 'insert', 'withdraw'):
            raise ValueError('Invalid cycle workflow')
        with self.lock:
            if self.active:
                raise RuntimeError('Contact cycle already active')
            if self.isolation_only and not simulated:
                raise RuntimeError('Isolated service cannot start real contact')
            if not simulated and not self.configuration_id:
                raise RuntimeError('Real cycle requires confirmed field configuration')
            if not simulated and not self.settings['positiveXInsertionConfirmed']:
                raise RuntimeError('Confirm NEW direction: +X insertion / -X withdrawal')
            if not simulated:
                self.validate_stage_budgets(workflow)  # before prepare/tool/socket/thread/state-file creation
            if not simulated and self.session.snapshot()['contact_session_blocked']:
                raise RuntimeError('contact_session_blocked: inspect contact and use explicit field recovery')
            if not simulated and (any(v is None for v in self.identity.values()) or self.identity.get('feedbackPort') != 30004):
                raise RuntimeError('Real contact requires complete confirmed robot/User/Tool identity and 30004')
            if not simulated and workflow == 'full' and self.prepare_tool:
                self.prepare_tool()  # explicit new user cycle, never clear stop inside reset/start
            self.active = True
            self.phase, self.result, self.error = 'STARTING', '', ''
            self.first_error, self.failure_detail, self.cleanup_errors = '', '', []
            self.last_robot, self.last_tool = {}, {}
            self.stage_records = []
            self.workflow = workflow
            self.simulated = simulated
            self.run_id += 1
            self.cancel.clear()
        threading.Thread(target=self._run, args=(simulated,), daemon=True).start()

    def validate_stage_budgets(self, workflow):
        s = self.settings
        stages = [('insertion', 'insertionMm', 'contactStageTimeoutSeconds', .4, 1),
                  ('exit', 'withdrawalMm', 'exitStageTimeoutSeconds', .6, 90)]
        for stage, distance, timeout, cap, minimum in stages:
            if workflow == 'insert' and stage == 'exit' or workflow == 'withdraw' and stage == 'insertion':
                continue
            speed = min(cap, s['contactSpeedMmS'])
            theoretical = s[distance] / speed
            if s[timeout] < minimum or s[timeout] <= theoretical:
                raise ValueError('V2 {} budget: {}mm at <={}mm/s requires >{:.1f}s and >={}s; {}={}s. '
                                 'No motion dispatched; query/stop/search need additional time.'.format(
                                     stage, s[distance], speed, theoretical, minimum, timeout, s[timeout]))

    def stop(self, reason=None):
        if reason:
            self._failure(RuntimeError(reason))
        self.cancel.set()
        # Real motion must be interrupted through a different local connection.
        # Simulated waits observe cancel; never send an unowned hardware stop.
        try:
            if not self.simulated:
                UnityRobotClient.emergency_stop()
        finally:
            if not self.simulated and self.workflow == 'full':
                self.stop_tool()

    def _phase(self, value):
        with self.lock:
            self.phase = value
        if not self.simulated and self.session.owner:
            self.session.update(force=True, phase=value)
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
            direction=direction, distance_mm=s['insertionMm' if direction > 0 else 'withdrawalMm'], speed_mm_s=s['contactSpeedMmS'],
            position_step_mm=s['contactStepMm'], min_position_step_mm=min(.2, s['contactStepMm']),
            control_period_s=1.5, min_servo_duration_s=.2,
            lateral_trigger_n=s['contactLateralTrigger'], lateral_clear_n=s['contactLateralClear'],
            lateral_hard_n=min(26, s['lateralLimitN']), lateral_slow_n=s['contactLateralSlow'],
            lateral_slow_ratio=.75, lateral_filter_samples=5, lateral_filter_alpha=.35,
            search_step_mm=.05, search_duration_s=.15, search_settle_s=.05, search_rounds=8,
            max_search_attempts=3, search_improvement_n=.20, hard_force_n=s['forceLimitN'],
            hard_moment_nm=s['momentLimitNm'], max_runtime_s=s['contactStageTimeoutSeconds' if direction > 0 else 'exitStageTimeoutSeconds'],
            original_contact_timing=s.get('originalContactTiming', False))
        self._check()
        client.begin_stage('INSERTING' if direction > 0 else 'EXITING', args.distance_mm)
        checkpoint = None if simulated else self.session.update
        result = run_exit(args, algorithm, client, self.cancel, checkpoint)
        with self.lock:
            self.stage_records.append(dict(stage=self.phase, **result))
        print('[ContactCycle][STAGE_RESULT] stage=%s result=%s moved_mm=%s reason=%s' %
              (self.phase, result.get('result'), result.get('moved_mm'), result.get('reason', '')), flush=True)
        if not simulated:
            # Full per-segment history belongs in run evidence, not the small interlock file.
            self.session.update(force=True, phase=self.phase,
                                stage_result={k: v for k, v in result.items() if k != 'history'})
        if result['result'] != 'completed':
            raise RuntimeError('%s: %s; %s; moved_mm=%s' % (self.phase, result['result'], result.get('reason', ''), result.get('moved_mm')))
        client.end_stage()

    def report(self):
        with self.lock:
            return dict(cycle_id=self.run_id, cycle_report_json=json.dumps(dict(
                profile=PROFILE, configuration_id=self.configuration_id, cycle_id=self.run_id,
                simulated=self.simulated, stages=self.stage_records, result=self.result,
                error=self.first_error, tool=self.last_tool, contact_session=self.contact_state()), ensure_ascii=False, allow_nan=False))

    def reset_contact_session(self, confirmed):
        """Read-only device checks after operator physically frees the tool. No motion/zeroing."""
        with self.lock:
            if self.isolation_only or self.active or confirmed is not True or not self.configuration_id:
                raise RuntimeError('Recovery requires idle cycle, confirmed configuration and human tool-free confirmation')
            previous = self.session.read()
            if previous is None:
                return
            started = time.monotonic()
            stable = None
            while time.monotonic() - started < 4:
                probe = UnityRobotClient.read_only_probe(self.settings['toolNumber'], self.identity['userNumber'])
                w = probe['values']
                if len(w) != 6 or not all(math.isfinite(x) for x in w):
                    raise RuntimeError('Recovery wrench invalid')
                raw = probe.get('force')
                if not isinstance(raw, (list, tuple)) or len(raw) != 6 or not all(math.isfinite(x) for x in raw):
                    raise RuntimeError('Recovery raw feedback wrench invalid')
                if (algorithm.norm3(w[:3]) >= self.settings['forceLimitN'] or
                    algorithm.norm3(w[3:]) >= self.settings['momentLimitNm'] or
                    algorithm.norm3(raw[:3]) >= self.settings['forceLimitN'] or
                    algorithm.norm3(raw[3:]) >= self.settings['momentLimitNm'] or
                    algorithm.lateral_cost(w) >= min(26, self.settings['lateralLimitN']) or
                    abs(w[0]) >= min(12, self.settings['forceLimitN'] * .6)):
                    raise RuntimeError('Recovery load precheck failed; do not clear contact state')
                tool = self.read_tool()
                age, speed = tool.get('feedback_age'), tool.get('live_speed')
                valid = (tool.get('tool_connected') and isinstance(age, (int, float)) and math.isfinite(age) and 0 <= age <= .25 and
                         tool.get('feedback_time', 0) >= started and isinstance(speed, (int, float)) and math.isfinite(speed) and abs(speed) <= 1 and
                         tool.get('device_error', -1) == 0 and not any(tool.get(k, True) for k in ('result_processing', 'operation_busy', 'stop_in_progress')))
                stable = (time.monotonic() if stable is None else stable) if valid else None
                if stable is not None and time.monotonic() - stable >= .3:
                    self.session.reset(self.identity)
                    try:
                        print('[ContactCycle][RECOVERY] ' + json.dumps(dict(human_confirmed_tool_free=True,
                            motion_sent=False, previous=previous, probe=probe,
                            tool_stop={k: tool.get(k) for k in ('feedback_age', 'feedback_time', 'live_speed', 'device_error')}),
                            ensure_ascii=False, allow_nan=False), flush=True)
                    except OSError:
                        pass  # Broken log pipe cannot turn a completed, read-only reset into a motion request.
                    return
                time.sleep(.05)
            raise RuntimeError('Recovery tool fresh stop not confirmed; contact state retained')

    def _tool_stopped(self, client, expected_run=None):
        started = time.monotonic()
        stable = None
        while time.monotonic() - started < 3.5:
            self._check()
            status = self.read_tool()
            self.last_tool = dict(status)
            age = status.get('feedback_age')
            valid = (status.get('tool_connected') and isinstance(age, (int, float)) and
                     math.isfinite(age) and 0 <= age <= .25 and
                     status.get('feedback_time', 0) >= started and
                     isinstance(status.get('live_speed'), (int, float)) and math.isfinite(status['live_speed']) and
                     abs(status['live_speed']) <= 1 and status.get('device_error', -1) == 0)
            if expected_run is not None:
                valid = valid and status.get('run_id') == expected_run and not status.get('result_processing', True) and status.get('tool_stop_confirmed', False)
            if status.get('state') in ('COMM_LOST', 'FEEDBACK_STALE', 'TIGHTEN_TIMEOUT', 'NG_DEVICE', 'EXIT_BLOCKED', 'STOP'):
                raise RuntimeError('Tool stop blocked: ' + str(status.get('state')))
            stable = (time.monotonic() if stable is None else stable) if valid else None
            if stable is not None and time.monotonic() - stable >= .3:
                return
            self._wait(.04, client)
        status = self.last_tool
        raise RuntimeError('EXIT_BLOCKED: fresh tool stop not confirmed; connected=%s age=%s live_speed=%s device_error=%s tool_io=%s' %
            (status.get('tool_connected'), status.get('feedback_age'), status.get('live_speed'),
             status.get('device_error'), json.dumps(status.get('tool_io', {}), ensure_ascii=True, allow_nan=False)))

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
            if not simulated:
                self.session.begin(self.identity, self.configuration_id, self.workflow)
            client = UnityRobotClient(simulate=simulated).open()
            if self.workflow != 'full':
                self._phase('INSERTING' if self.workflow == 'insert' else 'EXITING')
                self._stage(client, 1.0 if self.workflow == 'insert' else -1.0, simulated)
                with self.lock:
                    self.result = 'ROBOT_ONLY_DONE'
                return
            self._phase('INSERTING')
            if not simulated:
                self._check()
                if self.hold_tool:
                    self.hold_tool()
                self._tool_stopped(client)
            self._stage(client, 1.0, simulated)
            self._phase('BEFORE_TIGHTEN')
            self._wait(self.settings['beforeTightenSeconds'], client)
            self._phase('TIGHTENING')
            if simulated:
                self._wait(2, client)
                outcome = 'SIMULATED_OK'
            else:
                self._check()
                self.start_tool()  # clears old run before starting, never accepts previous OK
                tool_run = self.read_tool().get('run_id')
                if not tool_run:
                    raise RuntimeError('Tool did not return current run id')
                end = time.monotonic() + self.settings['tightenTimeoutSeconds']
                while time.monotonic() < end:
                    self._check()
                    status = self.read_tool()
                    self.last_tool = dict(status)
                    if status.get('run_id') != tool_run:
                        raise RuntimeError('Tool run id changed')
                    outcome = status['state']
                    if outcome in ('OK', 'NG_SLIP', 'JAM', 'NG_DEVICE', 'STOP', 'COMM_LOST', 'FEEDBACK_STALE', 'TIGHTEN_TIMEOUT', 'EXIT_BLOCKED'):
                        break
                    self._wait(0.1, client)
                else:
                    raise RuntimeError('Tightening result timeout')
                if outcome not in ('OK', 'NG_SLIP', 'JAM'):
                    raise RuntimeError('Tightening failed: ' + outcome)
                self._phase('VERIFY_TOOL_STOP')
                self._tool_stopped(client, tool_run)
            self._phase('BEFORE_EXIT')
            self._wait(self.settings['beforeExitSeconds'], client)
            if not simulated:
                self._tool_stopped(client, tool_run)
            self._phase('EXITING')
            self._stage(client, -1.0, simulated)
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
            if not simulated and self.workflow == 'full' and self.finish_tool:
                try:
                    self.finish_tool()
                except Exception as exc:
                    self._failure(exc)
                    self.result = 'FAILED'
            if self.cancel.is_set() and self.result in ('OK', 'SIMULATED_OK', 'NG_SLIP', 'JAM', 'ROBOT_ONLY_DONE'):
                self._failure(RuntimeError('Cycle cancelled'))
                self.result = 'CANCELLED'
            if not simulated and self.session.owner:
                try:
                    if not self.cancel.is_set() and not self.cleanup_errors and self.result in ('OK', 'NG_SLIP', 'JAM', 'ROBOT_ONLY_DONE') and self.workflow != 'insert':
                        self.session.complete()  # Only after successful withdrawal AND checked stop/release.
                    else:
                        self.session.update(force=True, status='blocked', phase=self.phase,
                                            result=self.result, reason=self.first_error,
                                            robot_is_last_observation=True, robot=self.last_robot,
                                            note='Possible contact remains; physical inspection required')
                except Exception as exc:
                    self._failure(exc)
                    self.result = 'FAILED'
            with self.lock:
                self.phase = 'DONE' if self.result in ('OK', 'SIMULATED_OK', 'ROBOT_ONLY_DONE') else 'FAILED'
                self.active = False
