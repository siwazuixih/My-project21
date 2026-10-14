#!/usr/bin/env python3
"""CR10AF force/position hybrid exit controller (Ubuntu 20.04, Python 3.8+).
The controller commands a small position increment along a selected tool X
axis.  Before advancing, it tries a bounded search in tool Y/Z to reduce the
transverse contact load.  ``GetForce(tool)`` supplies the compensated contact
wrench; the real-time port 30004 is an independent absolute safety monitor.
This file uses only the Python standard library.  It is designed for the
Dobot TCP/IP V4 feedback packet used by CR-A series robots.
"""
import argparse
import json
import math
import re
import socket
import struct
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
    def command(self, text):
        with self.lock:
            self.sock.sendall(text.encode("utf-8"))
            data = self.sock.recv(4096).decode("utf-8", errors="replace")
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
        # Offsets follow Dobot TCP/IP V4.6 MyType exactly.
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
                buf += self.sock.recv(8192)
                while len(buf) >= PACKET_SIZE:
                    # TCP does not preserve message boundaries.  Search for a
                    # valid packet header instead of assuming recv() began at
                    # an exact 1440-byte boundary.
                    value = self.decode(buf[:PACKET_SIZE])
                    if value:
                        with self.lock:
                            self.latest = value
                        buf = buf[PACKET_SIZE:]
                    else:
                        buf = buf[1:]
            except (OSError, socket.timeout):
                continue
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
    # w = [Fx, Fy, Fz, Mx, My, Mz]
    return math.hypot(w[1], w[2]) + 0.25 * math.hypot(w[3], w[5])

class HybridExit:
    def __init__(self, args):
        self.args = args
        self.dashboard = None
        self.feedback = None
        self.history = []
    def safety_ok(self, allow_running=False, enforce_lateral=False):
        f = self.feedback.get()
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
        target = self.relative_target(dx, dy, dz)
        self.dashboard.command(
            "ServoP({:.6f},{:.6f},{:.6f},{:.6f},{:.6f},{:.6f},t={:.3f},aheadtime=50,gain=500)".format(
                *target, duration))
        end = time.monotonic() + duration
        while time.monotonic() < end:
            # ServoP makes CR10AF report mode 7 (RUNNING).  It is safe only
            # during this finite motion command; all other modes still stop.
            self.safety_ok(allow_running=True, enforce_lateral=enforce_lateral)
            time.sleep(0.005)
        if stop_at_end:
            self.stop()
            time.sleep(0.02)
        self.safety_ok(allow_running=not stop_at_end,
                       enforce_lateral=enforce_lateral)

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
        self.dashboard = Dashboard(self.args.ip)
        self.feedback = Feedback(self.args.ip)
        deadline = time.monotonic() + self.args.max_runtime_s
        try:
            self.feedback.wait()
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
            self.stop()
            if self.feedback:
                self.feedback.close()
            if self.dashboard:
                self.dashboard.close()

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--ip", default="192.168.200.1")
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
