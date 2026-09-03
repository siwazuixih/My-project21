# Project Context

## 项目概况

- Unity + C# 项目，主要包含仿真平台、路径点选择、NavMesh 底盘导航、机械臂 IK/BIT* 规划、MuJoCo 仿真，以及 Dobot/底盘/升降缸等真实设备联动代码。
- 主要运行场景包括 `Assets/SimulationPlatform/Scenes/RunScene16.unity`、`RunScene21.unity`、`RunScene.unity`、`Assets/Scenes/SampleScene.unity`。
- 项目已有大量未提交修改，修改时应只碰当前任务相关文件，避免回退用户已有工作。
- GitHub 远程仓库为 `https://github.com/siwazuixih/My-project21.git`；发布版本继续使用 `release/software-v1.0` 分支，并用 `v1.2`、`v1.3`、`v1.4` 等标签标记具体版本。
- `SoftWare1.7` 已于 2026-08-19 发布到 `release/software-v1.0`，提交为
  `dad2be7521a875e0b9c6cdd6f1058dc70ba9b9c3`，并创建、推送 `v1.7` 标签和 GitHub
  Release；后续版本继续沿用同一发布分支和独立版本标签。
- Player 产品名为“飞机导管拧紧系统”，当前发布版本号为 `1.7.0`。
- Linux Player 构建完成后，`Assets/Editor/ExternalRuntimeBuildPostprocessor.cs` 会把项目根目录 `ExternalCode/*.py` 和 `Assets/微软雅黑.ttf` 自动复制到软件输出目录，以匹配运行时现有相对路径。
- 当前 Linux Player 的相机和 V26 控制器都默认调用 `/usr/bin/python3`，
  V26继续直接使用该解释器。视觉服务脚本会在启动早期查找软件目录上一级的
  `vision_env/bin/python`（也可由 `VISION_PYTHON_EXECUTABLE` 覆盖），并在
  OpenCV、RealSense、Torch、Ultralytics和 CUDA预检全部通过后自动切换；
  预检失败则留在系统 Python，继续提供普通实时图降级。目标电脑若混用用户级
  pip 包和 Ubuntu `/usr/lib/python3/dist-packages`，仍可能出现二进制 ABI冲突。
- 2026-07-25 工控机故障已确认是 `NumPy 2.2.6 + Ubuntu 系统 Matplotlib`
  不兼容；同时该机的 `opencv-python 5.0.0.93` 在 Python 3.10 上要求
  `NumPy >= 2`。不能只单独降级 NumPy，应固定整套 Python 依赖或建立独立环境。
- 另一台正式工控机可正常启动 V26 的 9100/9101 服务，但其 Ubuntu 系统
  Matplotlib `3.1.2` 的 `FigureCanvasAgg` 同时不提供 `print_jpg()` 和
  `print_jpeg()`，导致实时曲线线程持续报错并由 `/curve.jpg` 返回 503。
  V26源脚本现已正式实现优先 JPEG、无 JPEG接口时回退 PNG并同步 HTTP
  Content-Type；以后重新打包不需要再手改正式工控机文件。

## 关键脚本

- `Assets/VisionImageReceiver.cs`
  - RealSense 现场视频的 Unity 运行时接收器。
  - 自动查找 `RunScene` 中标题为“现场视频”的 `ObservParam` 区域。
  - 在该区域的 `Camera.png` Image 上方动态创建 `RealSenseLiveImage`，不直接修改场景 YAML。
  - 视频层四周缩进 1 像素以露出原蓝色边框，并在右上角重新创建红色 `● LIVE` 标记。
  - 找到目标 UI 后只完成绑定，相机默认关闭。
  - 按钮现显示“开启视觉/关闭视觉”；开启后启动唯一 Python视觉服务并拉取
    `/latest.jpg`。
  - 根据 `X-Vision-Mode` 只在模式切换时提示处理图或普通原图降级。
  - 点击“关闭视觉”、切回仿真模式、切换场景或退出运行时会终止拉流和 Python
    进程并释放相机。
  - 现场视频支持双击放大；运行时创建 Unity内部模态窗口，大小视频共用同一个实时 Texture2D，不增加相机或 HTTP 请求。
  - 大视频支持双击、右上角关闭按钮、点击遮罩和 `Esc` 关闭；停止视频或切换场景时自动关闭。
- `ExternalCode/realsense_image_server.py`
  - 已升级为唯一 RGB-D视觉服务：独立采集线程持续保留普通彩色 JPEG，后台线程
    可选加载 YOLO/SAM并生成处理图。
  - 默认模型为 `ExternalCode/models/best.pt` 和 `sam2_b.pt`，也可用
    `VISION_DETECTION_MODEL`、`VISION_SAM_MODEL` 覆盖。
  - 模型/依赖加载失败或连续3次处理失败时进入 `raw_fallback`，
    `/latest.jpg` 仍返回普通实时图；成功时同一地址返回处理图。
  - `X-Vision-Mode` 标识 `processed` 或 `raw_fallback`；`/status`
    提供相机、深度、模型和推理状态，`/result` 提供最新目标结果。
  - RGB-D启动失败会尝试彩色流降级；完全没有相机时仍无法提供图像。
- `ExternalCode/measure copy.py`
  - 2026-07-25 由视觉同学提供的原始 RGB-D 目标中心测量原型，已随 `v1.7` 纳入 Git，
    当前仍未接入 Unity。
  - 同时打开 D435I `1280×720@30 FPS` 彩色/深度流，用 YOLO 检测、SAM
    分割、最小外接旋转矩形中心和深度邻域中值，输出相机坐标 XYZ（毫米）。
  - 当前没有任何 CR10AF 网络通信或运动指令；只有 OpenCV 窗口、终端输出和
    `s/q` 键盘操作。
  - 存在 Windows `F:\...` 模型/输出硬编码；工程中缺少 `best.pt`、
    `sam2_b.pt`、Ultralytics/Torch 运行环境，构建脚本也不会复制模型权重。
  - 会独占 RealSense pipeline，不能和现有 `realsense_image_server.py`
    并行打开同一相机。正式集成时应合并为唯一 RGB-D 相机服务。
  - 当前从进程启动到退出始终保持相机打开，并逐帧执行 YOLO、逐检测框执行 SAM；
    `result_img` 已包含轮廓、旋转矩形、中心点和相机 XYZ 标注，适合直接作为
    Unity“现场视频”的显示源。
  - 推荐集成形态为单一视觉服务：持续采集、受控频率推理、HTTP 返回最新处理图和
    结构化测量结果。相机可在真实任务期间保持在线，但只有机械臂反馈确认静止后
    才允许自动流程采纳测量。
- `ExternalCode/point_move_demo.py`
  - 2026-07-25 新增并已随 `v1.7` 纳入 Git 的 Dobot点位运动示例，直接使用
    `dobot_api` 连接 29999/30004并执行两个示例点位。
  - 当前只作为视觉同学的运动原型参考，不接入 Unity，不随视觉服务启动；正式运动
    仍由 Unity 的 `DobotController` 独占控制。
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
  - Unity嵌入模式下同时提供 `127.0.0.1:9101/curve.jpg` 实时预览，默认生成
    `960×540` 深色图，约5 FPS；有 JPEG接口时使用 JPEG，旧版 Agg无 JPEG
    接口时自动回退 PNG并返回正确 Content-Type。该预览不替代最终保存 PNG。
  - 曲线 `/status` 包含实际 `content_type`，用于确认目标电脑选择了 JPEG还是
    PNG编码。
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
  - 2026-08-13 起，远目标会先投影到与底盘同高的 NavMesh，并要求
    `NavMeshPathStatus.PathComplete`；程序沿完整路径寻找首个进入机械臂工作半径的
    停靠点。路径采样、完整性或 IK 任一失败都会清空整组缓存并停在 `Idle`，不再用
    `{simPos}` 原地路径冒充成功任务。
  - 2026-08-13 已修复首次多点实测中的短折线：底盘真实起点与
    `NavMesh.SamplePosition()` 起点的水平偏差不超过 `0.25m` 时，采样点只用于
    `NavMesh.CalculatePath()`，执行路径从真实 `simPos` 直接接入主路径；偏差超过
    `0.25m` 会中止预计算并明确报告底盘已明显偏离导航面。
  - 当前“已在机械臂工作半径内”的判定只比较底盘与模型点的水平距离，不检查目标
    高度、末端观察位姿、底盘朝向或该站位下 IK 是否可解；满足半径后会直接保持原位。
    因此较高目标或受观察方向约束的目标可能在名义半径内却没有可接受 IK 解。后续若
    修复，应在当前站位 IK 失败时搜索目标周围的备用 NavMesh 站位/朝向，而不是单纯
    增加随机重试次数或放宽精度。
  - 2026-08-13 21:59 将原多点任务的第二点 `1x.001` 单独作为首点复测，底盘原点到
    模型点水平距离 `0.975m`，仍因小于 `1.15m` 而完全不移动；目标观察位姿
    `(0.725, 1.920, -0.024)/(0,90,0)` 最佳误差为 `38.86mm/0.08360rad`，仍失败。
    这排除了“前一任务状态继承”是必要条件，并进一步确认当前站位选择策略是核心缺口。
    但现有 IK 日志不会统计被碰撞检查丢弃的候选，单靠该日志尚不能区分严格运动学不可达
    与更优候选因碰撞被拒绝；后续诊断应分别隔离姿态约束和碰撞检查。
  - 2026-08-14 对同一单点关闭 `enableLookAt` 后连续三次成功；目标旋转为 `None`，位置
    误差稳定在 `0.77~0.99mm`，每次均返回3个无碰撞IK候选并完成BIT*，升降轴均保持
    `0m`。这已确认目标位置、六轴可达性和碰撞模型正常，固定完整姿态是失败的决定因素。
    当前roll fallback仍对“前向+上向”完整姿态做离散尝试，并没有真正释放绕观察轴的
    连续自由度。正式修复应优先增加“只约束末端前向指向目标、放开roll”的指向型IK，
    而不是永久退化为完全不约束方向。
  - 历史核对确认：`v1.2` 已有 `enableLookAt`、`faceAxis` 和完整Quaternion姿态IK；
    `v1.4`（提交 `2deef7d`）新增 `enableRollFallback/rollFallbackSteps`，按
    `±45/±90/±135/180°` 枚举观察轴roll。这是指向型IK的已有基础，但每个枚举项仍把
    目标前向轴和上向轴同时写入六维DLS误差，故每次仍是完整姿态约束，不等价于连续释放
    roll。后续实现应复用这些配置和接口，增加明确的姿态模式而不是重复另写一套规划器。
- `Assets/SimulationPlatform/Scripts/Behaviour/ModelCollisionHighlighter.cs`
  - 自动生成的 `_MjRoot` 子级 Hull 仅作为碰撞和射线代理；生成和保存碰撞体重载路径
    都不再给 Hull 添加本组件，因此不会独立处理鼠标高亮或选点点击。
  - 选点会统一解析到 Hull 对应的原模型 Transform；`MissionController` 入口还会按
    逻辑 Transform 再去重一次，防止旧列表或兼容加载数据重复加入同一目标。
  - 兼容旧 `.collider.xml` 或场景中已经残留本组件的 Hull：`Awake()`、鼠标、高亮和
    选中入口都会识别代理并早退，同时清理其静态高亮引用。原模型的 Renderer 状态在
    `Awake()` 中完成幂等初始化，已消除悬停 Hull 时因 `Start()` 未执行而产生的
    `SetModelOpacity()` 空引用。
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
  - `allowApproximateSolution` 仍要求位置和姿态误差分别不超过场景中的验收上限；
    `RunScene` 当前为位置 `0.01m`、姿态 `0.03rad`。日志中的“BIT*: IK 没有返回可接受
    候选”发生在 BIT* 搜索之前，不能解释为 BIT* 随机路径搜索失败。

## 崩溃线索

- 2026-06-24 排查过一次路径规划崩溃：Unity `Editor.log` 最后托管调用链在 `Simulation.OnPathPlanClicked()` -> `MissionController.DeepPrecomputeAll()` -> `BITStarPlanner.Plan()` -> `MujocoStaticIKSolver.SolveIK()`。
- 崩溃前最后日志为 BIT* 打印 IK 结果，随后 native 层报 `free(): invalid pointer` 和 `fatal signal 6`。
- 项目根目录 `MUJOCO_LOG.TXT` 曾出现 `Nan, Inf or huge value in QACC`，说明 MuJoCo 仿真可能进入数值不稳定状态。
- BIT*/IK 会为了 FK、碰撞检查、采样可视化而临时写 `MjScene.Instance.Data`。这类代码必须完整备份并恢复 `qpos/qvel/act/ctrl`，恢复后调用 `mj_forward`，不要只恢复 `qpos` 或只调用 `mj_kinematics`。

## 场景模型碰撞生成与诊断

- `Assets/AutoColliderGen_Final.cs` 将导入模型的各个 Mesh简化并生成 V-HACD凸包，
  再按开关创建 Unity `MeshCollider/Rigidbody` 和 MuJoCo `MjBody/MjGeom`。
- 旧实现无条件执行 `doVHACD = true`，`hollowParts` 实际不起作用；现已暴露为
  `forceVHACDForAllMeshes`，默认保持开启。`hullCount` 是每个源 Mesh的上限，装配体
  理论总上限为 `有效Mesh数 × hullCount`。
- `ModelImport` 会提前给原始 Mesh添加 Collider；生成器又会添加凸包 Collider，并可
  同时添加 MjGeom。诊断时要注意原始网格、PhysX凸包和 MuJoCo凸包的重复物理表示。
- `Assets/ColliderGenerationDiagnostic.cs` 由生成器自动挂到目标模型，默认显示多色半
  透明凸包，将与机器人 Collider包围盒相交的凸包标红，并一次性输出凸包/耗时、组件
  数、PhysX穿透、机器人位姿变化和生成期间的 MjScene重建次数。
- `createUnityPhysicsColliders` 控制 MeshCollider/Rigidbody/高亮脚本；
  `createMujocoGeoms` 控制 MjBody/MjGeom。两者都关闭时为仅显示诊断，两者都开时
  保持旧行为。
- MuJoCo插件的 `MjComponent` 在运行时新增后会请求 `MjScene.RecreateScene()`；生成器
  逐零件 `await` 可能使装配体触发多次整场景重建。插件重建只缓存关节 qpos/qvel，
  不完整缓存 act/ctrl，这既会卡顿，也可能造成机器人状态突变。
- VHACD插件输出 Mesh时只设置顶点和三角形，原本没有法线；直接交给
  `MjMeshShape.DebugDraw` 会在选中层级时重复报 Gizmo缺法线错误。生成器现先补算法线。
- 2026-08-13 对装配体 `simple_20260813144530` 的首次实测：31个源 Mesh生成80个凸包，
  生成后共111个 MeshCollider和80个 MjGeom，耗时18.29秒。结束检查没有发现 PhysX
  包围盒重叠或确认穿透，但机器人移动15.6483米，期间 MjScene恰好重建31次。当前第一
  嫌疑是逐零件 MuJoCo场景重建造成状态跳变。
- 随后的仅 PhysX对照实验使用同一模型和同样80个凸包：MjGeom=0、MjScene重建=0，
  PhysX重叠/穿透均为0，机器人位移和转角均为0；耗时由18.29秒降为10.95秒。由此已
  确认机器人位姿跳变主因是运行中逐零件添加 MjGeom触发的 MuJoCo反复重建，而不是
  PhysX凸包挤压机器人。
- `AutoColliderGen_Final`现默认使用批量 MuJoCo更新：所有新 `_MjRoot`先在未激活状态
  完成构建，最后统一替换旧生成物，使插件把组件增删合并为一次 MjScene重建。重建前
  保存时间、qpos/qvel/act/ctrl、warm-start、外力和 mocap状态，完成后恢复并执行
  `mj_forward`。`batchMujocoSceneRebuild`与`restoreMujocoStateAfterRebuild`是对应开关。
- 新批量方案的运行验收标准：同一31 Mesh/80凸包装配体应得到 `MjGeom=80`、
  `MjScene重建=1`、机器人位移接近0且无 PhysX穿透；代码编译已通过，实际运行结果仍需
  Unity下一次测试确认。
- 批量方案首次运行在统一重建后触发 MuJoCo原生错误：`mj_stackAlloc: out of memory`
  （ncon=265、nefc=1310，默认arena仅约200KB），随后Unity收到fatal signal退出。现将
  RunScene及生成器的MuJoCo arena配置为64M，并让生成场景凸包使用
  `contype=2/conaffinity=1`以禁止场景凸包互相碰撞、保留与默认1/1机器人的碰撞。下一次
  运行仍需确认实际ncon/nefc与机器人位移。
- Unity日志可能在重启后由 `Editor.log` 轮转为 `Editor-prev.log`；碰撞诊断复核还应对照
  项目 `Logs/Log_*.log` 的时间，不能只检查当前 `Editor.log`。
- 2026-08-13 16:30批量重建没有再退出，但恢复后的第一次`mj_forward`立即得到
  `ncon=247 / nefc=1220`，约5秒后机器人移动`1.0673 m`；同次PhysX检查仍为0穿透。
  这表明状态快照恢复已经执行，后续位姿变化主要由MuJoCo接触约束驱动，仍需确认具体
  接触对象和生成网格的运行时坐标。
- 原始`simple_20260813144530.glb`的网格访问器以毫米为尺度，顶层节点矩阵包含`0.001`
  比例；MuJoCo插件`MjMeshShape/MjcfGenerationContext`只导出`mesh.vertices`，不写入
  Transform缩放；项目使用的glTFast 6.14.1会把矩阵分解后的比例赋给
  `Transform.localScale`，不会烘焙进Mesh顶点。生成器现为MuJoCo单独复制网格，并用
  `rigidWorld.inverse * localToWorldMatrix`把层级缩放/镜像/剪切烘焙进顶点；PhysX、诊断
  显示和保存数据仍使用原局部网格。`Simulation`重新加载`.collider.xml`时也执行同样
  烘焙，避免下次加载复发。诊断同时记录lossyScale、局部/世界尺寸以及`mj_forward`后
  最深的20组MuJoCo几何体接触名称和距离。
- 缩放修复后首次运行的`mj_forward`已从`ncon=247/nefc=1220`降到`ncon=1/nefc=12`，
  证明原先绝大多数接触来自遗漏Transform比例的超大MuJoCo网格。随后发生的
  `munmap_chunk(): invalid pointer`并非几何或arena问题，而是接触诊断调用自动生成的
  `mj_id2name`绑定导致：原生函数返回内部只读字符串，该绑定声明为C# `string`后错误
  处理了内存所有权。现已完全禁用该调用，改用活动`MjGeom.MujocoId`建立纯托管名称映射。
- 2026-08-13最终复测通过：31个源Mesh生成80个凸包耗时11.14秒，MjScene只重建1次，
  机器人位移/转角为`0/0`，PhysX重叠/穿透为`0/0`；后续BIT*完成2个任务，用户用Ctrl
  主动拖动机械臂接触支架也能正常碰撞，运行正常退出。唯一初始MuJoCo接触是RunScene
  原有`default (1) <-> geom_13`（1点、dist约-4.10mm），不是动态生成的`_Hull_`凸包，
  且未造成位姿变化。当前问题可视为运行验收完成。

## 约定

- Unity 与 MuJoCo 坐标换算常见写法：MuJoCo `(x, y, z)` 对应 Unity `(x, z, y)`。
- 对真实设备相关逻辑保持保守，不要自动下发危险动作；优先先稳定仿真路径规划。
- Player 打包使用不含 Unity Editor 程序集的独立编译配置；运行时脚本不可无条件引用 `UnityEditor` 或 `UnityEditorInternal`。2026-07-23 已清理 ZCalendar 两处未使用的 Editor-only 引用。

## 正式运行状态界面

- Build Settings 当前启用的正式运行场景是 `Assets/SimulationPlatform/Scenes/RunScene.unity`。
- `Assets/SimulationPlatform/Scripts/Tool/SceneWindowSetting.cs` 会在每个场景
  `Awake()` 中调用 `Screen.SetResolution`。2026-07-24 已将 LoginScene、
  Main、RunScene、MainScene、脚本新组件默认值及 Player Settings 统一为
  `1850×1015` 窗口模式，以适配 1920×1080 Ubuntu 桌面的顶栏、标题栏和边框。
- Main、RunScene、MainScene 的正式屏幕空间 Canvas 使用
  `Scale With Screen Size / Reference 1920×1080 / Expand`，按 1920×1080
  设计稿整体缩放到 1850×1015，避免右侧曲线和底部按钮因固定像素布局而重叠。
- LoginScene 原本已经使用 `Scale With Screen Size`，保留其既有
  `1095×685` 参考配置；RunScene 的世界空间 `PointerCanvas` 也保持原配置。
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

## CR10AF 与视觉引导控制边界

- 真实机械臂为越疆 `CR10AF`，RunScene 当前配置地址
  `192.168.192.19`。
- `DobotController` 持有 `29999` Dashboard 控制连接和 `30005`
  反馈连接；反馈包为 1440 字节，当前解析 RobotMode、速度比例、六关节角和关节速度。
- `ConnectCommander` 把仿真规划关节结果转换为角度后，通过同一个
  `DobotController.MoveJoints()` 下发粗定位。
- 自动流程推荐保持 Unity 为 CR10AF 唯一运动命令所有者：
  `仿真规划→Unity粗定位→实际反馈确认→Python视觉只测量→Unity变换/校验并精定位→拧紧`。
- Python 视觉服务不应直接连接 CR10AF。若未来必须做高频视觉伺服，应引入单一
  机器人网关统一仲裁，而不是 Unity/Python 轮流断开并抢占控制权。
- 越疆 V4 `RequestControl()` 用于切换 TCP 控制模式，文档要求机器人处于未上电
  或下使能且非暂停/松抱闸状态；这也是不采用阶段性控制权交接的重要原因。
- 视觉精定位前必须把当前基于估算时间的机械臂等待改成反馈闭环，至少验证：
  30005 数据新鲜、RobotMode 空闲、六关节速度接近零、实际关节在目标容差内。
- 视觉测量需要明确相机安装方式：
  - 眼在手上：`T_base_target = T_base_tool × T_tool_camera × T_camera_target`。
  - 固定相机：`T_base_target = T_base_camera × T_camera_target`。
- 所有视觉结果进入运动规划前必须带时间戳/置信度，并经过工作空间、关节限位、
  最大单次修正、碰撞和速度限制检查。

## NavMesh 运行时烘焙

- `Assets/SimulationPlatform/Scripts/Function/Simulation.cs` 的 `OnPathPlanClicked()` 会在开始任务前调用 `RebuildRuntimeNavMesh()`。
- 2026-06-25 调整后，运行时烘焙不再只依赖机器人根节点的 `NavMeshModifier`，而是对 `MissionController.gameObject` 整棵子层级逐个添加/更新 `NavMeshModifier`。
- 原因：`NavMeshSurface` 会用自身 `LayerMask` 过滤 `NavMeshModifier` 所在物体的 Layer；如果根节点在 `Robot` 层但 Surface 不包含 `Robot` 层，根节点 modifier 可能不会生效。
- `SampleScene` 中 `cr10_robot356/ground` 是 MuJoCo 世界地面，不属于机器人障碍物，必须保留进 NavMesh 烘焙；运行时代码会跳过名字为 `ground`/`floor`/`plane` 的世界地面对象，并确保它们 `ignoreFromBuild=false`。
- 其他机器人子物体会设置 `ignoreFromBuild=true`，用于排除底盘和机械臂本体。
- 新日志会打印实际重建的 Surface 层级路径、排除机器人对象数、保留地面对象数、Surface LayerMask，以及该 Surface 是否包含机器人根节点 Layer。
- 多目标预计算日志使用 `[底盘预计算]` 前缀，包含每个目标的起点、目标、NavMesh
  采样点、起点采样偏移、`CalculatePath` 返回值、路径状态、角点数、最终停靠点、
  移动量和路径点数。
- 2026-08-13 当天功能修改及跨版本合并顺序见
  `.codex/PORTING_NOTES_2026-08-13.md`。

## IK 姿态约束模式

- `Assets/MujocoStaticIKSolver.cs` 支持三种 `OrientationConstraintMode`：
  `PositionOnly` 只约束位置；`DirectionOnly` 约束末端 Site 的 Z 前向轴；`FullPose`
  同时约束前向和上向，锁定完整 Quaternion。
- `DirectionOnly` 不只是删除 Up 轴误差，还会对角速度 Jacobian 使用
  `P = I - ff^T` 投影，去掉绕当前前向轴的速度分量，因此 roll 是连续零空间自由度。
- 原有离散 `enableRollFallback/rollFallbackSteps` 只在 `FullPose` 中使用；它不能替代
  DirectionOnly。
- 无目标 Quaternion、上层 `enableLookAt=false` 或 `rotWeight<0.001` 时，求解器自动按
  `PositionOnly` 运行，旧调用接口保持兼容。
- `RunScene` 正式配置为 `enableLookAt=true`、`orientationConstraintMode=DirectionOnly`、
  `faceAxis=(0,0,1)`。上层仍负责生成“朝向目标”的 Quaternion，IK只采用其前向轴。
- 运行日志 `[IK姿态] mode=...` 是确认实际模式的首要证据；DirectionOnly 的 `rotErr`
  表示末端前向轴夹角（弧度），不再表示完整 Quaternion/roll 误差。
- 2026-08-14 切换前的隔离实测证明：同一高位单点在固定完整姿态下失败，在纯位置下
  三次均以约0.77~0.99mm位置误差成功且升降轴为0。正式 DirectionOnly 实现后仍需用
  该单点和原三点任务在 Unity 中回归。
- 该功能跨版本迁移需要合并 `Assets/MujocoStaticIKSolver.cs`，并在目标正式场景 Inspector
  设置 DirectionOnly、恢复 `enableLookAt`；无需修改 BIT*、ArmController 或
  MissionController 的公开规划接口。
- 2026-08-14 首次 DirectionOnly 实测确认模式已生效，但当前高位单点仍失败：固定
  Unity `+X` 接近方向下，最佳无碰撞结果为位置 `31.78mm`、方向 `3.98度`，超过
  `10mm/1.72度` 验收上限。它比旧 FullPose 的 `38.86mm/4.79度` 有改善，但说明释放
  roll 仍不足以解决该站位的固定接近方向约束。
- 同一点 PositionOnly 可稳定达到约1mm，所以下一步应区分精确方向几何不可达与DLS局部
  极小：先记录碰撞前后最优候选、失败qpos、关节限位和最终前向；随后优先尝试
  PositionOnly预热+位置优先DirectionOnly，必要时再讨论约5度方向锥或备用底盘站位。
- `MujocoStaticIKSolver` 现已实现上述第一优先方案：DirectionOnly候选默认先进行内部
  PositionOnly预热，再以位置为一级任务、投影方向任务到 `I-Jp#Jp` 零空间进行精化。
  预热不产生实际中间动作，最终仍只把通过现有精度和碰撞验收的目标交给BIT*。
- 对应回退字段为 `enablePositionWarmStart`、`enablePositionPriorityDirectionSolve` 和
  `positionWarmStartMaxIterations`；默认分别为true、true、2000。它们只影响
  DirectionOnly，未改变PositionOnly/FullPose公开入口。
- DirectionOnly失败时应优先查看 `[IK预热汇总]`、`[IK碰撞汇总]`、`[IK方向诊断]`、
  `[IK关节限位]` 和必要时的 `[IK碰撞候选]`。碰撞前后最佳候选会分开记录，Geom名称
  只通过托管MjGeom解析，不调用`mj_id2name`。
- 该分层方案没有放宽RunScene的 `10mm/0.03rad` 近似上限，也没有启用方向锥；若实测
  仍失败，应根据新日志决定是搜索备用底盘站位，还是由任务精度要求决定是否允许方向锥。
- 2026-08-14 分层方案三次运行复测：PositionOnly预热每轮约29~30/40次严格收敛，最佳
  预热位置误差0.73~0.81mm；最终位置优先候选稳定为2.90mm位置误差、11.933度方向误差。
  全局最佳候选无碰撞，六个转动关节均远离限位，说明当前位置任务和分层开关正常，失败
  集中在固定原点底盘下的精确 `+X` 接近方向。继续相同随机重试价值很低；应优先做方向
  可行性/备用底盘站位扫描。当前11.933度残差也意味着5度方向锥不足以直接接纳该候选。
- 用户接受以“真实相机能够看见接头”为方向精度目标，倾向先采用较大的DirectionOnly方向
  锥而不增加备用底盘站位搜索。当前推荐第一档为半角15度（0.261799rad）：只放宽
  DirectionOnly最终近似验收，不修改10mm位置上限、碰撞检查和方向优化过程。真实视频是
  1280x720 RealSense链路，不应拿RunScene的Unity Camera FOV代替硬件FOV；上线前需要用
  现场视频确认接头在画面中部且视觉识别稳定。
- 方向锥约束的是`tip`到达固定观察点后的前向轴，不是末端位置区域：ArmController先把
  观察点设为接头沿机器人方向退后`observationDistance`（当前约0.25m），再生成观察点到
  接头的理想LookRotation。IK位置误差为`tip`到观察点的距离，DirectionOnly角度误差为
  `tip` Z前向轴与理想视线的夹角。真实相机若与`tip`存在光心/光轴外参偏差，仍需标定或
  现场画面验证，不能只凭IK角度断言接头一定入画。
- 2026-08-14经用户确认，RunScene已将`maxAcceptedRotationError`设为`0.2617994rad`
  （DirectionOnly半角15度）。严格方向停止阈值仍为0.005rad、位置近似上限仍为10mm、
  碰撞检查仍开启；因此这是最终近似候选验收放宽，不是取消方向优化或放宽末端位置。

## GitHub 正式版本发布流程

- 正式发布使用 `release/software-v1.0` 分支；每个发布版本创建对应的带注释标签，例如
  `v1.7`，并在 GitHub 上基于该标签创建 Release、填写版本更新说明。
- 发布前先在 `ProjectSettings/ProjectSettings.asset` 更新 `bundleVersion`，确保软件内部
  版本号与 Git 标签一致。
- 先执行 `git fetch origin` 和 `git status -sb`，确认本地分支与远程分支没有意外的
  `ahead/behind`；若远程领先，应先停止并检查，不在脏工作区中盲目拉取。
- 仅在已经确认整个非忽略工作区都属于本次版本时使用 `git add -A`。本项目的大型 FBX
  和字体资源由 `.gitignore` 排除；发布前仍应用暂存区文件列表和大小检查确认它们没有
  被跟踪或误加入。
- 提交前使用 `git --no-pager diff --cached --stat` 检查范围，并执行
  `dotnet build "My project21.5.sln" --no-restore`；编译必须为0错误，既有警告需在发布
  记录中注明。
- 推荐顺序为：提交代码、推送发布分支、确认标签不存在、创建带注释标签、单独推送标签：
  `git commit` → `git push` → `git tag -a` → `git push origin <tag>`。分支和标签是不同
  Git引用，普通分支推送不会自动上传标签。
- 发布后用 `git status -sb` 和 `git log -1 --decorate` 确认本地分支、远程分支和版本
  标签指向同一提交；随后在 GitHub `Releases` 中选择已有标签，填写“新增功能、功能优化、
  工程调整、编译验证、注意事项”等内容并发布。
- 后续建议在每次打标签前同步维护仓库根目录 `CHANGELOG.md`。GitHub Release 面向版本
  使用者，`CHANGELOG.md` 用于在仓库内长期保存完整版本历史。

## 伍老师21.4功能集成边界（2026-08-19）

- 跨版本集成以21.5/v1.7为主线，只移植伍老师版本中能对应具体反馈、且不会覆盖21.5
  后续能力的小块改动；不要直接替换整个脚本、场景或项目目录。
- 视角状态由`CameraController`提供读写接口，保存到`SimulationParam`，并同步写入
  `ProjectRecord`字段兼容伍老师旧XML；恢复必须安排在模型相机复制/自动取景之后。
- 项目记录中的`Replaces`必须在加载最后一条记录时清空并回填，随后等主模型实例化完成，
  按`JointReplaceRecord.HierarchyIndices`和`JointId`逐条重放；完成后再恢复视角并重建
  NavMesh。
- 替换已选接头必须同时清理`ModelCollisionHighlighter.selectedObject`、
  `SeletectedObjects`和`PathPointManager`标记，并通过`ResolveLogicalSelectionTarget()`处理
  21.5新增的碰撞代理，不能只隐藏旧GameObject。
- `MainScene`的`SceneEdit.ColliderBtn`已绑定“网格切割”按钮，继续复用现有
  `AutoColliderGen_Final`生成流程；Main/MainScene不可互相整场覆盖，因为两者UI布局和
  进入路径不同。
- 伍老师的`SelectedObjectRecord`目标点持久化暂不采用：原方案没有可靠恢复红色高亮，
  路径点坐标和节点匹配也存在歧义。若以后确实要跨会话保存规划目标，应重新设计稳定节点
  标识、目标顺序、路径标记世界坐标和高亮恢复，而不是直接复制该提交。
- 当前集成工作位于本地`integration/wulaoshi-21.4`分支；`v1.7`仍固定在发布提交，后续
  需要Unity运行回归通过后再决定合并和发布新版本。

## 切割碰撞网格持久化（2026-08-27，第二阶段修正版）

- 碰撞网格sidecar位于场景GLB同目录。场景基础缓存命名为`<模型>.collider.xml`；替换
  接头后的项目最终装配命名为`<模型>.project-<Project.Id>.collider.xml`，不同项目互不覆盖。
- `ColliderModel`当前格式为v2。每个Root保存可读的`ParentPath`和唯一定位用的
  `ParentIndexPath`；GLB中同名节点很多，禁止恢复为单独调用`Transform.Find(ParentPath)`。
  旧v1文件只在名称路径唯一时兼容恢复，发生歧义必须提示用户重新切割。
- 切割结果必须从`_MjRoot`子级的`MeshCollider.sharedMesh`提取。默认关闭碰撞体可视化时
  Hull没有`MeshFilter`，以后不要恢复为扫描`MeshFilter`，否则会保存原始显示网格。
- `SceneEdit.OnColliderBtnClick()`在切割成功后立即调用保存；用户不需要再点一次“保存场景”。
  但新建场景或替换GLB后必须先保存场景，让模型进入Scene专属Files目录，才能切割。
- `ColliderManager.ApplyColliderDataAndWaitAsync()`是共用恢复入口：按索引路径定位并用名称
  校验，创建`MjBody/MjGeom/MeshCollider/Rigidbody`，等待`MjScene.postInitEvent`后核验每个
  `MjGeom`的MuJoCo名称和ID。恢复前清理已有`_MjRoot`，避免重复加载。
- 项目加载顺序必须是“重放全部接头替换 -> 恢复项目级碰撞 -> 恢复相机和NavMesh”。项目
  页切割后必须立即提取并按Project.Id保存最终装配；不能只调用`Generate()`。
- 诊断日志统一使用`[碰撞持久化/*]`前缀。真正成功的判据是
  `[碰撞持久化/MuJoCo验证] ... Result=PASS`，不能只看Unity创建了多少对象。
- 项目`TagManager.asset`没有`VHACD` Tag，恢复代码不得执行`gameObject.tag = "VHACD"`；
  是否为凸包使用`ColliderMeshData.IsVHACD`判断。
- 生成和恢复时都必须直接设置`MjGeom.ShapeType = Mesh`；不能只在`UNITY_EDITOR`条件下
  修改SerializedProperty，否则打包Player会使用默认Sphere。
- 2026-08-27运行复测已确认项目级最终装配缓存闭环成功：重新进入包含替换接头的项目后
  恢复31个Root、76个Mesh，MuJoCo绑定76/76并输出`Result=PASS`。这比只检查Unity层级
  数量更可靠，后续仍应以`[碰撞持久化/MuJoCo验证]`为最终判据。
- 刚切割与保存恢复使用相同调试颜色`RGBA=(0, 1, 0, 0.4)`。仿真界面的
  `RuntimeConsole`会在日志按钮左侧动态创建“隐藏碰撞/显示碰撞”按钮；该按钮只切换
  `_MjRoot`子级`MeshRenderer.enabled`，绝不能停用`MeshCollider`、`MjGeom`或整个对象。
- `ModelTool.AddMeshCollidersToModel()`必须跳过`_MjRoot`生成代理，并复用已有
  `MeshCollider/Rigidbody/ModelCollisionHighlighter`。否则模型管理页先恢复碰撞后，基础
  导入初始化会把代理再次当普通显示模型处理，引发重复Rigidbody异常。
- 网格数值使用`CultureInfo.InvariantCulture`和round-trip格式保存，读取时也必须使用同一
  Culture；大网格超过65535顶点时设置`IndexFormat.UInt32`。
- XML写盘采用同目录`.tmp`临时文件和`File.Replace/File.Move`，防止覆盖过程中留下半份
  文件。完成后不应残留`.tmp`；替换成功后的`.bak`会尽力清理。
- 本功能位于本地`feature/collider-persistence`分支，基线为SoftWare1.8提交`2c9ab48`。
  Unity 2022.3.62f2c1批处理导入编译已通过。已有旧XML必须在模型管理页重新切割一次；
  每个含替换接头的项目还需在项目页切割一次生成项目级缓存，再验证重进和真实碰撞。
