#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Serve Intel RealSense color frames to Unity over local HTTP."""

import json
import os
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import cv2
import numpy as np
import pyrealsense2 as rs


HOST = os.environ.get("REALSENSE_HTTP_HOST", "127.0.0.1")
PORT = int(os.environ.get("REALSENSE_HTTP_PORT", "8080"))
COLOR_WIDTH = int(os.environ.get("REALSENSE_COLOR_WIDTH", "1280"))
COLOR_HEIGHT = int(os.environ.get("REALSENSE_COLOR_HEIGHT", "720"))
COLOR_FPS = int(os.environ.get("REALSENSE_COLOR_FPS", "15"))
JPEG_QUALITY = max(
    1,
    min(100, int(os.environ.get("REALSENSE_JPEG_QUALITY", "80"))),
)


class RealSenseColorSource:
    def __init__(self):
        self._pipeline = rs.pipeline()
        self._config = rs.config()
        self._lock = threading.Lock()
        self._started = False
        self._device_name = "unknown"
        self._serial = "unknown"
        self._usb_type = "unknown"
        self._frame_count = 0
        self._last_frame_time = None
        self._last_error = ""

    def start(self):
        self._config.enable_stream(
            rs.stream.color,
            COLOR_WIDTH,
            COLOR_HEIGHT,
            rs.format.bgr8,
            COLOR_FPS,
        )
        profile = self._pipeline.start(self._config)
        self._started = True

        device = profile.get_device()
        self._device_name = self._read_info(device, rs.camera_info.name)
        self._serial = self._read_info(device, rs.camera_info.serial_number)
        self._usb_type = self._read_info(
            device,
            rs.camera_info.usb_type_descriptor,
        )
        print(
            "[realsense-http] camera started: "
            f"{self._device_name}, serial={self._serial}, "
            f"usb={self._usb_type}, "
            f"{COLOR_WIDTH}x{COLOR_HEIGHT}@{COLOR_FPS}",
            flush=True,
        )

    @staticmethod
    def _read_info(device, info):
        try:
            return device.get_info(info)
        except Exception:
            return "unknown"

    def get_jpeg(self):
        with self._lock:
            try:
                frames = self._pipeline.wait_for_frames(3000)
                color_frame = frames.get_color_frame()
                if not color_frame:
                    raise RuntimeError("RealSense returned no color frame")

                color_image = np.asanyarray(color_frame.get_data())
                encode_options = [
                    int(cv2.IMWRITE_JPEG_QUALITY),
                    JPEG_QUALITY,
                ]
                ok, encoded = cv2.imencode(
                    ".jpg",
                    color_image,
                    encode_options,
                )
                if not ok:
                    raise RuntimeError("OpenCV failed to encode JPEG")

                self._frame_count += 1
                self._last_frame_time = time.time()
                self._last_error = ""
                return encoded.tobytes()
            except Exception as exc:
                self._last_error = str(exc)
                raise

    def status(self):
        return {
            "ok": self._started and not self._last_error,
            "device": self._device_name,
            "serial": self._serial,
            "usb_type": self._usb_type,
            "width": COLOR_WIDTH,
            "height": COLOR_HEIGHT,
            "fps": COLOR_FPS,
            "jpeg_quality": JPEG_QUALITY,
            "frame_count": self._frame_count,
            "last_frame_time": self._last_frame_time,
            "last_error": self._last_error,
        }

    def stop(self):
        if not self._started:
            return

        self._pipeline.stop()
        self._started = False
        print("[realsense-http] camera stopped", flush=True)


class RealSenseImageHandler(BaseHTTPRequestHandler):
    server_version = "RealSenseUnityImageServer/1.0"

    @property
    def source(self):
        return self.server.frame_source

    def do_GET(self):
        if self.path in ("/", "/latest.jpg"):
            self._send_latest_frame()
            return

        if self.path == "/status":
            self._send_json(200, self.source.status())
            return

        self.send_error(404, "Not found")

    def _send_latest_frame(self):
        try:
            image = self.source.get_jpeg()
        except Exception as exc:
            self._send_json(
                503,
                {
                    "ok": False,
                    "error": str(exc),
                    "camera": self.source.status(),
                },
            )
            return

        status = self.source.status()
        self.send_response(200)
        self.send_header("Content-Type", "image/jpeg")
        self.send_header("Content-Length", str(len(image)))
        self.send_header("Cache-Control", "no-store")
        self.send_header(
            "X-Image-Source",
            f"realsense:{status['serial']}:{status['usb_type']}",
        )
        self.end_headers()
        self.wfile.write(image)

    def _send_json(self, status_code, payload):
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status_code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, _format, *_args):
        return


def main():
    source = RealSenseColorSource()
    server = None

    try:
        source.start()
        server = ThreadingHTTPServer((HOST, PORT), RealSenseImageHandler)
        server.frame_source = source
        print(
            f"[realsense-http] serving http://{HOST}:{PORT}/latest.jpg",
            flush=True,
        )
        server.serve_forever()
    except KeyboardInterrupt:
        print("[realsense-http] interrupted", flush=True)
    finally:
        if server is not None:
            server.server_close()
        source.stop()


if __name__ == "__main__":
    main()
