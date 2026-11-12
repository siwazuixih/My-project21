#!/usr/bin/env python3
"""V2 controller adapter; +X insertion, -X withdrawal use measured small steps.
Only Unity owns robot TCP. Legacy search helpers are retained but not dispatched.
"""
import argparse
import json
import math
import sys
import time
from unity_robot_client import UnityRobotClient

PROFILE = 'contact-compliant-v2-20261029'

def pose(dashboard):
    return dashboard.pose()

def tool_wrench(dashboard, tool):
    values = dashboard.wrench(tool)
    if len(values) != 6 or not all(math.isfinite(x) for x in values):
        raise RuntimeError('Invalid compensated tool wrench')
    return values

def norm3(v):
    return math.sqrt(sum(x * x for x in v))

def lateral_cost(w):
    # Tool Y/Z are transverse to this application's tool X guide axis.
    return math.hypot(w[1], w[2]) + 0.25 * math.hypot(w[3], w[5])

def filtered_lateral_cost(samples, alpha=0.35):
    """Use an EMA of recent lateral costs to reject one-frame spikes."""
    if not samples:
        return 0.0
    value = float(samples[0])
    for sample in samples[1:]:
        value = alpha * float(sample) + (1.0 - alpha) * value
    return value

class HybridExit:
    def __init__(self, args, client=None, cancel=None):
        self.args, self.client, self.cancel = args, client, cancel
        self.dashboard = self.feedback = None
        self.history = []
        self.lateral_samples = []
        self.lateral_active = False
        self.search_attempts = 0
        self.deadline = None

    def safety_ok(self, allow_running=False, enforce_lateral=False):
        if self.cancel is not None and self.cancel.is_set():
            raise RuntimeError('Cycle cancelled')
        if self.deadline is not None and time.monotonic() >= self.deadline:
            raise TimeoutError('Robot runtime budget exhausted')
        f = self.dashboard.status()
        if not f or not 0 <= time.monotonic() - f['time'] <= 0.25:
            raise RuntimeError('Feedback is stale; stopping')
        allowed = (5, 7) if allow_running else (5,)
        if f['mode'] not in allowed or f['error'] or f['collision']:
            raise RuntimeError('Robot is not ready: ' + str(f))
        if len(f['force']) != 6 or len(f['pose']) != 6 or not all(math.isfinite(x) for x in tuple(f['force']) + tuple(f['pose'])):
            raise RuntimeError('Non-finite robot feedback')
        if norm3(f['force'][:3]) >= self.args.hard_force_n or norm3(f['force'][3:]) >= self.args.hard_moment_nm:
            raise RuntimeError('Hard force/moment limit reached')
        if enforce_lateral and lateral_cost(tool_wrench(self.dashboard, self.args.tool)) >= self.args.lateral_hard_n:
            raise RuntimeError('Hard lateral force limit reached')

    def stop(self):
        if self.dashboard:
            self.dashboard.finish_stage()  # checked stage stop, not cancellation

    def relative_target(self, dx, dy, dz):
        return self.dashboard.relative_target(dx, dy, dz)

    def servo_relative(self, dx, dy, dz, duration, stop_at_end=True, enforce_lateral=False):
        self.safety_ok(allow_running=True, enforce_lateral=enforce_lateral)
        self.dashboard.move_original_interval(dx, dy, dz, duration, stop_at_end, enforce_lateral)
        self.safety_ok(allow_running=not stop_at_end, enforce_lateral=enforce_lateral)

    def run(self):
        from cr10af_exit_optimized import run_exit
        return run_exit(self.args, sys.modules[__name__], self.client, self.cancel)

    def lateral_state(self, wrench):
        """Return filtered transverse load and hysteretic search state."""
        raw = lateral_cost(wrench)
        self.lateral_samples.append(raw)
        self.lateral_samples = self.lateral_samples[-self.args.lateral_filter_samples:]
        filtered = filtered_lateral_cost(self.lateral_samples, self.args.lateral_filter_alpha)
        if self.lateral_active:
            if filtered <= self.args.lateral_clear_n:
                self.lateral_active = False
        elif filtered >= self.args.lateral_trigger_n:
            self.lateral_active = True
        return filtered, self.lateral_active

    def lateral_search(self):
        initial = tool_wrench(self.dashboard, self.args.tool)
        start = initial
        step = self.args.search_step_mm
        moved_search = False
        best = (lateral_cost(start), 0.0, 0.0, start)
        # Try up to four local rounds. This prevents a single small step from
        # being mistaken for a local minimum when the initial offset is large.
        for _ in range(self.args.search_rounds):
            round_best = best
            for dy, dz in ((step, 0), (-step, 0), (0, step), (0, -step)):
                self.servo_relative(0, dy, dz, self.args.search_duration_s,
                                    stop_at_end=True, enforce_lateral=True)
                time.sleep(self.args.search_settle_s)
                trial = tool_wrench(self.dashboard, self.args.tool)
                self.servo_relative(0, -dy, -dz, self.args.search_duration_s,
                                    stop_at_end=True, enforce_lateral=True)
                time.sleep(self.args.search_settle_s)
                if lateral_cost(trial) + self.args.search_improvement_n < round_best[0]:
                    round_best = (lateral_cost(trial), dy, dz, trial)
            if round_best[1] == 0.0 and round_best[2] == 0.0:
                break
            best = round_best
            self.servo_relative(0, best[1], best[2], self.args.search_duration_s,
                                stop_at_end=True, enforce_lateral=True)
            time.sleep(self.args.search_settle_s)
            moved_search = True
            start = tool_wrench(self.dashboard, self.args.tool)
            best = (lateral_cost(start), 0.0, 0.0, start)
        if moved_search:
            final = tool_wrench(self.dashboard, self.args.tool)
            self.history.append({"phase": "lateral_search", "start": initial,
                                 "final": final, "rounds": self.args.search_rounds})
            return True
        self.history.append({"phase": "lateral_search", "result": "local_minimum", "wrench": initial})
        return False

    def _run_algorithm(self):
        plan = vars(self.args).copy()
        if not self.args.execute:
            return {"plan": plan, "result": "dry_run"}
        moved = 0.0
        try:
            self.dashboard = self.client or UnityRobotClient(simulate=self.args.simulate).open()
            deadline = time.monotonic() + self.args.max_runtime_s
            self.deadline = deadline
            self.safety_ok()
            while moved < self.args.distance_mm - 1e-9:
                if time.monotonic() > deadline:
                    return {"plan": plan, "result": "time_budget_reached",
                            "moved_mm": moved, "history": self.history}
                w = tool_wrench(self.dashboard, self.args.tool)
                lateral, searching = self.lateral_state(w)
                if searching:
                    self.search_attempts += 1
                    if self.search_attempts > self.args.max_search_attempts:
                        return {"plan": plan, "result": "lateral_jam",
                                "reason": "search budget exhausted",
                                "moved_mm": moved, "history": self.history}
                    if not self.lateral_search():
                        return {"plan": plan, "result": "lateral_jam",
                                "moved_mm": moved, "history": self.history}
                    self.lateral_samples.clear()
                    self.lateral_active = False
                    continue
                self.search_attempts = 0
                step = min(self.args.position_step_mm, self.args.distance_mm - moved)
                # Slow down before the search threshold to reduce force spikes.
                speed = self.args.speed_mm_s
                if lateral >= self.args.lateral_slow_n:
                    speed *= self.args.lateral_slow_ratio
                step = min(step, max(self.args.min_position_step_mm,
                                     speed * self.args.control_period_s))
                self.servo_relative(self.args.direction * step, 0, 0,
                                    max(self.args.min_servo_duration_s, step / speed),
                                    stop_at_end=False, enforce_lateral=True)
                moved += step
                self.history.append({"phase": "axis_advance", "moved_mm": moved,
                                     "wrench": tool_wrench(self.dashboard, self.args.tool),
                                     "lateral_raw_n": lateral_cost(w),
                                     "lateral_filtered_n": lateral,
                                     "speed_mm_s": speed})
            return {"plan": plan, "result": "completed", "moved_mm": moved,
                    "history": self.history}
        except Exception as exc:
            return {"plan": plan, "result": "safety_stop", "reason": str(exc),
                    "moved_mm": moved, "history": self.history}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--ip", default="192.168.192.19")
    p.add_argument("--tool", type=int, default=1, help="Dobot TCP tool number")
    p.add_argument("--distance-mm", type=float, default=20.0,
                   help="Total exit/insertion distance (default: 20 mm)")
    p.add_argument("--direction", type=float, choices=(-1.0, 1.0), default=-1.0,
                   help="+1 = tool +X insertion; -1 = tool -X exit (default)")
    p.add_argument("--speed-mm-s", type=float, default=1.2)
    p.add_argument("--position-step-mm", type=float, default=2.0,
                   help="Maximum ServoP segment length (default: 2 mm)")
    p.add_argument("--min-position-step-mm", type=float, default=0.20,
                   help="Minimum adaptive segment length (default: 0.2 mm)")
    p.add_argument("--control-period-s", type=float, default=1.50,
                   help="Maximum continuous segment period (default: 1.50 s)")
    p.add_argument("--min-servo-duration-s", type=float, default=0.20,
                   help="Minimum ServoP duration (default: 0.20 s)")
    p.add_argument("--lateral-trigger-n", type=float, default=24.0,
                   help="Filtered lateral load that starts Y/Z search (default: 24 N)")
    p.add_argument("--lateral-clear-n", type=float, default=18.0,
                   help="Filtered lateral load that exits search state (default: 18 N)")
    p.add_argument("--lateral-hard-n", type=float, default=26.0,
                   help="Raw lateral load hard stop during motion (default: 26 N)")
    p.add_argument("--lateral-slow-n", type=float, default=18.0,
                   help="Filtered lateral load that reduces X speed (default: 18 N)")
    p.add_argument("--lateral-slow-ratio", type=float, default=0.75,
                   help="Speed ratio near lateral threshold (default: 0.75)")
    p.add_argument("--lateral-filter-samples", type=int, default=5,
                   help="Number of samples in lateral EMA window (default: 5)")
    p.add_argument("--lateral-filter-alpha", type=float, default=0.35,
                   help="EMA response factor (default: 0.35)")
    p.add_argument("--search-step-mm", type=float, default=0.05,
                   help="Small transverse search step (default: 0.05 mm)")
    p.add_argument("--search-duration-s", type=float, default=0.15)
    p.add_argument("--search-settle-s", type=float, default=0.05)
    p.add_argument("--search-rounds", type=int, default=8,
                   help="Maximum stage Y/Z search rounds (default: 8)")
    p.add_argument("--max-search-attempts", type=int, default=3,
                   help="Maximum repeated search attempts before lateral_jam (default: 3)")
    p.add_argument("--search-improvement-n", type=float, default=0.20)
    p.add_argument("--hard-force-n", type=float, default=30.0)
    p.add_argument("--hard-moment-nm", type=float, default=10.0)
    p.add_argument("--max-runtime-s", type=float, default=30.0)
    p.add_argument("--execute", action="store_true",
                   help="Required before physical movement")
    p.add_argument("--simulate", action="store_true", help="Unity numeric simulation only")
    args = p.parse_args()
    numeric = [value for value in vars(args).values()
               if isinstance(value, (int, float)) and not isinstance(value, bool)]
    if (not all(math.isfinite(x) for x in numeric) or
            args.distance_mm <= 0 or args.speed_mm_s <= 0 or
            args.position_step_mm <= 0 or args.min_position_step_mm <= 0 or
            args.min_position_step_mm > args.position_step_mm or
            args.lateral_clear_n >= args.lateral_trigger_n or
            args.lateral_hard_n <= args.lateral_trigger_n or
            args.search_rounds < 1 or
            args.max_search_attempts < 1 or
            args.lateral_filter_samples < 1 or
            not 0 <= args.tool <= 50 or
            args.control_period_s <= 0 or args.min_servo_duration_s <= 0 or
            args.search_duration_s <= 0 or args.search_settle_s < 0 or
            args.search_step_mm <= 0 or args.search_improvement_n < 0 or
            args.max_runtime_s <= 0 or args.hard_force_n <= 0 or args.hard_moment_nm <= 0 or
            args.lateral_clear_n < 0 or args.lateral_slow_n < 0 or
            args.lateral_slow_n > args.lateral_trigger_n or
            not 0.0 < args.lateral_filter_alpha <= 1.0 or
            not 0.0 < args.lateral_slow_ratio <= 1.0):
        p.error("invalid distance/speed/step/lateral parameters")
    from cr10af_exit_optimized import run_exit
    if args.execute and not args.simulate:
        p.error('Real contact must start from the Unity field panel (persistent contact guard required)')
    if not any(x == '--max-runtime-s' or x.startswith('--max-runtime-s=') for x in sys.argv[1:]):
        args.max_runtime_s = max(args.max_runtime_s, 90.0)
    result = run_exit(args, sys.modules[__name__])
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["result"] in ("completed", "dry_run") else 1

if __name__ == "__main__":
    sys.exit(main())
