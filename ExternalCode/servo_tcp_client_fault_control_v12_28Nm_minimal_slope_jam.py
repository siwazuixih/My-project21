#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
伺服电批 TCP 控制 + 时间-扭矩算法 V12，目标扭矩 28 N.m

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
"""

import csv
import socket
import threading
import time
from collections import deque
from pathlib import Path

import numpy as np
import matplotlib.pyplot as plt
import matplotlib.animation as animation
from matplotlib.widgets import Button

# =========================
# TCP 参数
# =========================
IP = "192.168.192.21"
PORT = 1200
CONNECT_TIMEOUT = 3.0
RECONNECT_DELAY = 2.0

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
JAM_AUTO_STOP = False                     # 调试阶段建议 False，只报警不停止
JAM_MIN_RUNTIME = 2.0                     # 启动后 2s 内不判卡滞
JAM_TORQUE_TH = 0.75 * TARGET_TORQUE      # 21.0Nm 以上才允许判卡滞
JAM_SPEED_TH = 3                          # 速度绝对值 <= 3 认为接近停转
JAM_WINDOW_TIME = 1.0                     # 看最近 1s
JAM_RISE_MAX = 0.8                        # 最近1s扭矩增长小于0.8Nm，认为不上升
JAM_FLUCT_MAX = 1.2                       # 最近1s波动小于1.2Nm，认为平台
JAM_CONFIRM_N = 3                         # 连续3个周期确认才报警

# =========================
# 最小改动版：卡涩/卡滞增加时间-扭矩斜率与局部波动特征
# 不改原有OK/滑牙/保存/成功指令逻辑，只增强 JAM 预警可靠性。
# =========================
JAM_SLOPE_ENABLE = True
JAM_SLOPE_MIN = 0.2                       # 最近窗口平均斜率小于此值，说明扭矩基本不上升
JAM_SLOPE_STD_MAX = 1.5                   # 斜率标准差较小，说明进入平台/卡住
JAM_FLUCT_MIN = 0.05                      # 过滤完全静止/无效数据，窗口波动至少超过该值才参与判定

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
# 缓存
# =========================
time_data = deque(maxlen=20000)
torque_raw_data = deque(maxlen=20000)
torque_data = deque(maxlen=20000)
speed_data = deque(maxlen=20000)
angle_data = deque(maxlen=20000)
status_data = deque(maxlen=20000)

lock = threading.Lock()
sock_lock = threading.Lock()
client_socket = None

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


CMD_RESET = build_pc_cmd(0x00, 0x00)
CMD_FORWARD = build_pc_cmd(0x01, 0x01)
CMD_REVERSE = build_pc_cmd(0x01, 0x02)


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


def mark_all_rows_state(result_state: str):
    """保存前把本次曲线所有行的状态统一标成最终结果，便于CSV和PNG追溯。"""
    global status_data
    with lock:
        n = len(time_data)
        status_data.clear()
        status_data.extend([result_state] * n)


def finish_result(result_state: str, message: str, signal_func=None, stop_after=True):
    """统一结束流程：设置状态 -> 输出提示/指令 -> 保存同名CSV和PNG -> 复位停机。"""
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
    if stop_after:
        reset_driver()
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


def start_forward(event=None):
    print("\n准备正转：先复位，再启动")
    reset_driver()
    time.sleep(0.20)
    clear_run("RUN_FORWARD")
    send_cmd(CMD_FORWARD, "正转")


def start_reverse(event=None):
    print("\n准备反转：先复位，再启动")
    reset_driver()
    time.sleep(0.20)
    clear_run("RUN_REVERSE")
    send_cmd(CMD_REVERSE, "反转")


def stop_driver(event=None):
    global state
    reset_driver()
    with lock:
        if state not in ("OK", "NG_SLIP", "JAM", "NG_DEVICE"):
            state = "STOP"
    save_run_outputs(force=True)
    print("已发送复位/停止")

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
        if current_state not in ("RUN_FORWARD", "RUN_REVERSE", "JAM_WARN"):
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
            with lock:
                state = "NG_SLIP"
                fault_latched = True
            print(f"\n⚠️⚠️⚠️ 检测到滑牙 NG_SLIP ⚠️⚠️⚠️")
            print(f"峰值扭矩={peak:.2f} Nm，当前扭矩={torque_now:.2f} Nm，回落={peak_drop:.2f} Nm")
            print("处理动作：立即复位停机 → 保存CSV和曲线 → 输出NG_SLIP → 等待人工处理")
            send_slip_ng_signal()
            reset_driver()
            save_run_outputs()
            return

    # 4) 高负载卡滞/卡涩：高扭矩 + 低速度 + 最近1s扭矩基本不上升
    # 最小改动版：保留V12原始判据，同时增加 slope_mean / slope_std / fluct 三个辅助特征。
    # 注意：默认只报警不停止，防止误停。若现场验证准确后可把 JAM_AUTO_STOP=True。
    if JAM_ENABLE and run_time >= JAM_MIN_RUNTIME and torque_now >= JAM_TORQUE_TH:
        recent_mask = t_arr >= (t_arr[-1] - JAM_WINDOW_TIME)
        t_recent = t_arr[recent_mask]
        y_recent = y_arr[recent_mask]
        speed_recent = speed_arr[recent_mask]
        if len(y_recent) >= 8:
            rise = float(y_recent[-1] - y_recent[0])
            fluct = float(np.max(y_recent) - np.min(y_recent))
            mean_speed = abs(float(np.mean(speed_recent)))

            # 时间-扭矩斜率特征：dT/dt
            dt = np.diff(t_recent) + 1e-6
            dy = np.diff(y_recent)
            slope = dy / dt
            slope_mean = float(np.mean(slope)) if len(slope) else 0.0
            slope_std = float(np.std(slope)) if len(slope) else 0.0

            old_jam_condition = (
                mean_speed <= JAM_SPEED_TH
                and rise <= JAM_RISE_MAX
                and fluct <= JAM_FLUCT_MAX
                and torque_now < OK_TORQUE_TH
            )

            slope_jam_condition = (
                JAM_SLOPE_ENABLE
                and mean_speed <= JAM_SPEED_TH
                and torque_now < OK_TORQUE_TH
                and fluct >= JAM_FLUCT_MIN
                and fluct <= JAM_FLUCT_MAX
                and slope_mean <= JAM_SLOPE_MIN
                and slope_std <= JAM_SLOPE_STD_MAX
            )

            if old_jam_condition or slope_jam_condition:
                jam_counter += 1
            else:
                jam_counter = 0

            if jam_counter >= JAM_CONFIRM_N:
                if JAM_AUTO_STOP:
                    with lock:
                        state = "JAM"
                        fault_latched = True
                    print(
                        f"\n⚠️ 卡滞检测：torque={torque_now:.2f} Nm，speed={mean_speed:.1f}，"
                        f"1s上升={rise:.2f} Nm，波动={fluct:.2f} Nm，"
                        f"斜率均值={slope_mean:.2f} Nm/s，斜率波动={slope_std:.2f}"
                    )
                    send_jam_ng_signal()
                    reset_driver()
                    save_run_outputs()
                    return
                else:
                    if not jam_warned:
                        with lock:
                            state = "JAM_WARN"
                        jam_warned = True
                        print(
                            f"\n⚠️ 卡滞/卡涩预警但不停止：torque={torque_now:.2f} Nm，speed={mean_speed:.1f}，"
                            f"1s上升={rise:.2f} Nm，波动={fluct:.2f} Nm，"
                            f"斜率均值={slope_mean:.2f} Nm/s，斜率波动={slope_std:.2f}"
                        )
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
    global save_done, last_save_time

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
        fig_save, ax_save = plt.subplots(figsize=(9, 5))
        ax_save.plot(x, filt, lw=2, label="filtered")
        ax_save.plot(x, raw, lw=1, alpha=0.35, label="raw")
        ax_save.axhline(OK_TORQUE_TH, linestyle="--", linewidth=1, label=f"OK threshold {OK_TORQUE_TH:.1f} N.m")
        ax_save.set_title(f"Time-Torque Curve | STATE: {current_state} | Target={TARGET_TORQUE:.0f} N.m")
        ax_save.set_xlabel("Time (s)")
        ax_save.set_ylabel("Torque (N.m)")
        ax_save.set_ylim(0, 36)
        ax_save.grid(True)
        ax_save.legend(loc="upper left")
        fig_save.tight_layout()
        fig_save.savefig(png_path, dpi=200)
        plt.close(fig_save)

        print(f"数据和曲线已保存到同一文件夹：{day_dir}")
        print(f"CSV: {csv_path}")
        print(f"PNG: {png_path}")
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

                    raw_torque = parsed["torque"]
                    filtered_torque = filter_torque(raw_torque)
                    if filtered_torque is None:
                        continue

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
# 绘图与按钮
# =========================
fig, ax = plt.subplots()
plt.subplots_adjust(bottom=0.22)
line, = ax.plot([], [], lw=2, label="filtered")
raw_line, = ax.plot([], [], lw=1, alpha=0.35, label="raw")
ax.legend(loc="upper left")

ax.set_title("Time-Torque Curve | 28 N.m FSM V12")
ax.set_xlabel("Time (s)")
ax.set_ylabel("Torque (N.m)")
ax.grid(True)
ax.set_ylim(0, 36)

ax_forward = plt.axes([0.12, 0.05, 0.18, 0.075])
ax_reverse = plt.axes([0.41, 0.05, 0.18, 0.075])
ax_stop = plt.axes([0.70, 0.05, 0.18, 0.075])

btn_forward = Button(ax_forward, "Forward")
btn_reverse = Button(ax_reverse, "Reverse")
btn_stop = Button(ax_stop, "Reset/Stop")

btn_forward.on_clicked(start_forward)
btn_reverse.on_clicked(start_reverse)
btn_stop.on_clicked(stop_driver)


def update(frame):
    with lock:
        current_state = state
        if len(time_data) == 0:
            ax.set_title(f"Time-Torque Curve | STATE: {current_state}")
            return line, raw_line
        x = list(time_data)
        y = list(torque_data)
        y_raw = [abs(v) for v in torque_raw_data]

    line.set_data(x, y)
    raw_line.set_data(x, y_raw)
    if len(x) > 1:
        ax.set_xlim(max(0, x[-1] - 8), max(8, x[-1] + 0.2))
    ax.set_title(f"Time-Torque Curve | STATE: {current_state} | Target={TARGET_TORQUE:.0f} N.m")
    return line, raw_line


ani = animation.FuncAnimation(fig, update, interval=80, cache_frame_data=False)

print("\n命令说明：")
print(f"复位/停止: {hexstr(CMD_RESET)}")
print(f"正转:     {hexstr(CMD_FORWARD)}")
print(f"反转:     {hexstr(CMD_REVERSE)}")
print("\nV12_28Nm 判据：")
print(f"拧紧完成: torque >= {OK_TORQUE_TH:.2f} Nm 且 {OK_HOLD_TIME:.1f}s 内波动 <= {OK_STABLE_BAND:.2f} Nm")
print(f"滑牙: peak >= {SLIP_MIN_PEAK:.2f} Nm，峰值回落 >= {SLIP_DROP_TH:.2f} Nm，连续 {SLIP_CONFIRM_N} 次")
print(f"卡滞: torque >= {JAM_TORQUE_TH:.2f} Nm，speed <= {JAM_SPEED_TH}，{JAM_WINDOW_TIME:.1f}s 上升 <= {JAM_RISE_MAX:.2f} Nm")
print(f"卡滞斜率辅助: slope_mean <= {JAM_SLOPE_MIN:.2f} Nm/s，slope_std <= {JAM_SLOPE_STD_MAX:.2f}，fluct {JAM_FLUCT_MIN:.2f}~{JAM_FLUCT_MAX:.2f} Nm")
print(f"卡滞自动停机: {JAM_AUTO_STOP}")
print(f"成功指令: {SUCCESS_CMD_TEXT}，滑牙NG指令: {SLIP_NG_CMD_TEXT}，TCP发送: {SEND_RESULT_TO_TCP_DEVICE}")
print(f"保存目录: {BASE_SAVE_DIR}")

threading.Thread(target=tcp_receive, daemon=True).start()

plt.show()
