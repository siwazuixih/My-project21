"""Bounded, small-step exit. Reuses the installed controller and force API."""
import copy
import inspect
import math
import statistics
import time

VERSION = 'exit-small-step-20261023'


class ExitStop(RuntimeError):
    def __init__(self, result, message):
        super().__init__(message)
        self.result = result


def settings(args):
    a = copy.copy(args)
    a.speed_mm_s = min(a.speed_mm_s, 0.6)
    a.position_step_mm = min(a.position_step_mm, 0.15)
    a.min_position_step_mm = min(a.min_position_step_mm, 0.03, a.position_step_mm)
    a.control_period_s = min(a.control_period_s, 0.25)
    a.lateral_trigger_n = min(a.lateral_trigger_n, 12.0)
    a.lateral_clear_n = min(a.lateral_clear_n, 8.0, a.lateral_trigger_n * 0.65)
    a.lateral_slow_n = min(a.lateral_slow_n, 6.0, a.lateral_trigger_n * 0.5)
    a.lateral_slow_ratio = min(a.lateral_slow_ratio, 0.5)
    a.search_step_mm = min(a.search_step_mm, 0.05)
    a.search_rounds = min(a.search_rounds, 2)
    # Absolute force/moment/lateral limits, tool number, and force API stay intact.
    return a


def make_controller(base):
    class OptimizedExit(base.HybridExit):
        def __init__(self, args):
            super().__init__(settings(args))
            self.origin = None
            self.axis = None
            self.measured_mm = 0.0
            self.last_wrench = None
            self.search_total = 0
            self.max_search_total = 8
            self.max_lateral_offset_mm = 0.3
            self.axial_soft_n = min(18.0, self.args.hard_force_n * 0.6)
            self.force_has_feedback = 'feedback' in inspect.signature(base.tool_wrench).parameters

        def read_pose(self):
            p = tuple(base.pose(self.dashboard))
            if len(p) != 6 or not all(math.isfinite(v) for v in p):
                raise ExitStop('invalid_pose', 'Pose must contain six finite values')
            return p

        def wrench(self):
            self.safety_ok(allow_running=True)
            if self.force_has_feedback:
                w = base.tool_wrench(self.dashboard, self.args.tool, self.feedback)
            else:
                w = base.tool_wrench(self.dashboard, self.args.tool)
            if len(w) != 6 or not all(math.isfinite(v) for v in w):
                raise ExitStop('invalid_force', 'Force must contain six finite values')
            self.last_wrench = tuple(w)
            # Never discard a high sample by filtering it away.
            if base.norm3(w[:3]) >= self.args.hard_force_n or base.norm3(w[3:]) >= self.args.hard_moment_nm:
                raise ExitStop('safety_stop', 'Compensated wrench reached original hard limit')
            if base.lateral_cost(w) >= self.args.lateral_hard_n:
                raise ExitStop('safety_stop', 'Compensated lateral load reached original hard limit')
            if abs(w[0]) >= self.axial_soft_n:
                raise ExitStop('axial_load_stop', 'Axial load reached early exit stop; inspect tool release')
            return tuple(w)

        def sampled_cost(self):
            costs = []
            for _ in range(3):
                costs.append(base.lateral_cost(self.wrench()))
                time.sleep(0.02)
            return statistics.median(costs)

        def update_progress(self, p):
            delta = tuple(p[i] - self.origin[i] for i in range(3))
            self.measured_mm = sum(delta[i] * self.axis[i] for i in range(3))
            sideways = math.sqrt(sum((delta[i] - self.measured_mm * self.axis[i]) ** 2 for i in range(3)))
            if sideways > self.max_lateral_offset_mm + 0.01:
                raise ExitStop('offset_limit', 'Measured lateral offset exceeded 0.30 mm')
            angular = max(abs((p[i] - self.origin[i] + 180.0) % 360.0 - 180.0) for i in range(3, 6))
            if angular > 0.5:
                raise ExitStop('orientation_changed', 'Tool orientation changed during exit')
            if self.measured_mm < -0.05 or self.measured_mm > self.args.distance_mm + 0.05:
                raise ExitStop('travel_limit', 'Measured X travel exceeded exit bounds')

        def move_checked(self, dx, dy, dz, duration):
            self.wrench()
            before = self.read_pose()
            target = tuple(self.relative_target(dx, dy, dz))
            if len(target) != 6 or not all(math.isfinite(v) for v in target):
                raise ExitStop('invalid_pose', 'Invalid relative target')
            delta = tuple(target[i] - before[i] for i in range(3))
            length = base.norm3(delta)
            if length < 1e-6 or abs(length - math.sqrt(dx*dx + dy*dy + dz*dz)) > 0.01:
                raise ExitStop('invalid_pose', 'Relative target displacement is inconsistent')
            target_delta = tuple(target[i] - self.origin[i] for i in range(3))
            axial = sum(target_delta[i] * self.axis[i] for i in range(3))
            lateral = math.sqrt(sum((target_delta[i] - axial * self.axis[i])**2 for i in range(3)))
            if lateral > self.max_lateral_offset_mm:
                raise ExitStop('offset_limit', 'Search target exceeds lateral offset budget')
            self.servo_relative(dx, dy, dz, duration, stop_at_end=True, enforce_lateral=True)
            after = self.read_pose()
            self.update_progress(after)
            achieved = sum((after[i]-before[i]) * delta[i] / length for i in range(3))
            error = base.norm3(tuple(after[i]-target[i] for i in range(3)))
            self.history.append({'phase': 'exit_segment', 'requested_delta': [dx,dy,dz],
                                 'achieved_mm': achieved, 'target_error_mm': error,
                                 'measured_exit_mm': self.measured_mm})
            if achieved < length * 0.25 or error > max(0.03, length * 0.4):
                raise ExitStop('tracking_error', 'Actual displacement did not follow target; no further pulling')
            self.wrench()

        def search_exit(self):
            # Stay at the first verified improvement instead of repeatedly
            # returning into contact to compare all four directions.
            for _ in range(self.args.search_rounds):
                cost = self.sampled_cost()
                if cost <= self.args.lateral_clear_n:
                    return True
                if self.search_total >= self.max_search_total:
                    return False
                self.search_total += 1
                improved = False
                step = self.args.search_step_mm
                for dy, dz in ((step,0), (-step,0), (0,step), (0,-step)):
                    self.move_checked(0,dy,dz,max(0.2,self.args.search_duration_s))
                    trial = self.sampled_cost()
                    if trial + self.args.search_improvement_n < cost:
                        self.history.append({'phase':'exit_search','before':cost,'after':trial,
                                             'accepted_delta':[0,dy,dz]})
                        improved = True
                        break
                    # Return only after all original hard checks passed.
                    self.move_checked(0,-dy,-dz,max(0.2,self.args.search_duration_s))
                if not improved:
                    return False
            return self.sampled_cost() <= self.args.lateral_clear_n

        def run(self):
            plan = vars(self.args).copy()
            plan.update(exit_strategy=VERSION, axial_soft_stop_n=self.axial_soft_n,
                        max_lateral_offset_mm=self.max_lateral_offset_mm,
                        max_search_total=self.max_search_total)
            if not self.args.execute:
                return {'plan':plan,'result':'dry_run'}
            outcome, reason = 'completed', None
            try:
                if self.args.direction != -1.0:
                    raise ExitStop('invalid_direction', 'Optimized exit requires direction -1.0')
                self.dashboard = base.Dashboard(self.args.ip)
                self.feedback = base.Feedback(self.args.ip)
                self.deadline = time.monotonic() + self.args.max_runtime_s
                self.feedback.wait()
                self.safety_ok()
                self.wrench()
                self.origin = self.read_pose()
                target = tuple(self.relative_target(-1.0,0,0))
                if len(target)!=6 or not all(math.isfinite(v) for v in target):
                    raise ExitStop('invalid_pose','Invalid exit direction reference')
                vector = tuple(target[i]-self.origin[i] for i in range(3))
                length = base.norm3(vector)
                if abs(length-1.0)>0.01:
                    raise ExitStop('invalid_pose','Invalid tool X unit vector')
                self.axis = tuple(v/length for v in vector)
                while self.args.distance_mm - self.measured_mm > 0.01:
                    self.safety_ok(allow_running=True)
                    w = self.wrench()
                    cost = base.lateral_cost(w)
                    if cost >= self.args.lateral_trigger_n:
                        if not self.search_exit():
                            raise ExitStop('lateral_jam', 'Bounded early search did not clear contact')
                        w = self.wrench()
                        cost = base.lateral_cost(w)
                        if cost >= self.args.lateral_trigger_n:
                            raise ExitStop('lateral_jam', 'Load rose again after search')
                    speed = self.args.speed_mm_s
                    loaded = cost >= self.args.lateral_slow_n or abs(w[0]) >= self.axial_soft_n * 0.5
                    if loaded:
                        speed *= self.args.lateral_slow_ratio
                    step = min(self.args.position_step_mm, 0.05 if loaded else 0.15,
                               speed*self.args.control_period_s,
                               self.args.distance_mm-self.measured_mm)
                    self.move_checked(-step,0,0,max(self.args.min_servo_duration_s,step/speed))
            except ExitStop as exc:
                outcome, reason = exc.result, str(exc)
            except Exception as exc:
                outcome, reason = 'safety_stop', str(exc)
            finally:
                self.stop()
                if self.dashboard and self.origin is not None and self.axis is not None:
                    try:
                        # A hard stop can interrupt a segment before move_checked
                        # reads its endpoint. Report the final measured travel too.
                        final_pose = self.read_pose()
                        self.update_progress(final_pose)
                    except Exception as exc:
                        self.history.append({'phase':'final_pose_check','reason':str(exc)})
                        if outcome == 'completed':
                            outcome, reason = 'safety_stop', 'Final pose check failed: ' + str(exc)
                if self.feedback: self.feedback.close()
                if self.dashboard: self.dashboard.close()
            result = {'plan':plan,'result':outcome,'moved_mm':self.measured_mm,
                      'last_wrench':self.last_wrench,'history':self.history}
            if reason: result['reason'] = reason
            return result
    return OptimizedExit


def run_exit(args, base):
    return make_controller(base)(args).run()
