#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Tiny fake servo-tool TCP server for Unity bridge testing.

It listens like the real servo tool and prints whatever control frames arrive.
Use it to verify:
  Unity button -> Python bridge -> virtual servo server
"""

import socket
import threading
import time


HOST = "127.0.0.1"
PORT = 1200


def hexstr(data: bytes) -> str:
    return " ".join(f"{b:02X}" for b in data)


def describe_cmd(data: bytes) -> str:
    if len(data) >= 7 and data[:2] == b"\x55\xAA":
        run_cmd = data[6]
        if run_cmd == 0x00:
            return "RESET/STOP"
        if run_cmd == 0x01:
            return "FORWARD"
        if run_cmd == 0x02:
            return "REVERSE"
    return "UNKNOWN"


def handle_client(conn: socket.socket, addr):
    print(f"[virtual-tool] client connected: {addr}")
    with conn:
        while True:
            data = conn.recv(1024)
            if not data:
                print(f"[virtual-tool] client disconnected: {addr}")
                return
            print(f"[virtual-tool] {describe_cmd(data)} | {hexstr(data)}")
            conn.sendall(b"FAKE_FEEDBACK\r\n")
            time.sleep(0.05)


def main():
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((HOST, PORT))
    server.listen(5)
    print(f"[virtual-tool] listening on {HOST}:{PORT}")

    while True:
        conn, addr = server.accept()
        threading.Thread(target=handle_client, args=(conn, addr), daemon=True).start()


if __name__ == "__main__":
    main()
