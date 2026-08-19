# 2026-08-13 修改迁移说明

这份清单只记录 2026-08-13 在 `My project21.5` 中有意完成的功能修改，供后续适配到
其他人开发的新版本。不要用当前工作区的全部 `git diff` 代替本清单：工作区还混有窗口、
视频、外部 Python、场景 UI 等其他日期或其他任务的未提交改动。

## 一、今天实际涉及的文件

### 功能代码和资源：7 个文件

1. `Assets/AutoColliderGen_Final.cs`
   - 增加碰撞生成诊断接入，以及 PhysX、MuJoCo、V-HACD 的独立开关。
   - 动态生成的 `_MjRoot` 先保持未激活，所有零件完成后只触发一次 MuJoCo 场景重建。
   - 重建前后完整保存并恢复 MuJoCo 的时间、`qpos/qvel/act/ctrl`、warm-start、外力和
     mocap 状态，恢复后执行 `mj_forward`。
   - 将 MuJoCo arena 设为 `64M`；生成场景凸包使用 `contype=2 / conaffinity=1`，禁止
     固定场景凸包之间互撞，同时保留与默认 `1/1` 机器人的碰撞。
   - 为 `MjMeshShape` 烘焙 Unity 完整层级缩放、镜像和剪切，解决 GLB 毫米网格在
     MuJoCo 中被放大约 1000 倍的问题。
   - 接触对象名称使用托管的 `MjGeom.MujocoId -> MujocoName` 映射，不再调用存在原生
     字符串所有权风险的 `mj_id2name` 绑定。
   - 生成 Hull 不再添加 `ModelCollisionHighlighter`；Hull 只保留碰撞和射线代理职责。
   - `MujocoMeshTransformUtility` 定义在本文件末尾，下面的 `Simulation.cs` 会依赖它。

2. `Assets/ColliderGenerationDiagnostic.cs`
   - 新增一次性诊断组件：统计源 Mesh、凸包、顶点、三角面、耗时、MeshCollider、
     MjGeom 和 MjScene 重建次数。
   - 在 Scene 中用多色半透明网格显示生成凸包；疑似与机器人相交的凸包标红。
   - 记录 PhysX 包围盒重叠、`Physics.ComputePenetration`、机器人生成前后位姿、非单位
     缩放和 MuJoCo 接触摘要。

3. `Assets/ColliderGenerationDiagnostic.cs.meta`
   - 上述新脚本的 Unity GUID 元数据。迁移时应与 `.cs` 一起复制，避免场景/预制体引用
     发生变化。

4. `Assets/SimulationPlatform/Scripts/Function/Simulation.cs`
   - 重新加载已保存 `.collider.xml` 时，对场景凸包应用同样的 `2/1` MuJoCo 碰撞过滤。
   - 重新加载时同样调用 `MujocoMeshTransformUtility.CreateBakedMesh()`，避免保存后再次
     打开时恢复为错误尺寸。
   - `.collider.xml` 重载生成的 Hull 同样不再添加高亮组件。
   - 本文件还含其他任务遗留改动，迁移时只移植 `CreateColliderObject()` 中上述两段，
     不要整文件覆盖。

5. `Assets/SimulationPlatform/Scenes/RunScene.unity`
   - 实际运行场景中的碰撞生成器启用 MuJoCo、批量重建和状态恢复。
   - `MjGlobalSettings.GlobalSizes.Memory` 配置为 `64M`。
   - 该场景同时含 UI、窗口尺寸等其他改动；迁移到新版本时建议在 Inspector 手工设置
     对应字段，或只合并精确 YAML 字段，不要整场景替换。

6. `Assets/MissionController.cs`
   - 多目标预计算会把远目标投影到 NavMesh，要求 `PathComplete`，并沿完整路径截取首个
     进入机械臂工作半径的停靠点。
   - 路径、采样或 IK 失败时清空全部缓存并回到 `Idle`，不再把 `{simPos}` 当成功路径。
   - 任务入口把生成 Hull 和原模型统一成逻辑目标并去重。
   - 真实起点与 NavMesh 采样点水平偏差不超过 `0.25m` 时，采样点仅供
     `NavMesh.CalculatePath()` 使用，不加入底盘执行航点；超过 `0.25m` 则明确失败，
     解决首目标“先转向短移、再转回主路径”的现象。

7. `Assets/SimulationPlatform/Scripts/Behaviour/ModelCollisionHighlighter.cs`
   - 自动生成 Hull 仍参与射线和碰撞，但禁用其独立点击处理。
   - `ResolveLogicalSelectionTarget()` 将 Hull 映射回原模型，添加和删除路径点都按逻辑
     Transform 处理，解决一次点击加入两个重复目标的问题。
   - 原模型 Renderer 状态在 `Awake()` 中初始化；旧数据中仍带组件的 Hull 会在所有
     高亮/选中入口识别为代理并退出，解决鼠标悬停时持续空引用和静态高亮竞争。

### 项目记录：3 个文件

8. `.codex/PROJECT_CONTEXT.md`：沉淀当前结构、故障根因、关键参数和后续开发约束。
9. `.codex/WORKLOG.md`：按执行顺序记录当天每轮诊断、修改、验证和日志证据。
10. `.codex/PORTING_NOTES_2026-08-13.md`：本迁移清单。

因此，今天有意涉及的是 **7 个功能代码/资源文件 + 3 个项目记录文件，共 10 个文件**。
运行生成的 `Logs/Log_*.log`、`MUJOCO_LOG.TXT` 只是验证证据，不属于需要移植的源文件。

## 二、推荐迁移顺序

### A. 场景碰撞生成修复

1. 复制 `ColliderGenerationDiagnostic.cs` 和 `.meta`。
2. 按方法和字段合并 `AutoColliderGen_Final.cs`；确认保留文件末尾的
   `MujocoMeshTransformUtility`。
3. 合并 `Simulation.CreateColliderObject()` 中的过滤与网格烘焙逻辑。
4. 在目标版本的实际运行场景 Inspector 中设置：
   - `createMujocoGeoms = true`
   - `batchMujocoSceneRebuild = true`
   - `restoreMujocoStateAfterRebuild = true`
   - `mujocoArenaMemory = 64M`
   - `disableGeneratedGeomSelfCollision = true`
   - `MjGlobalSettings.GlobalSizes.Memory = 64M`

### B. 多目标规划和重复选点修复

1. 先合并 `ModelCollisionHighlighter.ResolveLogicalSelectionTarget()` 及点击去重逻辑。
2. 再合并 `MissionController` 的目标规范化、`TryBuildChassisPath()` 和预计算失败处理。
3. 两个文件必须配套迁移，因为 `MissionController` 会调用
   `ModelCollisionHighlighter.ResolveLogicalSelectionTarget()`。

## 三、不要直接移植的当前脏文件

以下文件虽然当前 `git status` 可能显示修改，但不属于 2026-08-13 这批碰撞/路径修复，
不要因为“全部复制”而带到新版本：

- `Assets/Editor/ExternalRuntimeBuildPostprocessor.cs`
- `Assets/SimulationPlatform/Scenes/Main.unity`
- `Assets/SimulationPlatform/Scenes/MainScene.unity`
- `Assets/SimulationPlatform/Scripts/Tool/SceneWindowSetting.cs`
- `Assets/VisionImageReceiver.cs`
- `ExternalCode/realsense_image_server.py`
- `ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py`
- `ProjectSettings/ProjectSettings.asset`
- `MUJOCO_LOG.TXT`、`Logs/` 和未跟踪的外部测试脚本/模型

`ChassisController.cs`、`ArmController.cs`、`RobotData.cs`、`PathPointManager.cs` 和
`RobotDiagnosticUI.cs` 今天只用于排查，没有修改。

## 四、迁移后的验收标准

- 同一测试装配体：31 个源 Mesh、80 个凸包，`MjScene重建=1`，机器人位移/转角接近
  `0/0`，PhysX 重叠/确认穿透为 `0/0`，不闪退。
- MuJoCo 恢复后接触规模不应回到旧故障的 `ncon=247 / nefc=1220`；本版本最终实测为
  `ncon=1 / nefc=12`。
- 点击一个带生成 Hull 的模型，只新增一个任务点。
- 两个远近不同的任务均能预计算；不可达路径应明确失败并停在 `Idle`。
- 首目标日志应显示起点偏移和“采样点仅用于路径计算”，执行路径不再包含近距离采样
  航点；原复现场景的路径点数应由 3 个降为 2 个，不再先短移再转向。
- 最后重新执行 Unity C# 编译和 Player 打包检查，确认目标版本使用的 MuJoCo 插件 API
  与当前版本兼容。

## 五、鼠标悬停空引用修复

- 根因是 Hull 上的组件在 `Awake()` 被禁用后不执行 `Start()`，但 Unity 旧式鼠标消息
  仍可能进入透明度逻辑，导致未初始化的 `allRenderers` 被遍历。
- 已完成配套修复：生成与 `.collider.xml` 重载均不再添加该组件；高亮脚本对旧代理
  组件早退并清理静态引用，同时在 `Awake()` 幂等初始化原模型的 Renderer 状态。
- 移植时这三个文件必须一起合并，否则只移除一条创建路径会在另一条入口复发。

## 六、2026-08-14 追加：DirectionOnly 指向型 IK

这部分是次日追加，不计入上文“2026-08-13 共10个文件”的数量。

### 需要迁移的功能文件

1. `Assets/MujocoStaticIKSolver.cs`
   - 新增 `PositionOnly / DirectionOnly / FullPose` 三种姿态约束模式。
   - DirectionOnly只约束末端 Site 的 Z 前向轴；使用轴角误差并处理180度反向特例。
   - 对角速度 Jacobian 使用 `I - ff^T` 投影，真正释放绕前向轴的连续 roll 自由度。
   - FullPose保留原有完整姿态与离散 roll fallback；旧的无 Quaternion 调用仍是
     PositionOnly。

2. `Assets/SimulationPlatform/Scenes/RunScene.unity`
   - `MujocoStaticIKSolver.orientationConstraintMode = 1`（DirectionOnly）。
   - `MissionController.arm.enableLookAt = 1`，继续由上层生成目标前向。
   - 目标新版本建议在 Inspector 手工设置这两个字段，不要整场景覆盖，因为当前场景还
     混有UI、模型和其他功能改动。

### 不需要配套覆盖的文件

- `Assets/ArmController.cs`、`Assets/BITStarPlanner.cs`、`Assets/MissionController.cs`、
  `Assets/RobotData.cs` 的公开调用链没有因本功能改变；它们继续传递现有位置和
  `Quaternion?`。

### 运行验收

- Console 应出现 `[IK姿态] mode=DirectionOnly, targetRotation=True`。
- 复测此前固定姿态失败、纯位置成功的高位单点；要求位置误差进入既有阈值，末端 Z
  前向轴指向模型点，同时不要求完整 Euler/roll 与输入 Quaternion 一致。
- 再复测原三点任务、碰撞检查、底盘路径和升降轴，确认放宽姿态没有绕过碰撞验收。
- C# 已通过 `dotnet build "My project21.5.sln" --no-restore`（0 error）；Unity运行时
  运动学与任务回归仍需在目标场景中实测。

### 2026-08-14 后续增强：预热、分层求解和失败诊断

- 仍只需迁移 `Assets/MujocoStaticIKSolver.cs`，不新增 `.meta`，也不要求修改
  ArmController、BITStarPlanner或MissionController公开接口。
- DirectionOnly现在默认先运行内部PositionOnly预热，再以位置为一级任务、方向为零空间
  二级任务精化；中间预热解不会作为机器人动作执行。
- 新字段默认值：
  - `enablePositionWarmStart = true`
  - `enablePositionPriorityDirectionSolve = true`
  - `positionWarmStartMaxIterations = 2000`
- 目标版本若已有同名求解器组件，合并脚本后在 Inspector 核对三个字段；无需放宽原位置
  和方向验收阈值。
- 新诊断标签包括 `[IK预热汇总]`、`[IK碰撞汇总]`、`[IK失败诊断]`、`[IK方向诊断]`、
  `[IK关节限位]` 和 `[IK碰撞候选]`。碰撞Geom名称使用托管组件解析，不可改回
  `mj_id2name`。
- 静态编译已通过，仍需在Unity中复测高位单点和原三点任务；只有运行日志能确认当前
  机器人模型的零空间精化是否找到满足精确方向的可行分支。
- 2026-08-14运行复测结果：分层开关已生效，三轮PositionOnly预热每轮约29~30/40次
  严格收敛，最终位置误差由旧DirectionOnly的31.78mm降至2.90mm；但方向残差稳定在
  11.933度，仍超过1.719度验收上限。全局最佳候选无碰撞，转动关节也未触限，因此迁移
  到新版本时不要误判为碰撞或升降缸故障；后续需要配套方向可行性/备用底盘站位方案，
  或由任务要求明确允许大于当前残差的方向容差。
- 用户随后确认以“相机能够看见接头”为要求，RunScene的
  `MujocoStaticIKSolver.maxAcceptedRotationError`已从`0.03`改为`0.2617994rad`
  （DirectionOnly半角15度）。迁移时应只在目标场景的DirectionOnly组件上设置该值；
  不要同步放宽10mm位置上限、0.005rad严格停止阈值或碰撞检查。
