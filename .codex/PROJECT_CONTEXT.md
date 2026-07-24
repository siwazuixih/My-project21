# Project Context

## 项目概况

- Unity + C# 项目，主要包含仿真平台、路径点选择、NavMesh 底盘导航、机械臂 IK/BIT* 规划、MuJoCo 仿真，以及 Dobot/底盘/升降缸等真实设备联动代码。
- 主要运行场景包括 `Assets/SimulationPlatform/Scenes/RunScene16.unity`、`RunScene21.unity`、`RunScene.unity`、`Assets/Scenes/SampleScene.unity`。
- 项目已有大量未提交修改，修改时应只碰当前任务相关文件，避免回退用户已有工作。
- GitHub 远程仓库为 `https://github.com/siwazuixih/My-project21.git`；发布版本继续使用 `release/software-v1.0` 分支，并用 `v1.2`、`v1.3`、`v1.4` 等标签标记具体版本。
- `SoftWare1.5` 已位于 `release/software-v1.0` 并创建 `v1.5` 标签；2026-07-24 当前视觉相机、力矩曲线和 V26 UI 控制版计划作为 `SoftWare1.6 / v1.6` 发布。
- Player 产品名为“飞机导管拧紧系统”，当前发布版本号为 `1.6.0`。
- Linux Player 构建完成后，`Assets/Editor/ExternalRuntimeBuildPostprocessor.cs` 会把项目根目录 `ExternalCode/*.py` 和 `Assets/微软雅黑.ttf` 自动复制到软件输出目录，以匹配运行时现有相对路径。

## 关键脚本

- `Assets/VisionImageReceiver.cs`
  - RealSense 现场视频的 Unity 运行时接收器。
  - 自动查找 `RunScene` 中标题为“现场视频”的 `ObservParam` 区域。
  - 在该区域的 `Camera.png` Image 上方动态创建 `RealSenseLiveImage`，不直接修改场景 YAML。
  - 视频层四周缩进 1 像素以露出原蓝色边框，并在右上角重新创建红色 `● LIVE` 标记。
  - 找到目标 UI 后只完成绑定，相机默认关闭。
  - 仅在用户点击“开启视频”后启动 Python HTTP 服务，并默认每 `0.2` 秒拉取 `/latest.jpg`。
  - 点击“关闭视频”、切回仿真模式、切换场景或退出运行时会终止拉流和 Python 进程并释放相机。
  - 现场视频支持双击放大；运行时创建 Unity内部模态窗口，大小视频共用同一个实时 Texture2D，不增加相机或 HTTP 请求。
  - 大视频支持双击、右上角关闭按钮、点击遮罩和 `Esc` 关闭；停止视频或切换场景时自动关闭。
- `ExternalCode/realsense_image_server.py`
  - 使用 `pyrealsense2` 读取 D435I 的 `1280×720 BGR8 @ 15 FPS` 彩色流。
  - 使用 OpenCV 编码 JPEG，通过 `127.0.0.1:8080/latest.jpg` 提供给 Unity。
  - `/status` 提供设备、序列号、USB 类型、帧数和最近错误。
- `Assets/ServoTighteningController.cs`
  - 正式项目中的 V26 拧紧程序运行时控制器。
  - 自动在真实运行工具栏创建“启动程序/关闭程序”按钮，该按钮只管理 V26 Python 后台程序。
  - 在程序按钮右侧创建“开始拧紧”“反转回位”“立即停止”三个动作按钮，分别映射 `forward`、`reverse`、`stop`。
  - 开始拧紧和反转回位仅在 V26 就绪且电批已连接时启用；立即停止在 V26 就绪后即可使用。
  - 开始拧紧和反转回位必须在 3 秒内二次点击确认；每次命令入队后重新锁定。立即停止无需确认，并会取消待确认动作。
  - 启动并管理 V26 Python 进程，通过 `127.0.0.1:9100` 下发命令和轮询状态。
  - 自动查找 `RunScene` 中标题为“实时力矩曲线”的区域，在其 `Camera.png` 占位图上方创建 `ServoTorqueCurveImage`。
  - 曲线 `RawImage` 在空闲和程序关闭后也保持深色显示，用于遮住公用视频占位图自带的 `LIVE`；现场视频的 `LIVE` 不受影响。
  - V26 就绪后默认每 `0.2` 秒从 `127.0.0.1:9101/curve.jpg` 拉取实时力矩曲线，关闭程序时停止并释放纹理。
  - 小曲线支持双击放大；运行时创建 Unity内部模态窗口，大小图共享同一个 Texture2D。
  - 大图支持双击、右上角关闭按钮、点击遮罩和 `Esc` 关闭，程序停止或场景切换时自动关闭。
  - UI 与后续全自动任务共用 `StartProgramAsync`、`ConnectToolAsync`、`StartTighteningAsync`、`ReverseHomeAsync`、`StopToolAsync`、`QueryStatusAsync`、`StopProgramAsync`。
  - `enableRealToolMotion` 默认关闭，未显式解锁时拒绝正转和反转，但始终允许停止。
  - 关闭 V26 前先下发停止并等待安全状态；未确认停止时不杀进程。
- `ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py`
  - 从旧 `My project21` 迁入的正式 V26 电批控制和时间—力矩算法。
  - 电批默认地址 `192.168.192.21:1200`，Unity 控制口默认 `127.0.0.1:9100`，均可用环境变量覆盖。
  - Unity 命令为 `connect`、`forward`、`reverse`、`stop/reset`、`status`。
  - 拧紧数据继续保存到 `ExternalCode/servo_logs/YYYYMMDD/`，同次运行生成同名 CSV 和 PNG。
  - `SERVO_UNITY_EMBEDDED_MODE=1` 时使用 Matplotlib Agg 后端并保持后台运行，不打开独立曲线窗口；原保存逻辑不变。
  - Unity嵌入模式下同时提供 `127.0.0.1:9101/curve.jpg` 实时预览，默认生成 `960×540` 深色 JPEG，约 5 FPS；该预览不替代最终保存 PNG。
  - 实时预览、最终保存 PNG和独立桌面图的可见文字均已中文化；协议内部状态码仍保持英文。
  - Matplotlib使用 `Assets/微软雅黑.ttf`，Unity弹窗使用 `Assets/微软雅黑 SDF.asset`。
- `Assets/TorqueCurvePointerHandler.cs`
  - 处理小曲线和大曲线的双击，以及弹窗遮罩的直接点击关闭。
  - 使用 EventSystem 指针事件，不在 `Update()` 中轮询鼠标位置。
- `Assets/VisionVideoPointerHandler.cs`
  - 处理现场视频小图/大图的双击和视频弹窗遮罩点击。
  - 与力矩曲线交互保持一致，但分别绑定各自控制器，生命周期互不干扰。
- `Assets/MissionController.cs`
  - 任务主流程控制。
  - `StartMissionSequence()` 会触发 `DeepPrecomputeAll()`。
  - `DeepPrecomputeAll()` 会逐个目标点计算底盘路径、临时移动仿真底盘、复位机械臂、计算观察位姿，并调用 `refs.bitPlanner.Plan(...)` 或 IK。
- `Assets/ArmController.cs`
  - 机械臂初始化、复位、运行时规划和路径执行。
  - `StartArmSequence()` 会根据配置使用预计算路径、BIT* 或简单 IK 插值。
- `Assets/BITStarPlanner.cs`
  - BIT* 路径规划器，依赖 `MujocoStaticIKSolver` 和一组参与规划的 `MjActuator`。
  - 规划入口 `Plan(Vector3 targetPos, Quaternion? targetRot = null)` 先调用 IK 得到 MuJoCo 全量 `qpos`，再抽取 actuator 对应的 compact state。
  - 碰撞检查通过临时写入 `MjScene.Instance.Data->qpos` 后调用 MuJoCo 位置/接触计算。
- `Assets/MujocoStaticIKSolver.cs`
  - 梯度下降 IK 求解器。
  - 返回值是 MuJoCo 全量 `qpos`，不是 compact actuator 数组。

## 崩溃线索

- 2026-06-24 排查过一次路径规划崩溃：Unity `Editor.log` 最后托管调用链在 `Simulation.OnPathPlanClicked()` -> `MissionController.DeepPrecomputeAll()` -> `BITStarPlanner.Plan()` -> `MujocoStaticIKSolver.SolveIK()`。
- 崩溃前最后日志为 BIT* 打印 IK 结果，随后 native 层报 `free(): invalid pointer` 和 `fatal signal 6`。
- 项目根目录 `MUJOCO_LOG.TXT` 曾出现 `Nan, Inf or huge value in QACC`，说明 MuJoCo 仿真可能进入数值不稳定状态。
- BIT*/IK 会为了 FK、碰撞检查、采样可视化而临时写 `MjScene.Instance.Data`。这类代码必须完整备份并恢复 `qpos/qvel/act/ctrl`，恢复后调用 `mj_forward`，不要只恢复 `qpos` 或只调用 `mj_kinematics`。

## 约定

- Unity 与 MuJoCo 坐标换算常见写法：MuJoCo `(x, y, z)` 对应 Unity `(x, z, y)`。
- 对真实设备相关逻辑保持保守，不要自动下发危险动作；优先先稳定仿真路径规划。
- Player 打包使用不含 Unity Editor 程序集的独立编译配置；运行时脚本不可无条件引用 `UnityEditor` 或 `UnityEditorInternal`。2026-07-23 已清理 ZCalendar 两处未使用的 Editor-only 引用。

## 正式运行状态界面

- Build Settings 当前启用的正式运行场景是 `Assets/SimulationPlatform/Scenes/RunScene.unity`。
- `RunScene` 中“现场视频”文字对象为 `ObservParam`，其子级 `Image` 使用 `Assets/SimulationPlatform/Resources/Run/Camera.png`，尺寸为 `287×162`。该 PNG 四角透明，但中心 Alpha 为 `255`，实际是带深色不透明背景的画面框，而不是可直接叠在视频上方的透明边框。
- RealSense 接收器把略微缩进的 `RawImage` 放在原 Image 上方，利用四周露出的 1 像素保留蓝色边框，并动态补回右上角 `LIVE` 标记。
- 右侧机械臂模式和六轴角度由 `Assets/DobotController.cs` 动态刷新。
- 右侧升降缸高度、速度、转矩由 `Assets/LiftCylinderController.cs` 动态刷新；未连接时使用现有三个文本框显示中文连接和参数占位。
- 实物跟随状态由 `Assets/RealRobotFollower.cs` 动态刷新。
- 2026-07-23 起，这些面向正式运行用户的动态状态统一显示中文；物理单位仍保留 `mm`、`mm/s` 和 `°`。
- 左侧调试日志来自 `Assets/Canvas.prefab/ConsoleBackground` 上的 `Assets/RuntimeConsole.cs`。
- 2026-07-23 起，`RuntimeConsole` 会在运行时创建“收起日志/展开日志”按钮；收起只隐藏 UI，日志采集和缓存继续运行。
- `RunScene/SimulationPlatform/Canvas/Panel` 下有场景原生 UI 对象 `RuntimeConsoleToggleButton`，位于底部公共蓝色工具栏，可在场景中用 Rect Tool 直接拖动并保存。
- `RuntimeConsole` 会优先跨 Canvas 查找这个活动按钮并绑定点击事件；其他没有该场景按钮的场景仍使用运行时创建逻辑作为兼容兜底。
- `Assets/Canvas.prefab` 内原按钮已改名为 `RuntimeConsoleToggleButtonLegacy` 并停用，保留结构但不再参与正式运行显示。
- `RuntimeConsole.startCollapsed` 和 `Canvas.prefab` 的对应序列化值默认开启；进入正式运行时日志窗口默认收起，但日志监听和缓存继续工作。
- `VisionImageReceiver` 会在 `RuntimeConsoleToggleButton` 右侧 120 像素运行时创建 `VisionVideoToggleButton`。
- 视频按钮跟随 `StatusReal.activeInHierarchy`：仿真模式隐藏，真实运行显示；文字在“开启视频”和“关闭视频”之间切换。
- `ServoTighteningController` 会在日志按钮右侧 240 像素创建 `ServoTighteningToggleButton`，即位于视频按钮右侧。
- 拧紧按钮同样只在真实运行界面显示；切回仿真模式时会请求安全停止并关闭自己启动的 V26。
- `RunScene` 中用户复制的力矩区域为 `ObservParam (1)`，标题文字为“实时力矩曲线”，子级 `Image` 尺寸为 `287×162`；曲线控制器按标题和 `Camera` Sprite 运行时绑定，不依赖易变化的复制编号。

## NavMesh 运行时烘焙

- `Assets/SimulationPlatform/Scripts/Function/Simulation.cs` 的 `OnPathPlanClicked()` 会在开始任务前调用 `RebuildRuntimeNavMesh()`。
- 2026-06-25 调整后，运行时烘焙不再只依赖机器人根节点的 `NavMeshModifier`，而是对 `MissionController.gameObject` 整棵子层级逐个添加/更新 `NavMeshModifier`。
- 原因：`NavMeshSurface` 会用自身 `LayerMask` 过滤 `NavMeshModifier` 所在物体的 Layer；如果根节点在 `Robot` 层但 Surface 不包含 `Robot` 层，根节点 modifier 可能不会生效。
- `SampleScene` 中 `cr10_robot356/ground` 是 MuJoCo 世界地面，不属于机器人障碍物，必须保留进 NavMesh 烘焙；运行时代码会跳过名字为 `ground`/`floor`/`plane` 的世界地面对象，并确保它们 `ignoreFromBuild=false`。
- 其他机器人子物体会设置 `ignoreFromBuild=true`，用于排除底盘和机械臂本体。
- 新日志会打印实际重建的 Surface 层级路径、排除机器人对象数、保留地面对象数、Surface LayerMask，以及该 Surface 是否包含机器人根节点 Layer。
