#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Minimal Unity bridge for the servo tool.

Run this script, then let Unity send JSON-line commands to 127.0.0.1:9100:
  {"cmd":"connect"}
  {"cmd":"forward"}
  {"cmd":"reverse"}
  {"cmd":"stop"}
  {"cmd":"status"}

This intentionally only verifies Unity -> Python -> tool control. The full
fault detection and curve saving still live in the original V12 script.
"""

import json
import os
import socket
import threading
import time


TOOL_IP = os.environ.get("SERVO_TOOL_IP", "192.168.192.21")
TOOL_PORT = int(os.environ.get("SERVO_TOOL_PORT", "1200"))
BRIDGE_HOST = os.environ.get("SERVO_BRIDGE_HOST", "127.0.0.1")
BRIDGE_PORT = int(os.environ.get("SERVO_BRIDGE_PORT", "9100"))
CONNECT_TIMEOUT = float(os.environ.get("SERVO_CONNECT_TIMEOUT", "1.0"))
RECONNECT_DELAY = 1.0

tool_socket = None
tool_lock = threading.Lock()
receiver_running = False
last_error = ""
last_command = "none"
last_feedback_time = 0.0


def crc16_modbus(data: bytes) -> int:
    crc = 0xFFFF
    for b in data:
        crc ^= b
        for _ in range(8):
            if crc & 0x0001:
                crc = (crc >> 1) ^ 0xA001
            else:
                crc >>= 1
    return crc & 0xFFFF


def build_pc_cmd(data_mode: int, run_cmd: int) -> bytes:
    body = bytes([0x55, 0xAA, 0x07, 0x01, 0x00, data_mode & 0xFF, run_cmd & 0xFF])
    crc = crc16_modbus(body)
    return body + crc.to_bytes(2, "little") + b"\x0D\x0A"


CMD_RESET = build_pc_cmd(0x00, 0x00)
CMD_FORWARD = build_pc_cmd(0x01, 0x01)
CMD_REVERSE = build_pc_cmd(0x01, 0x02)


def hexstr(data: bytes) -> str:
    return " ".join(f"{b:02X}" for b in data)


def close_tool():
    global tool_socket
    with tool_lock:
        if tool_socket is not None:
            try:
                tool_socket.close()
            except OSError:
                pass
        tool_socket = None


def ensure_tool_connected() -> bool:
    global tool_socket, receiver_running, last_error
    with tool_lock:
        if tool_socket is not None:
            return True

        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            s.settimeout(CONNECT_TIMEOUT)
            s.connect((TOOL_IP, TOOL_PORT))
            s.settimeout(0.5)
            tool_socket = s
            last_error = ""
            if not receiver_running:
                receiver_running = True
                threading.Thread(target=tool_receive_loop, daemon=True).start()
            print(f"[bridge] connected tool {TOOL_IP}:{TOOL_PORT}")
            return True
        except OSError as exc:
            last_error = str(exc)
            tool_socket = None
            print(f"[bridge] tool connect failed: {exc}")
            return False


def send_tool_cmd(cmd: bytes, name: str) -> bool:
    global tool_socket, last_error, last_command
    if not ensure_tool_connected():
        return False

    with tool_lock:
        try:
            tool_socket.sendall(cmd)
            last_command = name
            print(f"[bridge] send {name}: {hexstr(cmd)}")
            return True
        except OSError as exc:
            last_error = str(exc)
            print(f"[bridge] send failed {name}: {exc}")
            try:
                tool_socket.close()
            except OSError:
                pass
            tool_socket = None
            return False


def tool_receive_loop():
    global tool_socket, last_feedback_time, last_error, receiver_running
    while True:
        with tool_lock:
            s = tool_socket

        if s is None:
            time.sleep(RECONNECT_DELAY)
            continue

        try:
            data = s.recv(1024)
            if not data:
                raise ConnectionError("tool disconnected")
            last_feedback_time = time.time()
        except socket.timeout:
            continue
        except OSError as exc:
            last_error = str(exc)
            close_tool()
            time.sleep(RECONNECT_DELAY)


def run_tool_command(cmd: str):
    normalized = (cmd or "").strip().lower()

    if normalized == "connect":
        ok = ensure_tool_connected()
        if ok:
            send_tool_cmd(CMD_RESET, "reset")
        return ok, "connected" if ok else "connect_failed"

    if normalized in ("stop", "reset"):
        ok = send_tool_cmd(CMD_RESET, "reset")
        return ok, "stopped" if ok else "stop_failed"

    if normalized == "forward":
        ok_reset = send_tool_cmd(CMD_RESET, "reset")
        time.sleep(0.2)
        ok_forward = send_tool_cmd(CMD_FORWARD, "forward")
        return ok_reset and ok_forward, "forward_started" if ok_forward else "forward_failed"

    if normalized == "reverse":
        ok_reset = send_tool_cmd(CMD_RESET, "reset")
        time.sleep(0.2)
        ok_reverse = send_tool_cmd(CMD_REVERSE, "reverse")
        return ok_reset and ok_reverse, "reverse_started" if ok_reverse else "reverse_failed"

    if normalized == "status":
        return True, "status"

    return False, f"unknown_cmd:{cmd}"


def make_status(ok: bool, message: str):
    with tool_lock:
        connected = tool_socket is not None
    return {
        "ok": ok,
        "message": message,
        "tool_connected": connected,
        "last_command": last_command,
        "last_error": last_error,
        "last_feedback_age": None if last_feedback_time <= 0 else round(time.time() - last_feedback_time, 3),
    }


def handle_unity_client(conn: socket.socket, addr):
    with conn:
        file = conn.makefile("rwb")
        for raw in file:
            try:
                request = json.loads(raw.decode("utf-8"))
                ok, message = run_tool_command(request.get("cmd", ""))
                response = make_status(ok, message)
            except Exception as exc:
                response = make_status(False, f"bridge_error:{exc}")

            file.write((json.dumps(response, ensure_ascii=False) + "\n").encode("utf-8"))
            file.flush()


def main():
    print(f"[bridge] Unity bridge listening on {BRIDGE_HOST}:{BRIDGE_PORT}")
    print(f"[bridge] Servo tool target is {TOOL_IP}:{TOOL_PORT}")
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((BRIDGE_HOST, BRIDGE_PORT))
    server.listen(5)

    while True:
        conn, addr = server.accept()
        threading.Thread(target=handle_unity_client, args=(conn, addr), daemon=True).start()


if __name__ == "__main__":
    main()
