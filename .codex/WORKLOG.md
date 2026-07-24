# Worklog

## 2026-07-24 - v1.6 发布与 GitHub 上传准备（进行中）

### 本次任务目标

- 对当前版本执行 Unity 软件打包前检查。
- 生成 Linux 64 位正式包。
- 将本版提交到 `release/software-v1.0` 并发布为 GitHub `v1.6`。

### 已读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `.gitignore`
- `ProjectSettings/ProjectSettings.asset`
- `ProjectSettings/EditorBuildSettings.asset`
- `Assets/VisionImageReceiver.cs`
- `Assets/ServoTighteningController.cs`
- `ExternalCode/*.py`
- 当前 Git 分支、远程、标签、状态和场景差异

### 当前修改

- 产品名从 `My project2` 改为“飞机导管拧紧系统”。
- `bundleVersion` 从 `0.1` 改为 `1.6.0`。
- 新增 `Assets/Editor/ExternalRuntimeBuildPostprocessor.cs`：
  - Linux 打包完成后自动复制 `ExternalCode/*.py`。
  - 自动复制 `Assets/微软雅黑.ttf`。
  - 缺少脚本或字体时主动令构建失败，避免生成不能运行的假完整包。
- 新增 `DEPLOYMENT.md`，记录 Linux 打包、Python/RealSense 依赖、端口、数据目录和人工验收清单。
- `.gitignore` 新增 Python 缓存、拧紧结果日志和外部程序日志排除规则。

### 审计结论

- 当前分支为 `release/software-v1.0`，远程为
  `https://github.com/siwazuixih/My-project21.git`。
- 远程当前提交和本地基线均为 `SoftWare1.5`，且已有 `v1.5` 标签；
  本次应使用新提交和 `v1.6` 标签，不能覆盖旧标签。
- Build Settings 已启用 Login、Main、RunScene、MainScene 四个场景。
- `Assets/微软雅黑 SDF.asset` 约 128 MB，超过 GitHub 普通文件上限并已被忽略；
  其他电脑从仓库打开时会使用已有 TMP 中文字体回退。
- `dipan2.fbx`、`机舱.fbx` 和 `Files/` 大型数据仍按现有规则不进入 GitHub，
  完整迁移工程时需要另行提供或后续启用 Git LFS。
- 未发现本次待提交脚本中的密码、Token 或 API Key。

### 当前阻塞和下一步

- 第一次命令行构建未开始，因为 Unity 检测到该工程正在另一个 Editor 实例中打开；
  Unity 正常拒绝多实例，没有生成半成品。
- 用户需要先在 Unity 中保存并关闭工程。
- 关闭后继续：
  1. 生成 `Builds/SoftWare1.6/AircraftTubeTighteningSystem.x86_64`。
  2. 核对外部 Python 脚本、字体和构建日志。
  3. 更新本条记录的最终结果。
  4. 提交、创建 `v1.6` 标签并推送 GitHub。

## 2026-07-24 - Unity 拧紧程序控制按钮

### 本次任务目标

- 将 V26 原程序的三个控制动作放到 Unity“启动程序”按钮旁边。
- 明确区分“启动 Python 程序”和“让真实电批开始运动”。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/ServoTighteningController.cs`
- `Assets/VisionImageReceiver.cs`
- `ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py`
- `Assets/SimulationPlatform/Scenes/RunScene.unity` 中底部工具栏尺寸和位置

### 修改的文件

- `Assets/ServoTighteningController.cs`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动

- 原“启动拧紧/关闭拧紧”改名为“启动程序/关闭程序”，该按钮只管理 V26 Python 后台程序。
- 在其右侧运行时创建三个紧凑按钮：
  - “开始拧紧” → `forward`
  - “反转回位” → `reverse`
  - “立即停止” → `stop`
- 三个按钮沿用日志按钮的字体和交互样式，宽度缩为 82 像素，避免与右侧原有运行控制按钮重叠。
- 只有 V26 程序就绪且处于真实运行界面时才显示可操作状态；两个运动按钮还要求电批连接成功。
- “开始拧紧”和“反转回位”保留真实设备安全门：
  - 第一次点击只进入确认状态，按钮显示“再次确认”。
  - 必须在 3 秒内再次点击同一按钮才会临时解锁并发送运动命令。
  - 命令入队后立即重新锁定，下一次运动仍需再次确认。
- “立即停止”不要求确认；点击时同时取消待确认动作并锁住后续运动。
- 切换到仿真界面时，新增按钮随原程序按钮隐藏，现有安全停止程序逻辑保持不变。
- 没有修改场景 YAML，也没有连接或驱动真实电批。

### 为什么这样改

- “启动程序”与“开始拧紧”分开后，操作含义清楚，也适合后续自动流程分别调用程序生命周期和运动指令接口。
- 对真实运动保留二次确认，同时让停止命令始终直接可用，可降低误触风险。

### 验证情况

- 使用 Unity 2022.3.62f2c1 的完整 `Assembly-CSharp` 参数编译通过。
- 没有新增 C# 错误，仅有项目原有警告。
- 静态检查确认按钮映射继续复用 `StartTighteningAsync`、`ReverseHomeAsync` 和 `StopToolAsync`，没有新建旁路协议。
- 未做真实电批动作测试。

### 当前状态、后续及手动检查

- 代码和编译验证已完成。
- 重新进入 Play Mode 和“真实运行”后，检查按钮是否依次显示为：
  - 启动程序
  - 开始拧紧
  - 反转回位
  - 立即停止
- 未启动程序时三个动作按钮应为禁用；程序和电批连接就绪后两个运动按钮启用，停止按钮在程序就绪后启用。
- 在确保设备周围安全、急停可用后，再测试二次确认和停止功能。
- 检查新增按钮与右侧原有“重置底盘”等按钮是否保持间距。

## 2026-07-24 - 去除力矩曲线区域的 LIVE 标记

### 本次任务目标

- 去掉“实时力矩曲线”右上角从视频占位图继承的 `LIVE` 标记。
- 保留“现场视频”区域原有的 `LIVE` 标记。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/ServoTighteningController.cs`
- `Assets/VisionImageReceiver.cs`
- `Assets/SimulationPlatform/Resources/Run/Camera.png`

### 修改的文件

- `Assets/ServoTighteningController.cs`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动及原因

- 确认 `LIVE` 来自公用视频占位图 `Camera.png`，不是 Python 力矩曲线绘制内容。
- 力矩控制器绑定曲线区域后，始终显示自己的深色空白 `RawImage`；启动程序后再用实时曲线纹理替换。
- 拧紧程序关闭或异常退出时，曲线图层恢复为空白深色而不再隐藏，防止下层带 `LIVE` 的视频占位图重新露出。
- 没有修改公用 `Camera.png`，避免影响现场视频区域；没有修改 `RunScene.unity`。

### 验证情况

- 使用 Unity 2022.3.62f2c1 的完整 `Assembly-CSharp` 编译参数检查通过。
- 没有新增 C# 错误，仅有项目原有警告。
- `git diff --check` 通过。

### 当前状态、后续及手动检查

- 代码处理已完成。
- 需要重新进入 Unity Play Mode，确认力矩框在程序启动前、运行中和关闭后均不再显示 `LIVE`。
- 同时确认现场视频右上角的 `LIVE` 仍正常保留。

## 2026-07-24 - 现场视频双击放大窗口

### 本次任务目标

- 为右侧“现场视频”增加与力矩曲线一致的双击放大交互。
- 放大时继续使用现有 RealSense 采集和 Unity纹理，不增加第二路相机或网络请求。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/VisionImageReceiver.cs`
- `Assets/TorqueCurvePointerHandler.cs`
- `Assets/ServoTighteningController.cs`

### 修改的文件

- `Assets/VisionImageReceiver.cs`
- 新增 `Assets/VisionVideoPointerHandler.cs`
- 新增 `Assets/VisionVideoPointerHandler.cs.meta`
- 更新 `.codex/PROJECT_CONTEXT.md`
- 更新 `.codex/WORKLOG.md`

### 具体改动

- `RealSenseLiveImage` 开启 UI Raycast，并绑定 `VisionVideoPointerHandler`。
- 双击右侧小视频后，在根 Canvas 上运行时创建：
  - 全屏半透明遮罩 `VisionVideoPopupOverlay`
  - 居中视频面板 `VisionVideoPopupPanel`
  - 标题“现场视频（双击缩小）”
  - 右上角关闭按钮
  - 大图 `LargeVisionVideoImage`
- 弹窗根据当前根 Canvas 尺寸自适应，上限约 1120 像素宽；视频区域保持约 16:9。
- 弹窗标题优先使用项目现有“微软雅黑 SDF”，找不到时回退到视频按钮现有 TMP 字体。
- 支持以下关闭方式：
  - 双击大视频
  - 点击右上角“×”
  - 点击面板外半透明遮罩
  - 按 `Esc`
- 视频小图和弹窗大图共用 `latestTexture`：
  - 每次收到新帧时同时更新两个 RawImage引用。
  - 不新增 HTTP 请求。
  - 不重复启动相机。
  - 不复制额外 Texture2D。
- 关闭视频、切回仿真、切换场景或退出应用时，会先关闭弹窗，再销毁纹理和释放相机。
- 没有修改 `RunScene.unity` 或其他场景 YAML。

### 为什么这样改

- 复用现有纹理可以保持大小画面完全同步，并避免增加相机、JPEG下载和内存负担。
- 运行时创建弹窗不会干扰用户当前已修改的正式场景布局。

### 验证情况

- 使用完整 Linux Player `Assembly-CSharp` 参数编译通过：
  - `VisionImageReceiver.cs`
  - `VisionVideoPointerHandler.cs`
  - 同时包含力矩弹窗相关脚本，未产生类型冲突。
- 没有本次新增的 C# 错误；只存在项目原有警告和旧 Roslyn分析器警告。
- `git diff --check` 对本次视频脚本通过。
- 静态检查确认：
  - 新帧会更新弹窗纹理。
  - `StopVideo()` 会先关闭弹窗再释放纹理。
  - `ReleaseLatestTexture()` 会清空弹窗纹理引用。

### 当前是否完成

- 视频双击放大功能和编译验证已完成。
- 需要在 Unity Play Mode 中手动确认点击范围、弹窗大小和关闭交互。

### 还存在的问题

- 当前没有自动化的 Unity UI 指针点击测试。
- 放大画面的清晰度取决于当前 RealSense `1280×720` JPEG，正常情况下足以显示约 1120 像素宽的大图。

### 下一步

- 重新进入 Play Mode并开启视频。
- 双击右侧小视频，依次测试四种关闭方式。
- 确认弹窗打开期间视频持续更新，关闭视频时弹窗自动消失且相机释放。

### 需要在 Unity Editor 检查

- 单击小视频不应打开弹窗，双击应打开。
- 弹窗视频是否保持比例、没有明显拉伸。
- 双击大图、“×”、遮罩和 `Esc` 是否均能关闭。
- 曲线弹窗与视频弹窗是否互不影响。

## 2026-07-24 - 力矩曲线中文化与双击放大窗口

### 本次任务目标

- 将实时力矩曲线和最终保存 PNG 中的可见内容中文化。
- 使用项目已有微软雅黑字体，避免中文显示为方框。
- 支持双击右下角小曲线，在 Unity 内显示清晰的大图弹窗。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/ServoTighteningController.cs`
- `ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py`
- `Assets/微软雅黑.ttf`
- `Assets/微软雅黑 SDF.asset`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`

### 修改的文件

- `ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py`
- `Assets/ServoTighteningController.cs`
- 新增 `Assets/TorqueCurvePointerHandler.cs`
- 新增 `Assets/TorqueCurvePointerHandler.cs.meta`
- 更新 `.codex/PROJECT_CONTEXT.md`
- 更新 `.codex/WORKLOG.md`

### 具体改动

- 字体处理：
  - Python/Matplotlib 使用项目字体源文件 `Assets/微软雅黑.ttf`。
  - Unity弹窗优先查找并使用 `Assets/微软雅黑 SDF.asset`。
  - 如果运行时未找到指定 SDF，弹窗回退到现有拧紧按钮使用的 TMP 字体。
  - Unity启动 V26 时通过 `SERVO_CURVE_FONT` 传入字体绝对路径。
- 绘图中文化：
  - `Time-Torque Curve` → `时间—力矩曲线`
  - `Time (s)` → `时间（秒）`
  - `Torque` → `力矩`
  - `filtered` → `滤波力矩`
  - `raw` → `原始力矩`
  - `OK threshold` → `合格阈值`
  - `Target` → `目标力矩`
- 状态只在显示层翻译，内部协议和自动化状态码保持不变：
  - `IDLE` → `空闲`
  - `MONITOR` → `监控中`
  - `RUN_FORWARD` → `正在拧紧`
  - `OK` → `拧紧合格`
  - `NG_SLIP` → `滑牙异常`
  - `JAM/JAM_WARN` → `卡滞异常/卡滞预警`
  - `NG_DEVICE` → `设备异常`
  - `STOP` → `已停止`
  - `HOME_READY` → `回位完成`
- 中文化同时应用于：
  - Unity实时预览 JPEG。
  - V26 最终保存的高分辨率 PNG。
  - 单独运行 V26 时的 Matplotlib桌面图和三个控制按钮。
- 将实时图从 `574×324` 提升到 `960×540`，小框自动缩小，大图弹窗保持清晰。
- 双击放大：
  - `ServoTorqueCurveImage` 开启 UI Raycast。
  - 新增 `TorqueCurvePointerHandler`，使用 `PointerEventData.clickCount` 识别双击。
  - 双击右下角小图打开 Unity内部模态弹窗。
  - 弹窗尺寸根据根 Canvas 自适应，上限约 1120 像素宽，曲线区域保持约 16:9。
  - 弹窗与小图共用同一个实时 Texture2D，不增加第二路 HTTP 请求。
  - 弹窗标题为“实时力矩曲线（双击缩小）”，使用微软雅黑 SDF。
  - 支持双击大图、右上角“×”、点击遮罩或按 `Esc` 关闭。
  - 关闭拧紧程序、程序异常退出、切换场景或退出应用时自动关闭弹窗并释放纹理。
- 没有直接修改用户当前改动较多的 `RunScene.unity`。

### 为什么这样改

- SDF字体资产只能供 Unity TMP 使用，Matplotlib绘入 JPEG 必须使用原始 `.ttf`，因此两端分别复用同一微软雅黑字体族的正确资源。
- 内部状态码继续使用英文，避免中文显示改动破坏 TCP协议和后续自动流程判断。
- 大小图共享纹理可避免重复网络拉取和额外纹理内存增长。

### 验证情况

- Python源码语法编译检查通过。
- 使用完整 Linux Player `Assembly-CSharp` 参数编译通过：
  - `ServoTighteningController.cs`
  - `TorqueCurvePointerHandler.cs`
  - 仅有项目原有警告和旧 Roslyn 分析器警告。
- 对本次脚本执行 `git diff --check` 通过。
- 使用隔离配置验证中文实时曲线：
  - 电批地址 `127.0.0.1:1`
  - 控制端口 `19100`
  - 曲线端口 `19101`
  - 未连接或驱动真实电批。
- 曲线接口返回：
  - HTTP 200
  - 有效 `960×540` JPEG
  - 测试图约 37 KB
  - `/status` 为 `ok=true`
  - `last_error` 为空。
- 视觉检查确认“时间—力矩曲线”“空闲”“目标力矩”“滤波力矩”“原始力矩”“合格阈值”以及中文坐标轴均正确显示，没有缺字方框。
- 隔离 V26 测试进程已正常结束。

### 当前是否完成

- 曲线中文化、高清实时图、Unity双击弹窗和编译验证已完成。
- 需要在 Unity Play Mode 中手动确认双击手感、弹窗尺寸和遮罩效果。

### 还存在的问题

- 当前没有自动化的 Unity UI 点击测试，因此双击、Esc和各关闭方式需要在实际 Game 视图验证。
- 实时 JPEG 分辨率提高后单帧约 37 KB；本机 5 FPS 负担较低，但仍需观察长期运行时CPU占用。

### 下一步

- 重新进入 Play Mode，启动拧紧程序。
- 双击右下角曲线，检查大图窗口和微软雅黑 SDF 标题。
- 分别测试双击大图、右上角“×”、遮罩和 `Esc` 关闭。
- 确认真实拧紧结束后保存 PNG 也已中文化。

### 需要在 Unity Editor 检查

- 小图文字是否清晰且全部中文。
- 双击小图是否只打开一个弹窗。
- 大图是否保持比例且没有拉伸。
- 弹窗打开期间曲线是否继续实时更新。
- 关闭拧紧程序时弹窗是否自动关闭。

## 2026-07-24 - V26 实时力矩曲线显示到正式 UI

### 本次任务目标

- 将 V26 实时绘制的时间—力矩曲线显示到用户在 `RunScene` 中复制的新区域。
- 保持 V26 原有 CSV 和最终 PNG 保存目录、命名和内容不变。
- 曲线显示随“启动拧紧/关闭拧紧”按钮启停。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`
- `Assets/ServoTighteningController.cs`
- `Assets/VisionImageReceiver.cs`
- `ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py`

### 发现的现有场景改动

- 用户已经保存了新区域：
  - GameObject：`ObservParam (1)`
  - 标题：`实时力矩曲线`
  - 子级画面框：`Image`
  - 画面框尺寸：`287×162`
  - 使用与现场视频相同的 `Camera.png` Sprite。
- `RunScene.unity` 同时包含大量其他用户/Unity生成的场景修改和内嵌 Mesh 变化。
- 本次没有直接编辑、整理或回退该场景 YAML，只通过运行时脚本绑定新区域。

### 修改的文件

- `ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py`
- `Assets/ServoTighteningController.cs`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动

- V26 新增 Unity 实时曲线输出：
  - 仅在 `SERVO_UNITY_EMBEDDED_MODE=1` 时启用。
  - 后台以默认 `0.2` 秒间隔绘制 `574×324` JPEG。
  - 默认接口为 `http://127.0.0.1:9101/curve.jpg`。
  - `/latest.jpg` 是同一图像的兼容路径。
  - `/status` 返回绘制次数、最近绘制时间和错误。
- 实时预览曲线包含：
  - 原始力矩 `raw`
  - 滤波力矩 `filtered`
  - 当前 V26 状态
  - 目标力矩 28 N·m
  - OK 阈值 27.4 N·m
  - 最近 8 秒时间窗口
- 实时预览使用深色配色以匹配正式运行 UI。
- 实时曲线使用独立 Figure 和 Agg Canvas，不修改 V26 原有桌面 Figure，也不修改 `save_run_outputs()`：
  - 最终 CSV 内容不变。
  - 最终高分辨率 PNG 内容和保存方式不变。
  - 保存位置继续为 `ExternalCode/servo_logs/YYYYMMDD/`。
- 实时预览绘制和最终归档 PNG 绘制共用渲染锁，避免两个线程同时调用 Matplotlib。
- Unity 控制器新增曲线绑定和拉取：
  - 自动查找标题严格等于“实时力矩曲线”的旧版 `Text`。
  - 找到其直接子级、Sprite 名为 `Camera` 的 `Image`。
  - 在不透明占位图上方创建 `ServoTorqueCurveImage` RawImage。
  - 四边缩进 1 像素，保留原蓝色边框。
  - 拧紧程序就绪后约 5 FPS 拉取曲线。
  - 关闭拧紧程序、切换场景或程序异常退出时停止拉取、销毁纹理并恢复占位框。
  - 第一帧成功后记录曲线分辨率日志。
- Unity 启动 V26 时同步传入曲线端口和绘制间隔环境变量。

### 为什么这样改

- Unity 显示的是 V26 根据同一批实时数据绘制的曲线，不需要在 C# 中复制滤波和状态判断逻辑。
- 实时预览与最终归档职责分离，既能保持 UI 连续刷新，也不会高频覆盖最终保存 PNG。
- 运行时绑定避免直接修改当前改动量很大的场景 YAML。

### 验证情况

- Python源码语法编译检查通过。
- 使用 21.5 完整 Linux Player `Assembly-CSharp` 参数编译通过。
- 没有本次新增的 C# 错误；只存在项目原有警告和旧 Roslyn 分析器版本警告。
- 对本次脚本和记录文件执行 `git diff --check` 通过。
- 全仓库 `git diff --check` 仍报告用户现有 `RunScene.unity` 中 Unity序列化的尾随空格，本次未改动这些行。
- 使用隔离配置完成实时曲线测试：
  - 电批地址：`127.0.0.1:1`
  - 控制测试端口：`19100`
  - 曲线测试端口：`19101`
  - 未连接或驱动真实电批。
- 曲线接口验证：
  - HTTP 200
  - `Content-Type: image/jpeg`
  - 有效 JPEG，尺寸 `574×324`
  - 测试文件约 20 KB
  - `/status` 返回 `ok=true`
  - 绘制间隔实测约 5 FPS
  - `last_error` 为空
- 视觉检查确认深色背景、坐标轴、图例和阈值线显示正常。
- 测试后已正常结束隔离 V26 进程。

### 当前是否完成

- V26 实时力矩曲线生成、HTTP输出、Unity正式区域绑定和编译验证已完成。
- 需要用户在 Unity Play Mode 中确认实际布局和真实反馈数据曲线。

### 还存在的问题

- 当前曲线显示刷新目标约 5 FPS，适合力矩曲线；如现场反馈频率很高，可在验证稳定后再调高。
- 当前正式 UI 仍只有程序启停按钮，连接、正转、回位、停止等接口已存在但尚未制作正式按钮。
- `enableRealToolMotion` 仍默认关闭，不会因曲线接入而允许真实正转/反转。

### 下一步

- 进入真实运行，点击“启动拧紧”，确认新区域出现 IDLE 曲线底图。
- 使用电批物理开关产生反馈时，确认 raw/filtered 曲线随时间变化。
- 确认一次拧紧结束后原目录仍生成同名 CSV 和最终 PNG。
- 曲线确认后，再设计正式的连接、开始拧紧、回位和急停 UI。

### 需要在 Unity Editor 检查

- Console/运行时日志是否出现：
  - `[ServoTightening] Bound live torque curve ...`
  - `[ServoTightening] First live torque curve received: 574x324.`
- “实时力矩曲线”框是否完整显示曲线且保留蓝色边框。
- 未启动拧紧程序时是否显示原占位框。
- 点击“关闭拧紧”后曲线是否隐藏并恢复占位框。

## 2026-07-24 - V26 拧紧程序 Unity 控制第一阶段

### 本次任务目标

- 将旧 `My project21` 中的 V26 电批拧紧程序迁入正式项目 `My project21.5`。
- 在真实运行底部工具栏、现有“开启视频”按钮右侧增加拧紧程序启停按钮。
- 建立 UI 和后续全自动流程共用的 Unity 控制接口。
- 第一阶段只允许启动程序、查询状态和安全停止，不自动驱动真实电批。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- 旧项目 `My project21/ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py`
- 旧项目 `My project21/Assets/ServoToolUnityController.cs`
- `Assets/VisionImageReceiver.cs`
- `Assets/RuntimeConsole.cs`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`

### 修改的文件

- 新增 `ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py`
- 新增 `Assets/ServoTighteningController.cs`
- 新增 `Assets/ServoTighteningController.cs.meta`
- 更新 `.codex/PROJECT_CONTEXT.md`
- 更新 `.codex/WORKLOG.md`

### 具体改动

- 将旧 21 的 V26 控制、力矩判定、异常停止、CSV/PNG 保存及 `127.0.0.1:9100` Unity 控制协议迁入 21.5。
- V26 原有命令保持不变：
  - `connect`
  - `forward`
  - `reverse`
  - `stop/reset`
  - `status`
- V26 原有保存规则保持不变：
  - 保存到 `ExternalCode/servo_logs/YYYYMMDD/`
  - 同一次拧紧生成同名 CSV 和 PNG。
- 新增 Unity 运行时单例 `ServoTighteningController`：
  - 自动在 `RuntimeConsoleToggleButton` 右侧 240 像素创建 `ServoTighteningToggleButton`，即位于视频按钮右侧。
  - 仅在 `StatusReal.activeInHierarchy=true` 的真实运行界面显示。
  - 按钮在“启动拧紧”和“关闭拧紧”之间切换。
  - 启动时先检查并连接已有 V26；不存在时才启动自己管理的 Python 进程。
  - 等待 `9100` 控制端口真正返回状态后才标记程序就绪。
  - 每 0.5 秒查询一次状态，并缓存电批连接、状态及最近 CSV/PNG 路径。
  - 切回仿真模式会请求安全停止并关闭自己启动的程序。
- 对后续 UI 和全自动任务流程提供统一方法：
  - `StartProgramAsync()`
  - `ConnectToolAsync()`
  - `StartTighteningAsync()`
  - `ReverseHomeAsync()`
  - `StopToolAsync()`
  - `QueryStatusAsync()`
  - `StopProgramAsync()`
- 增加真实设备运动安全开关：
  - `enableRealToolMotion` 默认关闭。
  - 默认拒绝 `forward` 和 `reverse`。
  - 必须显式调用 `SetRealToolMotionEnabled(true)` 后才允许运动命令。
  - `stop` 不受该开关限制。
- 关闭程序时先发送 `stop` 并查询安全状态；未确认停止时拒绝杀死 V26 进程。
- 增加 `SERVO_UNITY_EMBEDDED_MODE=1`：
  - Unity 启动 V26 时使用 Matplotlib `Agg` 后端，不弹出独立绘图窗口。
  - 后台 TCP 控制、力矩采集和原有 CSV/PNG 保存继续运行。
  - 单独运行脚本且未设置该环境变量时，仍保留原 `plt.show()` 行为。
- 为 `/usr/bin/python3` 用户环境安装了兼容 Python 3.8 的 `matplotlib 3.7.5`。

### 为什么这样改

- UI 手动按钮和未来全自动流程必须调用同一控制层，避免出现两套设备通信逻辑。
- “启动程序”和“驱动电批”属于不同安全层级；启动 Python 不应等同于立即正转。
- 关闭 Python 前必须确认真实电批已经停止，不能用杀进程代替设备急停。
- Unity 最终要在正式 UI 内显示力矩曲线，因此 Unity 启动模式不需要额外 Matplotlib 桌面窗口。

### 验证情况

- Python 源码语法编译检查通过，未产生缓存文件。
- `/usr/bin/python3` 已可导入：
  - NumPy `1.24.4`
  - Matplotlib `3.7.5`
- 使用 21.5 完整 Linux Player `Assembly-CSharp` 参数编译通过。
- 没有本次新增的 C# 错误；只存在项目原有警告和复用旧 Roslyn 的分析器版本警告。
- `git diff --check` 通过。
- 使用隔离配置完成控制口实测：
  - 电批地址强制设为 `127.0.0.1:1`，未接触真实设备。
  - Unity 测试控制口设为 `127.0.0.1:19100`。
  - V26 嵌入模式持续运行，没有因 `plt.show()` 返回而退出。
  - `status` 返回 `ok=true`、`state=IDLE`、`tool_connected=false`、`samples=0`。
  - 测试后通过 `Ctrl+C` 正常结束进程。
- 没有测试 `connect`、`forward`、`reverse` 或真实电批动作。

### 当前是否完成

- 第一阶段程序迁移、UI启停按钮、统一控制 API、安全门和隔离通信验证已完成。
- 实时力矩曲线尚未显示到用户复制的 UI 区域。
- 正式 UI 中尚未增加连接、正转、回位和急停按钮；相应 API 已准备好。

### 还存在的问题

- 用户复制的力矩图区域需要保存场景，并使用与“现场视频”不同的标题和对象名称。
- 后续需要给 V26 增加当前 Matplotlib 曲线图输出接口，再由 Unity 显示到该区域。
- `enableRealToolMotion` 当前默认关闭；在真实设备联调前应先完成 UI 二次确认、急停和状态显示。
- 当前“启动拧紧”会启动 V26 的后台连接/监控线程，但不会自动发送正转或反转命令。

### 下一步

- 在 Unity Play Mode 验证真实运行工具栏上的“启动拧紧/关闭拧紧”按钮位置和状态。
- 确认用户复制的区域名称后，接入实时力矩曲线。
- 增加正式状态显示和连接/拧紧/回位/急停控制，再接入全自动任务状态机。

### 需要在 Unity Editor 检查

- 仿真模式不显示“启动拧紧”按钮。
- 真实运行模式下，该按钮位于“开启视频”右侧。
- 点击“启动拧紧”后按钮变为“关闭拧紧”，日志出现控制端口就绪信息。
- 未解锁真实运动安全开关时，不测试正转或反转。
- 点击“关闭拧紧”后，确认日志显示完成安全停止检查并关闭 V26。

## 2026-07-24 - 真实运行视频手动启停按钮

### 本次任务目标

- 明确 RealSense 相机和视频传输的启动时机。
- 取消进入 `RunScene` 后自动打开相机的行为。
- 在日志按钮旁增加“开启视频/关闭视频”按钮，并且只在“真实运行”UI 中显示。
- 关闭视频或切回仿真模式时释放相机。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/VisionImageReceiver.cs`
- `Assets/RuntimeConsole.cs`
- `Assets/SimulationPlatform/Scripts/Function/Simulation.cs`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`

### 修改的文件

- `Assets/VisionImageReceiver.cs`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动

- 原行为：
  - `RunScene` 加载并找到“现场视频”区域后立即启动 Python。
  - Python 启动时立即打开 D435I。
  - Unity 随即开始轮询 JPEG。
- 新行为：
  - 进入 `RunScene` 时只绑定预留画面框并创建控制按钮，相机默认关闭。
  - 运行时在 `RuntimeConsoleToggleButton` 右侧 120 像素创建 `VisionVideoToggleButton`。
  - 按钮复制日志按钮的尺寸、颜色、字体和交互过渡。
  - `StatusReal.activeInHierarchy=false` 时按钮隐藏。
  - 切换到“真实运行”后显示“开启视频”。
  - 点击“开启视频”后才启动 Python、打开相机并开始 HTTP/JPEG 拉取；按钮文字变为“关闭视频”。
  - 点击“关闭视频”会停止 Unity 拉流、销毁纹理、终止自己启动的 Python 进程并隐藏实时画面。
  - 从真实运行切回仿真模式时，如果视频仍开启，会自动执行同一关闭流程并释放相机。
  - 场景切换和退出 Play Mode 时也执行关闭流程。
- 没有修改 `Simulation.UpdateModeUI()`、`RunScene.unity` 或 Prefab。

### 为什么这样改

- 相机属于真实运行资源，不应在仿真模式或只进入场景时自动占用。
- 复用 `StatusReal` 的显隐状态可以与现有“仿真实验/真实运行”切换保持一致，不需要改正式模式控制脚本。
- 运行时创建按钮可以避免直接编辑场景 YAML，并且位置相对日志按钮稳定。

### 验证情况

- 使用 21.5 完整 Linux Player `Assembly-CSharp` 参数重新编译通过。
- 没有本次新增的 C# 错误；只存在项目原有警告和命令行复用旧 Roslyn 时的分析器版本警告。
- `git diff --check` 通过。
- 检查确认旧自动启动的 `realsense_image_server.py` 进程已经不存在。
- 随后独立打开 D435I 读取一帧成功，并在 `finally` 中停止 pipeline，证明相机已释放。

### 当前是否完成

- 手动启停逻辑、真实模式可见性和编译验证已完成。
- 需要在 Unity Play Mode 中检查按钮位置、显示条件和点击启停结果。

### 还存在的问题

- 视频按钮目前是运行时对象，位置由日志按钮位置加 `X=120` 得到；如果以后移动日志按钮，视频按钮会自动跟随，但不能在非运行状态 Hierarchy 中单独拖动。
- Python 进程使用 `Process.Kill()` 终止；操作系统会释放 USB 设备，已经通过重新打开相机验证。

### 下一步

- 重新进入 `RunScene` Play Mode。
- 仿真模式确认不显示视频按钮且相机未打开。
- 切换“真实运行”，点击“开启视频”，确认现场视频出现。
- 点击“关闭视频”，确认恢复占位框并可用 Viewer 或独立测试重新打开相机。

### 需要在 Unity Editor 检查

- 仿真模式：日志按钮可见，视频按钮不可见。
- 真实运行：日志按钮右侧出现“开启视频”。
- 开启后：按钮显示“关闭视频”，现场画面开始变化。
- 关闭后：按钮恢复“开启视频”，画面回到原占位图。
- 真实运行开启视频后切回仿真：视频按钮消失，相机自动释放。

## 2026-07-24 - RealSense 现场视频第一阶段接入

### 本次任务目标

- 将已验证可用的 Intel RealSense D435I 彩色画面接入正式软件 `My project21.5`。
- 直接使用 `RunScene` 中预留的“现场视频”区域，不创建额外悬浮测试窗口。
- 保持场景和 Prefab 不变，先独立验证相机 HTTP 服务，再由 Unity 运行时绑定画面。

### 读取的关键文件和状态

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`
- `Assets/SimulationPlatform/Resources/Run/Camera.png`
- 旧项目 `My project21` 的 `Assets/VisionImageReceiver.cs`
- 旧项目 `My project21` 的 `ExternalCode/vision_static_image_server.py`
- 独立相机测试区的 RealSense USB 3.x 验证记录
- Git 状态：修改前位于 `release/software-v1.0`，基准提交为 `61a1ea4 SoftWare1.5`，工作区干净

### 修改的文件

- 新增 `ExternalCode/realsense_image_server.py`
- 新增 `Assets/VisionImageReceiver.cs`
- 新增 `Assets/VisionImageReceiver.cs.meta`
- 更新 `.codex/PROJECT_CONTEXT.md`
- 更新 `.codex/WORKLOG.md`

### 具体改动

- Python 服务：
  - 使用 `pyrealsense2` 启动 D435I 彩色流。
  - 默认配置为 `1280×720`、`BGR8`、`15 FPS`。
  - 使用 OpenCV 将请求到的帧编码为 JPEG。
  - 提供 `http://127.0.0.1:8080/latest.jpg` 和 `/status`。
  - 响应头 `X-Image-Source` 包含 RealSense 序列号和 USB 类型。
  - 在正常中断时停止 pipeline 并释放相机。
- Unity 接收器：
  - 通过 `RuntimeInitializeOnLoadMethod` 自动创建，不要求手工修改场景。
  - 每次加载场景时查找文字为“现场视频”的现有 UI 区域。
  - 识别该区域下使用 `Camera.png` 的 `287×162` 边框覆盖层。
  - 首版运行时在边框下创建同尺寸 `RawImage`。
  - 找到目标区域后才启动 Python 服务，以免在登录等无关场景占用相机。
  - 以默认约 5 FPS 拉取 JPEG；替换纹理时销毁旧纹理。
  - 退出运行时停止自己启动的 Python 进程并释放纹理。
- 未修改 `RunScene.unity`、`Canvas.prefab` 或其他正式 UI 资源。

### 为什么这样改

- `RunScene` 已明确预留“现场视频”位置和透明边框，直接复用比新增调试窗口更符合正式软件布局。
- 运行时绑定避免直接编辑体量较大的场景 YAML，也保留以后在 Unity Editor 中调整预留框位置的自由。
- Python 负责 RealSense SDK，Unity 只通过本机 HTTP 接收 JPEG，可复用已经验证过的图像传输边界并降低 Unity 与硬件 SDK 的耦合。

### 验证情况

- Python AST 语法检查通过。
- 独立启动服务成功识别：
  - 设备：Intel RealSense D435I
  - 序列号：`243222071170`
  - USB 类型：`3.2`
- `/latest.jpg` 返回 HTTP 200、`Content-Type: image/jpeg`。
- 返回图像为有效 `1280×720` JPEG，OpenCV 解码结果为 `(720, 1280, 3)`。
- `X-Image-Source` 为 `realsense:243222071170:3.2`。
- `Ctrl+C` 停止服务后日志确认 camera pipeline 已释放。
- 首次 C# 编译发现项目自定义 `Scene` 类型与 Unity Scene 同名，已用 `UnityScene` 别名消除冲突。
- 使用 21.5 的完整 Linux Player `Assembly-CSharp` 编译参数重新编译通过；没有本次新增的 C# 错误，只有项目原有警告。
- `git diff --check` 通过。
- 首次 Play Mode 日志确认已经收到真实帧：
  - `[VisionImage] First frame received: 1280x720`
  - `source=realsense:243222071170:3.2`
- 实际界面只看到原深色画面框；移动相机时局部线条变化，但完整视频被遮挡。
- 检查 `Camera.png` 原始像素确认：
  - PNG 有 Alpha 通道。
  - 四角透明。
  - 中心像素 Alpha 为 `255`，属于完全不透明背景，并非透明覆盖层。
- 已修正 UI 层级：
  - `RealSenseLiveImage` 改为放在原 `Camera.png` Image 上方。
  - 视频层四周缩进 1 像素，露出原蓝色边框。
  - 在视频层右上角动态创建新的红色 `● LIVE` 标记。
- 修正后再次使用完整 Linux Player 编译参数编译通过；没有新增 C# 错误。

### 当前是否完成

- Python 相机服务、正式 UI 运行时绑定、遮挡修复和编译验证已完成。
- 尚需重新进入 Unity Editor 的 `RunScene` Play Mode，确认遮挡修复后的实际画面。

### 还存在的问题

- 当前 HTTP/JPEG 拉取间隔为 `0.2` 秒，目标约 5 FPS；这是稳定性优先的第一阶段，不是最终低延迟视频方案。
- Editor 测试依赖 `/usr/bin/python3` 中已安装的 `pyrealsense2`、NumPy 和 OpenCV。
- 正式 Player 打包后，`ExternalCode/realsense_image_server.py` 需要随可执行文件一起部署到 Player 同级 `ExternalCode/`；本阶段尚未修改打包流程。
- Udev rules 尚未系统级安装，但当前用户权限已经通过独立相机和 HTTP 服务验证。

### 下一步

- 在 Unity 打开 `Assets/SimulationPlatform/Scenes/RunScene.unity` 并进入 Play Mode。
- 确认“现场视频”框内出现 D435I 实时彩色画面，边框和 `LIVE` 标记仍在画面上方。
- 退出 Play Mode 后确认 Python 服务停止，相机可再次被 `test.py` 或 Viewer 打开。
- 视觉确认通过后，再增加连接状态、帧率显示和 Player 外部脚本部署。

### 需要在 Unity Editor 检查

- Console 是否出现：
  - `[VisionImage] Bound RealSense preview ...`
  - `[VisionImage] RealSense HTTP server started.`
  - `[VisionImage] First frame received: 1280x720 ...`
- “现场视频”画面是否完整位于原 `287×162` 框内。
- 原蓝色边框和右上角红色 `LIVE` 是否仍可见。
- 退出 Play Mode 后终端是否不存在残留的 `realsense_image_server.py` 进程。

## 2026-07-23 - 恢复 21.5 的原 Git 历史并准备 v1.5 提交

### 本次任务目标

- 让 `My project21.5` 延续 `My project21.2` 的原 Git/GitHub 历史。
- 继续使用原分支 `release/software-v1.0`，准备提交 `SoftWare1.5` 和标签 `v1.5`。
- 在真正提交和推送前核对远程状态、暂存文件与 GitHub 大文件限制。

### 读取的关键文件和状态

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `My project21.2/.git` 中的分支、标签、远程地址和提交历史
- `My project21.5/.gitignore`
- `My project21.5` 相对 `v1.4` 的工作区和暂存区状态

### Git 状态

- 远程仓库：`https://github.com/siwazuixih/My-project21.git`
- 当前分支：`release/software-v1.0`
- 当前基准：`2deef7d SoftWare1.4`，标签 `v1.4`
- `git fetch origin` 后，本地与 `origin/release/software-v1.0` 的领先/落后数量均为 `0`。
- 已将 21.5 的 325 个非忽略文件加入暂存区，包括用户确认需要上传的 `Assets/SimulationPlatform/Scenes/RunScene21.qq.unity`。
- 暂存区没有超过 50 MiB 的文件。
- `dipan2.fbx`、`微软雅黑 SDF.asset`、`机舱.fbx` 继续按原 `.gitignore` 排除，不属于本次提交。

### 为什么这样处理

- `21.5` 是从已有项目继续开发，但复制时遗漏了隐藏的 `.git`，不应重新创建无历史的新仓库。
- 继续使用原发布分支并创建 `v1.5` 标签，可以保持与 `v1.2`、`v1.3`、`v1.4` 相同的版本管理方式。
- 先暂存并检查、后提交和推送，便于在上传前发现不应进入仓库的文件。

### 当前是否完成

- Git 历史恢复、远端同步检查和暂存准备已完成。
- 尚未创建 `SoftWare1.5` 提交、`v1.5` 标签，也尚未推送 GitHub。

### 下一步

- 完成敏感信息检查并由用户确认暂存清单。
- 提交 `SoftWare1.5`。
- 创建带说明标签 `v1.5`。
- 推送 `release/software-v1.0` 和 `v1.5`。

## 2026-07-23 - 运行时日志界面默认收起

### 本次任务目标

- 正式运行界面启动时默认收起左侧日志窗口。
- 保留底部“展开日志”按钮和后台日志采集。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/RuntimeConsole.cs`
- `Assets/Canvas.prefab`

### 修改的文件

- `Assets/RuntimeConsole.cs`
- `Assets/Canvas.prefab`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动

- 将 `RuntimeConsole.startCollapsed` 的脚本默认值由 `false` 改为 `true`。
- 将正式运行使用的 `Canvas.prefab` 中已序列化的 `startCollapsed` 由 `0` 改为 `1`。
- 启动时仍调用原有 `SetCollapsed(startCollapsed)`，因此只隐藏日志背景、标题和正文，不停用日志监听。

### 为什么这样改

- Unity Prefab 已经保存了该字段，仅修改 C# 字段初始值不会覆盖现有 Prefab 的序列化值。
- 同时修改脚本默认值和 Prefab 值，既保证当前正式运行界面生效，也保证以后新添加的组件默认收起。

### 验证情况

- 已确认脚本默认值和 Prefab 序列化值均为开启。
- 使用 Unity Linux Player 编译参数重新编译 `Assembly-CSharp`，退出码为 `0`。
- 仅有项目原有警告，没有本次修改产生的错误。
- 当前目录无法发现 `.git` 元数据，因此不能运行有效的 `git status`/`git diff`。

### 当前是否完成

- 默认收起设置和编译验证已完成。

### 还存在的问题

- 无代码问题；需要在 Unity Play Mode 中确认初始视觉状态。

### 下次继续开发从哪里开始

- 如需恢复默认展开，可在 `Canvas.prefab/ConsoleBackground` 的 `RuntimeConsole` 组件中取消勾选 `Start Collapsed`。

### 需要在 Unity Editor 检查

- 进入 `RunScene` Play Mode 后，左侧日志窗口是否默认隐藏。
- 底部按钮初始文字是否为“展开日志”。
- 点击“展开日志”后是否能显示启动期间已经记录的日志。

## 2026-07-23 - 日志折叠按钮迁移到底部公共工具栏

### 本次任务目标

- 将日志“收起/展开”按钮从原日志 Canvas Prefab 移到 `RunScene` 底部蓝色公共工具栏。
- 让按钮不再受 Prefab 子对象限制，可以直接在场景中拖动并保存位置。
- 保证仿真运行和真实运行切换时按钮都存在。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/RuntimeConsole.cs`
- `Assets/Canvas.prefab`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`
- `/home/qq/.config/unity3d/Editor.log`

### 修改的文件

- `Assets/RuntimeConsole.cs`
- `Assets/Canvas.prefab`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动

- 在 `RunScene/SimulationPlatform/Canvas/Panel` 下新增场景原生按钮 `RuntimeConsoleToggleButton`。
- 按钮使用左下角锚点，默认位置为 `Pos X = 650`、`Pos Y = 44.5`，尺寸为 `110 × 34`。
- 按钮位于 `StatusSim`、`StatusReal` 的共同父节点 `Panel` 下，因此切换仿真/真实模式时不会随其中一个状态栏一起隐藏。
- `RuntimeConsole.CreateToggleButton()` 改为优先在当前活动场景中按名称查找按钮并绑定点击事件。
- 如果其他场景没有场景原生按钮，脚本仍会在日志 Canvas 中自动创建按钮作为兼容兜底。
- 原 `Canvas.prefab` 中的按钮改名为 `RuntimeConsoleToggleButtonLegacy` 并停用，避免正式运行时出现重复按钮。

### 为什么这样改

- Prefab 实例的子对象不能直接拖到另一个 Canvas，Unity 会提示 `Cannot restructure Prefab Instance`。
- 把按钮创建为 `RunScene` 自身的场景对象后，它可以在 Hierarchy 中自由调整位置，不需要解包整个 Canvas Prefab。
- 放在底部两个模式状态栏的公共父节点，可避免只在某一种运行模式中显示。

### 验证情况

- 已核对新增按钮的 GameObject、RectTransform、Image、Button 和 TextMeshPro 组件 fileID 唯一，父子引用完整。
- Unity 已成功导入修改后的 `Canvas.prefab`，Editor 日志中没有 Prefab YAML 或反序列化错误。
- 使用 Unity Linux Player 的编译参数重新编译 `Assembly-CSharp`，退出码为 `0`。
- 编译仅有项目原有警告，没有本次修改产生的错误。
- 当前工作目录无法发现 `.git` 元数据，因此不能使用 `git status`/`git diff`；本次只修改上述项目内文件。

### 当前是否完成

- 代码、Prefab 和 `RunScene` 场景迁移已完成。
- 需要用户在 Unity 中重新打开或刷新 `RunScene`，确认按钮位置并按需要拖动。

### 还存在的问题

- 新按钮只作为 `RunScene` 的场景原生对象存在；其他场景继续使用脚本自动创建的兼容按钮。
- 外部修改场景 YAML 后，如果 Unity 弹出场景文件已改变提示，应选择重新加载磁盘版本，不要用旧的内存场景覆盖。

### 下次继续开发从哪里开始

- 在 `RunScene` Hierarchy 展开 `SimulationPlatform > Canvas > Panel`。
- 选中 `RuntimeConsoleToggleButton`，使用 Rect Tool 调整位置。

### 需要在 Unity Editor 检查

- 底部蓝色工具栏是否出现“收起日志”按钮。
- 切换“仿真实验/真实运行”时按钮是否始终显示。
- 点击按钮是否可以正常收起和展开左侧日志。
- 拖动按钮、保存场景并重新打开后，位置是否保持。

## 2026-07-23 - 日志折叠按钮改为可在 Prefab 中直接拖动

### 本次任务目标

- 将原先仅在运行时生成的日志折叠按钮改为真实 Prefab UI 对象。
- 允许用户在 Unity Editor 中使用 Rect Tool 直接拖动按钮，并永久保存位置和尺寸。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/RuntimeConsole.cs`
- `Assets/Canvas.prefab`
- `/home/qq/.config/unity3d/Editor.log`

### 修改的文件

- `Assets/Canvas.prefab`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动

- 在 `Canvas.prefab` 根节点下新增真实 UI 对象 `RuntimeConsoleToggleButton`。
- 按钮包含：
  - `RectTransform`
  - `Image`
  - `Button`
  - 中文 TextMeshPro 文字子对象
- 默认位置为 `Pos X = 295`、`Pos Y = 230`。
- 默认尺寸为 `Width = 110`、`Height = 34`。
- 现有 `RuntimeConsole.CreateToggleButton()` 会优先按名称找到该 Prefab 按钮并绑定点击事件；如果旧 Canvas 中不存在该对象，仍保留运行时创建作为兼容兜底。

### 为什么这样改

- 运行时动态生成的对象只能在 Play Mode 临时拖动，退出运行后不会保存位置。
- 把按钮做成 Prefab 中的真实对象后，可以在 Prefab Mode 使用 Rect Tool 拖动并保存，而且不会改变日志折叠逻辑。
- 本次 Prefab YAML 修改只新增独立按钮和一个根级子节点引用，没有修改现有 Console 或其他按钮绑定。

### 验证情况

- 已确认新增对象、RectTransform、Button、Image、TextMeshPro 的本地 fileID 均唯一且父子引用完整。
- 当前 Unity Editor 已检测项目资源变化，最新日志没有 Prefab YAML 或反序列化错误。
- 使用 Unity 的 Linux Player 编译参数重新编译 `Assembly-CSharp`，退出码为 `0`。
- 编译只出现项目原有警告，没有本次修改产生的错误。

### 当前是否完成

- 可拖动 Prefab 按钮已创建，代码兼容和 Player 编译验证已完成。
- 需要用户在 Unity Editor 中打开 `Assets/Canvas.prefab`，按需要拖动按钮并保存。

### 还存在的问题

- 按钮属于 `Canvas.prefab`，应在 Prefab Mode 中调整；若只在 Play Mode 拖动，退出运行后位置仍不会保存。
- 如果某个场景对 Canvas Prefab 产生了同名按钮的场景级覆盖，需要在该场景中检查是否应用了 Prefab Override。

### 下次继续开发从哪里开始

- 双击 `Assets/Canvas.prefab`。
- 在 Hierarchy 选中 `RuntimeConsoleToggleButton`。
- 按 `T` 切换 Rect Tool，拖动后保存 Prefab。

### 需要在 Unity Editor 检查

- Prefab Hierarchy 中是否能看到 `RuntimeConsoleToggleButton`。
- 拖动并保存后进入正式运行，按钮是否出现在新位置。
- 点击“收起日志/展开日志”是否仍正常切换，并且收起期间日志继续缓存。

## 2026-07-23 - 左侧运行时调试日志支持收起和展开

### 本次任务目标

- 让正式运行界面左侧的 `Console Log` 调试窗口可以收起，避免遮挡模型和操作区域。
- 收起期间继续采集日志，方便需要时重新展开检查。
- 不直接修改正式运行场景或 `Canvas.prefab`。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/RuntimeConsole.cs`
- `Assets/RuntimeConsole.cs.meta`
- `Assets/Canvas.prefab`（只读检查 Console 层级、尺寸和字体）
- `Assets/SimulationPlatform/Scenes/RunScene.unity`

### 修改的文件

- `Assets/RuntimeConsole.cs`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动

- 在 `RuntimeConsole` 启动时动态创建一个独立的中文切换按钮。
- 展开时按钮显示“收起日志”，调试窗口保持原显示。
- 收起时隐藏：
  - 日志背景
  - `Console Log` 标题
  - 日志正文
- 收起后只保留“展开日志”按钮，按钮位于原窗口右上方并保持在 UI 最上层。
- `RuntimeConsole` GameObject 和日志监听不会被停用，所以收起期间仍会缓存新日志。
- 新增 `startCollapsed` 配置，默认值为 `false`，即启动时仍保持展开。
- 动态按钮复用日志文本的中文字体，避免中文按钮缺字。

### 为什么这样改

- `RuntimeConsole` 当前挂在 `Canvas.prefab/ConsoleBackground` 上，如果直接停用整个对象，日志订阅也会在 `OnDisable()` 中取消，而且无法依靠对象内部按钮重新展开。
- 将按钮创建为 Console 背景的同级 UI，只隐藏背景和文字组件，可以同时保证按钮始终可点、日志继续采集。
- 使用运行时脚本创建按钮，不需要手工编辑大型 Prefab YAML，也能自动作用于所有使用该 Canvas Prefab 的运行场景。

### 验证情况

- 已确认日志监听仍由 `OnEnable()` 注册、`OnDisable()` 注销，折叠操作不会触发这两个生命周期。
- 已确认展开/收起按钮只切换 UI 显示，不清空 `logLines`。
- 使用 Unity 的 Linux Player 编译参数重新编译 `Assembly-CSharp`，退出码为 `0`。
- 编译只出现项目原有警告，没有本次修改产生的错误。

### 当前是否完成

- 折叠功能代码和 Player 编译验证已完成。
- 需要在 Unity 正式运行界面实际点击按钮，检查位置、层级和中文字体效果。

### 还存在的问题

- 按钮位置根据当前 Console 面板的 RectTransform 自动计算；如果后续改变 Canvas 缩放或 Console 尺寸，可能需要微调按钮偏移。
- 当前没有保存上一次展开/收起状态；每次进入界面默认按 `startCollapsed` 决定。

### 下次继续开发从哪里开始

- 在 `RunScene` 进入正式运行，点击“收起日志”和“展开日志”。
- 如果希望默认收起，可以在 `Canvas.prefab` 的 `RuntimeConsole` Inspector 中勾选 `Start Collapsed`，或后续把代码默认值改为 `true`。

### 需要在 Unity Editor 检查

- 展开时按钮是否位于日志窗口右上方且不遮挡标题。
- 点击“收起日志”后是否只剩一个小按钮，模型区域能正常操作。
- 收起期间制造几条新日志，重新展开后确认最新日志仍然存在。
- 检查不同窗口分辨率下按钮是否仍在可见区域。

## 2026-07-23 - 正式运行右侧状态参数中文化

### 本次任务目标

- 将正式运行界面右侧的机械臂模式、关节角度、升降缸参数和实物跟随状态改为中文。
- 升降缸未连接时不再显示空白。
- 不修改通信协议、真机控制命令、IP、数值、单位或场景 YAML。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/DobotController.cs`
- `Assets/LiftCylinderController.cs`
- `Assets/RealRobotFollower.cs`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`（只读检查 UI 绑定和文本框尺寸）
- `ProjectSettings/EditorBuildSettings.asset`

### 修改的文件

- `Assets/DobotController.cs`
- `Assets/LiftCylinderController.cs`
- `Assets/RealRobotFollower.cs`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动

- `DobotController`
  - `Mode` 改为“工作模式”。
  - `UNKNOWN` 改为“未知”。
  - 模式状态统一为中文，包括“已下电”“已使能、空闲”“单次运动中”“已暂停”“碰撞状态”等。
  - `J1`～`J6` 改为“关节1”～“关节6”。
  - 预留但当前场景未绑定的 `SpeedScaling`、`JointSpeed` 分别改为“速度缩放”“关节速度”。
  - 每次刷新界面时根据模式编号重新取得中文状态，避免场景中旧序列化值 `UNKNOWN` 继续显示。
- `LiftCylinderController`
  - 已连接时显示“升降缸高度”“升降缸速度”“升降缸转矩”。
  - 未连接、连接失败、主动断开或监控循环检测到断开时显示中文占位状态，不再留空。
  - 未连接时三行分别显示连接状态、高度占位，以及速度/转矩占位。
- `RealRobotFollower`
  - `Real Follow` 改为“实物跟随”。
  - 状态改为“已关闭”“等待真机反馈”“正在跟随真机”“反馈数据不完整”。

### 为什么这样改

- 右侧静态栏目标题原本大多已是中文，英文来自脚本在运行时持续覆盖 TextMeshPro 文本，因此应修改动态赋值代码。
- 使用脚本修改可以同时覆盖 Editor 和正式 Player，也不需要直接修改体量较大的场景 YAML。
- `RunScene` 的升降缸区域只绑定了三个文本框，所以未连接时在现有三行中组合显示连接、参数占位；连接后恢复显示三个真实参数。

### 验证情况

- 已搜索三个脚本，原来的 `Mode:`、`UNKNOWN`、`SpeedScaling:`、`JointSpeed:`、`Lift:`、`Lift Speed:`、`Lift Torque:`、`Real Follow:` 及英文跟随状态均已移除。
- 使用 Unity 失败构建时生成的 Linux Player 编译参数重新编译 `Assembly-CSharp`，退出码为 `0`。
- 编译只出现项目原有警告，没有本次修改产生的错误。
- 本次未连接真实机械臂、底盘或升降缸，也未下发任何真机动作。

### 当前是否完成

- 代码修改和 Player 脚本编译验证已完成。
- 需要在 Unity 正式运行界面检查中文文本在当前分辨率下是否完整显示。

### 还存在的问题

- 当前三个升降缸文本框高度和数量固定，未连接时速度与转矩共用第三行；如果实际界面显得拥挤，需要后续在 UI 中增加第四个文本框或调整布局。
- 当前工作区 Git 元数据在受限环境中不可用，本次未回退或整理任何其他现有修改。

### 下次继续开发从哪里开始

- 打开 Build Settings 中启用的 `Assets/SimulationPlatform/Scenes/RunScene.unity`，进入“真实运行”界面检查右侧文字。
- 如果关节或升降缸文字出现换行、截断，再只调整对应 TextMeshPro 文本框宽度或字号。

### 需要在 Unity Editor 检查

- 未连接设备时：
  - 工作模式应显示“工作模式：0 未知”。
  - 关节应显示“关节1”到“关节6”。
  - 升降缸区域应显示“升降缸连接：未连接”和参数占位。
- 连接设备后：
  - 机械臂模式应显示中文状态。
  - 升降缸应显示高度、速度和转矩的真实数值。
  - 打开“实时同步”后，实物跟随状态应显示中文。
- 检查右侧窄栏是否存在文字换行或被截断。

## 2026-07-23 - 修复 21.5 Player 打包编译失败

### 本次任务目标

- 排查 `My project21.5` 在 Unity Editor 内可以运行、但无法打包成软件的问题。
- 所有诊断记录只写入 `My project21.5/.codex`，不写到项目父文件夹。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `/home/qq/.config/unity3d/Editor.log`
- `Assets/Plugins/ZCalendar/Scripts/ZCalendarModel.cs`
- `Assets/Plugins/ZCalendar/Scripts/LevelChoiceItem.cs`
- Unity 生成的 Player 编译响应文件 `Library/Bee/artifacts/2400b0aP.dag/Assembly-CSharp-firstpass.rsp*`

### 修改的文件

- `Assets/Plugins/ZCalendar/Scripts/ZCalendarModel.cs`
- `Assets/Plugins/ZCalendar/Scripts/LevelChoiceItem.cs`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动

- 删除 `ZCalendarModel.cs` 中未使用的 `using UnityEditorInternal.Profiling.Memory.Experimental;`。
- 删除 `LevelChoiceItem.cs` 中未使用的 `using UnityEditor;`。
- `LevelChoiceItem.cs` 含旧编码字节，本次保留原文件编码和 CRLF，只按字节删除目标行，避免产生整文件重编码改动。

### 为什么这样改

- 2026-07-23 的实际打包日志明确报错：
  - `ZCalendarModel.cs(6,7): error CS0246`
  - Player 编译找不到 `UnityEditorInternal`。
- `UnityEditor` 和 `UnityEditorInternal` 只存在于 Unity Editor 环境，不会随 Player 软件发布。
- Editor 内 Play Mode 会加载编辑器程序集，所以项目能在 Unity 内运行；打包时 Unity 使用独立的 Player 编译配置，因此该引用会导致构建失败。
- 两个引用均未被脚本实际使用，直接移除比增加条件编译更小、更稳妥。

### 验证情况

- 使用 Unity 本次失败构建生成的原始 Linux Player 编译参数，重新编译 `Assembly-CSharp-firstpass`。
- 编译退出码为 `0`，原来的 `UnityEditorInternal` 错误已消失，`LevelChoiceItem.cs` 也未产生 `UnityEditor` 错误。
- 当前只剩 ZCalendar 插件原有的 `CS0618` 和 `CS0414` 警告，不会阻止 Player 打包。
- 为避免与当前已打开的 Unity Editor 争用工程，本次没有从第二个 Unity 进程直接生成正式软件包。

### 当前是否完成

- 已定位并修复本次日志中导致打包中止的 Player 脚本编译错误。
- Player 专用的首层脚本程序集已验证编译通过。
- 仍需在当前 Unity Editor 中重新点击一次 `Build`，完成全流程打包确认。

### 还存在的问题

- 尚未生成完整 Player，因此如果后续构建阶段出现新的资源、原生插件或场景错误，需要根据下一次 `Editor.log` 继续排查。
- 当前工作区的 Git 元数据在受限环境中不可用，本次无法用 `git status` 区分项目原有未提交修改；没有回退或整理任何现有文件。

### 下次继续开发从哪里开始

- 先在 Unity Editor 重新打包。
- 若仍失败，读取最新 `/home/qq/.config/unity3d/Editor.log`，从最后一个 `Error building Player` 向上找第一条 `error`，不要继续处理本次已经消失的 CS0246。

### 需要在 Unity Editor 检查

- 等待脚本刷新完成，Console 中不应再出现 `UnityEditorInternal` 或 `ZCalendarModel.cs(6,7)`。
- 使用与本次相同的平台和 Build Settings 重新点击 `Build`。
- 如果打包成功，启动生成的软件，检查日历/时间选择界面能否正常打开和选择。

## 2026-06-25 - 21.2 两点路径规划闪退防护

### 本次任务目标

- 用户扩大权限后，要求直接检查并修改实际运行的 `My project21.2`。
- 针对“NavMesh 正常后，选择两个点仍闪退”的问题，把之前定位出的 MuJoCo 状态污染修复落实到 `21.2`。

### 读取的关键文件

- `AGENTS.md`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`
- `Assets/BITStarPlanner.cs`
- `Assets/MujocoStaticIKSolver.cs`

### 修改的文件

- `Assets/BITStarPlanner.cs`
- `Assets/MujocoStaticIKSolver.cs`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动

- `BITStarPlanner`
  - 修复 `Plan()` 中 IK 返回值先打印后判空的问题。
  - 对 `q_start`、IK full qpos、`q_goal` 增加合法性检查和关节限位夹紧。
  - 新增 MuJoCo 状态快照，完整备份/恢复 `qpos/qvel/act/ctrl`。
  - `GetPathInWorldSpace()`、`CheckCartesianLimits()`、`IsValidConfig()`、`RecordSampleForDebug()` 改为 `try/finally` 恢复完整状态。
  - 临时 FK/碰撞检查恢复后使用 `mj_forward`，不再只依赖 `mj_kinematics`。
  - 修复 `RecordSampleForDebug()` 原先只恢复 `qpos[0..nv)` 的错位污染风险。
- `MujocoStaticIKSolver`
  - IK 开始时完整备份 MuJoCo 状态，结束时完整恢复。
  - 每次随机重试前恢复到同一个干净起点，避免上一轮尝试污染下一轮。
  - IK 诊断日志临时套用 best qpos 时也使用完整状态快照和 `try/finally`。

### 为什么这样改

- 最新崩溃日志显示，程序在 `BITStarPlanner.Plan()` 打印 IK 结果后立刻出现 native `free(): invalid pointer`，并未进入 `[基准数据]` 日志，说明不是 NavMesh 或 BIT* 长时间搜索导致。
- 两个目标点会连续触发多次 `Plan()`；第一次规划里的 FK、碰撞检查、采样可视化如果只恢复部分 MuJoCo 状态，第二次 IK 更容易在污染后的 `MjData` 上崩溃。
- 完整恢复 `qpos/qvel/act/ctrl` 并调用 `mj_forward`，可以减少 MuJoCo 派生状态不一致引发的 native 崩溃。

### 验证情况

- 已运行 `git diff --check -- Assets/BITStarPlanner.cs Assets/MujocoStaticIKSolver.cs`，通过。
- 尝试 `dotnet build Assembly-CSharp.csproj --no-restore`，仍失败于 Unity 生成文件 `Temp/obj/Assembly-CSharp/project.assets.json` 缺失，未进入 C# 编译阶段。

### 当前是否完成

- `My project21.2` 中针对两点路径规划闪退的代码防护已完成。
- 需要在 Unity Editor 中触发脚本编译，并重新运行“两点目标 -> 路径规划”验证。

### 还存在的问题

- 如果仍闪退，需要继续查看最新 `~/.config/unity3d/Editor.log` 末尾托管栈，看崩溃点是否已经从 `BITStarPlanner.cs:49` 转移。
- 如果不再闪退但路径/末端位置仍不对，需要回到 IK 坐标、末端 site、视觉末端对象和 compact actuator 映射继续排查。

### 下次继续开发从哪里开始

- 先看 Unity 是否有 C# 编译错误。
- 再运行两个目标点规划，观察最后日志是否能越过 `IK:` 并打印 `[基准数据]`、`BIT* 启动竞速模式`、`平滑后节点数`。
- 如果失败，把最新 Console 和 `Editor.log` 末尾贴回。

### 需要在 Unity Editor 检查

- `BIT*Planner` 的 `ikSolver`、`endEffectorSite`、`actuators` 绑定是否完整。
- 重新执行“设置两个目标点 -> 路径规划”。
- 重点观察是否还在 `IK:` 后直接闪退。

## 2026-06-25 - 运行时 NavMesh 烘焙排除机器人本体并保留世界地面

### 本次任务目标

- 用户已在 Inspector 中调整 `NavMeshSurface` 的 Include Layers，并把机器人对象设置为 `Robot` 层，但不确定运行时代码是否调用的是同一个 Surface。
- 按排查结论修改运行时烘焙逻辑，避免机器人本体被 NavMesh 构建收进去。
- 用户补充 `cr10_robot356/ground` 是 MuJoCo 世界地面，不能排除，否则 NavMesh 没有地板；本次同步保留该类地面对象。

### 读取的关键文件

- `AGENTS.md`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`
- `Assets/SimulationPlatform/Scripts/Function/Simulation.cs`
- `Library/PackageCache/com.unity.ai.navigation@1.1.7/Runtime/NavMeshSurface.cs`
- `Library/PackageCache/com.unity.ai.navigation@1.1.7/Runtime/NavMeshModifier.cs`

### 修改的文件

- `Assets/SimulationPlatform/Scripts/Function/Simulation.cs`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动

- `RebuildRuntimeNavMesh()` 不再只给机器人根节点加一个 `NavMeshModifier`。
- 新增 `ApplyIgnoreFromNavMeshBuild()`，对 `MissionController.gameObject` 整棵子层级逐个添加/更新 `NavMeshModifier`。
- 对机器人本体子物体设置：
  - `ignoreFromBuild = true`
  - `applyToChildren = false`
- 对名字为 `ground`、`ground_*`、`floor*`、`plane`、`plane_*` 的世界地面对象设置：
  - `ignoreFromBuild = false`
  - `applyToChildren = false`
- 运行时只重建当前 `Simulation` 所在 Scene 里的 `NavMeshSurface`，避免误重建别的场景或隐藏残留 Surface。
- 新增日志输出实际重建的 Surface 层级路径、排除机器人对象数、保留地面对象数、Surface LayerMask，以及 Surface 是否包含机器人根节点所在 Layer。

### 为什么这样改

- AI Navigation 的 `NavMeshSurface` 会用自身 `LayerMask` 过滤 `NavMeshModifier` 所在 GameObject 的 layer。
- 如果只在 `cr10_robot356` 根节点挂 modifier，而根节点是 `Robot` 层、Surface 又不包含 `Robot` 层，这个根 modifier 可能被跳过。
- 机器人子层级里存在大量机器人几何体，逐子物体添加 ignore modifier 可以明确排除 Default 层漏网机器人零件，不再依赖手动 Layer 设置完全正确。
- `cr10_robot356/ground` 是世界大地，不是机器人本体，所以必须从排除逻辑中跳过并保留进 NavMesh。

### 验证情况

- 已确认 `NavMeshSurface.layerMask` API 存在。
- 已确认 `NavMeshModifier` 默认 affected agents 为 `All`，运行时添加的 modifier 会影响 Humanoid Agent。
- 使用 `git diff --ignore-space-at-eol --ignore-cr-at-eol` 查看，真实代码改动集中在 `RebuildRuntimeNavMesh()` 及新增 helper。
- 尝试 `dotnet build Assembly-CSharp.csproj --no-restore`：
  - 沙箱内失败于 `Temp/obj` 只读。
  - 提权后仍失败于 Unity 生成文件 `Temp/obj/Assembly-CSharp/project.assets.json` 缺失。
  - 该 Unity 工程仍需在 Unity Editor 内触发脚本编译确认。

### 当前是否完成

- 运行时 NavMesh 烘焙排除机器人层级的代码修改已完成。
- 需要用户在 Unity Editor 里等待脚本编译，然后重新点击路径规划验证 Scene 视图中的 NavMesh 是否有地板且不再把机器人本体烘进去。

### 还存在的问题

- `Assets/SimulationPlatform/Scripts/Function/Simulation.cs` 在本次开始前已处于修改状态，且文件存在 CRLF/LF 混合换行；普通 `git diff --check` 会被换行符噪声刷屏。
- 如果运行后仍看到异常 NavMesh，需要根据新日志确认实际重建的是哪个 Surface，以及 `Surface LayerMask` 数值是否符合预期。

### 下次继续开发从哪里开始

- 先看 Console 中 `NavMesh 已根据当前场景重新构建：...` 的 Surface 路径，确认它就是当前 `SampleScene/NavMesh Surface`。
- 再看 `已排除机器人对象数` 是否接近机器人层级对象数量，同时 `保留地面对象数` 应至少包含 `ground`。
- 如果排除数正常但 NavMesh 仍异常，再检查是否有环境模型/碰撞体本身被放在机器人层级外并参与烘焙。

### 需要在 Unity Editor 检查

- 等待脚本编译无红色编译错误。
- 点击路径规划前观察 Console 新日志：
  - 重建的 Surface 路径
  - 已排除机器人对象数
  - 保留地面对象数
  - Surface LayerMask
  - 是否包含 Robot 层
- Scene 视图打开 AI Navigation 的 `Show NavMesh`，确认地面仍有 NavMesh，同时机器人底盘/机械臂本体不再被烘焙为障碍或可达面。

## 2026-06-24 - IK/执行链诊断日志

### 本次任务目标

- 用户手动按 IK 值写入执行器后末端仍到不了目标点，要求按前面判断增加有帮助的注释/输出。
- 本次只增加诊断日志，不改变 IK、BIT*、路径执行或真机控制逻辑。

### 读取的关键文件

- `AGENTS.md`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`
- `Assets/MujocoStaticIKSolver.cs`
- `Assets/BITStarPlanner.cs`
- `Assets/ArmController.cs`
- `Assets/MissionController.cs`

### 修改的文件

- `Assets/MujocoStaticIKSolver.cs`
- `Assets/BITStarPlanner.cs`
- `Assets/ArmController.cs`
- `Assets/MissionController.cs`
- `.codex/WORKLOG.md`

### 具体改动

- `MujocoStaticIKSolver`
  - 在 IK 返回前打印 `[IK诊断]`：
    - `bestAttempt`
    - `converged`
    - 位置误差、姿态误差、rest 距离
    - 目标 Unity 坐标、site 换算后的 Unity 坐标、两者距离
    - full qpos 全量数组
    - actuator 到 qpos 下标的映射和值
- `BITStarPlanner`
  - 打印 `[BIT*诊断] q_start compact` 和 `q_goal compact`，包括 actuator 序号、joint 名称、qpos 下标和值。
- `ArmController`
  - `ExecutePath()` 开始时打印路径点数量、是否 simpleLerp、目标点和姿态。
  - 每个路径点打印原始数组和实际写入的 actuator 控制值。
  - 如果路径点长度不是 actuator 数量，会警告可能是 full qpos / compact state 混用。
  - 运动结束后打印视觉末端位置、锁定目标位置和距离。
- `MissionController`
  - 深度预计算时打印目标点、最终停车点、朝向、观察点和观察姿态。

### 为什么这样改

- 当前最大不确定性是：
  - IK 是否真正收敛，还是只返回了一个 bestQpos。
  - Unity 目标坐标和 MuJoCo site 坐标是否一致。
  - 执行链是否把 10 维 full qpos 当成 7 维 compact actuator state 执行。
  - 视觉末端 `armEndEffector` 和 IK 使用的 `endEffectorSite` 是否一致。
- 新增日志覆盖这些判断点，便于下一次只根据 Console 输出定位修复点。

### 验证情况

- 已运行 `git diff --check -- Assets/MujocoStaticIKSolver.cs Assets/BITStarPlanner.cs Assets/ArmController.cs Assets/MissionController.cs`，通过。
- 尝试 `dotnet build Assembly-CSharp.csproj --no-restore`，仍失败于 Unity 生成文件 `Temp/obj/Assembly-CSharp/project.assets.json` 缺失，无法用 CLI 完整编译验证。

### 当前是否完成

- 诊断日志已添加完成。
- 需要在 Unity Editor 中重新运行同一流程，收集 `[IK诊断]`、`[BIT*诊断]`、`[执行诊断]`、`[预计算诊断]` 日志。

### 还存在的问题

- 还未修复“末端到不了目标点”的根因，本次只是加诊断输出。
- 下一步需要根据日志判断是否修改 IK 收敛条件、坐标转换、full qpos 到 compact state 转换，或 endEffector/site 绑定。

### 下次继续开发从哪里开始

- 优先看 `[IK诊断] converged` 是否为 `False`，以及 `unityDistance` 是否明显大于 `stopThreshold`。
- 再看 `[执行诊断] 路径点长度` 是否出现 10 vs 7 的警告。
- 最后比较 `[IK诊断] siteUnity` 和 `[执行诊断] visualEndEffector` 是否是同一个点。

### 需要在 Unity Editor 检查

- 重新执行“设置目标点 -> 路径规划 -> 运行”。
- 把一组完整 Console 日志发回来，尤其是：
  - `[预计算诊断]`
  - `[IK诊断]`
  - `[BIT*诊断]`
  - `[执行诊断]`

## 2026-06-24 - BIT* 路径规划崩溃防护

### 本次任务目标

- 根据上一次排查结论，修改路径规划崩溃的高风险点。
- 用户询问是否只需要修改 BIT*，本次选择先只修改 `Assets/BITStarPlanner.cs`，不改场景、不改真实设备控制、不改 IK 算法主体。

### 读取的关键文件

- `AGENTS.md`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`
- `Assets/BITStarPlanner.cs`
- `Assets/MujocoStaticIKSolver.cs`
- `Assets/MissionController.cs`

### 修改的文件

- `Assets/BITStarPlanner.cs`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动

- 在 `BITStarPlanner.Plan()` 中：
  - 对 `q_start` 做限位夹紧和合法性检查。
  - 修复 IK 返回 `null` 时先 `Select()` 打日志导致空引用的隐患。
  - 对 IK 全量 `qpos` 做长度、NaN、Inf 检查。
  - 从 IK 全量 `qpos` 抽取 compact state 后，对 `q_goal` 做限位夹紧和合法性检查。
- 在 MuJoCo 临时姿态检查中：
  - `CheckCartesianLimits()` 和 `IsValidConfig()` 改为 `try/finally` 恢复 `qpos`，降低异常或提前 return 后污染 MuJoCo 状态的风险。
  - 碰撞接触 `dist` 出现 NaN/Inf 时直接判为无效姿态。
  - `IsValidConnection()` 增加起点/终点和距离合法性检查。
- 在初始化和工具函数中：
  - 检查 `ikSolver`、`endEffectorSite`、`actuators`、`Joint` 绑定。
  - 检查 site id 是否有效。
  - 新增 compact/full qpos 合法性检查、关节范围获取、限位夹紧、有限数检查等 helper。

### 为什么这样改

- 上次排查显示崩溃不是普通 C# exception，而是在 BIT* 拿到 IK 结果后继续做 MuJoCo native 碰撞/连通检查时触发 `free(): invalid pointer`。
- 先在 BIT* 层阻止坏数据进入 MuJoCo native 调用，比直接调整 IK 参数或场景 YAML 风险更小、回退更容易。
- IK 侧仍可能产生不理想姿态，但本次先保证规划器不会把明显非法状态继续送入碰撞检查。

### 验证情况

- 已运行 `git diff --check -- Assets/BITStarPlanner.cs`，无 whitespace 错误。
- 尝试 `dotnet build Assembly-CSharp.csproj --no-restore`，失败原因是 Unity 生成的 `Temp/obj/Assembly-CSharp/project.assets.json` 不存在。
- 尝试 `dotnet build Assembly-CSharp.csproj`，CLI 在 restore 阶段直接失败且没有具体编译错误输出。该 Unity 工程当前无法用 dotnet CLI 完整验证，需要在 Unity Editor 内触发脚本编译确认。

### 当前是否完成

- 代码层面的 BIT* 防护修改已完成。
- 需要用户在 Unity Editor 中实际运行同一套“设置目标点 -> 路径规划”流程确认 native 崩溃是否消失。

### 还存在的问题

- 如果仍然在 IK 求解内部或 MuJoCo native 内部崩溃，下一步需要继续收窄到 `MujocoStaticIKSolver.CheckCollision()`、`RandomizeConfiguration()`、`RunGradientDescent()` 的状态恢复和数值稳定性。
- 当前项目工作区已有大量未提交修改，本次没有回退或整理这些历史改动。

### 下次继续开发从哪里开始

- 先看 Unity Console 是否有新的 `BIT*:` 错误日志。
- 如果还崩溃，重新查看 `~/.config/unity3d/Editor.log` 末尾调用栈，确认最后托管行号是否从 `BITStarPlanner.cs` 转移到 `MujocoStaticIKSolver.cs` 或其他位置。

### 需要在 Unity Editor 检查

- `BIT*Planner` 对象的 `ikSolver`、`endEffectorSite`、`actuators` 列表是否完整绑定。
- 重新运行同样的三个目标点路径规划。
- 观察 Console 是否出现：
  - `IK 返回了非法 qpos`
  - `关节数组为空或长度不足`
  - `严重超出限位`
  - `找不到末端 Site`
