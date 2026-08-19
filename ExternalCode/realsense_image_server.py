#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Serve RealSense frames and optional YOLO/SAM results to Unity."""

import json
import os
import subprocess
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def switch_to_optional_vision_environment():
    """Use the isolated vision Python when it is complete and importable."""
    if os.environ.get("VISION_ENV_REEXECUTED") == "1":
        return

    script_path = os.path.abspath(__file__)
    software_directory = os.path.dirname(
        os.path.dirname(os.path.dirname(script_path))
    )
    configured_python = os.environ.get(
        "VISION_PYTHON_EXECUTABLE",
        "",
    ).strip()
    vision_python = (
        configured_python
        or os.path.join(
            software_directory,
            "vision_env",
            "bin",
            "python",
        )
    )
    if not os.path.isfile(vision_python) or not os.access(
        vision_python,
        os.X_OK,
    ):
        return

    try:
        check = subprocess.run(
            [
                vision_python,
                "-c",
                (
                    "import cv2,numpy,pyrealsense2,torch,ultralytics;"
                    "assert torch.cuda.is_available()"
                ),
            ],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.PIPE,
            text=True,
            timeout=30,
            check=False,
        )
    except Exception as exc:
        print(
            "[vision] Isolated environment check failed; "
            "continuing with current Python: " + str(exc),
            flush=True,
        )
        return

    if check.returncode != 0:
        error = (check.stderr or "").strip().replace("\n", " ")
        print(
            "[vision] Isolated environment is not ready; "
            "continuing with current Python: " + error[-500:],
            flush=True,
        )
        return

    environment = os.environ.copy()
    environment["VISION_ENV_REEXECUTED"] = "1"
    print(
        "[vision] Switching to isolated Python: " + vision_python,
        flush=True,
    )
    os.execve(
        vision_python,
        [vision_python, script_path] + sys.argv[1:],
        environment,
    )


switch_to_optional_vision_environment()

import cv2
import numpy as np
import pyrealsense2 as rs


HOST = os.environ.get("REALSENSE_HTTP_HOST", "127.0.0.1")
PORT = int(os.environ.get("REALSENSE_HTTP_PORT", "8080"))
COLOR_WIDTH = int(os.environ.get("REALSENSE_COLOR_WIDTH", "1280"))
COLOR_HEIGHT = int(os.environ.get("REALSENSE_COLOR_HEIGHT", "720"))
COLOR_FPS = int(os.environ.get("REALSENSE_COLOR_FPS", "15"))
DEPTH_WIDTH = int(os.environ.get("REALSENSE_DEPTH_WIDTH", str(COLOR_WIDTH)))
DEPTH_HEIGHT = int(os.environ.get("REALSENSE_DEPTH_HEIGHT", str(COLOR_HEIGHT)))
DEPTH_FPS = int(os.environ.get("REALSENSE_DEPTH_FPS", str(COLOR_FPS)))
JPEG_QUALITY = max(
    1,
    min(100, int(os.environ.get("REALSENSE_JPEG_QUALITY", "80"))),
)
VISION_ENABLED = os.environ.get(
    "VISION_PROCESSING_ENABLED",
    "1",
).strip().lower() not in ("0", "false", "no", "off")
VISION_CONFIDENCE = max(
    0.01,
    min(1.0, float(os.environ.get("VISION_CONFIDENCE", "0.2"))),
)
VISION_INTERVAL = max(
    0.05,
    float(os.environ.get("VISION_INFERENCE_INTERVAL", "0.15")),
)
VISION_FAILURE_LIMIT = max(
    1,
    int(os.environ.get("VISION_FAILURE_LIMIT", "3")),
)

SCRIPT_DIRECTORY = Path(__file__).resolve().parent
DEFAULT_MODEL_DIRECTORY = SCRIPT_DIRECTORY / "models"
DETECTION_MODEL_PATH = Path(
    os.environ.get(
        "VISION_DETECTION_MODEL",
        str(DEFAULT_MODEL_DIRECTORY / "best.pt"),
    )
).expanduser()
SAM_MODEL_PATH = Path(
    os.environ.get(
        "VISION_SAM_MODEL",
        str(DEFAULT_MODEL_DIRECTORY / "sam2_b.pt"),
    )
).expanduser()


def encode_jpeg(image):
    options = [int(cv2.IMWRITE_JPEG_QUALITY), JPEG_QUALITY]
    ok, encoded = cv2.imencode(".jpg", image, options)
    if not ok:
        raise RuntimeError("OpenCV failed to encode JPEG")
    return encoded.tobytes()


def depth_median_metres(depth_image, center_x, center_y, depth_scale):
    if depth_image is None or depth_scale is None:
        return None

    height, width = depth_image.shape[:2]
    x0 = max(0, center_x - 5)
    x1 = min(width, center_x + 6)
    y0 = max(0, center_y - 5)
    y1 = min(height, center_y + 6)
    values = depth_image[y0:y1, x0:x1]
    valid = values[values > 0]
    if valid.size == 0:
        return None
    return float(np.median(valid)) * float(depth_scale)


class VisionProcessor:
    """Load optional models and turn an aligned RGB-D frame into annotations."""

    def __init__(self):
        self._lock = threading.Lock()
        self._detector = None
        self._segmenter = None
        self._state = "disabled" if not VISION_ENABLED else "loading"
        self._model_error = ""
        self._last_error = ""
        self._consecutive_failures = 0
        self._load_thread = None

    def start_loading(self):
        if not VISION_ENABLED:
            return
        self._load_thread = threading.Thread(
            target=self._load_models,
            name="vision-model-loader",
            daemon=True,
        )
        self._load_thread.start()

    def _load_models(self):
        missing = [
            str(path)
            for path in (DETECTION_MODEL_PATH, SAM_MODEL_PATH)
            if not path.is_file()
        ]
        if missing:
            self._set_fallback(
                "视觉模型文件不存在: " + ", ".join(missing),
                model_error=True,
            )
            return

        try:
            from ultralytics import SAM, YOLO

            detector = YOLO(str(DETECTION_MODEL_PATH))
            segmenter = SAM(str(SAM_MODEL_PATH))
        except Exception as exc:
            self._set_fallback(
                "视觉模型加载失败: " + str(exc),
                model_error=True,
            )
            return

        with self._lock:
            self._detector = detector
            self._segmenter = segmenter
            self._state = "ready"
            self._model_error = ""
            self._last_error = ""
            self._consecutive_failures = 0
        print(
            "[vision] YOLO/SAM models loaded; processed preview enabled",
            flush=True,
        )

    def _set_fallback(self, message, model_error=False):
        with self._lock:
            self._state = "raw_fallback"
            if model_error:
                self._model_error = message
            self._last_error = message
        print(f"[vision] {message}; raw preview remains available", flush=True)

    def is_ready(self):
        with self._lock:
            return self._state == "ready"

    def record_processing_failure(self, exc):
        message = "视觉处理失败: " + str(exc)
        with self._lock:
            self._last_error = message
            self._consecutive_failures += 1
            failures = self._consecutive_failures
            if failures >= VISION_FAILURE_LIMIT:
                self._state = "raw_fallback"
        print(
            f"[vision] {message}; failure {failures}/{VISION_FAILURE_LIMIT}",
            flush=True,
        )
        if failures >= VISION_FAILURE_LIMIT:
            print(
                "[vision] processing disabled after repeated failures; "
                "raw preview remains available",
                flush=True,
            )

    def process(self, color_image, depth_image, intrinsics, depth_scale):
        with self._lock:
            if self._state != "ready":
                return None, []
            detector = self._detector
            segmenter = self._segmenter

        result_image = color_image.copy()
        targets = []
        detection_results = detector.predict(
            source=color_image,
            conf=VISION_CONFIDENCE,
            verbose=False,
        )
        boxes_result = detection_results[0].boxes
        if boxes_result is None or len(boxes_result) == 0:
            self._mark_success()
            return result_image, targets

        boxes = boxes_result.xyxy.cpu().numpy()
        confidences = (
            boxes_result.conf.cpu().numpy()
            if boxes_result.conf is not None
            else np.ones(len(boxes), dtype=float)
        )
        class_ids = (
            boxes_result.cls.cpu().numpy()
            if boxes_result.cls is not None
            else np.full(len(boxes), -1, dtype=float)
        )

        for box, confidence, class_id in zip(
            boxes,
            confidences,
            class_ids,
        ):
            x1, y1, x2, y2 = [int(value) for value in box]
            segmentation_results = segmenter(
                color_image,
                bboxes=[[x1, y1, x2, y2]],
                verbose=False,
            )
            masks = segmentation_results[0].masks
            if masks is None:
                continue

            mask = masks.data[0].cpu().numpy()
            if mask.shape[:2] != color_image.shape[:2]:
                mask = cv2.resize(
                    mask,
                    (color_image.shape[1], color_image.shape[0]),
                    interpolation=cv2.INTER_NEAREST,
                )
            mask = (mask * 255).astype(np.uint8)
            contours, _ = cv2.findContours(
                mask,
                cv2.RETR_EXTERNAL,
                cv2.CHAIN_APPROX_SIMPLE,
            )
            if not contours:
                continue

            contour = max(contours, key=cv2.contourArea)
            rect = cv2.minAreaRect(contour)
            (center_x_float, center_y_float), _, angle = rect
            center_x = int(center_x_float)
            center_y = int(center_y_float)
            rectangle_points = np.int32(cv2.boxPoints(rect))

            cv2.drawContours(
                result_image,
                [contour],
                -1,
                (0, 255, 255),
                2,
            )
            cv2.drawContours(
                result_image,
                [rectangle_points],
                0,
                (0, 255, 0),
                2,
            )
            cv2.circle(
                result_image,
                (center_x, center_y),
                6,
                (0, 0, 255),
                -1,
            )

            depth_metres = depth_median_metres(
                depth_image,
                center_x,
                center_y,
                depth_scale,
            )
            camera_xyz = None
            if depth_metres is not None and intrinsics is not None:
                x_mm = (
                    (center_x - intrinsics.ppx)
                    * depth_metres
                    / intrinsics.fx
                    * 1000.0
                )
                y_mm = (
                    (center_y - intrinsics.ppy)
                    * depth_metres
                    / intrinsics.fy
                    * 1000.0
                )
                z_mm = depth_metres * 1000.0
                camera_xyz = {
                    "x_mm": round(float(x_mm), 3),
                    "y_mm": round(float(y_mm), 3),
                    "z_mm": round(float(z_mm), 3),
                }

            target = {
                "center_u": center_x,
                "center_v": center_y,
                "rect_angle_deg": round(float(angle), 3),
                "confidence": round(float(confidence), 4),
                "class_id": int(class_id),
                "camera_xyz": camera_xyz,
            }
            targets.append(target)
            self._draw_target_text(result_image, target)

        self._mark_success()
        return result_image, targets

    def _mark_success(self):
        with self._lock:
            self._last_error = ""
            self._consecutive_failures = 0

    @staticmethod
    def _draw_target_text(image, target):
        x = target["center_u"] + 10
        y = target["center_v"] - 20
        lines = [
            f"Center  conf:{target['confidence']:.2f}",
        ]
        xyz = target["camera_xyz"]
        if xyz is not None:
            lines.extend(
                [
                    f"X:{xyz['x_mm']:.1f}mm",
                    f"Y:{xyz['y_mm']:.1f}mm",
                    f"Z:{xyz['z_mm']:.1f}mm",
                ]
            )
        else:
            lines.append("Depth unavailable")

        for index, text in enumerate(lines):
            cv2.putText(
                image,
                text,
                (x, y + index * 20),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.5,
                (0, 255, 0) if index else (0, 0, 255),
                2,
            )

    def status(self):
        with self._lock:
            return {
                "enabled": VISION_ENABLED,
                "state": self._state,
                "ready": self._state == "ready",
                "detection_model": str(DETECTION_MODEL_PATH),
                "sam_model": str(SAM_MODEL_PATH),
                "model_error": self._model_error,
                "last_error": self._last_error,
                "consecutive_failures": self._consecutive_failures,
                "failure_limit": VISION_FAILURE_LIMIT,
            }


class RealSenseVisionSource:
    """Own the camera and keep raw preview independent from visual inference."""

    def __init__(self):
        self._pipeline = None
        self._align = None
        self._lock = threading.Lock()
        self._stop_event = threading.Event()
        self._capture_thread = None
        self._processing_thread = None
        self._processor = VisionProcessor()
        self._started = False
        self._depth_available = False
        self._depth_start_error = ""
        self._depth_scale = None
        self._intrinsics = None
        self._device_name = "unknown"
        self._serial = "unknown"
        self._usb_type = "unknown"
        self._frame_count = 0
        self._inference_count = 0
        self._last_frame_time = None
        self._last_inference_time = None
        self._last_camera_error = ""
        self._latest_sequence = 0
        self._processed_sequence = 0
        self._latest_color = None
        self._latest_depth = None
        self._latest_raw_jpeg = None
        self._latest_processed_jpeg = None
        self._latest_targets = []

    def start(self):
        profile = self._start_camera_with_depth_fallback()
        self._started = True
        device = profile.get_device()
        self._device_name = self._read_info(device, rs.camera_info.name)
        self._serial = self._read_info(device, rs.camera_info.serial_number)
        self._usb_type = self._read_info(
            device,
            rs.camera_info.usb_type_descriptor,
        )
        color_profile = (
            profile.get_stream(rs.stream.color).as_video_stream_profile()
        )
        self._intrinsics = color_profile.get_intrinsics()
        if self._depth_available:
            self._depth_scale = (
                device.first_depth_sensor().get_depth_scale()
            )
            self._align = rs.align(rs.stream.color)

        self._capture_thread = threading.Thread(
            target=self._capture_loop,
            name="realsense-capture",
            daemon=True,
        )
        self._processing_thread = threading.Thread(
            target=self._processing_loop,
            name="vision-inference",
            daemon=True,
        )
        self._capture_thread.start()
        self._processing_thread.start()
        self._processor.start_loading()

        print(
            "[realsense-http] camera started: "
            f"{self._device_name}, serial={self._serial}, "
            f"usb={self._usb_type}, "
            f"{COLOR_WIDTH}x{COLOR_HEIGHT}@{COLOR_FPS}, "
            f"depth={'yes' if self._depth_available else 'no'}",
            flush=True,
        )

    def _start_camera_with_depth_fallback(self):
        try:
            pipeline = rs.pipeline()
            config = rs.config()
            config.enable_stream(
                rs.stream.color,
                COLOR_WIDTH,
                COLOR_HEIGHT,
                rs.format.bgr8,
                COLOR_FPS,
            )
            config.enable_stream(
                rs.stream.depth,
                DEPTH_WIDTH,
                DEPTH_HEIGHT,
                rs.format.z16,
                DEPTH_FPS,
            )
            profile = pipeline.start(config)
            self._pipeline = pipeline
            self._depth_available = True
            return profile
        except Exception as exc:
            self._depth_start_error = str(exc)
            print(
                "[realsense-http] RGB-D start failed; retrying color-only "
                f"fallback: {exc}",
                flush=True,
            )
            try:
                pipeline.stop()
            except Exception:
                pass

        pipeline = rs.pipeline()
        config = rs.config()
        config.enable_stream(
            rs.stream.color,
            COLOR_WIDTH,
            COLOR_HEIGHT,
            rs.format.bgr8,
            COLOR_FPS,
        )
        profile = pipeline.start(config)
        self._pipeline = pipeline
        self._depth_available = False
        return profile

    @staticmethod
    def _read_info(device, info):
        try:
            return device.get_info(info)
        except Exception:
            return "unknown"

    def _capture_loop(self):
        while not self._stop_event.is_set():
            try:
                frames = self._pipeline.wait_for_frames(3000)
                if self._depth_available:
                    frames = self._align.process(frames)
                color_frame = frames.get_color_frame()
                depth_frame = (
                    frames.get_depth_frame()
                    if self._depth_available
                    else None
                )
                if not color_frame:
                    raise RuntimeError("RealSense returned no color frame")

                color_image = np.asanyarray(
                    color_frame.get_data()
                ).copy()
                depth_image = (
                    np.asanyarray(depth_frame.get_data()).copy()
                    if depth_frame
                    else None
                )
                raw_jpeg = encode_jpeg(color_image)
                now = time.time()

                with self._lock:
                    self._latest_sequence += 1
                    self._latest_color = color_image
                    self._latest_depth = depth_image
                    self._latest_raw_jpeg = raw_jpeg
                    self._frame_count += 1
                    self._last_frame_time = now
                    self._last_camera_error = ""
            except Exception as exc:
                with self._lock:
                    self._last_camera_error = str(exc)
                if not self._stop_event.is_set():
                    print(
                        f"[realsense-http] capture failed: {exc}",
                        flush=True,
                    )
                    time.sleep(0.2)

    def _processing_loop(self):
        last_sequence = 0
        while not self._stop_event.is_set():
            if not self._processor.is_ready():
                time.sleep(0.05)
                continue

            with self._lock:
                sequence = self._latest_sequence
                color_image = self._latest_color
                depth_image = self._latest_depth

            if color_image is None or sequence == last_sequence:
                time.sleep(0.01)
                continue

            started_at = time.monotonic()
            try:
                processed_image, targets = self._processor.process(
                    color_image,
                    depth_image,
                    self._intrinsics,
                    self._depth_scale,
                )
                if processed_image is None:
                    continue
                processed_jpeg = encode_jpeg(processed_image)
                now = time.time()
                with self._lock:
                    self._latest_processed_jpeg = processed_jpeg
                    self._latest_targets = targets
                    self._processed_sequence = sequence
                    self._inference_count += 1
                    self._last_inference_time = now
                last_sequence = sequence
            except Exception as exc:
                self._processor.record_processing_failure(exc)
                with self._lock:
                    self._latest_processed_jpeg = None
                    self._latest_targets = []
                last_sequence = sequence

            elapsed = time.monotonic() - started_at
            remaining = VISION_INTERVAL - elapsed
            if remaining > 0:
                self._stop_event.wait(remaining)

    def get_image(self):
        with self._lock:
            raw_image = self._latest_raw_jpeg
            processed_image = self._latest_processed_jpeg
            serial = self._serial
            usb_type = self._usb_type

        processor_status = self._processor.status()
        if processor_status["ready"] and processed_image is not None:
            return processed_image, "processed", serial, usb_type
        if raw_image is not None:
            return raw_image, "raw_fallback", serial, usb_type
        raise RuntimeError("RealSense frame is not ready")

    def result(self):
        with self._lock:
            return {
                "ok": self._processor.is_ready()
                and self._latest_processed_jpeg is not None,
                "timestamp": self._last_inference_time,
                "frame_sequence": self._processed_sequence,
                "targets": list(self._latest_targets),
            }

    def status(self):
        with self._lock:
            camera = {
                "started": self._started,
                "device": self._device_name,
                "serial": self._serial,
                "usb_type": self._usb_type,
                "width": COLOR_WIDTH,
                "height": COLOR_HEIGHT,
                "fps": COLOR_FPS,
                "depth_available": self._depth_available,
                "depth_start_error": self._depth_start_error,
                "frame_count": self._frame_count,
                "last_frame_time": self._last_frame_time,
                "last_error": self._last_camera_error,
            }
            preview_ready = self._latest_raw_jpeg is not None
            processed_ready = self._latest_processed_jpeg is not None
            inference_count = self._inference_count
            last_inference_time = self._last_inference_time

        processor = self._processor.status()
        preview_mode = (
            "processed"
            if processor["ready"] and processed_ready
            else "raw_fallback"
        )
        return {
            "ok": self._started and preview_ready,
            "preview_ready": preview_ready,
            "preview_mode": preview_mode,
            "jpeg_quality": JPEG_QUALITY,
            "inference_count": inference_count,
            "last_inference_time": last_inference_time,
            "camera": camera,
            "vision": processor,
        }

    def stop(self):
        if not self._started:
            return

        self._stop_event.set()
        try:
            self._pipeline.stop()
        except Exception as exc:
            print(
                f"[realsense-http] camera stop warning: {exc}",
                flush=True,
            )
        for worker in (self._capture_thread, self._processing_thread):
            if worker is not None and worker.is_alive():
                worker.join(timeout=1.0)
        self._started = False
        print("[realsense-http] camera stopped", flush=True)


class RealSenseImageHandler(BaseHTTPRequestHandler):
    server_version = "RealSenseUnityVisionServer/2.0"

    @property
    def source(self):
        return self.server.frame_source

    def do_GET(self):
        path = self.path.split("?", 1)[0]
        if path in ("/", "/latest.jpg"):
            self._send_latest_frame()
            return
        if path == "/status":
            self._send_json(200, self.source.status())
            return
        if path == "/result":
            self._send_json(200, self.source.result())
            return
        self.send_error(404, "Not found")

    def _send_latest_frame(self):
        try:
            image, mode, serial, usb_type = self.source.get_image()
        except Exception as exc:
            self._send_json(
                503,
                {
                    "ok": False,
                    "error": str(exc),
                    "status": self.source.status(),
                },
            )
            return

        self.send_response(200)
        self.send_header("Content-Type", "image/jpeg")
        self.send_header("Content-Length", str(len(image)))
        self.send_header("Cache-Control", "no-store")
        self.send_header(
            "X-Image-Source",
            f"realsense:{serial}:{usb_type}:{mode}",
        )
        self.send_header("X-Vision-Mode", mode)
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
    source = RealSenseVisionSource()
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
