# Ubuntu 20.04 管接头力位协同柔顺拧紧程序

本目录包含完整的机器人程序和电批主程序。将整个目录复制到 Ubuntu
20.04 的用户可写目录，使用图形桌面上的终端运行。机器人默认地址为
192.168.192.19，电批默认地址为 192.168.192.21:1200。
从桌面运行的逐步命令见同目录的《真机使用说明.pdf》；
《真机使用说明.txt》便于复制粘贴命令。

2026-10-23 方向修订：卡入为工具 +X（`--direction 1.0`），退出为工具 -X（`--direction -1.0`）。省略方向参数时默认为退出。
此前现场 `GetForce(1)` 返回 -1 的力反馈接口兼容问题未在本次方向修订中修复；若仍报此错，机器人动作会被程序中止。

## 安装

```bash
sudo apt update
sudo apt install -y python3 python3-venv python3-pip python3-tk
cd /实际存放目录/ubuntu20_04
python3 -m venv .venv
source .venv/bin/activate
python3 -m pip install -r requirements.txt
```

requirements.txt 适用于 Ubuntu 20.04 自带的 Python 3.8；Tk 由系统包提供。
主界面使用 TkAgg；后台保存曲线使用 Agg，避免后台线程创建图形窗口。

## 不连接设备的检查

```bash
python3 servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py --check
python3 cr10af_hybrid_x.py --ip 192.168.192.19 --distance-mm 20 --direction 1.0
python3 cr10af_hybrid_x.py --ip 192.168.192.19 --distance-mm 20 --direction -1.0
python3 -m unittest discover -s tests -v
```

主程序无参数运行时也只检查配置。导入程序或使用 --help 均不会连接设备。
这些检查验证代码与配置，不验证设备协议、实际力反馈口径或运动效果。

## 参数配置

默认卡入和退出行程均为 20 mm，两段缓冲均为 5 s，目标扭矩仍为原程序的
28 N·m。默认行程已比原先使用命令中的 8 mm 增加；它是配置值，需要按
当前工装净空和工艺核对。

```bash
export CR10AF_IP="192.168.192.19"
export SERVO_IP="192.168.192.21"
export SERVO_PORT="1200"
export CR10AF_DISTANCE_MM="20.0"
export CR10AF_SPEED_MM_S="1.2"
export CR10AF_STEP_MM="2.0"
export CR10AF_TOOL="1"
export CR10AF_MAX_RUNTIME_S="30.0"
export CR10AF_TIMEOUT_S="45.0"
export TIGHTEN_MAX_RUNTIME_S="60.0"
```

机器人脚本默认取主程序同目录下的 cr10af_hybrid_x.py。如需另行指定：

```bash
export CR10AF_SCRIPT="/实际绝对路径/cr10af_hybrid_x.py"
```

日志默认保存在本目录 servo_logs/YYYYMMDD。可以通过 SERVO_LOG_DIR 设置
另一可写目录。增大行程或降低速度时，应同时增加机器人内部运行预算和
外部进程超时，外部超时必须更大；搜索和通信耗时也需包含在预算中。
本版本没有重新标定力/扭矩阈值。

## 连接真机

在确认工具坐标、工装空间、网络路由和设备停机功能后，用同一终端运行：

```bash
python3 servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py --execute
```

程序连接电批并发送复位、停止及监控回传帧，打开界面；点击 Auto Cycle
或按 f 才开始自动周期：

工具 +X 卡入 → 5 s → 正转拧紧 → 判定 OK / 滑牙 / 卡滞 →
停止电批并确认停转 → 5 s → 工具 -X 退出。

## 单独控制

先确认现场工具方向、净空、工件位置和设备停机功能。以下两条机器人命令
均会实际运动，每次只执行所需的一条；它们不负责确认电批是否停转。
如卡入中途停止，应按实际位置确定退出距离，不要盲目退出完整 20 mm。

```bash
# 只卡入：工具 +X，20 mm
python3 cr10af_hybrid_x.py --ip 192.168.192.19 --tool 1 --distance-mm 20 --direction 1.0 --execute

# 只退出：工具 -X，20 mm
python3 cr10af_hybrid_x.py --ip 192.168.192.19 --tool 1 --distance-mm 20 --direction -1.0 --execute
```

只拧紧时先确认批头已正确卡入，再启动主界面：

```bash
python3 servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py --execute
```

点击 Tighten Only 或按 t，仅启动电批正转拧紧，不调度机器人运动。
Auto Cycle 或 f 仍执行完整自动周期。Reset/Stop、s 或 Esc 请求停止。

退出前要求速度绝对值不超过 1 且新鲜反馈持续至少 0.3 s，并且反馈没有
设备错误码。发送停止帧成功不能代替实际停转确认；无法确认时显示
EXIT_BLOCKED，取消自动退出。滑牙和卡滞后的退出指机器人 -X 退出，
没有额外增加电批自动反转。若工件仍有扭转载荷或机械约束，停止现场
调试并人工处理；速度反馈本身不证明扭转载荷已释放。

Reset/Stop、s 或 Esc 中断周期并发送停止请求；窗口关闭和 Ctrl+C 也请求
停止。Reverse 或 r 仅手动反转电批，低速低扭矩反馈成立时显示
RELEASE_CONFIRMED，不将该标签当作几何原点到位。仅用物理开关拧紧时
保存曲线并判定结果，不自动执行机器人退出，因为该操作未经过自动卡入。

通信失联、反馈超时、设备 NG 和拧紧超时终止周期，不调度自动退出。
软件网络停止请求不能替代设备硬件急停。

## 本版本的修复与验证边界

修复 Ubuntu 路径、可安全导入的主入口、退出函数作用域、子进程长输出
阻塞、TCP 分包头保留、后台曲线保存、重复按钮操作、停止事件被内部复位
清除，以及完成/卡滞时间窗口问题。接收线程持续运行，由独立线程处理
停止、结果保存和退出，便于确认停转。

cr10af_hybrid_x.py 保留原算法和原设备协议字段。
没有连接真机，未测定当前固件协议或宣称运动平顺性/异常识别率提升。
自动测试使用模拟套接字和模拟反馈，不发出真实设备指令。
