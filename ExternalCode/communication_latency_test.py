#!/usr/bin/env python3
"""Read-only robot A/B benchmark and direct RealSense/Python frame timing.

No import-time device access. No robot motion, enabling, Stop or frame-setting commands.
RealSense clock semantics: https://github.com/realsenseai/librealsense/blob/master/include/librealsense2/hpp/rs_frame.hpp
"""
import argparse
import csv
from datetime import datetime, timezone
import json
import math
import os
from pathlib import Path
import re
import socket
import statistics
import time
import uuid


def stats(values):
    values = sorted(float(x) for x in values if x is not None and math.isfinite(x))
    if not values:
        return dict(count=0, mean=None, median=None, p95=None, p99=None, maximum=None)
    def percentile(p):
        index = (len(values) - 1) * p
        lower = int(index)
        upper = min(lower + 1, len(values) - 1)
        return values[lower] + (values[upper] - values[lower]) * (index - lower)
    return dict(count=len(values), mean=statistics.mean(values), median=statistics.median(values),
                p95=percentile(.95), p99=percentile(.99), maximum=values[-1])


def frame_age(timestamp_ms, domain, host_epoch_ms, clock_jump=False):
    # Hardware-clock values cannot be subtracted from the host wall clock.
    if clock_jump:
        return None, 'host_wall_clock_jump'
    if domain not in ('global_time', 'system_time'):
        return None, 'unmapped_camera_clock'
    age = host_epoch_ms - timestamp_ms
    if not math.isfinite(age) or age < 0 or age > 10000:
        return None, 'invalid_or_uncalibrated_timestamp'
    return age, 'host_timestamp_estimate_not_optical_event_latency'


def robot_command(query, user=0, tool=1):
    if not 0 <= user <= 50 or not 0 <= tool <= 50:
        raise ValueError('User/Tool must be 0..50')
    if query == 'pose':
        return 'GetPose(user=%d,tool=%d)' % (user, tool)
    if query == 'angle':
        return 'GetAngle()'
    if query == 'force':
        return 'GetForce(%d)' % tool
    raise ValueError('Only pose/angle/force queries are allowed')


def parse_robot_reply(reply, command):
    match = re.fullmatch(r'\s*(-?\d+)\s*,\s*\{([^}]*)\}\s*,\s*(.*?)\s*;\s*', reply, re.S)
    if not match:
        raise ValueError('Malformed robot reply: ' + reply)
    if re.sub(r'\s+', '', match[3]).lower() != re.sub(r'\s+', '', command).lower():
        raise ValueError('Robot response does not match query')
    if int(match[1]) != 0:
        raise ValueError('Robot rejected query: ' + reply)
    values = [float(x) for x in match[2].split(',')]
    if len(values) != 6 or not all(math.isfinite(x) for x in values):
        raise ValueError('Expected six finite robot values')
    return values


class Probe:
    def __init__(self, host, port, timeout, proxy=False):
        self.socket = socket.create_connection((host, port), timeout=timeout)
        self.socket.settimeout(timeout)
        self.reader = self.socket.makefile('rb')
        self.proxy = proxy

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.reader.close()
        self.socket.close()

    def query(self, query, user, tool, echo=False):
        if self.proxy:
            identifier = uuid.uuid4().hex
            payload = dict(id=identifier, op='latency_echo' if echo else 'latency_query', query=query, user=user, tool=tool)
            self.socket.sendall((json.dumps(payload) + '\n').encode('utf-8'))
            line = self.reader.readline(65537)
            if not line or len(line) > 65536:
                raise IOError('Unity disconnected or oversized response')
            result = json.loads(line)
            if result.get('id') != identifier or not result.get('ok'):
                raise IOError(result.get('error') or 'Unity reply id mismatch')
            if result.get('benchmark_schema') != 'latency-v1-2026-10-08':
                raise IOError('Unity build lacks the latency probe; rebuild the full application')
            return result
        command = robot_command(query, user, tool)
        self.socket.sendall((command + '\n').encode('utf-8'))
        data = bytearray()
        while len(data) < 16384:
            char = self.reader.read(1)
            if not char:
                raise IOError('Robot disconnected')
            if char in (b'\r', b'\n') and not data:
                continue
            data.extend(char)
            if char == b';':
                return dict(values=parse_robot_reply(data.decode('utf-8'), command))
        raise IOError('Oversized robot response')


def robot_benchmark(args, directory):
    if args.unity_host not in ('127.0.0.1', 'localhost', '::1'):
        raise ValueError('Unity test endpoint must be local')
    robot_command(args.query, args.user, args.tool)
    groups = dict(direct=[], unity=[])
    echo_times, internal, gate, transport = [], [], [], []
    summary = dict(kind='robot', timestamp=datetime.now(timezone.utc).isoformat(), query=args.query,
        command=robot_command(args.query, args.user, args.tool), samples_requested_per_route=args.samples,
        interval_ms=args.interval_ms, warmup_per_phase=args.warmup, order='direct/unity/unity/direct',
        robot_endpoint='%s:%d' % (args.robot_ip, args.dashboard_port), timeout_count=0, error_count=0,
        valid_comparison=False, note='RTT from the same Python clock; Unity uses production main-thread/gate path with per-query trace logging suppressed. Not movement completion or full algorithm latency.')
    error = None
    try:
        # Check the actual Unity robot endpoint before sending any baseline queries.
        with Probe(args.unity_host, args.unity_port, args.timeout, True) as probe:
            for i in range(args.warmup + min(args.samples, 100)):
                started = time.perf_counter_ns()
                result = probe.query(args.query, args.user, args.tool, echo=True)
                if result['robot_endpoint'] != summary['robot_endpoint']:
                    raise ValueError('Unity robot endpoint differs: ' + result['robot_endpoint'])
                if i >= args.warmup:
                    echo_times.append((time.perf_counter_ns() - started) / 1e6)
                time.sleep(args.interval_ms / 1000)
        halves = (args.samples // 2, args.samples - args.samples // 2)
        phases = [('direct', halves[0]), ('unity', halves[0]), ('unity', halves[1]), ('direct', halves[1])]
        with (directory / 'robot.csv').open('w', newline='', encoding='utf-8') as file:
            writer = csv.writer(file)
            writer.writerow(['utc', 'phase', 'route', 'sample', 'ok', 'rtt_ms', 'gateway_processing_ms',
                             'dashboard_exchange_ms', 'dashboard_gate_ms', 'dashboard_transport_ms', 'values', 'error'])
            for phase, (route, count) in enumerate(phases, 1):
                host, port = (args.robot_ip, args.dashboard_port) if route == 'direct' else (args.unity_host, args.unity_port)
                with Probe(host, port, args.timeout, route == 'unity') as probe:
                    for sample in range(-args.warmup, count):
                        started = time.perf_counter_ns()
                        try:
                            result = probe.query(args.query, args.user, args.tool)
                            elapsed = (time.perf_counter_ns() - started) / 1e6
                            if sample >= 0:
                                groups[route].append(elapsed)
                                if route == 'unity':
                                    internal.append(result['gateway_processing_ms'])
                                    gate.append(result['dashboard_gate_ms'])
                                    transport.append(result['dashboard_transport_ms'])
                                writer.writerow([datetime.now(timezone.utc).isoformat(), phase, route, sample, True,
                                    elapsed, result.get('gateway_processing_ms', ''), result.get('dashboard_exchange_ms', ''),
                                    result.get('dashboard_gate_ms', ''), result.get('dashboard_transport_ms', ''),
                                    json.dumps(result['values']), ''])
                        except Exception as exc:
                            summary['error_count'] += 1
                            if isinstance(exc, (socket.timeout, TimeoutError)):
                                summary['timeout_count'] += 1
                            writer.writerow([datetime.now(timezone.utc).isoformat(), phase, route, sample, False,
                                (time.perf_counter_ns() - started) / 1e6, '', '', '', '', '', str(exc)])
                            raise  # Do not reuse a timed-out stream with a potentially late reply.
                        if sample % 20 == 0:
                            file.flush()
                        remaining = args.interval_ms / 1000 - (time.perf_counter_ns() - started) / 1e9
                        if remaining > 0:
                            time.sleep(remaining)
                print('Completed phase %d: %s, %d samples' % (phase, route, count))
        summary['valid_comparison'] = all(len(v) == args.samples for v in groups.values())
    except (Exception, KeyboardInterrupt) as exc:
        error = str(exc) or type(exc).__name__
    summary.update(direct_rtt_ms=stats(groups['direct']), unity_rtt_ms=stats(groups['unity']),
        local_echo_rtt_ms=stats(echo_times), unity_internal_processing_ms=stats(internal),
        dashboard_gate_wait_ms=stats(gate), dashboard_wire_rtt_ms=stats(transport), error=error)
    if summary['valid_comparison']:
        summary['extra_mean_ms'] = summary['unity_rtt_ms']['mean'] - summary['direct_rtt_ms']['mean']
        summary['p95_difference_ms'] = summary['unity_rtt_ms']['p95'] - summary['direct_rtt_ms']['p95']
        summary['difference_note'] = 'Difference of route statistics, not per-request paired overhead; ABBA reduces but does not remove device/load drift.'
    return summary


def camera_benchmark(args, directory):
    # Camera SDK is deliberately imported only when the user explicitly runs camera mode.
    import numpy as np
    import pyrealsense2 as rs
    pipeline, config = rs.pipeline(), rs.config()
    if args.serial:
        config.enable_device(args.serial)
    config.enable_stream(rs.stream.color, args.width, args.height, rs.format.bgr8, args.fps)
    if not args.color_only:
        config.enable_stream(rs.stream.depth, args.depth_width, args.depth_height, rs.format.z16, args.fps)
    restore = []
    waits, copies, alignments, ages = [], [], [], []
    summary = dict(kind='camera', timestamp=datetime.now(timezone.utc).isoformat(),
        profile=dict(width=args.width, height=args.height, fps=args.fps, color_only=args.color_only,
                     depth_width=args.depth_width, depth_height=args.depth_height), requested_frames=args.samples,
        warmup_frames=args.warmup, frame_counter_gaps=0, duplicate_frames=0, invalid_age_frames=0,
        error=None, sdk_timestamp_source='get_timestamp', global_time_requested=args.global_time,
        note='wait_for_frames duration includes waiting for frame cadence; timestamp age is a host-clock estimate, not exposure-to-photon latency. No JPEG/HTTP/Unity/inference in this baseline.')
    summary['timestamp_domains'] = {}
    started_pipeline = False
    previous_number = None
    try:
        profile = pipeline.start(config)
        started_pipeline = True
        device = profile.get_device()
        summary['device'] = device.get_info(rs.camera_info.name)
        summary['serial'] = device.get_info(rs.camera_info.serial_number)
        summary['sdk_version'] = getattr(rs, '__version__', 'unknown')
        if args.global_time:
            for sensor in device.query_sensors():
                if sensor.supports(rs.option.global_time_enabled):
                    original = sensor.get_option(rs.option.global_time_enabled)
                    restore.append((sensor, original))
                    sensor.set_option(rs.option.global_time_enabled, 1)
        align = None if args.color_only else rs.align(rs.stream.color)
        with (directory / 'camera.csv').open('w', newline='', encoding='utf-8') as file:
            writer = csv.writer(file)
            writer.writerow(['utc', 'sample', 'frame_number', 'timestamp_domain', 'frame_timestamp_ms',
                'wait_for_frames_ms', 'align_ms', 'copy_ms', 'timestamp_to_python_ready_ms_estimate',
                'clock_valid', 'age_reason', 'color_bytes', 'depth_bytes'])
            for sample in range(-args.warmup, args.samples):
                wall_start, started = time.time_ns(), time.perf_counter_ns()
                frames = pipeline.wait_for_frames(3000)
                arrived = time.perf_counter_ns()
                original_color = frames.get_color_frame()
                if not original_color:
                    raise RuntimeError('Missing color frame')
                timestamp, number = original_color.get_timestamp(), original_color.get_frame_number()
                domain = str(original_color.get_frame_timestamp_domain()).split('.')[-1]
                if align:
                    frames = align.process(frames)
                aligned = time.perf_counter_ns()
                color = np.asanyarray(frames.get_color_frame().get_data()).copy()
                depth_frame = frames.get_depth_frame() if not args.color_only else None
                depth = np.asanyarray(depth_frame.get_data()).copy() if depth_frame else None
                ready, wall_ready = time.perf_counter_ns(), time.time_ns()
                wall_delta, monotonic_delta = (wall_ready - wall_start) / 1e6, (ready - started) / 1e6
                age, reason = frame_age(timestamp, domain, wall_ready / 1e6, abs(wall_delta - monotonic_delta) > 2)
                if sample < 0:
                    continue
                summary['timestamp_domains'][domain] = summary['timestamp_domains'].get(domain, 0) + 1
                if previous_number is not None:
                    summary['frame_counter_gaps'] += max(0, number - previous_number - 1)
                    summary['duplicate_frames'] += int(number == previous_number)
                previous_number = number
                waits.append((arrived - started) / 1e6)
                alignments.append((aligned - arrived) / 1e6)
                copies.append((ready - aligned) / 1e6)
                if age is None:
                    summary['invalid_age_frames'] += 1
                else:
                    ages.append(age)
                writer.writerow([datetime.now(timezone.utc).isoformat(), sample, number, domain, timestamp,
                    waits[-1], alignments[-1], copies[-1], '' if age is None else age, age is not None, reason,
                    color.nbytes, depth.nbytes if depth is not None else 0])
                if sample % 20 == 0:
                    file.flush()
    except (Exception, KeyboardInterrupt) as exc:
        summary['error'] = str(exc) or type(exc).__name__
    finally:
        for sensor, original in restore:
            try:
                sensor.set_option(rs.option.global_time_enabled, original)
            except Exception as exc:
                summary.setdefault('restore_errors', []).append(str(exc))
        if started_pipeline:
            try:
                pipeline.stop()
            except Exception as exc:
                summary.setdefault('cleanup_errors', []).append(str(exc))
    summary.update(wait_for_frames_ms=stats(waits), alignment_ms=stats(alignments), copy_ms=stats(copies),
        timestamp_to_python_ready_ms_estimate=stats(ages), completed=len(waits) == args.samples,
        timestamp_age_available=bool(ages))
    return summary


def parser():
    root = argparse.ArgumentParser(description=__doc__)
    modes = root.add_subparsers(dest='mode', required=True)
    for name in ('camera', 'robot'):
        sub = modes.add_parser(name)
        sub.add_argument('--samples', type=int, default=500)
        sub.add_argument('--warmup', type=int, default=30 if name == 'camera' else 20)
        sub.add_argument('--output', type=Path, help='Parent directory; each test creates a new report directory')
        if name == 'camera':
            sub.add_argument('--width', type=int, default=int(os.environ.get('REALSENSE_COLOR_WIDTH', '1280')))
            sub.add_argument('--height', type=int, default=int(os.environ.get('REALSENSE_COLOR_HEIGHT', '720')))
            sub.add_argument('--fps', type=int, default=int(os.environ.get('REALSENSE_COLOR_FPS', '15')))
            sub.add_argument('--depth-width', type=int, default=int(os.environ.get('REALSENSE_DEPTH_WIDTH', '1280')))
            sub.add_argument('--depth-height', type=int, default=int(os.environ.get('REALSENSE_DEPTH_HEIGHT', '720')))
            sub.add_argument('--color-only', action='store_true')
            sub.add_argument('--global-time', action='store_true', help='Temporarily enable SDK global time on supported sensors')
            sub.add_argument('--serial', default='')
        else:
            sub.add_argument('--robot-ip', default='192.168.192.19')
            sub.add_argument('--dashboard-port', type=int, default=29999)
            sub.add_argument('--unity-host', default='127.0.0.1')
            sub.add_argument('--unity-port', type=int, default=19120)
            sub.add_argument('--query', choices=['pose', 'angle', 'force'], default='pose')
            sub.add_argument('--user', type=int, default=0)
            sub.add_argument('--tool', type=int, default=1)
            sub.add_argument('--timeout', type=float, default=5)
            sub.add_argument('--interval-ms', type=float, default=50)
    return root


def main():
    args = parser().parse_args()
    if not 2 <= args.samples <= 10000 or not 0 <= args.warmup <= 1000:
        raise SystemExit('samples must be 2..10000; warmup 0..1000')
    if args.mode == 'robot' and (not 10 <= args.interval_ms <= 5000 or not .1 <= args.timeout <= 30):
        raise SystemExit('interval-ms must be 10..5000; timeout .1..30')
    parent = args.output or Path.cwd() / 'LatencyReports'
    directory = parent / (datetime.now().strftime('%Y%m%d-%H%M%S') + '-' + args.mode + '-' + uuid.uuid4().hex[:8])
    directory.mkdir(parents=True)
    try:
        summary = camera_benchmark(args, directory) if args.mode == 'camera' else robot_benchmark(args, directory)
    except Exception as exc:
        summary = dict(kind=args.mode, error=str(exc), completed=False)
    summary['arguments'] = {k: str(v) if isinstance(v, Path) else v for k, v in vars(args).items()}
    (directory / 'summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2, allow_nan=False), encoding='utf-8')
    print(json.dumps(summary, ensure_ascii=False, indent=2, allow_nan=False))
    print('Report: ' + str(directory.resolve()))
    return 1 if summary.get('error') else 0


if __name__ == '__main__':
    raise SystemExit(main())
