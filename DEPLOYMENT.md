# 飞机导管拧紧系统 v1.6.0 发布说明

## Unity 打包

- 使用 Unity `2022.3.62f2c1`。
- Build Settings 中保持以下场景及顺序：
  1. `Assets/SimulationPlatform/Scenes/LoginScene.unity`
  2. `Assets/SimulationPlatform/Scenes/Main.unity`
  3. `Assets/SimulationPlatform/Scenes/RunScene.unity`
  4. `Assets/SimulationPlatform/Scenes/MainScene.unity`
- 当前相机和拧紧程序按 Ubuntu/Linux 环境设计，推荐选择
  `Linux 64-bit`，不要勾选 Development Build。
- 打包结束后，编辑器脚本会自动将以下运行文件复制到软件目录：
  - `ExternalCode/*.py`
  - `Assets/微软雅黑.ttf`
- 发布时必须复制整个输出目录，不能只复制主程序可执行文件。

## 目标电脑运行依赖

- `/usr/bin/python3`
- Python 模块：`numpy`、`matplotlib`、`opencv-python`（提供 `cv2`）、
  `pyrealsense2`
- Intel RealSense SDK 和 Linux UDEV 权限规则
- RealSense D435I 建议使用 USB 3.x 数据线和 USB 3.x 接口

可在目标电脑先执行：

```bash
/usr/bin/python3 -c "import numpy, matplotlib, cv2, pyrealsense2; print('Python runtime OK')"
```

## 网络与端口

- RealSense 本机图像服务：`127.0.0.1:8080`
- V26 Unity 控制服务：`127.0.0.1:9100`
- V26 实时曲线服务：`127.0.0.1:9101`
- 电批默认地址：`192.168.192.21:1200`

运行前确认上述本机端口没有被其他程序占用，并确认目标电脑网卡与真实设备处于正确网段。

## 数据与权限

- 拧紧 CSV/PNG 保存到软件目录下
  `ExternalCode/servo_logs/YYYYMMDD/`。
- 软件输出目录必须允许当前用户写入，否则曲线和 CSV 无法保存。
- `servo_logs`、Python 缓存、Unity Build/Logs 不应提交到 GitHub。

## 发布前人工检查

- 登录、项目管理、模型加载和场景切换正常。
- 仿真运行和真实运行界面切换正常。
- 开启/关闭视频，相机释放正常；小视频双击放大正常。
- 启动/关闭 V26 程序正常；曲线中文显示和双击放大正常。
- “开始拧紧”和“反转回位”的二次确认正常。
- “立即停止”无需确认且可以停止设备。
- 真实设备测试前确保急停可用、运动范围内无人。

## GitHub 源码说明

- `Library`、`Temp`、`Builds`、日志和运行数据均由 `.gitignore` 排除。
- `Assets/微软雅黑 SDF.asset` 约 128 MB，超过 GitHub 普通文件上限，
  当前不进入仓库；代码会回退到项目已有 TMP 中文字体。
- `Assets/dipan2.fbx`、`Assets/机舱.fbx` 和 `Files/` 中的大型运行数据
  当前也被排除。若另一台电脑需要从 GitHub 完整重建工程，应另行提供这些
  大文件，或在后续启用 Git LFS。
