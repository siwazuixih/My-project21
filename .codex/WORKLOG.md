# Worklog

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
