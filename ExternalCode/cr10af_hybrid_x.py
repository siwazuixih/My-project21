#!/usr/bin/env python3
"""CR10AF contact algorithm adapted to the Unity robot gateway (Python 3.8+).
Tool-X feed and Y/Z search are retained. All robot feedback and movement now
pass through Unity; this module never connects to robot ports.
Without --execute only the plan is printed. --execute --simulate uses the
Unity numeric simulation backend, not physical motion or force measurement.
"""
import argparse
import json
import math
import time
from unity_robot_client import UnityRobotClient


def pose(dashboard):
    return dashboard.pose()


def tool_wrench(dashboard, tool):
    return dashboard.wrench(tool)
def norm3(v):
    return math.sqrt(sum(x * x for x in v))

def lateral_cost(w):
    # Tool Y/Z are transverse to this application's tool X guide axis.
    # w = [Fx, Fy, Fz, Mx, My, Mz]
    return math.hypot(w[1], w[2]) + 0.25 * math.hypot(w[3], w[5])

class HybridExit:
    def __init__(self, args, client=None):
        self.args = args
        self.dashboard = None
        self.history = []
        self.client = client
    def safety_ok(self, allow_running=False, enforce_lateral=False):
        f = self.dashboard.status()
        if not f or time.monotonic() - f["time"] > 0.25:
            raise RuntimeError("Feedback is stale; stopping")
        allowed_modes = (5, 7) if allow_running else (5,)
        if f["mode"] not in allowed_modes or f["error"] != 0 or f["collision"] != 0:
            raise RuntimeError("Robot is not ready: mode=%s error=%s collision=%s" %
                               (f["mode"], f["error"], f["collision"]))
        if norm3(f["force"][:3]) >= self.args.hard_force_n:
            raise RuntimeError("Hard force limit reached: %.2f N" % norm3(f["force"][:3]))
        if norm3(f["force"][3:]) >= self.args.hard_moment_nm:
            raise RuntimeError("Hard moment limit reached: %.2f Nm" % norm3(f["force"][3:]))
        if enforce_lateral and lateral_cost(f["force"]) >= self.args.lateral_trigger_n:
            raise RuntimeError("Lateral force limit reached: %.2f" % lateral_cost(f["force"]))
    def servo_relative(self, dx, dy, dz, duration, stop_at_end=True,
                       enforce_lateral=False):
        self.dashboard.move_relative(dx, dy, dz, duration, enforce_lateral=enforce_lateral)
        self.safety_ok(allow_running=False, enforce_lateral=enforce_lateral)

    def lateral_search(self):
        start = tool_wrench(self.dashboard, self.args.tool)
        step = self.args.search_step_mm
        best = (lateral_cost(start), 0.0, 0.0, start)
        # Search transverse plane Y,Z (perpendicular to tool‑X feed axis)
        for dy, dz in ((step, 0), (-step, 0), (0, step), (0, -step)):
            self.servo_relative(0, dy, dz, self.args.search_duration_s)
            time.sleep(0.12)
            trial = tool_wrench(self.dashboard, self.args.tool)
            self.servo_relative(0, -dy, -dz, self.args.search_duration_s)
            time.sleep(0.12)
            if lateral_cost(trial) + self.args.search_improvement_n < best[0]:
                best = (lateral_cost(trial), dy, dz, trial)
        if best[1] or best[2]:
            self.servo_relative(0, best[1], best[2], self.args.search_duration_s)
            time.sleep(0.12)
            final = tool_wrench(self.dashboard, self.args.tool)
            self.history.append({"phase": "lateral_search", "start": start,
                                 "final": final, "dy_mm": best[1], "dz_mm": best[2]})
            return True
        self.history.append({"phase": "lateral_search", "result": "local_minimum", "wrench": start})
        return False

    def run(self):
        plan = vars(self.args).copy()
        if not self.args.execute:
            return {"plan": plan, "result": "dry_run"}
        moved = 0.0
        owns_client = self.client is None
        deadline = time.monotonic() + self.args.max_runtime_s
        try:
            self.dashboard = self.client or UnityRobotClient(simulate=self.args.simulate).open()
            self.safety_ok()
            while moved < self.args.distance_mm - 1e-9:
                if time.monotonic() > deadline:
                    return {"plan": plan, "result": "time_budget_reached", "moved_mm": moved, "history": self.history}
                w = tool_wrench(self.dashboard, self.args.tool)
                if lateral_cost(w) > self.args.lateral_trigger_n:
                    if not self.lateral_search():
                        return {"plan": plan, "result": "lateral_jam", "moved_mm": moved, "history": self.history}
                    continue
                step = min(self.args.position_step_mm, self.args.distance_mm - moved)
                # Feed along tool‑X axis
                self.servo_relative(self.args.direction * step, 0, 0,
                                    max(0.20, step / self.args.speed_mm_s),
                                    stop_at_end=False, enforce_lateral=True)
                moved += step
                self.history.append({"phase": "axis_advance", "moved_mm": moved,
                                     "wrench": tool_wrench(self.dashboard, self.args.tool)})
            return {"plan": plan, "result": "completed", "moved_mm": moved, "history": self.history}
        except Exception as exc:
            return {"plan": plan, "result": "safety_stop", "reason": str(exc),
                    "moved_mm": moved, "history": self.history}
        finally:
            if owns_client and self.dashboard:
                self.dashboard.close()

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--simulate", action="store_true", help="Use Unity gateway simulation, never robot TCP")
    p.add_argument("--tool", type=int, default=1, help="Dobot TCP tool number")
    p.add_argument("--distance-mm", type=float, default=20.0)
    p.add_argument("--direction", type=float, choices=(-1.0, 1.0), default=1.0,
                   help="+1 = tool +X exit; -1 = tool -X insertion")
    p.add_argument("--speed-mm-s", type=float, default=2.0)
    p.add_argument("--position-step-mm", type=float, default=2.0)
    p.add_argument("--lateral-trigger-n", type=float, default=20.0)
    p.add_argument("--search-step-mm", type=float, default=0.10)
    p.add_argument("--search-duration-s", type=float, default=0.25)
    p.add_argument("--search-improvement-n", type=float, default=0.02)
    p.add_argument("--hard-force-n", type=float, default=30.0)
    p.add_argument("--hard-moment-nm", type=float, default=10.0)
    p.add_argument("--max-runtime-s", type=float, default=30.0)
    p.add_argument("--execute", action="store_true", help="Required before physical movement")
    args = p.parse_args()
    if args.distance_mm <= 0 or args.speed_mm_s <= 0 or args.position_step_mm <= 0:
        p.error("distance, speed and step must be positive")
    print(json.dumps(HybridExit(args).run(), ensure_ascii=False, indent=2))
if __name__ == "__main__":
    main()
