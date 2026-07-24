#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
伺服电批 TCP 控制 + 时间-扭矩算法 V26，目标扭矩 28 N.m（物理开关同步绘图 + 异常后只停止不自动回转版）

V8 改动：
1. 不再使用“低扭矩平台=卡滞”的论文判据作为自动报警条件，避免正常拧紧误报；
2. 卡滞改为高负载卡滞：高扭矩 + 低速度 + 扭矩几乎不上升，连续确认；
3. 默认关闭卡滞自动停机，只报警，保证曲线完整；
4. 修复 clear_run() 中 engaged_once 未 global 的问题；
5. 增加完整曲线保存、原始/滤波曲线显示；
6. 保留目标扭矩 98% + 0.5s 稳定保持完成判定；
7. 滑牙改为峰值后明显掉扭矩 + 仍在旋转/速度存在；
8. 每次结束时将 CSV 数据和 PNG 曲线保存到同一文件夹，并使用同一个文件名基准，保证一一对应；
9. 拧紧完成 OK 后输出成功指令，并可选通过 TCP 发送成功信号；
10. 滑牙后处理：立即复位停机、保存同名 CSV/PNG、输出 NG_SLIP 指令，禁止继续拧紧。

协议：
PC 控制帧：55 AA 07 01 00 数据发送模式 启停指令 CRC低 CRC高 0D 0A
启动时 data_mode=0x01，设备连续回传数据。

V20 新增：
1. 保留反转定时停止逻辑，但反转结束后使用“停止帧 + 复位帧 + 断开TCP重连”三重急停；
2. Stop 按钮采用急停重连，不再只依赖原连接 sendall；
3. 增加鼠标点击兜底事件，Button 控件不触发时仍可识别 Forward/Reverse/Stop 区域；
4. 反转回初始位改为带回传反转帧，便于确认指令是否执行。
"""

import csv
import io
import json
import os
import queue
import socket
import sys
import threading
import time
from collections import deque
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import numpy as np
UNITY_EMBEDDED_MODE = os.environ.get(
    "SERVO_UNITY_EMBEDDED_MODE",
    "0",
).strip().lower() in ("1", "true", "yes", "on")
import matplotlib
if UNITY_EMBEDDED_MODE:
    matplotlib.use("Agg")

import matplotlib.pyplot as plt
import matplotlib.animation as animation
from matplotlib.backends.backend_agg import FigureCanvasAgg
from matplotlib.font_manager import FontProperties
from matplotlib.figure import Figure
from matplotlib.widgets import Button

# 终端单键监听使用：Windows 用 msvcrt；Linux/macOS 用 termios/select。
try:
    import msvcrt  # type: ignore
except Exception:
    msvcrt = None

try:
    import select
    import termios
    import tty
except Exception:
    select = None
    termios = None
    tty = None

# =========================
# TCP 参数
# =========================
IP = os.environ.get("SERVO_TOOL_IP", "192.168.192.21")
PORT = int(os.environ.get("SERVO_TOOL_PORT", "1200"))
CONNECT_TIMEOUT = float(os.environ.get("SERVO_CONNECT_TIMEOUT", "3.0"))
RECONNECT_DELAY = 2.0

# Unity 内部控制口：Unity 只连这个端口，不直接连电批。
UNITY_BRIDGE_HOST = os.environ.get("SERVO_BRIDGE_HOST", "127.0.0.1")
UNITY_BRIDGE_PORT = int(os.environ.get("SERVO_BRIDGE_PORT", "9100"))
CURVE_HTTP_HOST = os.environ.get("SERVO_CURVE_HOST", "127.0.0.1")
CURVE_HTTP_PORT = int(os.environ.get("SERVO_CURVE_PORT", "9101"))
CURVE_RENDER_INTERVAL = max(
    0.1,
    float(os.environ.get("SERVO_CURVE_INTERVAL", "0.2")),
)
CURVE_JPEG_QUALITY = max(
    50,
    min(95, int(os.environ.get("SERVO_CURVE_JPEG_QUALITY", "82"))),
)
DEFAULT_CHINESE_FONT_PATH = (
    Path(__file__).resolve().parent.parent
    / "Assets"
    / "微软雅黑.ttf"
)
CURVE_FONT_PATH = Path(
    os.environ.get(
        "SERVO_CURVE_FONT",
        str(DEFAULT_CHINESE_FONT_PATH),
    )
)
CHINESE_FONT = (
    FontProperties(fname=str(CURVE_FONT_PATH))
    if CURVE_FONT_PATH.is_file()
    else None
)
matplotlib.rcParams["axes.unicode_minus"] = False

STATE_DISPLAY_NAMES = {
    "IDLE": "空闲",
    "MONITOR": "监控中",
    "RUN_FORWARD": "正在拧紧",
    "OK": "拧紧合格",
    "NG_SLIP": "滑牙异常",
    "JAM": "卡滞异常",
    "JAM_WARN": "卡滞预警",
    "NG_DEVICE": "设备异常",
    "STOP": "已停止",
    "HOME_READY": "回位完成",
    "MANUAL_REVERSE_HOME": "正在回位",
    "RETURN_HOME": "正在回位",
}


def state_display_name(value):
    key = str(value or "").strip().upper()
    return STATE_DISPLAY_NAMES.get(key, key or "未知")

# =========================
# 保存目录
# =========================
# 默认保存到用户主目录下 servo_logs/YYYYMMDD/
# 每次拧紧结束会生成同名 CSV 和 PNG，例如：
#   20250610_164501_OK.csv
#   20250610_164501_OK.png
BASE_SAVE_DIR = Path(__file__).resolve().parent / "servo_logs"

# =========================
# 结果指令
# =========================
# 默认只在终端输出结果指令，不发送给电批，避免给设备发送协议外数据。
# 如果你有上位机/PLC需要通过同一个 TCP 接收结果，可将 SEND_RESULT_TO_TCP_DEVICE 改为 True，
# 并按你的上位机/PLC协议修改 *_CMD。
SUCCESS_CMD_TEXT = "TIGHTEN_OK"
SUCCESS_CMD = b"TIGHTEN_OK\r\n"
SLIP_NG_CMD_TEXT = "NG_SLIP"
SLIP_NG_CMD = b"NG_SLIP\r\n"
DEVICE_NG_CMD_TEXT = "NG_DEVICE"
DEVICE_NG_CMD = b"NG_DEVICE\r\n"
JAM_NG_CMD_TEXT = "NG_JAM"
JAM_NG_CMD = b"NG_JAM\r\n"
SEND_RESULT_TO_TCP_DEVICE = False

# =========================
# 目标扭矩：28 N.m
# =========================
TARGET_TORQUE = 28.0
OK_TORQUE_TH = 0.98 * TARGET_TORQUE       # 27.44 N.m
OK_HOLD_TIME = 0.30                       # 达标后保持 0.3s，避免高扭矩区触发驱动器保护
OK_STABLE_BAND = 0.03 * TARGET_TORQUE     # 0.84 N.m，现场信号有波动，放宽一点
DRIVE_OK_STATUS = 0x01                    # 驱动器回传“拧紧成功/OK”的状态值
DRIVE_OK_MIN_TORQUE = 0.90 * TARGET_TORQUE # 驱动器OK时，至少达到25.2Nm才承认成功，防止误判

# =========================
# 滑牙判据
# =========================
SLIP_MIN_PEAK = 0.55 * TARGET_TORQUE      # 峰值超过 15.4Nm 后才允许判滑牙
SLIP_DROP_TH = 0.12 * TARGET_TORQUE       # 从峰值下降超过 3.36Nm
SLIP_CONFIRM_N = 4                        # 连续确认次数
SLIP_MIN_RUNTIME = 1.0                    # 启动后 1s 内不判滑牙

# =========================
# 卡滞判据：V7 高负载卡滞
# 说明：正常拧紧前段低扭矩平台不再判卡滞；
# 真正卡滞更像：高扭矩区、电批基本转不动、扭矩不上升。
# =========================
JAM_ENABLE = True
JAM_AUTO_STOP = True                      # 检测到卡滞后停止、保存、报警；V26不自动回转
JAM_MIN_RUNTIME = 2.0                     # 启动后 2s 内不判卡滞
JAM_TORQUE_TH = 0.75 * TARGET_TORQUE      # 21.0Nm 以上才允许判卡滞
JAM_SPEED_TH = 3                          # 速度绝对值 <= 3 认为接近停转
JAM_WINDOW_TIME = 1.0                     # 看最近 1s
JAM_RISE_MAX = 0.8                        # 最近1s扭矩增长小于0.8Nm，认为不上升
JAM_FLUCT_MAX = 1.2                       # 最近1s波动小于1.2Nm，认为平台
JAM_CONFIRM_N = 3                         # 连续3个周期确认才报警

# 若现场只想采集曲线，可把下面改 False
ENABLE_FAULT_DETECT = True
ENABLE_OK_AUTO_STOP = True
ENABLE_SLIP_DETECT = True

# =========================
# 滤波参数
# =========================
MAX_VALID_TORQUE = 80.0
MAX_STEP_TORQUE = 20.0                    # 单点突跳超过20Nm按毛刺处理
MEDIAN_POINTS = 3                         # 不要太大，避免30Nm末端被滤低
EMA_ALPHA = 0.45                          # 越大响应越快，30Nm终点建议 0.35~0.55
PLOT_ABS_TORQUE = True

# =========================
# 物理开关同步绘图参数（V25）
# =========================
# 如果使用工具本体物理开关启动拧紧，程序不发送正转指令，只保持 TCP 回传监听。
# 一旦检测到 speed/torque 从空闲状态跳变，就自动清空缓存并开始绘制本次曲线。
ENABLE_PHYSICAL_SWITCH_CAPTURE = True
PHYSICAL_START_SPEED_TH = 2.0             # 速度超过该值，认为物理开关已启动
PHYSICAL_START_TORQUE_TH = 0.30           # 扭矩超过该值，认为物理开关已启动
PHYSICAL_IDLE_STATES = ("IDLE", "STOP", "HOME_READY", "MONITOR")

# =========================
# 手动反转回初始位/开口向前参数（V26：异常后默认不自动回转）
# =========================
# 解析后的控制策略：
# 1) 正常拧紧/物理开关拧紧：使用 data_mode=0x01 保持回传，保证能画曲线；
# 2) 异常后回位：发送反转+回传指令 01/02；
# 3) 不再按固定时间直接停止，而是根据回传 speed/torque 判断“反转已释放/已停稳”；
# 4) REVERSE_HOME_MAX_TIME 只是安全上限，不是回位主判据。
AUTO_RETURN_HOME_AFTER_FAULT = False       # V26关键修改：异常后只停止/保存/报警，不自动持续反转；需要回位时手动点 Reverse
REVERSE_HOME_USE_FEEDBACK = True
REVERSE_HOME_MAX_TIME = 4.0               # 安全上限，超过后强制停止并提示
REVERSE_HOME_MIN_RUN_TIME = 0.15          # 至少反转这么久后才允许判到位
REVERSE_HOME_MOTION_SPEED_TH = 2.0        # 看到该速度以上，认为反转动作已发生
REVERSE_HOME_STOP_SPEED_TH = 1.0          # 速度低于该值，认为接近停止
REVERSE_HOME_RELEASE_TORQUE_TH = 1.2      # 扭矩低于该值，认为螺纹/卡涩已释放
REVERSE_HOME_STABLE_DWELL = 0.25          # 低速+低扭矩保持时间，到位确认
REVERSE_HOME_POST_STOP_REPEAT = 3
REVERSE_HOME_POST_STOP_GAP = 0.015
STOP_SAVE_CURRENT_CURVE = True


# =========================
# 缓存
# =========================
time_data = deque(maxlen=20000)
torque_raw_data = deque(maxlen=20000)
torque_data = deque(maxlen=20000)
speed_data = deque(maxlen=20000)
angle_data = deque(maxlen=20000)
status_data = deque(maxlen=20000)

lock = threading.RLock()
sock_lock = threading.Lock()
client_socket = None
command_queue = queue.Queue()
curve_image_lock = threading.Lock()
plot_render_lock = threading.Lock()
latest_curve_jpeg = None
latest_curve_time = None
curve_render_count = 0
curve_render_error = ""

start_time = time.time()
state = "IDLE"
fault_latched = False
slip_counter = 0
jam_counter = 0
jam_warned = False
ok_start_time = None
last_filtered_torque = None
last_valid_torque = None
median_buf = deque(maxlen=MEDIAN_POINTS)
recv_frame_count = 0
last_print_recv_time = 0.0
last_save_time = 0.0
run_id = time.strftime("%Y%m%d_%H%M%S")
save_done = False
stop_requested = threading.Event()
last_feedback = None
last_feedback_time = 0.0
last_saved_csv = ""
last_saved_png = ""

# =========================
# CRC16/MODBUS
# =========================
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


# 协议说明：
# 第6字节 data_mode：0x00=关闭数据发送，0x01=开启数据发送，0x02=锁住电批不能启动。
# 第7字节 run_cmd：0x00=停止工作，0x01=正转启动，0x02=反转启动。
# 为了绘图，正转必须使用 data_mode=0x01；否则设备通常不会连续回传扭矩，界面就没有曲线。
CMD_RESET = build_pc_cmd(0x00, 0x00)               # 复位/停止：55 AA 07 01 00 00 00 ...
CMD_STOP_WITH_FEEDBACK = build_pc_cmd(0x01, 0x00)  # 停止 + 保持回传：55 AA 07 01 00 01 00 ...
CMD_LOCK = build_pc_cmd(0x02, 0x00)                # 锁住电批：55 AA 07 01 00 02 00 ...
CMD_FORWARD = build_pc_cmd(0x01, 0x01)             # 正转 + 开启回传，用于绘制曲线
CMD_FORWARD_NO_FEEDBACK = build_pc_cmd(0x00, 0x01) # 正转但关闭回传，仅用于协议对照测试
CMD_REVERSE = build_pc_cmd(0x00, 0x02)             # 反转关闭回传，用于定量回初始位
CMD_REVERSE_WITH_FEEDBACK = build_pc_cmd(0x01, 0x02)# 反转 + 开启回传，必要时可切换
CMD_REVERSE_NO_FEEDBACK = CMD_REVERSE


def hexstr(data: bytes) -> str:
    return " ".join(f"{b:02X}" for b in data)

# =========================
# TCP 发送
# =========================
def send_cmd(cmd: bytes, name: str = "CMD") -> bool:
    global client_socket
    with sock_lock:
        if client_socket is None:
            print(f"发送失败：TCP 未连接，{name} = {hexstr(cmd)}")
            return False
        try:
            client_socket.sendall(cmd)
            print(f"发送 {name}: {hexstr(cmd)}")
            return True
        except Exception as e:
            print(f"发送失败 {name}: {e}")
            try:
                client_socket.close()
            except Exception:
                pass
            client_socket = None
            return False


def close_current_socket(reason: str = "关闭当前TCP连接"):
    """强制关闭当前连接，让接收线程自动重连。"""
    global client_socket
    with sock_lock:
        s = client_socket
        client_socket = None
    if s is not None:
        try:
            print(reason)
            try:
                s.shutdown(socket.SHUT_RDWR)
            except Exception:
                pass
            s.close()
        except Exception as e:
            print(f"关闭TCP连接异常: {e}")


def send_cmd_new_connection(cmd: bytes, name: str = "CMD-NEW", timeout: float = 1.0) -> bool:
    """
    兜底发送：重新建立一个短连接发送控制帧，然后立即关闭。
    用于排查/处理原连接处于回传状态时停止帧不生效的问题。
    """
    try:
        with socket.create_connection((IP, PORT), timeout=timeout) as s:
            s.sendall(cmd)
            print(f"短连接发送 {name}: {hexstr(cmd)}")
        return True
    except Exception as e:
        print(f"短连接发送失败 {name}: {e}")
        return False


def mark_all_rows_state(result_state: str):
    """保存前把本次曲线所有行的状态统一标成最终结果，便于CSV和PNG追溯。"""
    global status_data
    with lock:
        n = len(time_data)
        status_data.clear()
        status_data.extend([result_state] * n)


def finish_result(result_state: str, message: str, signal_func=None, stop_after=True, return_home=False):
    """统一结束流程：设置状态 -> 输出提示/指令 -> 保存同名CSV和PNG -> 停止；V26默认异常后不自动回转。

    V26 关键修正：
    - 检测到 NG_SLIP / JAM / NG_DEVICE 后，只保存、报警、硬停止；
    - 不再自动启动反转回位线程，避免异常后持续回转；
    - 如需回到开口向前位置，由人工确认后点击 Reverse 手动执行回位。
    """
    global state, fault_latched
    with lock:
        state = result_state
        fault_latched = True
    mark_all_rows_state(result_state)
    print(message)
    if signal_func is not None:
        signal_func()
    saved = save_run_outputs(force=True)
    if saved is None:
        print("⚠️ 没有保存成功：当前没有采集到数据或保存函数返回空")

    if return_home and AUTO_RETURN_HOME_AFTER_FAULT:
        # 注意：不要 close socket；否则下一步反转指令可能因 client_socket=None 发送失败。
        quick_stop_for_home(f"{result_state} 后回初始位前停止")
        stop_requested.clear()
        threading.Thread(
            target=_reverse_to_home,
            args=(f"RETURN_HOME_AFTER_{result_state}", False),
            daemon=True,
        ).start()
    elif stop_after:
        hard_stop_driver(f"{result_state} 后停止", repeat=4, delay=0.035, close_socket_after=True, try_new_connection=True)
        stop_requested.clear()
    return saved


def send_result_signal(text: str, cmd: bytes, name: str):
    """
    输出结果指令。
    默认只打印，不向电批发送协议外数据。
    """
    print(f"{name}: {text}")

    if SEND_RESULT_TO_TCP_DEVICE:
        send_cmd(cmd, name)


def send_success_signal():
    send_result_signal(SUCCESS_CMD_TEXT, SUCCESS_CMD, "✅ 成功指令")


def send_slip_ng_signal():
    send_result_signal(SLIP_NG_CMD_TEXT, SLIP_NG_CMD, "❌ 滑牙NG指令")


def send_device_ng_signal():
    send_result_signal(DEVICE_NG_CMD_TEXT, DEVICE_NG_CMD, "❌ 设备NG指令")


def send_jam_ng_signal():
    send_result_signal(JAM_NG_CMD_TEXT, JAM_NG_CMD, "❌ 卡滞NG指令")


def reset_driver() -> bool:
    return send_cmd(CMD_RESET, "复位/停止")


def hard_stop_driver(
    reason: str = "急停",
    repeat: int = 4,
    delay: float = 0.035,
    close_socket_after: bool = False,
    try_new_connection: bool = False,
) -> bool:
    """
    V22 停止序列：
    1) 01/00：停止并保持回传；
    2) 00/00：协议复位/停止；
    3) 02/00：锁住电批，禁止继续启动；
    4) 00/00：再次复位，解除锁定并回到可启动状态。

    如果终端看到这些发送日志但电批仍不停，说明当前 TCP 协议不支持运行中中断，
    需要使用驱动器专用急停/伺服OFF/使能关闭指令，或接入硬件IO急停。
    """
    stop_requested.set()
    ok = False
    print(f"\n{reason}: V22 停止序列开始，repeat={repeat}, close={close_socket_after}, new_conn={try_new_connection}")

    stop_sequence = [
        (CMD_STOP_WITH_FEEDBACK, "停止-保持回传"),
        (CMD_RESET, "协议复位/停止"),
        (CMD_LOCK, "锁住电批"),
        (CMD_RESET, "解除锁定/复位"),
    ]

    for i in range(max(1, repeat)):
        for cmd, name in stop_sequence:
            ok = send_cmd(cmd, f"{name}#{i + 1}") or ok
            time.sleep(delay)

    if close_socket_after:
        close_current_socket(f"{reason}: 停止序列后强制断开当前TCP连接")
        time.sleep(0.12)

    if try_new_connection:
        for i in range(2):
            for cmd, name in stop_sequence:
                ok = send_cmd_new_connection(cmd, f"短连接{name}#{i + 1}") or ok
                time.sleep(delay)

    print(f"{reason}: V22 停止序列结束")
    return ok


def wait_for_tcp_connected(timeout: float = 3.0) -> bool:
    """等待接收线程建立/恢复 TCP 连接。"""
    deadline = time.time() + timeout
    while time.time() < deadline:
        with sock_lock:
            if client_socket is not None:
                return True
        time.sleep(0.03)
    print("⚠️ 等待TCP连接超时，后续控制帧可能发送失败")
    return False


def send_cmd_burst(commands, name: str = "BURST") -> bool:
    """把多个控制帧尽量连续写入同一 TCP 连接，减少停止延迟。"""
    global client_socket
    data = b"".join(cmd for cmd, _ in commands)
    labels = " + ".join(label for _, label in commands)
    with sock_lock:
        if client_socket is None:
            print(f"突发发送失败：TCP 未连接，{name} = {labels}")
            return False
        try:
            client_socket.sendall(data)
            print(f"突发发送 {name}: {labels}")
            return True
        except Exception as e:
            print(f"突发发送失败 {name}: {e}")
            try:
                client_socket.close()
            except Exception:
                pass
            client_socket = None
            return False


def quick_stop_for_home(reason: str = "回位前快速停止") -> bool:
    """回位流程专用停止：不锁电批、不断开 TCP，避免影响随后的反转回位。"""
    stop_requested.set()
    print(f"\n{reason}: 快速停止开始")
    wait_for_tcp_connected(timeout=2.0)
    ok = False
    # 第一组尽量连续发送，减少实际停止滞后。
    ok = send_cmd_burst(
        [(CMD_STOP_WITH_FEEDBACK, "停止01/00"), (CMD_RESET, "复位00/00")],
        name="快速停止突发",
    ) or ok
    time.sleep(0.03)
    for i in range(2):
        ok = send_cmd(CMD_RESET, f"快速复位#{i + 1}") or ok
        time.sleep(0.025)
    print(f"{reason}: 快速停止结束")
    return ok


def fast_stop_after_home_reverse(reason: str = "回位后快速停止") -> bool:
    """反转定时结束后的快速停止。目标是尽快停住，保证开口方向重复性。"""
    stop_requested.set()
    print(f"\n{reason}: 到时停止")
    ok = False
    for i in range(max(1, REVERSE_HOME_POST_STOP_REPEAT)):
        ok = send_cmd_burst(
            [(CMD_STOP_WITH_FEEDBACK, "停止01/00"), (CMD_RESET, "复位00/00")],
            name=f"回位停止突发#{i + 1}",
        ) or ok
        time.sleep(REVERSE_HOME_POST_STOP_GAP)
    return ok

def clear_run(new_state: str):
    global start_time, state, fault_latched, slip_counter, jam_counter, jam_warned, ok_start_time
    global last_filtered_torque, last_valid_torque, median_buf, run_id, save_done, last_save_time
    with lock:
        time_data.clear()
        torque_raw_data.clear()
        torque_data.clear()
        speed_data.clear()
        angle_data.clear()
        status_data.clear()
        start_time = time.time()
        state = new_state
        fault_latched = False
        slip_counter = 0
        jam_counter = 0
        jam_warned = False
        ok_start_time = None
        run_id = time.strftime("%Y%m%d_%H%M%S")
        save_done = False
        last_save_time = 0.0
        last_filtered_torque = None
        last_valid_torque = None
        median_buf.clear()


def _sleep_interruptible(duration: float, step: float = 0.02) -> bool:
    """可被停止按钮打断的睡眠。返回 True 表示被 stop_requested 中断。"""
    end_time = time.time() + max(0.0, duration)
    while time.time() < end_time:
        if stop_requested.is_set():
            return True
        time.sleep(min(step, max(0.0, end_time - time.time())))
    return stop_requested.is_set()


def clear_pending_commands():
    """停止时清空尚未执行的按钮命令，避免停止后又继续执行反转/正转。"""
    while True:
        try:
            command_queue.get_nowait()
            command_queue.task_done()
        except queue.Empty:
            break


def enqueue_command(cmd_name: str):
    """按钮/快捷键只入队，不直接执行耗时TCP操作，避免Matplotlib界面假死。"""
    try:
        command_queue.put_nowait(cmd_name)
        print(f"按钮指令已接收: {cmd_name}")
    except Exception as e:
        print(f"按钮指令入队失败 {cmd_name}: {e}")


def start_forward(event=None):
    enqueue_command("FORWARD")


def start_reverse(event=None):
    # V12.2：反转按钮只用于回初始位，不再持续反转。
    enqueue_command("REVERSE_HOME")


def stop_driver(event=None):
    # V19：停止按钮立即置位，并在后台连续发送两种停止帧。
    stop_requested.set()
    clear_pending_commands()
    threading.Thread(target=_do_stop_driver, args=("手动立即停止",), daemon=True).start()


def _do_start_forward():
    stop_requested.clear()
    print("\n准备正转：先硬停止/复位，再启动")
    hard_stop_driver("正转前复位", repeat=2, delay=0.03, close_socket_after=False, try_new_connection=False)
    stop_requested.clear()
    if _sleep_interruptible(0.20):
        hard_stop_driver("正转启动被中断", repeat=4, delay=0.035, close_socket_after=True, try_new_connection=True)
        return
    clear_run("RUN_FORWARD")
    send_cmd(CMD_FORWARD, "正转")


def _reverse_to_home(reason: str = "RETURN_HOME", save_after: bool = False):
    """V25 反馈式反转回初始位。

    不再用固定时间作为回位主判据，而是：
    1. 先执行有效停止；
    2. 发送反转+回传 01/02；
    3. 监听回传 speed/torque：看到反转动作发生后，等待低速+低扭矩稳定；
    4. 到位后执行 V22 停止组合。

    如果超过 REVERSE_HOME_MAX_TIME 仍未满足到位条件，会安全停止并提示，
    这说明还需要解析设备的真实角度/原点状态或调整释放阈值，而不是继续按时间硬调。
    """
    global state, fault_latched

    print(f"\n开始反馈式反转回初始位：{reason}")
    quick_stop_for_home("反转回初始位前")
    stop_requested.clear()
    wait_for_tcp_connected(timeout=3.0)

    with lock:
        state = reason
        fault_latched = True

    # 每次启动电批，先发一次复位指令。
    send_cmd(CMD_RESET, "回位前复位")
    time.sleep(0.05)

    # 反馈闭环回位必须使用 01/02，保证反转过程中持续收到 speed/torque。
    if not send_cmd(CMD_REVERSE_WITH_FEEDBACK, "反馈反转回初始位 01/02"):
        print("⚠️ 反馈反转发送失败，执行停止")
        fast_stop_after_home_reverse("反转发送失败后停止")
        return

    t0 = time.time()
    seen_motion = False
    stable_since = None
    last_report = 0.0
    finish_reason = "MAX_TIME"

    while not stop_requested.is_set():
        now = time.time()
        elapsed = now - t0
        if elapsed > REVERSE_HOME_MAX_TIME:
            finish_reason = "MAX_TIME"
            break

        with lock:
            fb = dict(last_feedback) if last_feedback is not None else None
            fb_time = last_feedback_time

        if fb is None or now - fb_time > 0.6:
            if now - last_report > 0.5:
                print("等待反转回传数据...")
                last_report = now
            time.sleep(0.02)
            continue

        speed = abs(float(fb.get("speed", 0.0)))
        torque = abs(float(fb.get("filtered_torque", fb.get("torque", 0.0))))
        status = int(fb.get("tighten_status", 0))

        if speed >= REVERSE_HOME_MOTION_SPEED_TH:
            seen_motion = True

        # 到位/释放判据：已经发生反转动作，并且低速+低扭矩稳定一段时间。
        released = (
            seen_motion
            and elapsed >= REVERSE_HOME_MIN_RUN_TIME
            and speed <= REVERSE_HOME_STOP_SPEED_TH
            and torque <= REVERSE_HOME_RELEASE_TORQUE_TH
        )

        # 有些驱动器回位结束会给 OK 状态；该条件只作为辅助，不单独依赖。
        if released or (seen_motion and elapsed >= REVERSE_HOME_MIN_RUN_TIME and status == DRIVE_OK_STATUS and speed <= REVERSE_HOME_STOP_SPEED_TH):
            if stable_since is None:
                stable_since = now
            if now - stable_since >= REVERSE_HOME_STABLE_DWELL:
                finish_reason = "FEEDBACK_RELEASED"
                break
        else:
            stable_since = None

        if now - last_report > 0.5:
            print(f"回位监控: t={elapsed:.2f}s, speed={speed:.1f}, torque={torque:.2f}, status={status}, seen_motion={seen_motion}")
            last_report = now
        time.sleep(0.02)

    fast_stop_after_home_reverse(f"反转回初始位结束({finish_reason})")

    with lock:
        state = "STOP" if stop_requested.is_set() else "HOME_READY"

    if save_after:
        save_run_outputs(force=True)

    if stop_requested.is_set():
        print("反转回初始位已被手动停止")
    elif finish_reason == "FEEDBACK_RELEASED":
        print("✅ 反转回初始位完成：依据 speed/torque 反馈判定已释放/停稳")
    else:
        print("⚠️ 反转回初始位达到安全上限才停止；需要继续解析角度/状态位或调整释放阈值")

def _do_start_reverse():
    # 兼容旧函数名：现在只执行定量反转回初始位。
    _reverse_to_home("MANUAL_REVERSE_HOME", save_after=False)


def _do_stop_driver(source: str = "停止"):
    global state
    stop_requested.set()
    clear_pending_commands()
    print(f"\n{source}：立即硬停止")
    hard_stop_driver(source, repeat=5, delay=0.035, close_socket_after=True, try_new_connection=True)
    with lock:
        if state not in ("OK", "NG_SLIP", "JAM", "NG_DEVICE"):
            state = "STOP"
    if STOP_SAVE_CURRENT_CURVE:
        save_run_outputs(force=True)
    print("已发送硬停止")


def command_worker():
    """后台执行按钮命令，防止按钮点击后因TCP发送/保存阻塞界面。"""
    while True:
        cmd_name = command_queue.get()
        try:
            if cmd_name == "FORWARD":
                _do_start_forward()
            elif cmd_name == "REVERSE_HOME":
                _do_start_reverse()
            elif cmd_name == "STOP":
                _do_stop_driver()
            else:
                print(f"未知按钮指令: {cmd_name}")
        except Exception as e:
            print(f"执行按钮指令失败 {cmd_name}: {e}")
        finally:
            command_queue.task_done()

# =========================
# 回传帧解析
# =========================
def u16_le(buf: bytes, idx: int) -> int:
    return buf[idx] | (buf[idx + 1] << 8)


def parse_feedback_frame(frame: bytes):
    if len(frame) < 41:
        return None
    if frame[0] != 0x55 or frame[1] != 0xAA or frame[3] != 0x81:
        return None

    recv_crc = frame[-4] | (frame[-3] << 8)
    calc_crc = crc16_modbus(frame[:-4])
    if recv_crc != calc_crc:
        print(f"CRC异常: recv={recv_crc:04X}, calc={calc_crc:04X}, frame={hexstr(frame)}")
        return None

    torque_unit = frame[6]
    torque_raw = u16_le(frame, 7)
    speed_raw = u16_le(frame, 9)
    lock_angle_raw = u16_le(frame, 11)
    tighten_angle_raw = u16_le(frame, 13)
    work_time_ms = u16_le(frame, 15)
    direction = frame[17]
    tighten_status = frame[21]
    error_code = frame[22]

    if torque_unit == 0:
        torque_nm = torque_raw / 100.0 * 0.0980665
    else:
        torque_nm = torque_raw / 1000.0

    if direction == 0x01:
        torque_nm = -torque_nm
        speed_raw = -speed_raw
        lock_angle_raw = -lock_angle_raw
        tighten_angle_raw = -tighten_angle_raw

    return {
        "torque": torque_nm,
        "speed": speed_raw,
        "lock_angle": lock_angle_raw,
        "tighten_angle": tighten_angle_raw,
        "work_time_ms": work_time_ms,
        "direction": direction,
        "tighten_status": tighten_status,
        "error_code": error_code,
        "torque_unit": torque_unit,
        "torque_raw_count": torque_raw,
    }


def feed_receive_buffer(buffer: bytearray):
    frames = []
    while True:
        start = buffer.find(b"\x55\xAA")
        if start < 0:
            buffer.clear()
            break
        if start > 0:
            del buffer[:start]
        if len(buffer) < 3:
            break
        length = buffer[2]
        total_len = 2 + length + 2
        if len(buffer) < total_len:
            break
        frame = bytes(buffer[:total_len])
        del buffer[:total_len]
        if frame.endswith(b"\x0D\x0A"):
            frames.append(frame)
    return frames

# =========================
# 滤波
# =========================
def filter_torque(raw_torque: float):
    global last_filtered_torque, last_valid_torque

    y = abs(raw_torque) if PLOT_ABS_TORQUE else raw_torque

    if not np.isfinite(y) or abs(y) > MAX_VALID_TORQUE:
        print(f"丢弃异常扭矩点: raw={raw_torque:.3f} Nm")
        return None

    # 突跳保护：允许从低扭矩快速上升到高扭矩，但不允许单帧回落/跳变把滤波拉坏
    if last_valid_torque is not None and abs(y - last_valid_torque) > MAX_STEP_TORQUE:
        print(f"疑似毛刺，使用上一有效值: raw={y:.2f}, last={last_valid_torque:.2f}")
        y = last_valid_torque
    else:
        last_valid_torque = y

    median_buf.append(y)
    y_med = float(np.median(list(median_buf)))

    if last_filtered_torque is None:
        last_filtered_torque = y_med
    else:
        last_filtered_torque = EMA_ALPHA * y_med + (1.0 - EMA_ALPHA) * last_filtered_torque

    return last_filtered_torque


def update_last_feedback(parsed, filtered_torque):
    """保存最近一帧回传，供反馈式回位闭环使用。"""
    global last_feedback, last_feedback_time
    with lock:
        last_feedback = dict(parsed)
        last_feedback["filtered_torque"] = float(filtered_torque)
        last_feedback["abs_torque"] = abs(float(parsed.get("torque", 0.0)))
        last_feedback["abs_speed"] = abs(float(parsed.get("speed", 0.0)))
        last_feedback_time = time.time()


def should_begin_physical_run(parsed):
    """检测工具物理开关启动：当前处于空闲/监控状态，且速度或扭矩出现明显变化。"""
    if not ENABLE_PHYSICAL_SWITCH_CAPTURE:
        return False
    with lock:
        current_state = state
    if current_state not in PHYSICAL_IDLE_STATES:
        return False
    speed = abs(float(parsed.get("speed", 0.0)))
    torque = abs(float(parsed.get("torque", 0.0)))
    return speed >= PHYSICAL_START_SPEED_TH or torque >= PHYSICAL_START_TORQUE_TH


def begin_physical_run(parsed):
    """物理开关启动后自动开始一条新曲线。"""
    print("\n检测到工具物理开关启动：开始同步绘制本次拧紧曲线")
    clear_run("RUN_PHYSICAL")


def enable_monitor_feedback(reason="进入监控回传模式"):
    """空闲时发送 01/00，保持数据回传，便于物理开关启动时同步绘图。"""
    ok = send_cmd(CMD_STOP_WITH_FEEDBACK, reason)
    with lock:
        if state in ("IDLE", "STOP", "HOME_READY"):
            # 不清空数据，只把界面状态设成监控。
            # 真正启动时 begin_physical_run() 会清空缓存并重新计时。
            globals()["state"] = "MONITOR"
    return ok

# =========================
# 时间-扭矩 FSM 判定
# =========================
def detect_state(parsed):
    global state, fault_latched, slip_counter, jam_counter, jam_warned, ok_start_time

    if fault_latched or not ENABLE_FAULT_DETECT:
        return

    now = time.time()
    with lock:
        current_state = state
        if current_state not in ("RUN_FORWARD", "RUN_REVERSE", "RUN_PHYSICAL", "JAM_WARN"):
            return
        if len(torque_data) < 15:
            return
        t_arr = np.array(list(time_data), dtype=float)
        y_arr = np.array(list(torque_data), dtype=float)
        y_raw_arr = np.array([abs(v) for v in torque_raw_data], dtype=float)
        speed_arr = np.array(list(speed_data), dtype=float)
        run_time = now - start_time

    torque_now = float(y_arr[-1])
    raw_now = float(y_raw_arr[-1])
    peak = float(np.max(y_arr))

    # 1) 驱动器自身 OK/NG 状态：优先处理
    #    有些电批在达到内部目标后会直接给 OK 状态，不一定能满足上位机0.5s稳定保持。
    if parsed.get("tighten_status") == DRIVE_OK_STATUS and torque_now >= DRIVE_OK_MIN_TORQUE:
        finish_result(
            "OK",
            f"\n✅ 驱动器返回OK，拧紧成功：torque={torque_now:.2f} Nm",
            send_success_signal,
            stop_after=True,
        )
        return

    if parsed.get("tighten_status") == 0x02:
        finish_result(
            "NG_DEVICE",
            f"\n⚠️ 驱动器返回 NG: error_code={parsed.get('error_code')}",
            send_device_ng_signal,
            stop_after=True,
            return_home=False,
        )
        return

    # 2) 拧紧完成：达到 98% 目标扭矩，并在 0.5s 内稳定
    if ENABLE_OK_AUTO_STOP and torque_now >= OK_TORQUE_TH:
        recent_mask = t_arr >= (t_arr[-1] - OK_HOLD_TIME)
        y_recent = y_arr[recent_mask]
        if len(y_recent) >= 5 and (float(np.max(y_recent)) - float(np.min(y_recent))) <= OK_STABLE_BAND:
            finish_result(
                "OK",
                f"\n✅✅✅ 拧紧成功 OK：torque={torque_now:.2f} Nm，稳定保持 {OK_HOLD_TIME:.1f}s ✅✅✅",
                send_success_signal,
                stop_after=True,
            )
            return
        ok_start_time = now if ok_start_time is None else ok_start_time
    else:
        ok_start_time = None

    # 3) 滑牙：达到一定峰值后，扭矩明显回落，且工具仍有速度
    if ENABLE_SLIP_DETECT and run_time >= SLIP_MIN_RUNTIME and peak >= SLIP_MIN_PEAK and len(y_arr) >= 30:
        n = 10
        recent_speed = abs(float(np.mean(speed_arr[-n:])))
        peak_drop = peak - torque_now
        curr_mean = float(np.mean(y_arr[-n:]))
        prev_mean = float(np.mean(y_arr[-2*n:-n]))
        window_drop = prev_mean - curr_mean

        if peak_drop >= SLIP_DROP_TH and window_drop > 0.8 and recent_speed > 2:
            slip_counter += 1
        else:
            slip_counter = 0

        if slip_counter >= SLIP_CONFIRM_N:
            finish_result(
                "NG_SLIP",
                (
                    f"\n⚠️⚠️⚠️ 检测到滑牙 NG_SLIP ⚠️⚠️⚠️\n"
                    f"峰值扭矩={peak:.2f} Nm，当前扭矩={torque_now:.2f} Nm，回落={peak_drop:.2f} Nm\n"
                    "处理动作：保存CSV和曲线 → 输出NG_SLIP → 立即停止；不自动反转，需人工确认后手动回位"
                ),
                send_slip_ng_signal,
                stop_after=True,
                return_home=False,
            )
            return

    # 4) 高负载卡滞：高扭矩 + 低速度 + 最近1s几乎不上升
    # 注意：默认只报警不停止，防止误停。若现场验证准确后可把 JAM_AUTO_STOP=True。
    if JAM_ENABLE and run_time >= JAM_MIN_RUNTIME and torque_now >= JAM_TORQUE_TH:
        recent_mask = t_arr >= (t_arr[-1] - JAM_WINDOW_TIME)
        y_recent = y_arr[recent_mask]
        speed_recent = speed_arr[recent_mask]
        if len(y_recent) >= 8:
            rise = float(y_recent[-1] - y_recent[0])
            fluct = float(np.max(y_recent) - np.min(y_recent))
            mean_speed = abs(float(np.mean(speed_recent)))

            if mean_speed <= JAM_SPEED_TH and rise <= JAM_RISE_MAX and fluct <= JAM_FLUCT_MAX and torque_now < OK_TORQUE_TH:
                jam_counter += 1
            else:
                jam_counter = 0

            if jam_counter >= JAM_CONFIRM_N:
                if JAM_AUTO_STOP:
                    finish_result(
                        "JAM",
                        f"\n⚠️ 卡滞检测：torque={torque_now:.2f} Nm，speed={mean_speed:.1f}，1s上升={rise:.2f} Nm",
                        send_jam_ng_signal,
                        stop_after=True,
                        return_home=False,
                    )
                    return
                else:
                    if not jam_warned:
                        with lock:
                            state = "JAM_WARN"
                        jam_warned = True
                        print(f"\n⚠️ 卡滞预警但不停止：torque={torque_now:.2f} Nm，speed={mean_speed:.1f}，1s上升={rise:.2f} Nm")
        else:
            jam_counter = 0

# =========================
# 数据与曲线保存
# =========================
def _safe_state_name(name: str) -> str:
    return "".join(ch if ch.isalnum() or ch in ("_", "-") else "_" for ch in name)


def save_run_outputs(force: bool = False):
    """
    保存本次拧紧的 CSV 和 PNG。
    关键点：
    1. 在同一个 lock 里一次性拷贝数据快照；
    2. CSV 和 PNG 使用同一个 base_name；
    3. 默认每次拧紧只保存一次，避免 OK 后复位/停止重复保存。
    """
    global save_done, last_save_time, last_saved_csv, last_saved_png

    try:
        with lock:
            if len(time_data) == 0:
                return None
            if save_done and not force:
                return None

            x = list(time_data)
            raw = [abs(v) for v in torque_raw_data]
            filt = list(torque_data)
            speed = list(speed_data)
            angle = list(angle_data)
            states = list(status_data)
            current_state = _safe_state_name(state)
            current_run_id = run_id
            save_done = True
            last_save_time = time.time()

        day_dir = BASE_SAVE_DIR / time.strftime("%Y%m%d")
        day_dir.mkdir(parents=True, exist_ok=True)

        base_name = f"{current_run_id}_{current_state}"
        csv_path = day_dir / f"{base_name}.csv"
        png_path = day_dir / f"{base_name}.png"

        # 1) 保存 CSV
        with open(csv_path, "w", newline="", encoding="utf-8-sig") as f:
            w = csv.writer(f)
            w.writerow(["time_s", "raw_torque_Nm", "filtered_torque_Nm", "speed", "angle", "state"])
            w.writerows(zip(x, raw, filt, speed, angle, states))

        # 2) 用同一份数据快照生成 PNG，确保曲线和 CSV 一一对应
        with plot_render_lock:
            fig_save, ax_save = plt.subplots(figsize=(9, 5))
            ax_save.plot(x, filt, lw=2, label="滤波力矩")
            ax_save.plot(x, raw, lw=1, alpha=0.35, label="原始力矩")
            ax_save.axhline(
                OK_TORQUE_TH,
                linestyle="--",
                linewidth=1,
                label=f"合格阈值 {OK_TORQUE_TH:.1f} N·m",
            )
            ax_save.set_title(
                "时间—力矩曲线"
                f"｜状态：{state_display_name(current_state)}"
                f"｜目标力矩：{TARGET_TORQUE:.0f} N·m",
                fontproperties=CHINESE_FONT,
            )
            ax_save.set_xlabel("时间（秒）", fontproperties=CHINESE_FONT)
            ax_save.set_ylabel("力矩（N·m）", fontproperties=CHINESE_FONT)
            ax_save.set_ylim(0, 36)
            ax_save.grid(True)
            ax_save.legend(
                loc="upper left",
                prop=CHINESE_FONT,
            )
            fig_save.tight_layout()
            fig_save.savefig(png_path, dpi=200)
            plt.close(fig_save)

        print(f"数据和曲线已保存到同一文件夹：{day_dir}")
        print(f"CSV: {csv_path}")
        print(f"PNG: {png_path}")
        last_saved_csv = str(csv_path)
        last_saved_png = str(png_path)
        return csv_path, png_path

    except Exception as e:
        print(f"保存数据/曲线失败: {e}")
        return None


# 兼容旧调用名称
def save_csv():
    return save_run_outputs()

# =========================
# TCP 接收线程
# =========================
def tcp_receive():
    global client_socket, recv_frame_count, last_print_recv_time

    recv_buffer = bytearray()
    while True:
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            s.settimeout(CONNECT_TIMEOUT)
            s.connect((IP, PORT))
            s.settimeout(None)
            with sock_lock:
                client_socket = s

            print(f"TCP连接成功：{IP}:{PORT}")
            reset_driver()
            time.sleep(0.05)
            if ENABLE_PHYSICAL_SWITCH_CAPTURE:
                enable_monitor_feedback("空闲监控/保持回传 01/00")

            while True:
                data = s.recv(1024)
                if not data:
                    raise ConnectionError("连接断开")
                recv_buffer.extend(data)
                frames = feed_receive_buffer(recv_buffer)

                for frame in frames:
                    parsed = parse_feedback_frame(frame)
                    if parsed is None:
                        continue

                    # 如果使用工具本体物理开关启动，程序在第一帧运动数据到来时自动开始新曲线。
                    if should_begin_physical_run(parsed):
                        begin_physical_run(parsed)

                    raw_torque = parsed["torque"]
                    filtered_torque = filter_torque(raw_torque)
                    if filtered_torque is None:
                        continue
                    update_last_feedback(parsed, filtered_torque)

                    recv_frame_count += 1
                    t = time.time() - start_time

                    now = time.time()
                    if now - last_print_recv_time > 1.0:
                        print(
                            f"收到数据#{recv_frame_count}: t={t:.3f}s, "
                            f"raw={raw_torque:.2f} Nm, filt={filtered_torque:.2f} Nm, "
                            f"speed={parsed['speed']}, angle={parsed['tighten_angle']}, "
                            f"status={parsed['tighten_status']}, err={parsed['error_code']}, "
                            f"unit={parsed['torque_unit']}, raw_count={parsed['torque_raw_count']}"
                        )
                        last_print_recv_time = now

                    with lock:
                        time_data.append(t)
                        torque_raw_data.append(raw_torque)
                        torque_data.append(filtered_torque)
                        speed_data.append(parsed["speed"])
                        angle_data.append(parsed["tighten_angle"])
                        status_data.append(state)

                    detect_state(parsed)

        except Exception as e:
            print("TCP重连:", e)
            with sock_lock:
                try:
                    if client_socket is not None:
                        client_socket.close()
                except Exception:
                    pass
                client_socket = None
            time.sleep(RECONNECT_DELAY)


# =========================
# Unity 内部控制口
# =========================
def _latest_or_none(values):
    return None if len(values) == 0 else values[-1]


def unity_status(ok: bool = True, message: str = "status"):
    with lock:
        current_state = state
        latest_raw = _latest_or_none(torque_raw_data)
        latest_torque = _latest_or_none(torque_data)
        latest_speed = _latest_or_none(speed_data)
        latest_angle = _latest_or_none(angle_data)
        samples = len(time_data)
        current_run_id = run_id
        saved_csv = last_saved_csv
        saved_png = last_saved_png
        feedback_age = None if last_feedback_time <= 0 else round(time.time() - last_feedback_time, 3)

    with sock_lock:
        connected = client_socket is not None

    return {
        "ok": ok,
        "message": message,
        "tool_connected": connected,
        "state": current_state,
        "run_id": current_run_id,
        "samples": samples,
        "raw_torque": latest_raw,
        "filtered_torque": latest_torque,
        "speed": latest_speed,
        "angle": latest_angle,
        "feedback_age": feedback_age,
        "queue_size": command_queue.qsize(),
        "csv": saved_csv,
        "png": saved_png,
    }


def run_unity_command(cmd: str):
    normalized = (cmd or "").strip().lower()

    if normalized == "connect":
        ok = reset_driver()
        if ENABLE_PHYSICAL_SWITCH_CAPTURE and ok:
            enable_monitor_feedback("Unity连接后进入监控回传模式")
        return unity_status(ok, "connected" if ok else "connect_failed")

    if normalized == "forward":
        enqueue_command("FORWARD")
        return unity_status(True, "forward_queued")

    if normalized == "reverse":
        enqueue_command("REVERSE_HOME")
        return unity_status(True, "reverse_home_queued")

    if normalized in ("stop", "reset"):
        stop_driver()
        return unity_status(True, "stop_requested")

    if normalized in ("status", ""):
        return unity_status(True, "status")

    return unity_status(False, f"unknown_cmd:{cmd}")


def handle_unity_client(conn: socket.socket, addr):
    with conn:
        file = conn.makefile("rwb")
        for raw in file:
            try:
                request = json.loads(raw.decode("utf-8"))
                response = run_unity_command(request.get("cmd", ""))
            except Exception as e:
                response = unity_status(False, f"bridge_error:{e}")

            file.write((json.dumps(response, ensure_ascii=False) + "\n").encode("utf-8"))
            file.flush()


def unity_command_server():
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((UNITY_BRIDGE_HOST, UNITY_BRIDGE_PORT))
    server.listen(5)
    print(f"Unity控制口已启动：{UNITY_BRIDGE_HOST}:{UNITY_BRIDGE_PORT}")

    while True:
        conn, addr = server.accept()
        threading.Thread(target=handle_unity_client, args=(conn, addr), daemon=True).start()


# =========================
# Unity 实时力矩曲线图
# =========================
def curve_render_worker():
    """在独立线程中绘制 Unity 预览图，不改变最终 CSV/PNG 保存逻辑。"""
    global latest_curve_jpeg, latest_curve_time
    global curve_render_count, curve_render_error

    fig_live = Figure(figsize=(9.6, 5.4), dpi=100)
    canvas_live = FigureCanvasAgg(fig_live)
    ax_live = fig_live.add_subplot(111)
    filtered_line, = ax_live.plot(
        [],
        [],
        lw=2,
        color="#36a9ff",
        label="滤波力矩",
    )
    raw_curve_line, = ax_live.plot(
        [],
        [],
        lw=1,
        alpha=0.45,
        color="#aab7c4",
        label="原始力矩",
    )
    ax_live.axhline(
        OK_TORQUE_TH,
        color="#ffb020",
        linestyle="--",
        linewidth=1,
        label=f"合格阈值 {OK_TORQUE_TH:.1f} N·m",
    )
    fig_live.patch.set_facecolor("#071426")
    ax_live.set_facecolor("#0b1b30")
    ax_live.set_xlabel(
        "时间（秒）",
        color="white",
        fontsize=11,
        fontproperties=CHINESE_FONT,
    )
    ax_live.set_ylabel(
        "力矩（N·m）",
        color="white",
        fontsize=11,
        fontproperties=CHINESE_FONT,
    )
    ax_live.set_ylim(0, 36)
    ax_live.grid(True, color="#2b4665", alpha=0.55)
    ax_live.tick_params(colors="white", labelsize=9)
    for spine in ax_live.spines.values():
        spine.set_color("#4a6b8f")
    legend = ax_live.legend(
        loc="upper left",
        prop=CHINESE_FONT,
        fontsize=9,
        facecolor="#10243d",
        edgecolor="#4a6b8f",
    )
    for text_item in legend.get_texts():
        text_item.set_color("white")
    fig_live.subplots_adjust(
        left=0.12,
        right=0.98,
        bottom=0.18,
        top=0.86,
    )

    while True:
        try:
            with lock:
                current_state = state
                x = list(time_data)
                y = list(torque_data)
                y_raw = [abs(v) for v in torque_raw_data]

            with plot_render_lock:
                filtered_line.set_data(x, y)
                raw_curve_line.set_data(x, y_raw)
                if len(x) > 1:
                    ax_live.set_xlim(
                        max(0, x[-1] - 8),
                        max(8, x[-1] + 0.2),
                    )
                else:
                    ax_live.set_xlim(0, 8)

                ax_live.set_title(
                    "时间—力矩曲线"
                    f"｜状态：{state_display_name(current_state)}"
                    f"｜目标力矩：{TARGET_TORQUE:.0f} N·m",
                    color="white",
                    fontsize=13,
                    fontproperties=CHINESE_FONT,
                )

                output = io.BytesIO()
                canvas_live.print_jpg(
                    output,
                    pil_kwargs={"quality": CURVE_JPEG_QUALITY},
                )
                image_bytes = output.getvalue()

            with curve_image_lock:
                latest_curve_jpeg = image_bytes
                latest_curve_time = time.time()
                curve_render_count += 1
                curve_render_error = ""
        except Exception as exc:
            with curve_image_lock:
                curve_render_error = str(exc)
            print(f"Unity实时力矩曲线绘制失败: {exc}")

        time.sleep(CURVE_RENDER_INTERVAL)


class CurveImageHandler(BaseHTTPRequestHandler):
    server_version = "ServoTorqueCurveServer/1.0"

    def do_GET(self):
        if self.path in ("/", "/curve.jpg", "/latest.jpg"):
            self._send_curve()
            return
        if self.path == "/status":
            self._send_status()
            return
        self.send_error(404, "Not found")

    def _send_curve(self):
        with curve_image_lock:
            image = latest_curve_jpeg
            rendered_at = latest_curve_time

        if image is None:
            self.send_error(503, "Curve image is not ready")
            return

        self.send_response(200)
        self.send_header("Content-Type", "image/jpeg")
        self.send_header("Content-Length", str(len(image)))
        self.send_header("Cache-Control", "no-store")
        self.send_header(
            "X-Curve-Rendered-At",
            "" if rendered_at is None else f"{rendered_at:.3f}",
        )
        self.end_headers()
        self.wfile.write(image)

    def _send_status(self):
        with curve_image_lock:
            payload = {
                "ok": latest_curve_jpeg is not None and not curve_render_error,
                "render_count": curve_render_count,
                "last_render_time": latest_curve_time,
                "last_error": curve_render_error,
            }
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, _format, *_args):
        return


def curve_http_server():
    try:
        server = ThreadingHTTPServer(
            (CURVE_HTTP_HOST, CURVE_HTTP_PORT),
            CurveImageHandler,
        )
        server.daemon_threads = True
        print(
            "Unity力矩曲线接口已启动："
            f"http://{CURVE_HTTP_HOST}:{CURVE_HTTP_PORT}/curve.jpg"
        )
        server.serve_forever()
    except Exception as exc:
        print(f"Unity力矩曲线接口启动失败: {exc}")


# =========================
# 绘图与按钮
# =========================
fig, ax = plt.subplots()
plt.subplots_adjust(bottom=0.22)
line, = ax.plot([], [], lw=2, label="滤波力矩")
raw_line, = ax.plot([], [], lw=1, alpha=0.35, label="原始力矩")
ax.legend(loc="upper left", prop=CHINESE_FONT)

ax.set_title(
    "时间—力矩曲线｜28 N·m｜V26",
    fontproperties=CHINESE_FONT,
)
ax.set_xlabel("时间（秒）", fontproperties=CHINESE_FONT)
ax.set_ylabel("力矩（N·m）", fontproperties=CHINESE_FONT)
ax.grid(True)
ax.set_ylim(0, 36)

ax_forward = plt.axes([0.12, 0.05, 0.18, 0.075])
ax_reverse = plt.axes([0.41, 0.05, 0.18, 0.075])
ax_stop = plt.axes([0.70, 0.05, 0.18, 0.075])

btn_forward = Button(ax_forward, "正转")
btn_reverse = Button(ax_reverse, "反转回位")
btn_stop = Button(ax_stop, "复位/停止")
if CHINESE_FONT is not None:
    btn_forward.label.set_fontproperties(CHINESE_FONT)
    btn_reverse.label.set_fontproperties(CHINESE_FONT)
    btn_stop.label.set_fontproperties(CHINESE_FONT)

btn_forward.on_clicked(start_forward)
btn_reverse.on_clicked(start_reverse)
btn_stop.on_clicked(stop_driver)


def on_key_press(event):
    """键盘备用控制：f正转，r反转，s停止。"""
    if event.key in ("f", "F"):
        start_forward()
    elif event.key in ("r", "R"):
        start_reverse()
    elif event.key in ("s", "S", "escape"):
        stop_driver()


fig.canvas.mpl_connect("key_press_event", on_key_press)


def on_mouse_click(event):
    """鼠标点击兜底：如果 Button.on_clicked 在当前后端失效，仍按点击区域触发。"""
    if event.button != 1:
        return
    if event.inaxes is ax_forward:
        print("鼠标兜底触发: Forward")
        start_forward()
    elif event.inaxes is ax_reverse:
        print("鼠标兜底触发: Reverse")
        start_reverse()
    elif event.inaxes is ax_stop:
        print("鼠标兜底触发: Stop")
        stop_driver()


fig.canvas.mpl_connect("button_press_event", on_mouse_click)


def terminal_keyboard_worker():
    """
    终端物理按键备用控制。
    Windows：终端窗口中直接按 f/r/s。
    Linux/macOS：终端窗口中直接按 f/r/s；若标准输入不是TTY则自动退出。
    """
    if not sys.stdin or not sys.stdin.isatty():
        return

    print("终端快捷键已启用：f=正转，r=定量反转，s=硬停止，q=退出快捷键监听")

    if msvcrt is not None:
        while True:
            try:
                if msvcrt.kbhit():
                    ch = msvcrt.getch().decode(errors="ignore").lower()
                    if ch == "f":
                        start_forward()
                    elif ch == "r":
                        start_reverse()
                    elif ch == "s":
                        stop_driver()
                    elif ch == "q":
                        return
                time.sleep(0.05)
            except Exception as e:
                print(f"终端快捷键监听退出: {e}")
                return
    else:
        if termios is None or tty is None or select is None:
            return
        fd = sys.stdin.fileno()
        try:
            old_settings = termios.tcgetattr(fd)
            tty.setcbreak(fd)
            while True:
                rlist, _, _ = select.select([sys.stdin], [], [], 0.05)
                if rlist:
                    ch = sys.stdin.read(1).lower()
                    if ch == "f":
                        start_forward()
                    elif ch == "r":
                        start_reverse()
                    elif ch == "s":
                        stop_driver()
                    elif ch == "q":
                        return
        except Exception as e:
            print(f"终端快捷键监听退出: {e}")
        finally:
            try:
                termios.tcsetattr(fd, termios.TCSADRAIN, old_settings)
            except Exception:
                pass


def update(frame):
    with lock:
        current_state = state
        if len(time_data) == 0:
            ax.set_title(
                f"时间—力矩曲线｜状态：{state_display_name(current_state)}",
                fontproperties=CHINESE_FONT,
            )
            return line, raw_line
        x = list(time_data)
        y = list(torque_data)
        y_raw = [abs(v) for v in torque_raw_data]

    line.set_data(x, y)
    raw_line.set_data(x, y_raw)
    if len(x) > 1:
        ax.set_xlim(max(0, x[-1] - 8), max(8, x[-1] + 0.2))
    ax.set_title(
        "时间—力矩曲线"
        f"｜状态：{state_display_name(current_state)}"
        f"｜目标力矩：{TARGET_TORQUE:.0f} N·m",
        fontproperties=CHINESE_FONT,
    )
    return line, raw_line


ani = animation.FuncAnimation(fig, update, interval=80, cache_frame_data=False)

print("\n命令说明：")
print(f"复位/停止:       {hexstr(CMD_RESET)}")
print(f"停止-带回传:     {hexstr(CMD_STOP_WITH_FEEDBACK)}")
print(f"锁住电批:       {hexstr(CMD_LOCK)}")
print(f"正转-带回传:     {hexstr(CMD_FORWARD)}")
print(f"正转-协议00:     {hexstr(CMD_FORWARD_NO_FEEDBACK)}")
print(f"反转回位-协议00:     {hexstr(CMD_REVERSE)}")
print(f"反转-无回传:     {hexstr(CMD_REVERSE_NO_FEEDBACK)}")
print("\nV26_28Nm 物理开关同步绘图/异常后只停止版判据：")
print(f"拧紧完成: torque >= {OK_TORQUE_TH:.2f} Nm 且 {OK_HOLD_TIME:.1f}s 内波动 <= {OK_STABLE_BAND:.2f} Nm")
print(f"滑牙: peak >= {SLIP_MIN_PEAK:.2f} Nm，峰值回落 >= {SLIP_DROP_TH:.2f} Nm，连续 {SLIP_CONFIRM_N} 次")
print(f"卡滞: torque >= {JAM_TORQUE_TH:.2f} Nm，speed <= {JAM_SPEED_TH}，{JAM_WINDOW_TIME:.1f}s 上升 <= {JAM_RISE_MAX:.2f} Nm")
print(f"卡滞自动停机: {JAM_AUTO_STOP}")
print(f"成功指令: {SUCCESS_CMD_TEXT}，滑牙NG指令: {SLIP_NG_CMD_TEXT}，TCP发送: {SEND_RESULT_TO_TCP_DEVICE}")
print(f"异常后自动回位: {AUTO_RETURN_HOME_AFTER_FAULT}；异常后将只保存/报警/停止，不持续反转")
print(f"保存目录: {BASE_SAVE_DIR}")
print(f"电批目标: {IP}:{PORT}")
print(f"Unity控制口: {UNITY_BRIDGE_HOST}:{UNITY_BRIDGE_PORT}")
print("物理开关同步绘图: 已启用。空闲时发送 01/00 保持回传，检测到 speed/torque 后自动开始曲线")
print(f"异常后自动回位: {AUTO_RETURN_HOME_AFTER_FAULT}；手动Reverse仍可用于回位调试")
print("快捷键: f=正转，r=定量反转回初始位，s/Esc=立即急停；若无效请看终端是否打印“按钮指令已接收/鼠标兜底触发”")

threading.Thread(target=command_worker, daemon=True).start()
threading.Thread(target=tcp_receive, daemon=True).start()
threading.Thread(target=unity_command_server, daemon=True).start()
threading.Thread(target=terminal_keyboard_worker, daemon=True).start()

if UNITY_EMBEDDED_MODE:
    threading.Thread(target=curve_render_worker, daemon=True).start()
    threading.Thread(target=curve_http_server, daemon=True).start()
    print("Unity嵌入模式：不打开独立Matplotlib窗口，后台控制与数据保存保持运行")
    try:
        while True:
            time.sleep(1.0)
    except KeyboardInterrupt:
        print("Unity嵌入模式收到退出请求")
else:
    plt.show()
