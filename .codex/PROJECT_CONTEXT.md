# Project Context

## 项目概况

- Unity + C# 项目，主要包含仿真平台、路径点选择、NavMesh 底盘导航、机械臂 IK/BIT* 规划、MuJoCo 仿真，以及 Dobot/底盘/升降缸等真实设备联动代码。
- 主要运行场景包括 `Assets/SimulationPlatform/Scenes/RunScene16.unity`、`RunScene21.unity`、`RunScene.unity`、`Assets/Scenes/SampleScene.unity`。
- 项目已有大量未提交修改，修改时应只碰当前任务相关文件，避免回退用户已有工作。

## 关键脚本

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

## NavMesh 运行时烘焙

- `Assets/SimulationPlatform/Scripts/Function/Simulation.cs` 的 `OnPathPlanClicked()` 会在开始任务前调用 `RebuildRuntimeNavMesh()`。
- 2026-06-25 调整后，运行时烘焙不再只依赖机器人根节点的 `NavMeshModifier`，而是对 `MissionController.gameObject` 整棵子层级逐个添加/更新 `NavMeshModifier`。
- 原因：`NavMeshSurface` 会用自身 `LayerMask` 过滤 `NavMeshModifier` 所在物体的 Layer；如果根节点在 `Robot` 层但 Surface 不包含 `Robot` 层，根节点 modifier 可能不会生效。
- `SampleScene` 中 `cr10_robot356/ground` 是 MuJoCo 世界地面，不属于机器人障碍物，必须保留进 NavMesh 烘焙；运行时代码会跳过名字为 `ground`/`floor`/`plane` 的世界地面对象，并确保它们 `ignoreFromBuild=false`。
- 其他机器人子物体会设置 `ignoreFromBuild=true`，用于排除底盘和机械臂本体。
- 新日志会打印实际重建的 Surface 层级路径、排除机器人对象数、保留地面对象数、Surface LayerMask，以及该 Surface 是否包含机器人根节点 Layer。
