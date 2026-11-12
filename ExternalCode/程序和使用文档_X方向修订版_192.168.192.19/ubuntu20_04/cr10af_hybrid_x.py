#!/usr/bin/env python3
"""CR10AF force/position hybrid exit controller.

The controller commands a small position increment along the selected tool X
axis. Before advancing, it tries a bounded search in tool Y/Z to reduce the
transverse contact load. GetForce(tool) supplies the compensated contact
wrench; the real-time port 30004 is an independent absolute safety monitor.
"""
import argparse
import json
import math
import re
import socket
import struct
import sys
import threading
import time

PACKET_SIZE = 1440
FEEDBACK_MAGIC = 0x123456789ABCDEF
VALUES = re.compile(r"\{([^}]*)\}")


class Dashboard:
    def __init__(self, ip, port=29999, timeout=3.0):
        self.sock = socket.create_connection((ip, port), timeout=timeout)
        self.sock.settimeout(timeout)
        self.lock = threading.Lock()
        self.buffer = b""

    def command(self, text):
        with self.lock:
            self.sock.sendall(text.encode("utf-8"))
            deadline = time.monotonic() + self.sock.gettimeout()
            # Dashboard replies may be split across TCP reads. Their semicolon
            # terminates a reply; retain any bytes after it for the next call.
            while b";" not in self.buffer:
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise TimeoutError("Incomplete Dashboard reply")
                chunk = self.sock.recv(4096)
                if not chunk:
                    raise ConnectionError("Dashboard disconnected")
                self.buffer += chunk
                if len(self.buffer) > 65536:
                    raise RuntimeError("Dashboard reply too large")
            reply, self.buffer = self.buffer.split(b";", 1)
            data = reply.decode("utf-8", errors="replace").strip() + ";"
        if not data.startswith("0,"):
            raise RuntimeError("Controller rejected %s: %s" % (text, data))
        return data

    def close(self):
        try:
            self.sock.close()
        except OSError:
            pass


class Feedback:
    """Reads the CR real-time feedback stream and retains the latest packet."""

    def __init__(self, ip, port=30004):
        self.sock = socket.create_connection((ip, port), timeout=3.0)
        self.sock.settimeout(1.0)
        self.latest = None
        self.lock = threading.Lock()
        self.stop_event = threading.Event()
        self.thread = threading.Thread(target=self._loop, daemon=True)
        self.thread.start()

    @staticmethod
    def decode(packet):
        length = struct.unpack_from("<H", packet, 0)[0]
        magic = struct.unpack_from("<Q", packet, 48)[0]
        if length != PACKET_SIZE or magic != FEEDBACK_MAGIC:
            return None
        return {
            "time": time.monotonic(),
            "mode": struct.unpack_from("<Q", packet, 24)[0],
            "force": struct.unpack_from("<6d", packet, 576),
            "pose": struct.unpack_from("<6d", packet, 624),
            "error": packet[1029],
            "collision": packet[1038],
        }

    def _loop(self):
        buf = b""
        while not self.stop_event.is_set():
            try:
                chunk = self.sock.recv(8192)
                if not chunk:
                    break
                buf += chunk
                while len(buf) >= PACKET_SIZE:
                    value = self.decode(buf[:PACKET_SIZE])
                    if value:
                        with self.lock:
                            self.latest = value
                        buf = buf[PACKET_SIZE:]
                    else:
                        buf = buf[1:]
            except socket.timeout:
                continue
            except OSError:
                break

    def wait(self, timeout=3.0):
        end = time.monotonic() + timeout
        while time.monotonic() < end:
            with self.lock:
                item = self.latest
            if item:
                return item
            time.sleep(0.01)
        raise RuntimeError("No valid 30004 feedback packet received")

    def get(self):
        with self.lock:
            return self.latest

    def close(self):
        self.stop_event.set()
        try:
            self.sock.close()
        except OSError:
            pass
        self.thread.join(timeout=0.5)


def values(reply):
    match = VALUES.search(reply)
    if not match:
        raise RuntimeError("Cannot parse controller reply: %s" % reply)
    return tuple(float(x) for x in match.group(1).split(","))


def pose(dashboard):
    p = values(dashboard.command("GetPose()"))
    if len(p) != 6:
        raise RuntimeError("GetPose did not return 6 values")
    return p


def tool_wrench(dashboard, tool):
    w = values(dashboard.command("GetForce(%d)" % tool))
    if len(w) != 6:
        raise RuntimeError("GetForce did not return 6 values")
    return w


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
    def __init__(self, args):
        self.args = args
        self.dashboard = None
        self.feedback = None
        self.history = []
        self.lateral_samples = []
        self.lateral_active = False
        self.search_attempts = 0
        self.deadline = None

    def safety_ok(self, allow_running=False, enforce_lateral=False):
        if self.deadline is not None and time.monotonic() >= self.deadline:
            raise TimeoutError("Robot runtime budget exhausted")
        f = self.feedback.get()
        if not f or time.monotonic() - f["time"] > 0.25:
            raise RuntimeError("Feedback is stale; stopping")
        allowed_modes = (5, 7) if allow_running else (5,)
        if f["mode"] not in allowed_modes or f["error"] != 0 or f["collision"] != 0:
            raise RuntimeError("Robot is not ready: mode=%s error=%s collision=%s" %
                               (f["mode"], f["error"], f["collision"]))
        if not all(math.isfinite(x) for x in f["force"] + f["pose"]):
            raise RuntimeError("Non-finite robot feedback")
        if norm3(f["force"][:3]) >= self.args.hard_force_n:
            raise RuntimeError("Hard force limit reached: %.2f N" % norm3(f["force"][:3]))
        if norm3(f["force"][3:]) >= self.args.hard_moment_nm:
            raise RuntimeError("Hard moment limit reached: %.2f Nm" % norm3(f["force"][3:]))
        if enforce_lateral and lateral_cost(f["force"]) >= self.args.lateral_hard_n:
            raise RuntimeError("Hard lateral force limit reached: %.2f" % lateral_cost(f["force"]))

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

    def stop(self):
        if self.dashboard:
            try:
                self.dashboard.command("Stop()")
            except Exception:
                pass

    def relative_target(self, dx, dy, dz):
        x, y, z, rx, ry, rz = pose(self.dashboard)
        command = ("RelPointTool(pose={{{:.6f},{:.6f},{:.6f},{:.6f},{:.6f},{:.6f}}},"
                   "{{{:.6f},{:.6f},{:.6f},0,0,0}})").format(
                       x, y, z, rx, ry, rz, dx, dy, dz)
        return values(self.dashboard.command(command))

    def servo_relative(self, dx, dy, dz, duration, stop_at_end=True,
                       enforce_lateral=False):
        self.safety_ok(allow_running=True, enforce_lateral=enforce_lateral)
        target = self.relative_target(dx, dy, dz)
        if len(target) != 6 or not all(math.isfinite(x) for x in target):
            raise RuntimeError("Invalid relative pose")
        self.safety_ok(allow_running=True, enforce_lateral=enforce_lateral)
        self.dashboard.command(
            "ServoP({:.6f},{:.6f},{:.6f},{:.6f},{:.6f},{:.6f},t={:.3f},aheadtime=50,gain=500)".format(
                *target, duration))
        end = time.monotonic() + duration
        while time.monotonic() < end:
            # Keep the feedback loop alive during motion. Axis motion also
            # checks the lateral threshold so a jam stops within milliseconds.
            self.safety_ok(allow_running=True, enforce_lateral=enforce_lateral)
            time.sleep(0.005)
        if stop_at_end:
            self.stop()
            time.sleep(0.02)
        self.safety_ok(allow_running=not stop_at_end,
                       enforce_lateral=enforce_lateral)

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

    def run(self):
        plan = vars(self.args).copy()
        if not self.args.execute:
            return {"plan": plan, "result": "dry_run"}
        moved = 0.0
        try:
            self.dashboard = Dashboard(self.args.ip)
            self.feedback = Feedback(self.args.ip)
            deadline = time.monotonic() + self.args.max_runtime_s
            self.deadline = deadline
            self.feedback.wait()
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
        finally:
            self.stop()
            if self.feedback:
                self.feedback.close()
            if self.dashboard:
                self.dashboard.close()


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
    p.add_argument("--search-rounds", type=int, default=2,
                   help="Maximum local Y/Z search rounds (default: 2)")
    p.add_argument("--max-search-attempts", type=int, default=3,
                   help="Maximum repeated search attempts before lateral_jam (default: 3)")
    p.add_argument("--search-improvement-n", type=float, default=0.20)
    p.add_argument("--hard-force-n", type=float, default=30.0)
    p.add_argument("--hard-moment-nm", type=float, default=10.0)
    p.add_argument("--max-runtime-s", type=float, default=30.0)
    p.add_argument("--execute", action="store_true",
                   help="Required before physical movement")
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
    result = HybridExit(args).run()
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["result"] in ("completed", "dry_run") else 1


if __name__ == "__main__":
    sys.exit(main())
