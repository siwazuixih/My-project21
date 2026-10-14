# 相机与机械臂通信延迟小测试

本测试对应当前架构：相机由Python直接采集/推理，Unity仅拉取预览图；机械臂由Unity统一通信。因此相机测Python获得可用图像的时间，机械臂测Python直连与经Unity转发的差别。没有测试相机原图经Unity再回算法，也没有把图像显示延迟当成视觉算法输入延迟。

测试工具为`ExternalCode/communication_latency_test.py`。不运行视觉推理或拧紧算法，不会发送机器人运动、使能、Stop、User或Tool切换命令。测试期间先结束机械臂任务；直接/转发顺序交替，但不会用主动停止代替人工结束任务。

## 一、相机 → Python

先关闭软件的“视觉”功能，确认原视觉Python服务已退出、相机已释放。软件其他界面可以保持运行，以测试通常的电脑负载。独立工具需要pyrealsense2和NumPy，用已有视觉环境的Python运行（可将下列`python3`替换为现场`vision_env/bin/python`完整路径）。

在新构建的根目录运行：

```bash
python3 ExternalCode/communication_latency_test.py camera --global-time
```

默认沿用彩色1280×720@15fps和同分辨率深度、对齐深度至彩色、拷贝NumPy数组，预热30帧、测500帧，约35秒。若现场视觉服务为其他分辨率/帧率，必须填写相同设置，例如：

```bash
python3 ExternalCode/communication_latency_test.py camera --width 640 --height 480 --depth-width 640 --depth-height 480 --fps 30 --global-time
```

若原服务实际已降级为彩色流，使用`--color-only`测同样的彩色模式。工具不自动降级：配置不支持时报告失败，避免拿不同模式结果比较。`--global-time`仅在支持的传感器上临时启用SDK全局时间，结束后尝试恢复；报告包含恢复错误。启动后先预热再取样。

生成独立的`LatencyReports/...-camera-.../camera.csv`及`summary.json`，均以毫秒计：

- `wait_for_frames_ms`：Python等待下一帧的时间，包括帧率周期，15fps一帧周期约66.7ms；这个值不能直接称为USB传输延迟。
- `alignment_ms`、`copy_ms`：深度对齐、图像拷贝耗时。
- `timestamp_to_python_ready_ms_estimate`：SDK图像时间戳至Python已获得可用数组的年龄估算。硬件时钟不能直接减电脑时间，只接受可对应的global_time/system_time；负值、明显不匹配或检测到电脑时钟跳变时标无效。`timestamp_age_available=false`或统计`count=0`表示无法估算，不是延迟为0。
- 帧号缺口、重复帧和时间戳域也被记录。不要混淆不同时间戳域/流模式；`completed=false`表示测试未完成。

SDK的global时间是硬件时间经主机校正后的时间，system时间可能对应主机接收时刻；这不是“物体变化到相机完成曝光”的完整光学延迟。时间戳含义见[RealSense官方SDK说明](https://github.com/realsenseai/librealsense/blob/master/include/librealsense2/hpp/rs_frame.hpp)。若老师需要完整光学响应，还应做同步闪光/标记事件录像测试。

## 二、机械臂直连与软件转发

需要重新完整构建Unity软件，包含新Gateway测试端点；只复制Python无法测试转发。新Linux构建自动带上工具和本说明（`COMMUNICATION_LATENCY.md`）。

1. 打开软件并正常连接机械臂，结束所有机械臂/全流程任务，保持当前状态一致。无需启动电批、视觉或移动机器人，也无需因测试额外使能。
2. 软件保持运行，在同一工控机、新构建根目录执行：

```bash
python3 ExternalCode/communication_latency_test.py robot --robot-ip 192.168.192.19 --query pose --user 0 --tool 1
```

默认机器人Dashboard29999、Unity本机网关19120，参数必须与软件实际连接一致。测试先用本机echo验证Gateway版本/设备地址，再各测500次同一个`GetPose(user=0,tool=1)`。共四段：直连250 → 转发250 → 转发250 → 直连250，每段预热20次，发起间隔至少50ms（请求超过间隔时不补发突发请求），通常约一分钟。

也可以单独复测同学常用的力查询或关节角查询：

```bash
python3 ExternalCode/communication_latency_test.py robot --query force --tool 1
python3 ExternalCode/communication_latency_test.py robot --query angle
```

对应`GetForce(1)`或`GetAngle()`。这些只读查询是白名单，不允许传任意API命令；不会调用acquire、自动设置坐标系或发送停止。任务占用时Unity拒绝探测。控制器若不允许额外只读TCP连接，直连阶段会报告失败，不能据此断言“直连更慢”；需先核对控制器读接口权限。脚本不会发送RequestControl夺取控制权。

报告为`LatencyReports/...-robot-.../robot.csv`和`summary.json`：

- `direct_rtt_ms`：Python直连设备的完整查询往返时间。
- `unity_rtt_ms`：Python→Unity→设备→Unity→Python完整往返，客户端均使用同一Python单调时钟计时。
- `extra_mean_ms`、`p95_difference_ms`：两条路径统计量之差。它们不是同一条请求配对后的精确转发耗时；分段测量仍受设备/负载变化影响。只有样本齐全且地址一致时才给差值，失败不输出假的对比结论。
- `local_echo_rtt_ms`：没有设备请求的本机Gateway往返，仅作调度/通信参考，不能取代完整查询对比。
- `unity_internal_processing_ms`、`dashboard_gate_wait_ms`、`dashboard_wire_rtt_ms`：Unity内部处理、Dashboard串行锁等待、真实写入至完整应答收齐。所有分段为各自进程内持续时间，不用跨进程墙上时间直接相减。不要将差值除以2当作确定单向延迟。
- 均输出样本数、均值、中位数、P95、P99、最大值、错误/超时；超时立即结束并关闭该流，避免迟到回复被下一请求误认。

新只读端点复用生产Gateway的主线程处理与Dashboard串行通信，但关闭逐请求日志，避免测量受刷屏影响。结果不代表ServoP运动完成时间或整套算法延迟；原算法日志、边界检查、额外GetForce查询等会产生额外开销。

## 给老师的结果

先在电脑较空闲时测一轮，再在软件正常显示/计算负载下测一轮，保持同一设备、模式、帧率、查询与采样数。相机报告“时间戳年龄估算及对齐/拷贝耗时”；机械臂报告“直连平均/P95，转发平均/P95，增加多少，以及最慢/超时”。先保存实际统计再判断差别，不预设本机TCP一定只有微秒级。

测试报告在当前终端目录下创建新子目录，不覆盖现场配置或旧报告。首轮测试不需要安装新依赖；缺SDK时先使用原视觉环境。请提供整份报告目录。当前已验证的是假数据/本机模拟服务，真实硬件数值待现场运行。
