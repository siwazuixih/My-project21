# Worklog

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
