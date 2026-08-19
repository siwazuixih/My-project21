#!/usr/bin/env python3
"""
point_move_demo.py

示例：使用现有 Dobot API 连接机器人，并通过 MovJ 实现点位运动。
"""

import re
import socket
import threading
import time
import argparse

from dobot_api import DobotApiDashboard, DobotApiFeedBack


def parse_result_id(value_recv):
    if value_recv is None:
        return [2]
    if "Not Tcp" in value_recv:
        print("Control Mode Is Not Tcp")
        return [1]

    match = re.match(r'\s*(-?\d+),\{(-?\d*)\}', value_recv)
    if match:
        result_code = int(match.group(1))
        cmd_id_text = match.group(2)
        if cmd_id_text == "":
            return [result_code]
        return [result_code, int(cmd_id_text)]

    nums = re.findall(r'-?\d+', value_recv)
    return [int(num) for num in nums] if nums else [2]


class PointMoveDemo:
    def __init__(self, ip, dashboard_port=29999, feedback_port=30004):
        self.ip = ip
        self.dashboard_port = dashboard_port
        self.feedback_port = feedback_port

        self.dashboard = DobotApiDashboard(self.ip, self.dashboard_port)
        self.feedback = DobotApiFeedBack(self.ip, self.feedback_port)

        self.feed_data = {}
        self._feed_lock = threading.Lock()
        self._running = False
        self._feed_thread = None

    def start_feedback(self):
        self._running = True
        self._feed_thread = threading.Thread(target=self._feedback_loop, daemon=True)
        self._feed_thread.start()

    def _feedback_loop(self):
        while self._running:
            try:
                recv = self.feedback.feedBackData()
            except Exception as e:
                print(f"Feedback recv error: {e}")
                time.sleep(0.5)
                continue

            if recv is None:
                time.sleep(0.05)
                continue

            with self._feed_lock:
                self.feed_data['RobotMode'] = int(recv['RobotMode'][0])
                self.feed_data['DigitalInputs'] = int(recv['DigitalInputs'][0])
                self.feed_data['DigitalOutputs'] = int(recv['DigitalOutputs'][0])
                self.feed_data['CurrentCommandId'] = int(recv['CurrentCommandId'][0])

    def wait_robot_mode(self, expected_modes, timeout=10.0):
        start_time = time.time()
        last_mode = None
        while time.time() - start_time < timeout:
            with self._feed_lock:
                last_mode = self.feed_data.get('RobotMode')
            if last_mode in expected_modes:
                return last_mode
            time.sleep(0.1)
        raise TimeoutError(
            f"Robot mode did not become {expected_modes} within {timeout} seconds (last mode={last_mode})")

    def enable_robot(self):
        result = self.dashboard.EnableRobot()
        parsed = parse_result_id(result)
        if parsed[0] != 0:
            raise RuntimeError(f"EnableRobot failed: {result}")
        self.wait_robot_mode({5}, timeout=10.0)
        print("EnableRobot success")

    def movj(self, pose, user=-1, tool=-1, a=5, v=5, cp=0, coordinateMode=0):
        if len(pose) != 6:
            raise ValueError("Pose must contain 6 values: [x, y, z, rx, ry, rz]")
        result = self.dashboard.MovJ(*pose, coordinateMode, user=user, tool=tool, a=a, v=v, cp=cp)
        parsed = parse_result_id(result)
        print(f"MovJ result: {result}")
        if parsed[0] != 0:
            raise RuntimeError(f"MovJ failed: {result}")
        if len(parsed) < 2:
            raise RuntimeError(f"MovJ returned no command id: {result}")
        return parsed

    def wait_motion_complete(self, command_id, timeout=30.0):
        start_time = time.time()
        last_mode = None
        last_id = None
        while time.time() - start_time < timeout:
            with self._feed_lock:
                mode = self.feed_data.get('RobotMode')
                current_id = self.feed_data.get('CurrentCommandId')
            last_mode = mode
            last_id = current_id
            print(f"Waiting motion completion: RobotMode={mode}, CurrentCommandId={current_id}", end='\r')
            if mode == 5 and current_id == command_id:
                print()
                return True
            if mode == 9:
                # Try to fetch detailed error information from the controller
                try:
                    err_info = self.dashboard.GetError("en")
                    print()  # newline after the progress line
                    print("Controller GetError:", err_info)
                except Exception as ge:
                    print()
                    print(f"GetError call failed: {ge}")
                raise RuntimeError(f"Robot entered error mode while waiting for motion completion (command_id={command_id}, last_mode={mode}, last_id={current_id})")
            if mode == 11:
                try:
                    err_info = self.dashboard.GetError("en")
                    print()
                    print("Controller GetError (collision):", err_info)
                except Exception as ge:
                    print()
                    print(f"GetError call failed: {ge}")
                raise RuntimeError(f"Robot entered collision/unknown mode while waiting for motion completion (command_id={command_id}, last_mode={mode}, last_id={current_id})")
            time.sleep(0.1)
        print()
        raise TimeoutError(
            f"Motion did not complete within {timeout} seconds (last mode={last_mode}, current_id={last_id})")

    def close(self):
        self._running = False
        if self.dashboard:
            try:
                self.dashboard.close()
            except Exception as e:
                print(f"Dashboard close error: {e}")
        if self.feedback:
            try:
                self.feedback.close()
            except Exception as e:
                print(f"Feedback close error: {e}")


def check_port(ip, port, timeout=2.0):
    try:
        with socket.create_connection((ip, port), timeout=timeout):
            return True
    except OSError:
        return False


def main():
    parser = argparse.ArgumentParser(description="Dobot point motion demo")
    parser.add_argument("--ip", default="192.168.200.1", help="Robot IP address")
    args = parser.parse_args()
    if not check_port(args.ip, 29999) or not check_port(args.ip, 30004):
        print("Robot TCP 服务未就绪: 29999 或 30004 端口无法连接。")
        print("请确认机器人已进入 TCP/IP 控制模式，或检查机器人端口是否开启。")
        print(f"29999: {'OK' if check_port(args.ip, 29999) else 'FAILED'}, 30004: {'OK' if check_port(args.ip, 30004) else 'FAILED'}")
        return
    demo = PointMoveDemo(args.ip)
    try:
        demo.start_feedback()
        demo.enable_robot()
        # 这里使用关节角（示例值），并以 coordinateMode=1 调用 MovJ
        point_a = [112.2096, -26.0701, 155.8227, -123.9722, -22.2603, -8.4046]
        point_b = [112.894, -25.8522, 155.0512, -127.5921, -21.5191, 1.4812]
        print("Move to point A")
        cmd_a = demo.movj(point_a, coordinateMode=1)
        if len(cmd_a) >= 2:
            demo.wait_motion_complete(cmd_a[1])

        print("Move to point B")
        cmd_b = demo.movj(point_b, coordinateMode=1)
        if len(cmd_b) >= 2:
            demo.wait_motion_complete(cmd_b[1])

        print("Point motion demo finished")
    except KeyboardInterrupt:
        print("Interrupted by user")
    except Exception as e:
        print(f"Demo error: {e}")
    finally:
        demo.close()

if __name__ == '__main__':
    main()
