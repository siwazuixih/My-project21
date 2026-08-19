# Worklog

## 2026-08-13 - 最终复测通过与碰撞生成问题完整结论

### 本次任务目标

- 复核修复后的最新Unity运行日志。
- 验证碰撞生成、机器人位姿、MuJoCo稳定性和后续规划是否正常。
- 整理本次“切割后机器人被推飞/卡顿/闪退”的完整原因链。

### 读取的关键文件和证据

- `AGENTS.md`、`.codex/WORKLOG.md`、`.codex/PROJECT_CONTEXT.md`
- `Logs/Log_2026-08-13_16-44-01.log`
- Unity当前`Editor.log`及上一轮崩溃的`Editor-prev.log`
- 项目`MUJOCO_LOG.TXT`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`中`default (1)`和`geom_13`

### 最新运行结果

- 同一装配模型31个源Mesh成功生成80个凸包、1930个顶点、3540个三角面，耗时
  11.14秒；生成后有80个MjGeom。
- MuJoCo场景只重建1次，64M arena生效，状态恢复和`mj_forward`完整返回。
- `ncon=1 / nefc=12`；修复前相同模型为`247 / 1220`，异常约束已基本消失。
- 唯一初始接触为`default (1) <-> geom_13`，1个接触点，`dist=-0.004098 m`。
  两个名称均为RunScene中原先存在的序列化MjGeom，不属于本次动态创建的`_Hull_`
  凸包；生成后机器人位移`0.0000 m`、转角`0.00°`，因此它不是本次支架凸包造成的
  强制分离。
- PhysX包围盒重叠0、确认穿透0；用户随后用Ctrl拖动机械臂接触支架，碰撞响应正常。
- 后续BIT*完成2个任务，任务流程正常结束；当前Editor.log没有fatal signal、invalid
  pointer、arena溢出或新的Nan/Inf QACC。`MUJOCO_LOG.TXT`修改时间仍停在16:25旧故障。
- BIT*过程中出现少量采样超时警告，但每次搜索最终成功，与碰撞生成故障无关。

### 问题原因整理

1. **装配体卡顿和第一次大位移**：旧生成器每处理一个Mesh就激活MjBody/MjGeom并让出
   一帧，31个零件触发31次完整MjScene重建；插件只有限恢复状态，造成明显停顿和机器人
   15.6483米跳变。改为全部未激活生成、最后一次交换并完整保存/恢复运行状态。
2. **批量后第一次闪退**：一次恢复全部几何后，由错误几何产生265个接触、1310个约束，
   MuJoCo默认约200KB arena不足，在`mj_projectConstraint`栈分配失败。arena改为64M，并
   用`2/1`过滤禁止固定场景凸包互相碰撞。
3. **不再闪退但机器人仍被推开**：GLB网格顶点以毫米保存，glTFast把顶层`0.001`比例
   保留在Unity Transform；PhysX会继承该比例，MuJoCo的`MjMeshShape`却只导出
   `mesh.vertices`，忽略Transform缩放。结果MuJoCo中的支架凸包约放大1000倍，产生
   `247/1220`接触/约束并把机器人推走1.0673米。现给MuJoCo单独使用烘焙完整层级变换
   的网格副本，PhysX和可视化继续使用原网格。
4. **缩放修复后的诊断闪退**：几何已经正确并降到`1/12`，但新增诊断调用自动生成的
   `mj_id2name` C#字符串绑定，错误释放MuJoCo内部只读指针，触发
   `munmap_chunk(): invalid pointer`。现改为按活动`MjGeom.MujocoId`构建纯托管名称映射。

### 修改情况与当前结论

- 本轮只更新项目内`.codex/WORKLOG.md`和`.codex/PROJECT_CONTEXT.md`，没有继续修改代码、
  场景或碰撞参数。
- 本问题现已通过运行验收：生成不再推飞机器人、不再闪退，规划和主动接触测试正常。
- 当前诊断仍会输出“31个非单位缩放”“同时创建PhysX与MuJoCo”等提示，它们用于说明
  模型特征和排障配置，不代表本次运行失败。

### 后续建议

- 保存一次该场景的`.collider.xml`后，再退出并重新进入RunScene测试自动加载路径；
  `Simulation.CreateColliderObject`已使用同一缩放烘焙工具，但该持久化路径尚未得到本轮
  实际运行验收。
- 若以后导入其他单位或带非均匀缩放的GLB，应保留`[MuJoCo缩放诊断]`和接触对摘要，
  以便快速判断模型单位问题。

## 2026-08-13 - 缩放修复复测闪退：修复mj_id2name无效释放

### 本次任务目标

- 定位缩放烘焙版本生成结束后Unity再次闪退的直接原因。
- 保留安全的接触对诊断，同时避免任何MuJoCo原生字符串内存所有权问题。

### 读取的关键文件和证据

- `AGENTS.md`、`.codex/WORKLOG.md`、`.codex/PROJECT_CONTEXT.md`
- `Logs/Log_2026-08-13_16-40-39.log`、Unity `Editor.log`、项目`MUJOCO_LOG.TXT`
- `Assets/AutoColliderGen_Final.cs`和MuJoCo `MjBindings.cs`、`MjComponent.cs`

### 闪退证据与结论

- 31个零件和80个凸包均完成生成，状态恢复后的`mj_forward`成功返回：arena为64M，
  `ncon=1`、`nefc=12`。相比修复前的`247/1220`大幅下降，说明缩放烘焙本身有效，且
  这次不是arena耗尽。
- 上述日志之后立即出现`munmap_chunk(): invalid pointer`和fatal signal 6；调用位置正好
  是新增接触名称诊断。
- MuJoCo的`mj_id2name`返回模型内部只读`const char*`，但当前自动生成绑定声明为返回
  C# `string`。Linux P/Invoke封送在返回后错误处理该指针，导致无效释放并终止Unity。
- `MUJOCO_LOG.TXT`修改时间仍停留在16:25，未产生新的MuJoCo求解器错误。

### 修改的文件与内容

- `Assets/AutoColliderGen_Final.cs`
  - 接触诊断完全移除`MujocoLib.mj_id2name`调用。
  - 改为扫描当前活动`MjGeom`，使用已经绑定的`MujocoId`建立`id -> MujocoName`纯托管
    字典；未找到的ID只显示`geom#数字`。
  - 接触数组和距离统计保留，不读取、不修改也不释放MuJoCo名称缓冲区。
- `.codex/PROJECT_CONTEXT.md`、`.codex/WORKLOG.md`

### 验证情况

- `dotnet build Assembly-CSharp.csproj --no-restore`通过：0错误，18条项目原有警告。
- `git diff --check -- Assets/AutoColliderGen_Final.cs`通过。
- 未修改场景、模型、碰撞参数、arena或机器人控制逻辑。

### 当前状态与下次检查

- 本次由诊断代码引入的原生无效释放已修复；仍需Unity重新运行确认不再退出。
- 重新生成同一模型后，预期仍为约`ncon=1/nefc=12`，并能安全输出这一组接触对象。
- 若机器人保持原位，则缩放问题完成；若仍有轻微异常，再根据唯一接触对名称判断它是
  正常机器人-地面接触还是场景凸包与机器人接触。

## 2026-08-13 - 机器人生成后散开：补充MuJoCo接触对与运行时缩放取证

### 本次任务目标

- 继续排查批量生成后Unity不再退出、但机械臂与底座被MuJoCo推散的问题。
- 纠正把截图右上深蓝色UI画布误认成异常碰撞几何的判断。
- 先增加不改变物理行为的诊断证据，再决定是否修改网格坐标或接触过滤。

### 读取的关键文件和证据

- `AGENTS.md`、`.codex/WORKLOG.md`、`.codex/PROJECT_CONTEXT.md`
- `Assets/AutoColliderGen_Final.cs`、`Assets/ColliderGenerationDiagnostic.cs`
- 本地MuJoCo插件的`MjEngineTool.cs`、`MjMeshShape.cs`、`MjcfGenerationContext.cs`、
  `MjBindings.cs`
- `Logs/Log_2026-08-13_16-29-48.log`与
  `Files/9abf8d61-8941-4b75-a575-fe01ed3e385c/simple_20260813144530.glb`

### 修改的文件

- `Assets/AutoColliderGen_Final.cs`
- `Assets/ColliderGenerationDiagnostic.cs`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动及原因

- 在状态恢复后的第一次`mj_forward`之后，按几何体对汇总MuJoCo接触：输出总接触点数、
  接触对象组数、最深20组几何体名称、每组接触点数和最小`dist`。这样能直接判断接触
  来自生成场景与机器人、机器人内部，还是其他对象，同时限制日志数量避免刷屏。
- 每个参与生成的源Mesh现在记录运行时层级路径、`lossyScale`、Mesh局部Bounds尺寸和
  Renderer世界Bounds尺寸；生成摘要单独列出最多20个非单位缩放对象。
- 进一步核对glTFast 6.14.1源码，确认其会把GLB矩阵比例写入`Transform.localScale`；
  MuJoCo插件的网格导出和组件坐标转换均不处理该缩放，因此无需依赖截图即可确认比例
  在MuJoCo路径中丢失。
- 新增`MujocoMeshTransformUtility.CreateBakedMesh`：从Unity完整`localToWorldMatrix`
  中剥离MuJoCo本来会写入的位置/旋转，把剩余缩放、镜像和剪切烘焙到专用网格副本。
- 生成时只有`MjMeshShape`使用烘焙副本；MeshCollider、彩色诊断Mesh和原模型继续使用
  原始网格，避免Unity重复缩放。`Simulation.CreateColliderObject`重新加载保存碰撞体时
  也调用同一工具，保存文件仍保持原局部坐标。
- 没有关闭场景与机器人接触，也没有修改现有接触过滤参数；用户指出的深蓝色UI画布
  不再作为任何碰撞判断依据。

### 当前判断

- 16:30实测确认状态恢复成功后仍立即存在`247`个MuJoCo接触和`1220`个约束；约5秒后
  机器人移动`1.0673 m`，而PhysX穿透为0。问题已从“反复重建/状态丢失”进一步收敛到
  MuJoCo中的实际接触或坐标表示。
- 原始GLB确有毫米网格和顶层`0.001`矩阵；glTFast保留该Transform比例，而MuJoCo导出
  漏掉比例，因此生成碰撞体在MuJoCo中尺寸错误已由完整代码链确认。新增接触日志用于
  验收修复后接触是否消失，并排除同时存在的其他接触问题。

### 验证情况

- 缩放烘焙和接触诊断完成后，`dotnet build Assembly-CSharp.csproj --no-restore`通过：
  0错误，18条均为项目原有警告。
- `git diff --check`对本轮两个脚本通过。
- 未修改场景YAML、GLB、机器人控制、真实设备逻辑或碰撞行为。

### 当前是否完成与下一步

- 代码层缩放修复和诊断增强已完成，Unity运行验收尚未完成。
- 在Unity重新生成一次后，搜索`[MuJoCo接触诊断]`和`[MuJoCo缩放诊断]`；重点保存最深
  接触对名称、源Mesh的lossyScale和局部/世界尺寸。
- 预期同一模型的`ncon/nefc`会从`247/1220`明显下降，机器人位移应接近0；若仍有接触，
  直接按新增接触对名称继续处理实际相交零件，不再猜测。

## 2026-08-13 - 批量 MuJoCo重建首次测试闪退与原生内存修复

### 闪退证据与根因

- 最新测试日志为 `Logs/Log_2026-08-13_16-25-03.log`；31个源 Mesh均完成 V-HACD，
  项目日志在最后一个零件后立即中断，说明闪退发生在统一启用 MjGeom/重建阶段。
- `MUJOCO_LOG.TXT` 在16:25:26给出明确原生错误：
  `mj_stackAlloc: out of memory, stack overflow at mj_projectConstraint`；当时
  `ncon=265`、`nefc=1310`，arena最大约204768字节、可用99864字节，但一次还需
  104800字节。
- 随后 `Editor-prev.log`记录 Unity收到 fatal signal 5并退出。这不是托管C#异常，也不是
  状态数组越界的现有证据，而是一次性保留机器人当前位置后，较多接触约束超过 MuJoCo
  默认自动分配的内部 arena。

### 修复内容

- `RunScene`的 `MjGlobalSettings.GlobalSizes.Memory`由`-1`改为`64M`；生成器也新增
  `mujocoArenaMemory=64M`并在批量重建前运行时确保该设置生效，避免其他入口仍使用过小
  自动值。
- 新增`disableGeneratedGeomSelfCollision`：生成的场景凸包使用
  `contype=2 / conaffinity=1`。依据本地 MuJoCo 3.3.7的`filterBitmask`规则，两个生成
  凸包之间不会碰撞；默认`1/1`机器人仍能与生成场景发生接触。
- `Simulation.CreateColliderObject`加载已保存碰撞体时同样应用2/1过滤，避免重新加载后
  恢复无意义的场景内部接触。
- 状态恢复的`mj_forward`成功返回后新增arena字节数、`ncon`和`nefc`日志，下一次可直接
  判断内存余量与接触规模。

### 验证与下一步

- `dotnet build Assembly-CSharp.csproj`通过：0错误，18条项目既有警告。
- 尚未完成Unity运行复测。下一次应先确认不再闪退，再检查日志中的arena、ncon/nefc、
  MjScene重建次数和机器人位移；若ncon仍高且机器人移动，则说明机器人确实与某批
  MuJoCo凸包接触，需要继续输出具体 geom接触对，而不是继续增加内存掩盖几何问题。

## 2026-08-13 - MuJoCo碰撞体批量重建与运行状态保护

### 修改目标

- 修复装配体碰撞生成时每个源 Mesh分别触发一次 `MjScene.RecreateScene()`，导致机器人
  位姿跳变15.6483米和明显卡顿的问题。
- 恢复场景模型的 MuJoCo碰撞表示，使 BIT*/IK仍能使用新生成障碍物，而不是长期停留在
  仅 PhysX诊断模式。

### 修改文件

- `Assets/AutoColliderGen_Final.cs`
- `Assets/ColliderGenerationDiagnostic.cs`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`（只将实际运行引用的生成器重新开启
  MuJoCo，并明确启用两个新保护开关；保留场景中其他既有改动）
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 实现内容

- 新增 `batchMujocoSceneRebuild`，默认开启。运行时生成的新 `_MjRoot`先保持未激活，全部
  V-HACD凸包准备完成后在同一帧统一启用；旧生成物也在同一次批量交换中移除。
- 新增 `restoreMujocoStateAfterRebuild`，默认开启。在批量交换前保存 MuJoCo的时间、
  `qpos/qvel/act/ctrl`、warm-start、外力和 mocap状态；等待唯一一次重建完成后恢复状态，
  调用 `mj_forward`并同步 Unity对象。
- 不再在每个零件开始时单独清理旧 `_MjRoot`，避免重新生成时因逐零件 OnDisable再次造成
  多次重建。
- 移除循环中每10个 Mesh执行的 `GC.Collect/WaitForPendingFinalizers`，保留每个源 Mesh
  后短暂让出主线程，但此时 MuJoCo组件尚未激活，不会触发重建。
- 清理命令改为直接查找所有生成根节点，能覆盖未激活的批量节点。
- 诊断位姿错误提示不再一概归因于碰撞强制分离：有 PhysX穿透时提示检查凸包；无穿透
  但发生 MjScene重建时提示检查重建和状态恢复。
- 诊断现在以“MjScene仅重建1次”为批量成功标志；超过1次会明确提示存在其他脚本同时
  增删 MjComponent。

### 验证情况与下一步

- `dotnet build Assembly-CSharp.csproj --no-restore`通过：0错误，18条均为项目既有警告。
- 尚未在 Unity运行时执行新的 MuJoCo对照测试。下一次应使用同一装配体生成，预期日志：
  `MjGeom=80`、`MjScene重建=1`、机器人位移接近0、PhysX穿透0，并出现
  `[MuJoCo批量重建] ...成功恢复重建前运行状态`。
- 若仍发生位姿变化，应保留本次日志再核对重建前后 qpos地址；不要先扩大改动到真实设备
  控制或规划逻辑。

## 2026-08-13 - 仅 PhysX 对照实验确认机器人位姿跳变根因

### 测试配置与结果

- 用户取消 `createMujocoGeoms`，保留 `createUnityPhysicsColliders`，使用同一装配体
  `simple_20260813144530` 再次生成。
- 最新项目日志：`Logs/Log_2026-08-13_16-07-46.log`。
- 本次仍由31个源 Mesh生成80个凸包，最终 MeshCollider仍为111个，说明 V-HACD与
  PhysX凸包生成流程完整执行；MjGeom为0、MjScene重建为0。
- 结束检查：PhysX疑似重叠0、确认穿透0，机器人位移0.0000米、转角0.00°。
- 耗时由同时创建 MuJoCo时的18.29秒下降到10.95秒，减少7.34秒，约快40%。

### 已确认结论

- 同模型、同80个凸包，在仅 PhysX模式下机器人完全不移动；启用 MuJoCo时机器人移动
  15.6483米且发生31次 MjScene重建。因此本次“机器人被弄飞”的主因已经锁定为运行中
  逐零件添加 MjGeom导致的 MuJoCo场景反复重建/状态同步跳变，不是 PhysX切割凸包
  对机器人的强制分离。
- 现有原始31个 MeshCollider与新增80个凸包 MeshCollider仍属于重复物理表示，可能影响
  后续性能和碰撞精度，但不是本次机器人位姿突变的直接原因。
- 后续修复方向应是先离线/批量建立全部 MjBody/MjGeom，再只请求一次 MjScene重建，并
  在重建前后完整保存、恢复和前向计算机器人状态；修复前不应恢复当前逐零件 MuJoCo
  动态生成方式。
- 本轮只复核日志并确认根因，没有修改运行代码或场景。

## 2026-08-13 - 首次装配体碰撞诊断结果复核

### 日志位置纠正

- 用户在 15:59 已实际执行诊断。Unity 随后发生过重启/日志轮转，本次完整控制台记录
  位于 `~/.config/unity3d/Editor-prev.log`，不是新的 `Editor.log`。
- 项目内对应运行日志为 `Logs/Log_2026-08-13_15-58-55.log`。后续排查必须先按时间
  检查 `Editor.log`、`Editor-prev.log` 和项目 `Logs`，避免因轮转漏掉本次执行。

### 本次实测结果

- 当前模型 `simple_20260813144530`：源 Mesh 31，理论凸包上限 992，实际生成凸包
  80，耗时 18.29 秒。
- 生成前已有 31 个 MeshCollider；生成后为 111 个 MeshCollider，并新增 80 个
  MjGeom。80 个 V-HACD输出均缺法线，已由新代码补算。
- 生成结束检查：PhysX包围盒疑似重叠 0，`Physics.ComputePenetration`确认穿透 0。
- 同一期间机器人记录到 15.6483 米位移、0.76°转角；MjScene共重建 31 次，数量与
  装配体源 Mesh数完全一致。

### 当前判断

- 现有结果不支持把这次 15.65 米跳变直接归因于“最终凸包仍压住机器人”。结束时没有
  PhysX重叠；虽然一次性结束检查不能完全排除生成过程中的瞬时穿透，但 31 次 MjScene
  重建与位姿突变同时出现，使 MuJoCo运行时逐零件重建/状态恢复不完整成为第一嫌疑。
- 诊断脚本当前红色提示把明显位姿变化直接解释成碰撞强制分离，措辞过度，需要后续改为
  同时提示“瞬时物理穿透或 MuJoCo场景重建导致的状态跳变”，并用隔离测试再下结论。
- 下一次应先测仅 PhysX：`createUnityPhysicsColliders=true`、
  `createMujocoGeoms=false`。若 MjScene重建降为 0 且机器人不再移动，即可确认主要根因
  是 MuJoCo动态重建；再决定批量创建后单次重建及完整状态保存方案。
- 轮转日志中大量 Gizmo缺法线及 MuJoCo运行时异常包含更早的执行/脚本热重载阶段；本次
  15:59生成区间内未出现新的异常堆栈，不能把整份历史计数直接算到本次生成上。
- `LoginScene` 等场景的 Missing Script警告是另一项场景引用清理问题，目前没有证据表明
  它导致此次机器人位姿跳变。

## 2026-08-13 - 装配体碰撞生成诊断脚本与卡顿根因定位

### 本次任务目标

- 为 `My project21.5` 的 V-HACD 碰撞生成增加一次性诊断工具。
- 定位整体模型和装配体模型在切割后都可能把机器人推飞、装配体还会明显变卡的原因。
- 本轮以诊断和隔离测试为主，保留现有默认碰撞生成行为，不直接决定最终物理方案。

### 读取的关键文件和证据

- `AGENTS.md`、`.codex/WORKLOG.md`、`.codex/PROJECT_CONTEXT.md`
- `Assets/AutoColliderGen_Final.cs`、`Assets/MissionController.cs`、`Assets/RobotData.cs`
- `ModelImport.cs`、`SceneEdit.cs`、`Simulation.cs`、`ColliderManager.cs`
- 本地 MuJoCo Unity插件的 `MjComponent.cs`、`MjScene.cs`、`MjMeshShape.cs`
- Unity `Editor.log`

### 修改的文件

- `Assets/ColliderGenerationDiagnostic.cs`（新增）及其 `.meta`
- `Assets/AutoColliderGen_Final.cs`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动及原因

- 生成器默认自动给场景模型添加 `ColliderGenerationDiagnostic`。
- 每次生成统计有效源 Mesh数、理论凸包上限、实际凸包/顶点/三角面数、每个零件
  的凸包数和耗时、原有/最终 MeshCollider数、最终 MjGeom数及 MjScene重建次数。
- 诊断网格默认使用多种半透明颜色；与机器人 Collider包围盒相交的凸包标红。
- 生成结束后只检查一次 PhysX重叠，尽量用 `Physics.ComputePenetration` 输出凸包、
  机器人碰撞体、分离方向和深度；详细日志限制前20条，避免诊断自身刷屏。
- 自动记录生成前后机器人位移/转角，超过 `0.01 m / 1°` 时提示状态突变。
- 增加 `createUnityPhysicsColliders`、`createMujocoGeoms` 隔离开关，可做仅 PhysX、
  仅 MuJoCo和仅显示测试；两项默认都开，保持旧行为。
- 将写死的 `doVHACD = true` 改为 `forceVHACDForAllMeshes`；默认仍开，关闭后才真正
  按 `hollowParts` 区分 V-HACD和快速实心模式。
- V-HACD输出在交给 `MjMeshShape` 前检查并补算法线，避免 Scene Gizmo持续报缺法线错误。
- `ShouldSkip` 会检查整个父层级是否位于 `_MjRoot` 下，避免仅 PhysX模式的可视 Mesh
  在下一次生成时被再次分解。
- 移除了在空根节点上调用且实际不会添加任何碰撞体的
  `ModelTool.AddMeshCollidersToModel(root, true)`。

### 当前定位结论

- 旧代码无条件让所有 Mesh执行 V-HACD；`hullCount=32` 是每个 Mesh最多32个，不是
  整个装配体最多32个，零件数增加会使凸包和组件数成倍增长。
- 模型导入时已有原始 `MeshCollider`；分解后又添加凸包 `MeshCollider` 并同时创建
  `MjGeom`，存在原始整网格、PhysX凸包和 MuJoCo凸包多重碰撞表示。
- MuJoCo插件在运行中新增 `MjComponent` 会请求重建整个 MjScene；生成器每个零件后
  `await` 让出一帧，装配体可能触发多次整场景重建。插件只缓存恢复关节 qpos/qvel，
  不完整恢复 act/ctrl，是卡顿和机器人状态突变的重要嫌疑。
- 生成器每10个源 Mesh同步执行 `GC.Collect/WaitForPendingFinalizers`，会周期性停顿。
- 当前 `Editor.log` 统计到124次缺少 positions/normals 的 Gizmo错误，确认控制台刷屏
  也是编辑器变卡的实际原因；新生成网格已补法线。

### 验证情况

- 临时将新脚本加入 Unity自动生成的 `Assembly-CSharp.csproj` 后执行 `dotnet build`：
  编译成功，0错误；仅工程既有18条警告。随后撤销了对自动生成 csproj的临时修改。
- 对本轮脚本执行的 `git diff --check` 通过；全仓检查仍会报告用户原有场景/窗口脚本
  改动中的行尾空格，本轮未改动或格式化那些文件。
- 未自动操作用户当前 Unity场景，实际凸包数、重叠对象和重建次数需下一次测试确认。
- 没有修改场景 YAML、模型文件、数据库或真实设备控制逻辑。

### Unity手动检查与下一步

- 进入 `RunScene` 后先 Clear Console，再用当前装配体生成；搜索 `[碰撞诊断]`，记录
  生成统计、红色凸包、重叠明细、机器人位移和 MjScene重建次数。
- 清理生成物后依次测试：仅 PhysX（true/false）、仅 MuJoCo（false/true）、仅显示
  （false/false）。
- 若红色凸包覆盖机器人，优先检查模型坐标和切割结果；若仅 MuJoCo会飞且重建次数
  大于0，下一步应批量生成后只重建一次并完整保存 qpos/qvel/act/ctrl；若仅 PhysX
  会飞，则应去掉原始与凸包 MeshCollider的重复表示。

## 2026-07-26 - 视觉服务自动切换独立 Python 环境

### 本次任务目标

- 配合正式工控机的 Python 3.11 独立视觉环境，使现有 Unity 构建继续通过
  `/usr/bin/python3` 启动脚本时，也能安全切换到 GPU视觉环境。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `ExternalCode/realsense_image_server.py`
- `Assets/VisionImageReceiver.cs`
- `Assets/Editor/ExternalRuntimeBuildPostprocessor.cs`
- `TEST/AGENTS.md` 及 TEST 上下文

### 修改的文件

- `ExternalCode/realsense_image_server.py`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`
- 同时在顶层 `TEST` 增加正式工控机安装验证 TXT

### 具体改动及原因

- 脚本在导入 OpenCV/RealSense 前查找独立解释器：
  - 优先使用 `VISION_PYTHON_EXECUTABLE`。
  - 默认根据软件目录定位 `/home/a/software/vision_env/bin/python`。
- 切换前用独立解释器检查 OpenCV、NumPy、RealSense、PyTorch、
  Ultralytics和 CUDA；全部通过才使用 `os.execve()` 重启自身。
- 环境不存在、依赖不完整或 CUDA不可用时不切换，继续使用现有系统 Python，
  保留普通实时图降级能力。
- 这样无需让 V26拧紧程序共用视觉依赖，也无需修改 `/usr/bin/python3`。

### 当前状态与验证

- 代码修改完成，`python3 -m py_compile` 语法检查通过，
  `git diff --check` 未发现空白错误。
- 未连接或控制机械臂、电批和相机；独立解释器切换、CUDA和模型加载仍需在正式
  工控机按 TXT 验证。
- 正式工控机必须将新版脚本复制到现有软件的 `ExternalCode`，或重新打包软件。

### 下一步和 Unity 检查

- 先完成独立环境依赖/GPU验证，再单独启动视觉服务验证两个模型。
- 最后在真实运行 UI 点击“开启视觉”，确认 `preview_mode=processed`、
  推理计数增长、双击放大和关闭相机均正常。
- 同时确认启动拧紧程序后力矩曲线未受独立视觉环境影响。

## 2026-07-25 - 正式工控机视觉原图降级根因确认

### 工控机环境补充

- 系统 Python：`/usr/bin/python3` 为 Python `3.8.10`。
- 显卡：检测到 NVIDIA PCI 设备 `2b87`，具体型号和驱动/CUDA 能力仍需通过
  `nvidia-smi` 确认。
- 软件所在分区：总容量约 `1.9T`，可用约 `1.7T`，不存在磁盘空间不足问题。
- 后续应优先使用独立视觉 Python 环境；不要向拧紧程序共用的系统 Python
  直接安装或升级整套视觉依赖。

### 状态证据和结论

- 用户提供 `http://127.0.0.1:8080/status`：
  - `ok=true`、`preview_ready=true`。
  - D435 RGB-D启动正常，USB为 `3.2`，深度可用，普通帧持续增长。
  - 两个模型路径均正确指向构建目录的 `ExternalCode/models`。
  - `inference_count=0`，说明尚未进入任何一次推理。
  - `vision.model_error` 和 `last_error` 明确为
    `No module named 'ultralytics'`。
- 根因已确定为 Unity实际调用的 `/usr/bin/python3` 未安装 Ultralytics，不是
  相机、USB、权重路径、HTTP或Unity显示问题。

### 建议和后续

- 不建议直接把最新版 Ultralytics/Torch安装进共享的系统/用户 site-packages：
  该机的 `/usr/bin/python3` 同时运行 V26拧紧曲线，直接安装可能再次升级
  NumPy/OpenCV并破坏系统 Matplotlib兼容性。
- 推荐建立视觉专用虚拟环境，并让视觉脚本自动切换到该解释器；V26继续使用原来的
  `/usr/bin/python3`。
- 安装前先采集 `/usr/bin/python3 --version`、CPU/GPU信息和磁盘空间，再确定
  CPU版或CUDA版 Torch及兼容版本。
- 本轮只确认根因和安装边界，没有安装软件、修改依赖或控制设备。

## 2026-07-25 - 视觉模型已复制但仍原图降级的排查方案

### 本次任务目标和现象

- 用户已将 `best.pt`、`sam2_b.pt` 放入构建软件的
  `ExternalCode/models`，但 Unity日志仍显示 `raw_fallback`。
- 截图中普通 `1280×720` 实时图正常，说明相机、8080 HTTP服务、Unity拉流和
  降级机制均正常；故障范围仅在模型加载或视觉推理。

### 排查结论和顺序

- 第一证据来源是保持“开启视觉”时访问
  `http://127.0.0.1:8080/status`：
  - `vision.state=raw_fallback`
  - `vision.model_error` 记录模型文件、Ultralytics/Torch或权重加载错误。
  - `vision.last_error` 记录运行期 YOLO/SAM处理错误。
- 若是依赖错误，必须用 Unity实际调用的 `/usr/bin/python3` 检查
  `torch`、`ultralytics` 版本和加载位置，不能只检查另一个虚拟环境或 `pip`。
- 若依赖导入正常，再在软件根目录单独加载两个权重，区分 YOLO权重、SAM权重或
  Ultralytics版本接口不兼容。
- 若权重能加载但服务仍降级，应以 `/status` 的 `vision.last_error` 为准检查
  推理调用；连续3次异常后服务会按设计熔断回原图。
- 本轮只提供只读诊断步骤，没有安装依赖、修改代码或控制机械臂。

### 手动检查

- 保持“关闭视觉”按钮可见（表示服务正在运行）时获取 `/status` 完整 JSON并回传。
- 同时可检查两个 `.pt` 的文件大小，排除零字节、不完整复制或错文件。
- Unity Inspector本轮无需检查。

## 2026-07-25 - V26实时曲线 JPEG/PNG 正式兼容修复

### 本次任务目标

- 将此前在正式工控机手动完成的 PNG修改正式写回工程，确保以后重新打包不再手动
  修改 V26 Python文件。

### 修改内容

- 修改 `ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py`：
  - 优先使用 `print_jpg`，其次使用 `print_jpeg`。
  - 有 JPEG接口但不接受 `pil_kwargs` 时使用兼容调用。
  - 两个 JPEG接口都不存在时自动使用 `print_png`。
  - HTTP响应使用实际 `image/jpeg` 或 `image/png`，不再固定声明 JPEG。
  - `/status` 新增 `content_type`。
- 曲线 URL、Unity C#、最终 CSV/PNG保存、力矩算法、电批命令和安全逻辑均未改变。

### 验证情况

- Python语法编译通过。
- 模拟 Canvas的 JPEG和仅 PNG两条分支均通过。
- 使用 `127.0.0.1:1` 假电批地址安全启动嵌入模式，曲线服务返回
  `ok=true`、`render_count>0`、`last_error=""`；开发机选择
  `content_type=image/jpeg`。
- 测试没有发送正转或反转命令，结束时正常关闭。

### 当前状态和手动检查

- 工程源文件已修复，后续打包会自动带上，不需要再手改工控机。
- 正式工控机更新脚本后，应检查 `/status` 为 `ok=true`、
  `content_type=image/png`、`last_error=""`，并确认曲线正常显示。
- 本轮无需 Inspector检查，没有连接或驱动真实电批。

## 2026-07-25 - 视觉程序使用两个模型的职责说明

- 本轮目标：解释 `best.pt` 和 `sam2_b.pt` 为什么同时存在，未修改代码。
- 检查了 `measure copy.py` 的模型加载及处理链路：
  - `best.pt` 是视觉同学训练的 YOLO检测模型，负责识别目标并给出矩形框。
  - `sam2_b.pt` 是 SAM分割模型，以 YOLO框为提示获得精确像素轮廓。
  - 程序随后从轮廓计算最小外接旋转矩形、中心和角度，再结合 D435I深度计算 XYZ。
- 当前忠实复现原算法需要两个模型；若只保留 YOLO，可直接使用检测框中心，速度更快
  但中心/角度精度通常下降。长期可训练一个 YOLO分割模型，直接输出掩膜并移除 SAM。
- 本轮无需 Unity Editor手动检查；下一步由用户确认优先保持原算法，还是先采用单
  YOLO轻量方案。

## 2026-07-25 - 视觉处理失败自动返回普通实时图

### 本次任务目标

- 开始实施视觉程序第一阶段集成。
- 保证 YOLO/SAM 模型缺失、依赖缺失、模型加载失败或推理异常时，Unity
  “现场视频”仍能显示普通 RealSense 实时图像，不出现黑屏。

### 读取和检查的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `ExternalCode/measure copy.py`
- `ExternalCode/realsense_image_server.py`
- `Assets/VisionImageReceiver.cs`
- `Assets/Editor/ExternalRuntimeBuildPostprocessor.cs`
- `ExternalCode/point_move_demo.py`

### 修改的文件和内容

- `ExternalCode/realsense_image_server.py`
  - 改为单一常驻 RGB-D 相机服务；独立采集线程持续保留普通彩色 JPEG。
  - 优先启动对齐的彩色/深度流；RGB-D配置失败时重试彩色流。
  - 后台异步加载 `ExternalCode/models/best.pt` 和 `sam2_b.pt`。
  - 模型缺失、依赖/加载失败时进入 `raw_fallback`，继续返回普通实时图。
  - 模型就绪后执行 YOLO、SAM、轮廓/旋转矩形/中心点绘制和相机 XYZ 计算。
  - 连续3次处理异常后自动熔断回原图。
  - 增加 `X-Vision-Mode` 响应头、详细 `/status` 和结构化 `/result`。
- `Assets/VisionImageReceiver.cs`
  - 按钮文字改为“开启视觉/关闭视觉”，保留原 public 方法和场景绑定。
  - 仅在模式变化时记录“显示处理图”或“自动显示普通实时图”，避免刷屏。
- `Assets/Editor/ExternalRuntimeBuildPostprocessor.cs`
  - Linux构建后可选复制 `ExternalCode/models` 下的 `.pt` 文件。
  - 模型缺失只警告，不阻止打包；软件仍可使用原图模式。
- `ExternalCode/models/README.txt`
  - 记录默认模型文件名和环境变量覆盖方式。

### 为什么这样改

- RealSense必须保持唯一所有者，不能由原视频服务和视觉原型各打开一次。
- 模型加载和推理比采集更容易受权重、依赖和算力影响，算法异常不能同时破坏现场
  观察能力。
- Unity仍请求 `127.0.0.1:8080/latest.jpg`，现有小窗、双击放大、启停和
  Texture生命周期无需重写。

### 验证情况

- Python语法编译通过。
- 无模型降级测试通过：状态进入 `raw_fallback`，不会在模块导入时强制加载
  Ultralytics。
- 原图选择测试通过：视觉未就绪时返回普通 JPEG和 `raw_fallback`。
- `Assembly-CSharp-Editor.csproj` 编译通过，0错误；仅有工程既有警告。
- 开发机当前没有连接 RealSense；真机启动测试只能确认“无设备”，无法在本机
  验证实际 RGB-D 帧与模型推理。

### 当前状态、遗留问题和手动检查

- 第一阶段“算法失败仍有原图”代码已经完成。
- 工程当前没有两个 `.pt`，所以点击“开启视觉”应显示普通实时图并提示原图降级，
  这是预期行为。
- 下一步需将正确权重放到：
  - `ExternalCode/models/best.pt`
  - `ExternalCode/models/sam2_b.pt`
- 还需在装有模型及 Torch/Ultralytics 的电脑上验证当前 Ultralytics版本的
  SAM调用、推理帧率、标注布局和 `/result` 数值。
- Unity Editor手动检查：
  - 按钮显示“开启视觉/关闭视觉”。
  - 无模型时视频仍正常且只提示一次降级。
  - 双击小图仍能放大。
  - `/status` 显示 `preview_mode=raw_fallback` 和明确的模型错误。
- 本阶段没有接入或执行 `point_move_demo.py`，没有向 CR10AF 下发命令。

## 2026-07-25 - 视觉处理程序替换现场视频的集成方案分析

### 本次任务目标

- 在视觉机械臂运动逻辑尚未完成前，规划如何先把视觉检测/分割程序集成到 Unity。
- 判断视觉程序是否应始终打开相机，以及是否可以用处理后的图像直接替换现有
  “现场视频”区域。

### 读取和检查的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `ExternalCode/measure copy.py`
- `ExternalCode/realsense_image_server.py`
- `ExternalCode/point_move_demo.py`
- `Assets/VisionImageReceiver.cs`
- `Assets/Editor/ExternalRuntimeBuildPostprocessor.cs`

### 当前程序行为与结论

- `measure copy.py` 在进程启动时立即：
  - 加载 YOLO 和 SAM 模型。
  - 独占启动 D435I 彩色和深度 `1280×720@30 FPS`。
  - 在无限循环中逐帧取同步 RGB-D。
  - 每帧执行 YOLO；每个检测框再执行一次 SAM。
  - 在 `result_img` 上绘制分割轮廓、旋转矩形、中心点和相机坐标 XYZ。
  - 同时打开“Center Position”和“Depth”两个 OpenCV 窗口。
  - 直到按 `q` 或程序退出才停止并释放相机。
- 因此当前原型确实是“进程存活期间持续开相机并持续推理”，但不建议从软件登录
  到退出无条件一直运行；更合理的是“开启视觉/自动任务开始”时启动，任务结束、
  切回仿真或用户关闭时释放。
- 可以而且推荐用 `result_img` 直接替换现有“现场视频”里的原始彩色图：
  - 保留现有 UI 位置、RawImage、双击放大和 Unity 拉流逻辑。
  - 将现有 `realsense_image_server.py` 与视觉原型合并成唯一视觉服务。
  - 继续由 `127.0.0.1:8080/latest.jpg` 返回最新处理结果图。
- 不能让旧相机服务与 `measure copy.py` 同时运行；两者都会创建独立
  RealSense pipeline 并抢占同一台 D435I。

### 推荐运行结构

- 相机采集和视觉推理解耦：
  - 单一采集线程持续取得对齐后的 RGB-D。
  - 推理线程按受控频率处理最新帧，避免 HTTP 每次请求都触发一次 YOLO/SAM。
  - HTTP `/latest.jpg` 只返回最新 `result_img`，Unity仍可按现有方式刷新。
- 第一阶段可以在“开启视觉”后持续推理并显示标注图；考虑 YOLO+SAM算力，
  实际处理帧率应以工控机实测为准，不要求达到相机 30 FPS。
- 为后续全自动流程预留：
  - `/status`：相机、模型、推理帧率、最近错误。
  - `/result`：最新 XYZ、像素中心、矩形角度、置信度和时间戳。
  - `/measure`：机械臂确认静止后触发一次或一组稳定测量。
  - `/save`：替代键盘 `s`，由 Unity 控制保存。
- 相机可在完整真实任务阶段保持打开，但“相机在线”不等于“测量结果可用于运动”；
  机械臂运动时只预览，只有反馈确认静止后才采纳新的视觉测量。
- 保存内容继续保留原程序的彩色图、16位深度图、结果图和中心坐标文本，但保存根目录
  改为软件目录下的相对路径，不能继续使用 Windows `F:\...`。

### 当前阻塞和下一步

- 本轮只完成架构分析，没有修改用户提供的视觉原型、Unity 或场景。
- 当前还缺：
  - YOLO `best.pt` 和 SAM `sam2_b.pt` 的确定位置。
  - Linux工控机上的 Torch/Ultralytics运行环境和实际推理性能。
  - 模型路径、保存路径改为相对/可配置。
  - HTTP状态、图像和测量结果接口。
  - 构建后处理复制 `.pt` 模型文件。
- `point_move_demo.py` 是新出现的未跟踪 Dobot直连运动示例，当前会独立连接
  29999/30004并执行示例点位，不应在本阶段随视觉服务启动，也不应绕过 Unity
  的 CR10AF 单一控制权方案。
- 下一步建议先把视觉原型改造成“不连接机械臂的本地视觉服务”，先验收
  “开启视觉→显示处理图→读取结果→保存→关闭并释放相机”，再接自动流程。

## 2026-07-25 - Unity“开始拧紧”首次点击无动作说明

### 本次任务目标

- 分析正式工控机中 V26 已启动、实时曲线已恢复，但点击 Unity
  “开始拧紧”后电批没有动作，而单独运行 Python 程序时按钮可直接动作的问题。

### 读取和检查的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/ServoTighteningController.cs`
- 用户提供的 Unity 运行截图和日志

### 诊断结论

- 截图已显示 `First live torque curve received: 960x540`，说明前述 PNG
  兼容修改有效，实时曲线链路已经恢复。
- 日志 `Motion confirmation required: forward` 表明 Unity 已收到第一次
  “开始拧紧”点击，但按既有真机安全设计只进入待确认状态，没有下发 `forward`。
- 第一次点击后按钮文字会临时变为“再次确认”；必须在 3 秒内再次点击同一个按钮，
  第二次点击才会临时解锁真实电批运动并发送命令。
- 单独运行 Python 图形程序时没有 Unity 这一层二次确认，所以单击就会动作，两种
  表现符合当前代码设计。
- `UpdateButtonState()` 只有在 `ProgramReady && ToolConnected` 时才允许点击
  “开始拧紧”。截图中首次点击已经进入确认逻辑，因此当前不是曲线格式修改造成的，
  也不是按钮事件失效。

### 当前状态、后续问题和手动检查

- 本轮仅说明现有安全交互，没有修改 Unity、Python 或场景。
- 在确保人员、工件和急停条件安全后，按“开始拧紧”一次，并在按钮显示
  “再次确认”的 3 秒内再按一次。
- 若第二次点击后仍不动作，应保留第二次点击之后的新日志，检查是否出现
  `forward_queued`、电批连接中断或设备无回传；不要在未确认现场安全时连续点击。
- 不建议为方便测试直接删除真机二次确认。后续自动流程应通过明确的流程状态、
  电批连接状态和运动安全开关解锁，而不是模拟人工双击。

## 2026-07-25 - 正式工控机力矩曲线 HTTP 503 初步诊断

### 本次任务目标

- 分析另一台正式工控机点击“启动程序”后，拧紧程序保持运行但右下角实时力矩
  曲线黑屏，并持续出现 `HTTP/1.1 503 Service Unavailable` 的原因。

### 读取和检查的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/ServoTighteningController.cs`
- `ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py`
- 用户提供的正式工控机运行截图和 Unity 日志

### 诊断结论

- 此次现象与实验工控机先前的 NumPy/Matplotlib 导入失败不同：
  - Unity 已显示 `V26 bridge is ready at 127.0.0.1:9100`。
  - “启动程序”按钮已切换为“关闭程序”。
  - 因此 Python 主进程、9100 控制桥和 Unity 到本机的控制通信均已启动，
    不是整个 Python 程序提前退出。
- Unity 随后请求 `http://127.0.0.1:9101/curve.jpg`，收到的是 Python
  曲线服务主动返回的 HTTP 503。
- 按当前 Python 实现，只有 `latest_curve_jpeg is None` 时才会返回该 503；
  这说明 9101 HTTP 服务已经在线，但后台 `curve_render_worker` 尚未成功生成
  第一张 JPEG。
- 该线程会把具体绘图异常写入
  `http://127.0.0.1:9101/status` 的 `last_error`。在读取正式工控机的
  `last_error` 前，不能准确判定是 Pillow/JPEG、Matplotlib API、中文字体文件
  还是其他渲染环境问题。
- 端口被占用或普通网络不通的可能性较低：如果 9101 没有监听，Unity 通常会得到
  连接失败，而不是由当前曲线服务返回的 HTTP 503。
- 电批尚未连接也不是空闲曲线无法显示的原因；设计上即使没有电批数据，后台也应
  生成带坐标轴的空闲曲线。
- 正式工控机随后通过 `/status` 返回了准确异常：
  `'FigureCanvasAgg' object has no attribute 'print_jpg'`。
- 因此最终根因已确定：该机加载的 Matplotlib Agg 后端没有项目脚本调用的
  `print_jpg()` 接口。曲线线程在第一次 JPEG 编码时异常，导致
  `render_count` 始终为 `0`、`latest_curve_jpeg` 始终为空并持续返回 503。
- 正式工控机进一步确认使用 Ubuntu 系统包 Matplotlib `3.1.2`，并且
  `print_jpg=False`、`print_jpeg=False`。因此不能只更换 JPEG 方法名。
- 更适合多台工控机部署的修复方向，是在脚本中优先使用 JPEG 接口；当两个 JPEG
  接口都不存在时退回 Agg 一直支持的 PNG 输出，同时由 HTTP 响应返回正确的
  `image/png` 类型。Unity 的 `DownloadHandlerTexture` 可继续读取该纹理，
  无需更改力矩数据、控制协议或最终 CSV/PNG 保存逻辑。

### 当前状态、下一步和手动检查

- 本轮只做诊断，没有修改 Unity、Python业务代码、场景或设备参数。
- 已完成 `/status` 取证，无需继续检查网络和端口。
- 下一步采用 JPEG/PNG 自动回退的代码级兼容修复，重新打包或只替换构建目录中的
  V26 Python 文件；重启后验证 `/status` 的 `ok=true`、
  `render_count>0`，且 Unity 曲线区域出现空闲坐标图。
- Unity Editor 本轮无需检查 Inspector；这是目标工控机 Python 曲线渲染阶段的
  运行时诊断。

## 2026-07-25 - CR10AF 视觉引导集成与控制权架构分析

### 本次任务目标

- 检查视觉同学放入 `ExternalCode` 的程序。
- 分析“仿真规划→粗定位→视觉引导→拧紧”流程中，越疆 CR10AF 应持续由
  Unity 连接，还是在视觉阶段断开并把连接交给 Python。

### 读取和检查的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `ExternalCode/measure copy.py`
- `ExternalCode/realsense_image_server.py`
- `Assets/DobotController.cs`
- `Assets/ConnectCommander.cs`
- `Assets/RealRobotFollower.cs`
- `Assets/MissionController.cs`
- `Assets/ArmController.cs`
- `Assets/VisionImageReceiver.cs`
- `Assets/Editor/ExternalRuntimeBuildPostprocessor.cs`
- `RunScene.unity` 中的 CR10AF IP、端口和按钮绑定
- 越疆 CRAF/CRA 产品资料及 TCP/IP 二次开发接口 V4.6.0

### 代码检查结论

- 新文件 `ExternalCode/measure copy.py` 当前不包含任何 Socket、机械臂 IP、
  端口或运动指令，不会与 Unity 抢占 CR10AF 控制连接。
- 该脚本当前功能是：
  - 独占打开 D435I 彩色流和深度流，均为 `1280×720@30 FPS`。
  - 使用 YOLO 检测和 SAM 分割。
  - 用目标轮廓最小外接矩形中心及 11×11 深度中值计算相机坐标
    `X/Y/Z`，单位毫米。
  - 通过 OpenCV 独立窗口显示，并按 `s` 保存 PNG/TXT。
- 当前尚不能直接打包或由 Unity 调用：
  - YOLO 权重路径硬编码为 Windows `F:\...best.pt`。
  - 保存目录硬编码为 Windows `F:\...center_result`。
  - `sam2_b.pt`、YOLO `best.pt` 均未放入工程。
  - 开发机当前未安装 `ultralytics` 和 `torch`。
  - 构建后处理只复制 `ExternalCode/*.py`，不会复制 `.pt` 权重。
  - 脚本没有启动/停止/状态/测量结果 API，只有键盘和终端输出。
  - 它会再次独占 RealSense pipeline，不能与现有
    `realsense_image_server.py` 同时打开同一台相机。
  - 当前结果只有相机坐标 XYZ，没有完成相机到 CR10AF 基坐标的手眼标定变换，
    也没有提供拧紧所需的可靠 6D 姿态、结果稳定性、置信度和时间戳。

### CR10AF 当前连接链路

- RunScene 中 CR10AF 地址为 `192.168.192.19`。
- `DobotController` 使用：
  - `29999`：Dashboard 控制和命令应答。
  - `30005`：每 200ms、1440 字节的机械臂反馈。
- Unity 连接后发送 `RequestControl()`，然后通过同一控制器执行
  `EnableRobot()`、`SpeedFactor()` 和 `MovJ(...)`。
- `ConnectCommander` 将仿真规划结果转换为六关节角并调用
  `DobotController.MoveJoints()`。
- 当前机械臂“完成”主要依赖估算等待时间；虽然 Unity 已接收实际关节和 RobotMode，
  但尚未在 `SendArmRoutine` 中用反馈闭环确认到位。这一点必须在触发视觉测量前补齐。

### 推荐控制权架构

- 推荐 Unity 在进入真实运行后，持续保持 CR10AF 的 29999 控制连接和
  30005 反馈连接，作为整条自动流程的唯一机械臂运动控制者。
- 视觉 Python 进程只作为计算服务：
  - 从唯一的 RealSense 相机服务取得同步 RGB-D。
  - 返回相机坐标下的目标位置/姿态、置信度和时间戳。
  - 不直接连接 CR10AF，不直接发运动命令。
- Unity 用状态机串联：
  `粗定位→反馈确认静止/到位→视觉测量→坐标变换与安全检查→精定位→反馈确认→拧紧`。
- “持续连接”不代表持续发命令；视觉和拧紧阶段可保持 TCP 在线但禁止机械臂运动。
- 不推荐粗定位后断开 Unity、让视觉程序接管，再切回 Unity：
  - 增加控制权交接竞态、重连失败、反馈中断和状态过期。
  - 越疆 V4 的 `RequestControl()` 只有在未上电或下使能状态才允许切换到 TCP 模式，
    反复交接可能迫使机器人下使能。
  - Unity 的停止、状态显示和后续全自动恢复会失去统一入口。
- 如果未来需要高频连续视觉伺服，应增加一个始终独占 CR10AF 的“机器人网关”进程，
  Unity 和视觉都只向网关提交经过仲裁的请求；仍然不让两个进程轮流直连机器人。

### 当前状态、后续问题和手动检查

- 本轮只完成只读分析，没有修改视觉、Unity、场景或真实设备配置。
- 新视觉文件保持用户原样，仍为 Git 未跟踪文件。
- 下一步建议先把视觉脚本改造成可独立测试的本地服务，统一相机所有权和模型相对路径，
  暂时只返回测量结果，不接机械臂。
- 随后补齐：
  - 确认相机是眼在手上还是固定安装。
  - 手眼标定矩阵、工具坐标系和坐标单位。
  - 粗定位真实反馈闭环。
  - 视觉结果稳定性和最大修正范围。
  - CR10AF 真机 TCP 模式、固件/控制器版本和各命令返回码验证。
- Unity Editor 暂时无需绑定新组件；后续实现时先在真实运行 UI 添加视觉服务状态，
  再做不下发运动的“只测量”联调。

## 2026-07-25 - 工控机力矩曲线程序提前退出诊断

### 本次任务目标

- 分析 v1.6 打包程序在开发电脑运行正常、复制到工控机后力矩曲线失效的问题。

### 读取和检查的关键内容

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/ServoTighteningController.cs`
- `ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py`
- `DEPLOYMENT.md`
- 用户提供的工控机终端报错和 Unity 运行日志
- 开发电脑当前 Python 模块版本
- NumPy 官方兼容性说明及 PyPI 上 `opencv-python 5.0.0.93` 的依赖元数据

### 诊断结论

- 不是 Unity 曲线 RawImage、HTTP 拉流或 UI 自适应先发生故障，而是 V26
  Python 进程在启动阶段就已退出。
- Unity 日志“拧紧程序启动后提前退出，退出码: 1”与控制器的进程存活检测一致；
  Python 进程退出后，`127.0.0.1:9100` 控制服务和
  `127.0.0.1:9101/curve.jpg` 曲线服务都不会启动。
- 工控机当前从用户目录加载 `NumPy 2.2.6`，但从
  `/usr/lib/python3/dist-packages` 加载由 NumPy 1.x ABI 编译的系统
  Matplotlib。脚本在第 47 行导入 Matplotlib 时触发二进制 ABI
  不兼容并退出。
- 冲突来源是工控机安装了 `opencv-python 5.0.0.93`；该版本在
  Python 3.9 及以上声明需要 `NumPy >= 2`，因此直接安装最新 OpenCV
  会把 NumPy 升到 2.x。
- 开发电脑可用组合为：
  - Python 3.8.10
  - NumPy 1.24.4
  - Matplotlib 3.7.5
  - OpenCV 4.10.0.84
- 力矩脚本本身不导入 OpenCV；OpenCV 只供 RealSense 图像服务使用，
  但两个程序当前共用 `/usr/bin/python3`，所以相机依赖升级破坏了力矩绘图环境。

### 当前状态、建议和后续

- 本轮只完成诊断，没有修改 Unity、Python 业务代码，也没有在工控机卸载或安装模块。
- 短期修复不应只降级 NumPy；还需要把 OpenCV 从 5.0 降到支持 NumPy 1.x
  的版本，并一次性验证 `numpy`、`matplotlib`、`cv2`、`pyrealsense2`
  四个导入。
- 长期推荐给外部 Python 程序建立独立、固定版本的运行环境，并让 Unity
  明确使用该环境的 Python，避免目标电脑的系统包和用户级 pip 包混用。
- 下次继续位置：先在工控机采集 `python3 -m pip show` 和四模块导入结果，
  再按确认后的固定版本方案处理环境，随后测试 V26 的 9100/9101 服务及相机 8080 服务。
- Unity Editor 手动检查：本轮无需检查 Inspector；环境修复后应在真实运行页面验证
  “启动程序”、空闲曲线、开始拧紧、最终 CSV/PNG 保存和现场视频。

## 2026-07-24 - Ubuntu 1850×1015 自适应窗口

### 本次任务目标

- 解决 1920×1080 Ubuntu 桌面无法完整容纳 1920×1080 窗口内容区的问题。
- 保留按 1920×1080 设计的正式界面，同时避免右侧曲线、视频区域和底部按钮发生重叠。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `ProjectSettings/ProjectSettings.asset`
- `ProjectSettings/EditorBuildSettings.asset`
- `Assets/SimulationPlatform/Scripts/Tool/SceneWindowSetting.cs`
- LoginScene、Main、RunScene、MainScene 中的窗口配置和 Canvas Scaler

### 修改的文件和内容

- `ProjectSettings/ProjectSettings.asset`
  - Player 默认窗口恢复为 `1850×1015`。
- `Assets/SimulationPlatform/Scripts/Tool/SceneWindowSetting.cs`
  - 新组件默认窗口尺寸改为 `1850×1015`。
- Build Settings 中四个正式场景的 `SceneWindowSetting`
  - LoginScene、Main、RunScene、MainScene 均统一为 `1850×1015`。
- Main、RunScene、MainScene 的正式屏幕空间 Canvas
  - `UI Scale Mode` 改为 `Scale With Screen Size`。
  - `Reference Resolution` 改为 `1920×1080`。
  - `Screen Match Mode` 改为 `Expand`。
- LoginScene 原本已经使用自适应 Canvas，因此没有改动其 Canvas Scaler。
- RunScene 中用于世界空间指示物的 `PointerCanvas` 保持原配置，未被误改。

### 为什么这样改

- 1920×1080 内容区再加上 Ubuntu 顶栏、标题栏和窗口边框，物理上无法完整放进 1920×1080 桌面。
- 只把窗口缩回 1850×1015、但继续使用 `Constant Pixel Size`，会重新造成固定像素 UI 重叠。
- 使用 1920×1080 作为设计参考并选择 `Expand`，可以将正式 UI 等比缩小到可用窗口内，同时保证设计区域完整可见。

### 验证情况

- Main、RunScene、MainScene 的场景差异均严格为 Canvas Scaler 的 3 个参数。
- LoginScene 在清理换行符差异后没有产生实际修改。
- 四个正式场景和脚本默认值均核对为 `1850×1015`。
- 完整 `Assembly-CSharp` 编译通过，没有错误；仅有工程原有警告。
- 没有执行 Player 打包，也没有连接或控制真实设备。

### 当前状态、遗留问题及手动检查

- 代码和场景配置修改已完成。
- 需要在 Unity Game 视图中使用 `1850×1015` 检查 Main、RunScene、MainScene：
  - 顶部导航、右侧状态栏和底部工具栏完整可见。
  - “实时力矩曲线”和现场视频不侵入底栏。
  - 日志、视频、拧紧程序及三个拧紧动作按钮没有重叠。
- 重新打包后，需在目标 Ubuntu 电脑上确认窗口完整放入桌面，并检查登录和场景切换后窗口尺寸保持一致。
- `ProjectSettings.asset` 仍有用户先前产生的
  `Server: ObservParam;(1)` Scripting Define Symbols，本次未改动；正式打包前建议单独确认并清理。
- 下次继续位置：先完成上述 1850×1015 编辑器和 Player 视觉验收；如仍有个别控件偏移，再只调整对应控件锚点，不改回固定 1920×1080 窗口。

## 2026-07-24 - 1920×1080 Ubuntu 窗口容纳问题评估

### 本次任务目标

- 评估固定 1920×1080 Player 在 1920×1080 Ubuntu 桌面中无法完整容纳的问题。

### 检查内容与结论

- 确认当前 Player 和四个正式场景均请求窗口内容区 `1920×1080`。
- Ubuntu 窗口模式还需要系统顶栏、应用标题栏和边框，因此 1080 像素高的内容区不可能完整放进 1080 像素高的桌面。
- LoginScene 已使用 `Scale With Screen Size`；Main、RunScene、MainScene 的正式屏幕空间 Canvas 仍为 `Constant Pixel Size`。
- 只恢复 `1850×1015` 会重新引入底部按钮和右侧曲线重叠；只使用 1920×1080 则窗口放不下。

### 推荐方案

- 保留窗口模式，将 Player 和四个正式场景恢复为适合 Ubuntu 桌面的 `1850×1015`。
- 将 Main、RunScene、MainScene 的正式屏幕空间 Canvas 改为：
  - `Scale With Screen Size`
  - Reference Resolution `1920×1080`
  - Screen Match Mode `Expand`
- 这样 UI 按 1920×1080 设计稿整体缩放到 1850×1015，避免固定像素重叠。
- 备选方案是 1920×1080 无边框全屏，但在 2560×1440 等显示器上仍需考虑 Canvas 自适应。

### 当前状态

- 本轮只完成检查和方案评估，没有再次修改窗口尺寸或 Canvas。
- 等用户确认采用“自适应窗口”还是“无边框全屏”后再实施。

## 2026-07-24 - 四个正式场景统一为 1920×1080

### 本次任务目标

- 修复 Player 被场景脚本重新改为 `1850×1015`，导致右侧曲线和底部按钮重叠的问题。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `ProjectSettings/EditorBuildSettings.asset`
- `ProjectSettings/ProjectSettings.asset`
- `Assets/SimulationPlatform/Scripts/Tool/SceneWindowSetting.cs`
- LoginScene、Main、RunScene、MainScene

### 修改的文件和内容

- `Assets/SimulationPlatform/Scripts/Tool/SceneWindowSetting.cs`
  - 新组件默认窗口尺寸改为 `1920×1080`。
- Build Settings 中四个启用场景的 `SceneWindowSetting` 序列化参数均由
  `1850×1015` 改为 `1920×1080`：
  - `LoginScene.unity`
  - `Main.unity`
  - `RunScene.unity`
  - `MainScene.unity`
- 每个场景差异均严格为两行数值修改，没有改变其他对象、组件、锚点或资源。

### 为什么这样改

- `SceneWindowSetting.Awake()` 会覆盖 Player Settings；只修改 Player 面板不足以改变最终窗口尺寸。
- 四个场景必须保持一致，否则切换场景时仍会重新缩回 1850×1015。

### 验证情况

- 四个场景逐一检查，均为 `windowWidth=1920`、`windowHeight=1080`。
- 完整 `Assembly-CSharp` 编译通过，没有新增错误；仅有项目原有警告。
- 没有执行 Player 打包，也没有连接真实设备。

### 当前状态、遗留问题及手动检查

- 固定 1920×1080 的场景配置修复已完成。
- 重新打包并运行后，Player 日志应显示
  `requesting resize 1920 x 1080`，不应再出现 1850×1015。
- 需要在真实运行界面检查右侧曲线不侵入底栏、“立即停止”不再与“重置底盘”重叠。
- `ProjectSettings.asset` 当前仍有用户未提交的
  `Server: ObservParam;(1)` Scripting Define Symbols，本次未改动；正式打包前仍建议清除。

## 2026-07-24 - Player 与 Game 视图尺寸不一致诊断

### 本次任务目标

- 查明 Game 视图设为 `1920×1080` 时正常，但 Linux Player 中右侧曲线和底部按钮发生重叠的原因。

### 读取的关键文件与证据

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `ProjectSettings/ProjectSettings.asset`
- `Assets/SimulationPlatform/Scripts/Tool/SceneWindowSetting.cs`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`
- Linux Player 日志与
  `~/.config/unity3d/DefaultCompany/飞机导管拧紧系统/prefs`

### 诊断结论

- Player Settings 当前默认值虽已改为 `1920×1080`，但 `RunScene/RunObject`
  上的 `SceneWindowSetting` 仍序列化为 `1850×1015`。
- `SceneWindowSetting.Awake()` 会调用
  `Screen.SetResolution(windowWidth, windowHeight, isFullScreen, 0)`，
  所以进入 Login、Main、RunScene 等场景后会覆盖 Player Settings。
- Player 日志多次明确记录：
  - `requesting resize 1850 x 1015`
  - `resizing window to 1850 x 1015`
- Player prefs 也已记住当前分辨率 `1850×1015`，默认分辨率才是
  `1920×1080`。
- 正式 UI 根 Canvas 使用 `Constant Pixel Size`，不会随 1850×1015 自动缩放。
- 右侧力矩区域使用固定顶部锚点和 `y=-725`，底部工具栏按钮也使用固定像素位置；
  高度减少 65 像素、宽度减少 70 像素后就会侵入底栏并互相覆盖。
- Unity Game 视图选择固定 `1920×1080` 时不会按 Player 的场景窗口方式呈现，
  因此编辑器内看起来正常。

### 当前状态

- 本次只做诊断，没有修改场景、Canvas 或窗口脚本。
- `v1.6` 已由用户提交并推送。
- 当前 `ProjectSettings.asset` 还有用户未提交修改：
  - 默认分辨率改为 `1920×1080`。
  - 意外出现 `Server: ObservParam;(1)` Scripting Define Symbols；打包前应移除。

### 推荐下一步

- 当前固定 1920×1080 版本：统一四个 Build Settings 场景的
  `SceneWindowSetting` 为 `1920×1080`，并清理旧 Player prefs。
- 后续若要支持不同显示器：再将主 Canvas 改为
  `Scale With Screen Size / 1920×1080 / Match 0.5`，并逐项校正右侧曲线和底部按钮锚点。
- 修改后至少验证 `1920×1080`、`1850×1015` 两种 Game 视图尺寸。

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

## 2026-08-13 - 单点成功、多点失败只读排查

### 本次任务目标

- 只读排查“单点规划成功、多点规划失败”是否由底盘无法移动造成。
- 本次不修改业务代码、场景或参数。

### 读取的关键文件

- `Logs/Log_2026-08-13_19-31-52.log`
- `Assets/MissionController.cs`
- `Assets/ChassisController.cs`
- `Assets/ArmController.cs`
- `Assets/RobotData.cs`
- `Assets/SimulationPlatform/Scripts/Function/Simulation.cs`
- `Assets/SimulationPlatform/Scripts/Behaviour/ModelCollisionHighlighter.cs`
- `Assets/SimulationPlatform/Scripts/Manage/PathPointManager.cs`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`

### 排查结论

- 最新一次三目标预计算中，第一个目标的 IK 和 BIT* 直连成功；后两个目标是原模型与生成 Hull 的重复物理位置，均在 IK 阶段失败。
- 失败发生在真正启动底盘之前，不是底盘运动执行报错。第二目标首次 IK 检查时，MuJoCo 末端初始位置与第一目标完全相同，说明多目标预计算没有把虚拟底盘移动到第二目标附近。
- `DeepPrecomputeAll()` 调用 `NavMesh.CalculatePath()` 后没有检查返回值和 `NavMeshPath.status`。路径没有角点时，会静默缓存 `{ simPos }` 单点路径，`finalStopPoint` 保持原地，随后直接在原地对远目标执行 IK。
- 当前目标直接使用被选中模型/Hull 的 `Transform.position` 作为 NavMesh 终点。该点通常位于模型内部或离地，容易不是有效 NavMesh 点；代码没有先用 `NavMesh.SamplePosition()` 获取可导航终点。
- 同一次鼠标点击同时命中了原模型和生成的 Hull；日志显示二者都被加入 `SeletectedObjects`，因此实际目标数从用户期望的两个变成三个，并重复规划同一位置。
- 同一日志中曾有 2 点和 3 点预计算成功，但这些成功目标的位置相同或机械臂原地可达，不能证明底盘发生了平移；它能证明多点循环本身并非必然失败。

### 当前是否完成

- 只读根因排查已完成。
- 除本工作日志外未修改代码、场景和参数。

### 建议的下一步

- 下一轮若获准修改，先为每个目标输出 `simPos`、目标点、NavMesh 返回值/status/corners、`finalStopPoint` 和 `moveDelta`，验证静默原地回退。
- 再修正导航终点采样和完整路径校验；无完整路径时应停止该目标预计算，不应继续原地 IK。
- 对原模型与自动生成 Hull 做选择去重，只保留一个逻辑目标。

## 2026-08-13 - 多点底盘停靠路径与 Hull 重复选点修复

### 本次任务目标

- 按上一轮排查结论修复“单点成功、多点失败”：不再在 NavMesh 失败后让底盘原地做远目标 IK。
- 同时解决一次点击把原模型和自动生成 Hull 都加入任务列表的问题。

### 读取的关键文件

- `AGENTS.md`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`
- `Logs/Log_2026-08-13_19-31-52.log`
- `Assets/MissionController.cs`
- `Assets/ChassisController.cs`
- `Assets/RobotData.cs`
- `Assets/AutoColliderGen_Final.cs`
- `Assets/SimulationPlatform/Scripts/Behaviour/ModelCollisionHighlighter.cs`
- `Assets/SimulationPlatform/Scripts/Manage/PathPointManager.cs`
- `Assets/SimulationPlatform/Scripts/Function/Simulation.cs`

### 修改的文件

- `Assets/MissionController.cs`
- `Assets/SimulationPlatform/Scripts/Behaviour/ModelCollisionHighlighter.cs`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`

### 具体改动

- `MissionController.TryBuildChassisPath()`：
  - 目标超出机械臂工作半径时，先把目标 XZ 投影到与底盘同高的位置。
  - 使用 `NavMesh.SamplePosition()` 分别验证底盘起点和目标附近停靠点。
  - 调用 `NavMesh.CalculatePath()` 后同时检查返回值、`PathComplete` 和角点数量。
  - 沿完整路径的所有线段按 0.05m 采样，选择首个进入机械臂工作半径的停靠点，并截短执行路径。
  - 输出 `[底盘预计算]` 日志，记录采样点、路径状态、角点、停靠点、移动量和路径点数。
- `DeepPrecomputeAll()`：
  - 底盘路径或机械臂 IK 任一失败立即终止整组预计算。
  - 失败时清空底盘缓存、诊断快照和真机任务清单，并保持 `hasPrecalculated=false`。
  - 只有所有目标全部成功才允许执行缓存路径。
- `StartMissionSequence()`：
  - 预计算失败后回到 `Idle` 并返回，不再进入 `WaitingToStartPath` 或底盘执行状态。
- `ModelCollisionHighlighter`：
  - `_MjRoot` 下的自动生成 Hull 高亮组件在 `Awake()` 中停用点击处理，但保留 Collider/Rigidbody/MuJoCo 碰撞功能。
  - 新增逻辑目标解析，将 Hull 统一映射回原模型 Transform。
  - 选择和取消选择均使用逻辑目标。
- `ControlMission()`：
  - 按逻辑 Transform 的 InstanceID 做第二层去重，并输出 `[路径点去重]` 日志。

### 为什么这样改

- 旧代码忽略 `NavMesh.CalculatePath()` 的返回值和路径状态，失败时缓存 `{simPos}`，导致底盘实际不平移而机械臂直接求解远目标。
- 目标 Transform 常位于模型内部或离地，直接作为 NavMesh 终点不稳定；先在同高地面附近采样更符合底盘导航含义。
- 原模型和生成 Hull 都挂有高亮脚本，同一次射线点击会被两个 `Update()` 同时处理；Hull 应仅作为碰撞代理。

### 验证情况

- `git diff --check -- Assets/MissionController.cs Assets/SimulationPlatform/Scripts/Behaviour/ModelCollisionHighlighter.cs` 通过。
- `dotnet build Assembly-CSharp.csproj --no-restore` 未进入编译阶段：Unity 生成的
  `Temp/obj/Assembly-CSharp/project.assets.json` 不存在，报 `NETSDK1004`。
- 当前没有运行中的 Unity Editor，因此还不能用 Editor 自动脚本编译和实际 NavMesh/MuJoCo 运行验证。

### 当前是否完成

- 代码修改和静态差异检查已完成。
- 需要在 Unity Editor 中进行多目标运行验收。

### 还存在的问题

- `NavMesh.SamplePosition()` 的 `1.15m` 目标搜索半径沿用当前 `armReachDistance`；如果某些大型模型的可停靠边缘更远，会明确报告“目标附近没有可用 NavMesh 停靠点”，不会再原地误规划。
- 本次未修改 `Simulation.cs`、碰撞生成器、场景、视觉或外部 Python 的既有未提交改动。

### 下次继续开发从哪里开始

- 先查看 Unity 是否有 C# 编译错误。
- 选取两个真实不同的位置运行规划，检查 `[底盘预计算]` 中第二目标的 `移动量` 是否非零、路径状态是否为 `PathComplete`。
- 如果仍失败，根据日志中的起点采样、终点采样和失败原因调整 NavMesh 或停靠半径，而不是先改 IK。

### 需要在 Unity Editor 检查

- 同一次点击只应新增一个路径点，列表数量不应因 `_Hull_0` 再增加一次。
- 规划两个相距超过 `armReachDistance` 的目标，第二目标应显示非零底盘移动量和至少两个执行路径点。
- Scene 视图打开 `Show NavMesh`，确认白色全局路径在目标附近停下。
- 路径不可达时任务应停在 `Idle`，Console 明确打印失败原因，底盘不应执行原地伪路径。

## 2026-08-13 - 首目标底盘先小幅转向再转回的只读排查

### 本次任务目标

- 排查多点规划成功后，第一个目标执行时底盘先旋转、短距离前进、再转向主路径的原因。
- 本次不修改业务代码。

### 读取的关键文件

- `AGENTS.md`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`
- `Logs/Log_2026-08-13_20-11-19.log`
- `Assets/MissionController.cs`
- `Assets/ChassisController.cs`
- `Assets/RobotDiagnosticUI.cs`

### 排查结论

- 第一个目标的实际底盘起点为 `(0,0,0)`，NavMesh 起点采样为
  `(-0.021,0.083,0.164)`，水平偏移约 `0.165m`。
- 当前 `TryBuildChassisPath()` 同时把实际起点和 NavMesh 起点采样加入执行路径，因而
  第一个目标缓存了3个路径点：实际起点、采样起点、最终停靠点。
- 第一小段方向约为世界航向 `-7.3°`，从采样点到最终停靠点的主路径方向约为
  `72.7°`，方向相差约 `80°`。`ChassisController` 到达每个角点后都会切回
  `Rotating`，所以表现为先旋转和短移，再转回主方向继续前进。
- 第二个目标的实际起点 `(0.218,0,0.239)` 与采样点 `(0.218,0.083,0.239)` 的 XZ
  一致，去重后只有2个执行路径点，因此没有同样的小幅折返。
- 这不是 IK、机械臂动作或底盘执行器异常，而是上一轮新增的起点 NavMesh 采样点被
  当成独立执行角点造成的路径形状问题。

### 修改的文件

- 仅更新 `.codex/PROJECT_CONTEXT.md` 和 `.codex/WORKLOG.md` 记录结论。
- 未修改业务代码、场景或参数。

### 建议的下一步

- 推荐对小偏移起点采用“只用于 NavMesh 计算，不加入执行路径”的策略：执行路径从
  真实 `simPos` 直接接到 `navPath.corners[1]` 或截短后的首个主路径点。
- 增加起点采样偏移阈值；例如偏移在约 `0.25m` 内时跳过采样角点，偏移过大时明确
  报告底盘起点不在 NavMesh 附近，避免无条件穿越不可导航区域。
- 修改后验证第一个目标的 `执行路径点数` 从3变为2，并确认只旋转一次后沿主路径移动。

### 当前是否完成

- 原因已确认，尚未修改业务代码。

### 需要在 Unity Editor 检查

- 当前可在 Scene 视图查看第一条白色路径，应能看到原点到
  `(-0.021,0,0.164)` 再折向 `(0.218,0,0.239)` 的小折线。
- 后续修复后该折线应合并为从真实起点直接指向主路径/停靠点的一段。

## 2026-08-13 - 起点采样短折线修复与当日迁移清单

### 本次任务目标

- 修复首目标底盘先转向并短移到 NavMesh 起点采样，再转回主路径的问题。
- 整理今天所有有意修改的文件及跨版本移植顺序，避免与工作区其他未提交改动混淆。

### 修改的文件

- `Assets/MissionController.cs`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`
- `.codex/PORTING_NOTES_2026-08-13.md`（新增）

### 具体修改

- `TryBuildChassisPath()` 增加 `0.25m` 起点采样水平偏差阈值。
- 偏差不超过阈值时，`sourceHit` 仍作为 `NavMesh.CalculatePath()` 的合法计算起点，
  但不再加入执行 `route`；执行路径从真实 `simPos` 直接连接
  `navPath.corners[1]` 及后续主路径。
- 偏差超过阈值时明确返回失败，避免底盘从明显不在 NavMesh 上的位置直接穿越到路径。
- `[底盘预计算]` 日志增加起点偏移和“采样点仅用于路径计算”说明。
- 新建当天迁移清单，区分 7 个功能代码/资源文件、3 个项目记录文件，以及当前工作区
  中不属于今天这批修复的脏文件；同时记录依赖关系、迁移顺序和验收标准。

### 静态验证

- `git diff --check -- Assets/MissionController.cs` 通过。
- 对今天涉及的功能文件和三份 `.codex` 记录执行 `git diff --check`，全部通过。
- `dotnet build Assembly-CSharp.csproj --no-restore` 未进入 C# 编译：Unity 当前没有生成
  `Temp/obj/Assembly-CSharp/project.assets.json`，报 `NETSDK1004`。这是临时工程依赖缺失，
  不是本次代码的编译错误证据。
- 当前没有运行中的 Unity Editor，仍需 Editor 完成脚本编译与运行验收。

### 需要在 Unity Editor 检查

- 重现原第一目标，日志中的起点偏移应约为 `0.165m`，并显示采样点仅用于路径计算。
- 第一目标的执行路径点数应从 3 降为 2；底盘应直接沿主路径运动，不再先走短折线。
- 第二目标原有完整路径、多目标 IK/BIT* 和 Hull 选点去重行为不应退化。

## 2026-08-13 - 鼠标悬停模型持续 NullReference 的只读排查

### 本次任务目标

- 排查鼠标移动到场景模型上时持续出现、但暂时不影响功能的
  `NullReferenceException`。
- 本次只确认原因，不修改业务代码。

### 读取的关键文件和证据

- `AGENTS.md`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`
- `Logs/Log_2026-08-13_20-31-58.log`
- `Logs/Log_2026-08-13_20-11-19.log`
- `Assets/SimulationPlatform/Scripts/Behaviour/ModelCollisionHighlighter.cs`
- `Assets/AutoColliderGen_Final.cs`
- `Assets/SimulationPlatform/Scripts/Function/Simulation.cs`
- `Assets/SimulationPlatform/Scripts/Tool/ModelTool.cs`

### 排查结论

- 最新日志中的160条异常全部落在
  `ModelCollisionHighlighter.SetModelOpacity()` 第179行；入口包括
  `OnMouseEnter()`、`OnMouseExit()`，以及另一高亮对象在 `Update()` 中取消旧高亮。
- `SetModelOpacity()` 第179行会遍历 `allRenderers`，该字段和 `propBlock` 只在
  `Start()` 中初始化。
- 碰撞生成器会在每个动态 Hull 上添加 `ModelCollisionHighlighter`；上一轮为解决重复
  选点，又让这些 Hull 组件在 `Awake()` 中执行 `enabled=false`。被禁用后 `Start()`
  不运行，所以这两个运行时字段保持为空。
- Hull 仍有 `MeshCollider`，Unity 的旧式鼠标消息仍可能调用其
  `OnMouseEnter/OnMouseExit`。脚本随后进入透明度逻辑并访问未初始化字段，形成持续异常。
- 嵌套的 `Update -> HighlightModel(false)` 调用栈是静态
  `currentHighlightedObject` 已被代理 Hull 写入后，原模型尝试取消它的高亮造成的次生
  异常；不是底盘、NavMesh、MuJoCo接触或材质资源本身为空。

### 推荐修复方向

- 首选：`AutoColliderGen_Final.CreateGeom()` 和
  `Simulation.CreateColliderObject()` 都不再给生成 Hull 添加
  `ModelCollisionHighlighter`。Hull 只保留碰撞/射线代理；原模型脚本现有的
  `hit.transform.IsChildOf(transform)` 已能识别其子级 Hull，并统一完成高亮和选点。
- 同时保留防御性初始化或代理早退，兼容旧保存数据中已经带有该组件的 Hull。
- 不推荐只在 `SetModelOpacity()` 判空后返回：这样虽然不报错，代理仍会抢占
  `currentHighlightedObject`，可能留下高亮状态和鼠标交互竞争。

### 修改情况与当前状态

- 仅更新 `.codex/PROJECT_CONTEXT.md`、`.codex/WORKLOG.md` 和
  `.codex/PORTING_NOTES_2026-08-13.md` 记录结论与迁移警告。
- 未修改 `Assets` 业务代码、场景、碰撞参数或模型。
- 根因已确认，等待用户授权后再实施最小修复。

### 修复后需要在 Unity Editor 检查

- 重新生成碰撞体和重新加载 `.collider.xml` 两条入口都要测试。
- 鼠标反复进入/离开原模型及彩色 Hull 区域，不应再出现
  `ModelCollisionHighlighter.SetModelOpacity` 异常。
- 模型仍应正常变色、点击只增加一个任务点，Hull 的 PhysX/MuJoCo碰撞能力保持不变。

## 2026-08-13 - 鼠标悬停 Hull 空引用修复

### 本次任务目标

- 按上一轮定位结果，消除鼠标进入/离开生成 Hull 时持续出现的
  `ModelCollisionHighlighter.SetModelOpacity()` 空引用。
- 保留原模型高亮、单次选点以及 Hull 的 PhysX/MuJoCo碰撞能力。

### 修改的文件

- `Assets/AutoColliderGen_Final.cs`
- `Assets/SimulationPlatform/Scripts/Function/Simulation.cs`
- `Assets/SimulationPlatform/Scripts/Behaviour/ModelCollisionHighlighter.cs`
- `.codex/PROJECT_CONTEXT.md`
- `.codex/WORKLOG.md`
- `.codex/PORTING_NOTES_2026-08-13.md`

### 具体修改及原因

- `AutoColliderGen_Final.CreateGeom()` 不再给新生成 Hull 添加
  `ModelCollisionHighlighter`；Hull 继续保留 MeshCollider、Rigidbody、MjGeom 和诊断显示。
- `Simulation.CreateColliderObject()` 同样移除保存碰撞体重载时的高亮组件创建，堵住另
  一条复发入口。
- `ModelCollisionHighlighter` 把 Renderer 数组和 `MaterialPropertyBlock` 的初始化提前到
  `Awake()`，并保留 `Start()` 的幂等调用。
- 对旧保存数据/场景中已带组件的 Hull，`Awake()`、`HighlightModel()`、
  `SelectModel()`、`OnMouseEnter()` 和 `OnMouseExit()` 都会识别为碰撞代理并早退；同时
  清理它可能占用的 `currentHighlightedObject/selectedObject` 静态引用。
- 不能只在 `SetModelOpacity()` 判空：那只会隐藏异常，代理仍可能抢占全局高亮状态。
  本次从组件创建和旧数据兼容两层消除竞争。

### 验证情况

- 两条 Hull 创建路径中已不存在 `AddComponent<ModelCollisionHighlighter>()`。
- 对三个业务脚本执行 `git diff --check` 通过。
- Unity 临时工程仍缺少 `Temp/obj/Assembly-CSharp/project.assets.json`，不能在当前终端执行
  有效的 `dotnet build --no-restore`；需要 Unity Editor 完成实际脚本编译。

### 当前状态与后续检查

- 代码修复和项目内记录已完成，等待 Unity 运行验收。
- 分别测试“现场重新生成碰撞体”和“退出后从 `.collider.xml` 重载碰撞体”。
- 鼠标反复经过原模型和 Hull 区域，Console 不应再出现第179行空引用；原模型仍应正常
  高亮，单击只新增一个任务点，碰撞测试仍应正常。

## 2026-08-13 - 三点任务持续无解日志分析（只读）

### 本次任务目标

- 分析三个目标点多次规划仍找不到解的原因，不修改业务代码或场景参数。

### 日志证据

- 检查 `Logs/Log_2026-08-13_21-02-14.log`，确认三次独立尝试分别发生在
  `21:09:37`、`21:14:03` 和 `21:30:23`。
- 三次结果完全一致：目标1成功；目标2从目标1停靠点
  `(0.179, 0.000, -0.728)` 开始，模型点为 `(0.975, 1.920, -0.024)`。
- 目标2水平距离为 `1.063m`，小于 `armReachDistance=1.150m`，现有底盘逻辑因此直接
  判定“已在机械臂工作半径内”，底盘保持原位，没有搜索更合适的站位或朝向。
- 目标2随后进行了六轴/升降轴和多个末端滚转角的完整 IK 重试，但三次稳定得到相同
  最优误差：`bestPosErr=0.01492m`、`bestRotErr=0.02640rad`。
- `RunScene` 的近似解上限为位置 `0.01m`、姿态 `0.03rad`：姿态误差已达标，但位置
  误差 `14.92mm` 超出 `10mm` 上限 `4.92mm`，所以 IK 未返回候选。
- BIT* 在取得 IK 候选前即终止，并未进入随机路径搜索；整个三点预计算随目标2失败而
  清空，所以日志中不存在“目标3/3”，第三个点实际上没有被求解。
- 三次都得到同一误差，说明这是当前底盘站位、目标高度和强制观察位姿共同造成的稳定
  可达性/验收边界问题；继续增加相同随机重试次数预计没有收益。
- 本次三个选择对象互不相同，任务点数量正常增加到3，未发现重复选点复发；本轮日志也
  未出现 `NullReferenceException`。

### 根因判断与后续方向

- 主要结构性原因是底盘预计算仅用水平半径判定机械臂可达，未用实际 IK 验证当前站位。
  第二点高度为 `1.920m`，且手动观察方向固定为 `(-1,0,0)`，运行时观察距离为
  `0.25m`；这些姿态约束下，上一目标留下的底盘位置/朝向处于 IK 可达边界之外。
- 首选修复方向：当前站位 IK 失败时，在目标周围搜索多个 NavMesh 备用站位和朝向，
  选取可解且余量较好的位置；并给 `1.15m` 名义半径增加舒适工作区余量。
- 可用于确认而非正式修复的测试：将第二点单独作为首点、调整点序，或临时关闭
  `enableLookAt`。若单独首点可解，则可进一步确认是多点间底盘站位继承问题。
- 把 `maxAcceptedPositionError` 从 `0.01` 临时调到略高于 `0.01492` 会让该近似解通过，
  但会牺牲定位精度，仅适合作为诊断，不建议作为首选正式修复。

### 修改情况

- 仅更新 `.codex/PROJECT_CONTEXT.md` 与 `.codex/WORKLOG.md`。
- 未修改 `Assets` 业务代码、场景、模型或规划参数。

## 2026-08-13 - 原第二点改为单点后的复测分析（只读）

### 本次任务目标

- 核对用户把上次第二点单独设为首点后仍无法求解的新日志，修正上一轮原因判断。

### 读取的关键文件

- `Logs/Log_2026-08-13_21-02-14.log`
- `Assets/MissionController.cs`
- `Assets/ArmController.cs`
- `Assets/MujocoStaticIKSolver.cs`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`

### 新日志事实

- `21:59:04` 只选择了 `1x.001`，任务数量为1；不存在重复选点或第三点干扰。
- 模型点为 `(0.975, 1.920, -0.024)`。底盘从原点开始，水平距离 `0.975m` 小于
  `armReachDistance=1.150m`，所以代码直接输出“底盘保持原位”，没有进行 NavMesh
  站位搜索。
- 实际交给 IK 的观察位姿为 `(0.725, 1.920, -0.024)`、旋转 `(0,90,0)`；这是手动
  观察向量 `(-1,0,0)` 和运行时 `0.25m` 观察距离形成的强制末端位姿。
- `q_start` 体检为有效；求解器依次尝试原姿态和 `±45/±90/±135/180°` roll，且每种
  姿态都先尝试六轴、再释放 `0~0.5m` 的升降轴，共最多320次求解。
- 最佳无碰撞候选误差为 `bestPosErr=0.03886m`、`bestRotErr=0.08360rad`；位置和姿态
  都超过场景允许的 `0.01m/0.03rad`，因此没有候选交给 BIT*。
- 同一目标在上一轮第一点停靠位 `(0.179,0,-0.728)` 曾达到
  `0.01492m/0.02640rad`。从原点反而变差，说明问题不是目标顺序本身，而是该固定观察
  位姿对底盘站位/相对方位高度敏感。
- 本次碰撞生成后机器人位移为0、PhysX穿透为0、MuJoCo初始仅有原有1组接触；没有证据
  表明生成凸包再次把机器人推开。

### 修正后的判断

- 已排除“必须先经过第一个点才失败”；该点在当前原点站位本身也没有可接受解。
- 底盘并非驱动失效，而是 `MissionController` 只看模型点水平半径后主动决定不移动。
- 当前日志只记录通过碰撞检查后的最佳候选；更接近的候选若因碰撞被丢弃不会留下统计，
  因而还不能仅凭现有日志断言是纯运动学不可达。最可能范围已收敛为：固定LookAt姿态在
  当前站位接近关节/奇异边界，或更优姿态会与机器人/支架碰撞。
- 下一步最小隔离测试应先保持单点：临时关闭 `enableLookAt` 验证纯位置IK；随后恢复
  LookAt，仅在不执行运动的前提下临时关闭 `checkCollision` 验证是否为碰撞过滤。两项
  结果可决定先改底盘站位搜索还是先查具体碰撞对。
- 正式修复仍应取消“进入名义半径就必定原地”的假设：先验证当前站位IK，失败后在目标
  周围 NavMesh 搜索多个位置和朝向，并以实际观察位姿、IK精度和碰撞余量共同评分。

### 修改情况

- 仅更新项目内 `.codex/PROJECT_CONTEXT.md` 与 `.codex/WORKLOG.md`。
- 未修改业务代码、场景、模型或规划参数。

## 2026-08-14 - 关闭固定方向后的多次成功复核（只读）

### 本次任务目标

- 核对同一目标关闭方向约束后的多次实测，最终确认此前IK失败的决定因素。

### 读取的关键文件

- `Logs/Log_2026-08-14_09-25-24.log`
- `Assets/ArmController.cs`
- `Assets/MujocoStaticIKSolver.cs`
- `Assets/MissionController.cs`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`

### 实测结果

- 三次任务均为同一单点 `1x.001`，模型点仍为 `(0.975,1.920,-0.024)`，底盘仍因水平
  距离 `0.975m < 1.15m` 保持原点不动。
- 唯一关键变化是日志中的目标旋转从固定测试时的 `(0,90,0)` 变为 `None`。
- 三次均返回3个去重、通过碰撞检查的IK候选，并完成BIT*与整项任务；其中两次候选1
  可直接连接，另一次BIT*搜索5次后成功。
- 最佳位置误差分别约为 `0.86mm`、`0.77mm`、`0.86mm`，其余返回候选也在
  `0.84~0.99mm` 范围，均达到场景严格 `1mm` 收敛阈值。
- 所有成功候选的升降轴值均为 `0.0000m`，说明该目标不需要升降缸即可由六轴到达。
- 相同碰撞体和开启的IK碰撞检查下仍稳定成功，因此此前失败不是目标位置不可达、升降
  轴故障、底盘驱动失效或场景凸包阻挡，而是完整末端方向约束造成。

### 根因与推荐正式方案

- `CalculateObservationPose()` 在手动方向 `(-1,0,0)` 下生成固定前向，并构造完整
  Quaternion；IK同时约束末端前向轴和上向轴。目标点的位置有大量可行解，但强制
  `(0,90,0)` 后这些解会落入关节/奇异或碰撞边界。
- 当前 `rollFallback` 只是按 `±45/±90/±135/180°` 选择若干完整姿态重新求解；它没有
  真正把绕观察轴的roll变成连续自由度，也不会改变不可行的接近方向。
- 若现场任务仍需要相机/工具朝向目标，不建议永久关闭 `enableLookAt`。首选正式方案是
  新增“指向型IK”：只约束末端前向轴指向目标，放开上向轴/roll；若仍失败，再搜索邻近
  观察方向或备用底盘站位。完全位置模式只作为明确可配置的最后降级方案并输出警告。

### 修改情况与后续检查

- 本轮仅更新项目内 `.codex/PROJECT_CONTEXT.md` 与 `.codex/WORKLOG.md`。
- 未修改业务代码、场景或参数。
- 若用户授权实施，下一步应先修改IK姿态误差维度和候选评分，保留现有完整姿态模式作
  可选项，再用当前单点及原三点任务回归验证。

## 2026-08-14 - 现有“指向型IK”基础与版本历史核对（只读）

### 本次任务目标

- 核对用户记忆中已经实现过的“末端朝向目标但放开自身滚转”功能，判断现有代码距离
  正式 DirectionOnly 模式还差什么。

### 读取与对比

- `Assets/RobotData.cs`
- `Assets/ArmController.cs`
- `Assets/MujocoStaticIKSolver.cs`
- `Assets/BITStarPlanner.cs`
- `v1.2`、`v1.4`、`v1.5` 对上述文件的 Git 历史与 `git blame`

### 已经具备的部分

- `v1.2` 已有 `enableLookAt`、`faceAxis`、手动观察向量以及
  `CalculateObservationPose()`，能够生成“末端指定轴朝向目标”的观察位置和Quaternion。
- IK已有 `rotWeight`、角速度Jacobian、位置/姿态联合DLS，以及带姿态的BIT*入口。
- `v1.4` 提交 `2deef7d (SoftWare1.4)` 新增 `enableRollFallback` 和
  `rollFallbackSteps`；原始完整姿态失败后，会保持观察前向并尝试
  `±45/±90/±135/180°` 的不同roll。`v1.5`继续保留该实现。

### 为什么还不是真正的DirectionOnly

- `BuildRotationAttempts()` 只是生成8个离散的完整Quaternion目标。
- `RunDampedLeastSquares()` 对每个目标同时计算末端前向轴误差和上向轴误差，再把二者
  相加形成3维旋转误差；因此选定某个roll后，上向轴依然被锁定。
- 换句话说，现有功能是“枚举几个可能的完整姿态”，不是“只约束观察轴，roll作为连续
  自由度由求解器自行选择”。当前高位点在全部离散姿态下失败，正好暴露了该差别。

### 推荐复用方式

- 保留现有观察位置、`faceAxis`、BIT*接口、候选/碰撞检查和FullPose逻辑。
- 增加明确的姿态模式：`PositionOnly / DirectionOnly / FullPose`；默认建议
  `DirectionOnly`。
- DirectionOnly只使用末端前向轴与目标观察方向的叉积作为姿态误差，不再加入上向轴
  误差；roll fallback在该模式下无需执行。
- 验收应至少覆盖当前高位单点、原三点顺序、碰撞检查开启、升降轴与底盘保持原配置，
  并核对末端前向夹角而不是完整Quaternion误差。

### 修改情况

- 仅更新项目内 `.codex/PROJECT_CONTEXT.md` 与 `.codex/WORKLOG.md`。
- 未修改业务代码、场景、Prefab或规划参数。

## 2026-08-14 - 实现真正的 DirectionOnly 指向型 IK

### 本次任务目标

- 按用户确认的第二种方案，将“末端前向指向目标、绕前向轴 roll 连续自由”正式实现，
  同时保留纯位置和完整姿态模式。

### 代码与场景修改

- `Assets/MujocoStaticIKSolver.cs`
  - 新增 `PositionOnly / DirectionOnly / FullPose` 三种 `OrientationConstraintMode`。
  - `DirectionOnly` 使用末端 Site 的 Z 前向轴与目标前向轴之间的最短轴角误差，不再加入
    Up 轴误差；对正反向恰好相反的180度情况使用稳定垂直轴，避免叉积为零被误判成功。
  - 对角速度 Jacobian 应用 `P = I - ff^T` 投影，移除绕当前前向轴的分量，使 roll 真正
    成为求解零空间中的连续自由度，而不是仍被隐式压成固定值。
  - `FullPose` 保持原有前向+上向完整姿态约束和离散 roll fallback；`DirectionOnly` 不再
    进行无意义的离散 roll 枚举。
  - 无目标 Quaternion、`rotWeight` 为零时仍自动退化为 `PositionOnly`，保持既有无姿态
    调用接口兼容；新增 `[IK姿态]` 日志输出实际生效模式。
- `Assets/SimulationPlatform/Scenes/RunScene.unity`
  - 正式运行求解器设置 `orientationConstraintMode = 1`（DirectionOnly）。
  - 将上一轮纯位置隔离测试留下的 `enableLookAt = 0` 恢复为 `1`，让规划入口继续生成
    指向目标的 Quaternion，再由 DirectionOnly 只取其中前向方向。
  - 场景本来还包含用户其他未提交修改；本轮只新增/修改上述两个精确字段，没有清理或
    替换其他场景差异。

### 验证

- 首次 `dotnet build "My project21.5.sln" --no-restore` 因 Unity `Temp/obj` 中缺少
  `project.assets.json` 未进入源码编译；随后普通 build 重建临时依赖清单。
- 编译发现并修正一次 `[Header]` 放在枚举而非字段上的标注错误。
- 最终执行 `dotnet build "My project21.5.sln" --no-restore` 成功：0 error，17项均为
  项目原有 warning。
- 尚需用户在 Unity 中对当前高位单点和原三点任务做运行回归，日志应出现
  `[IK姿态] mode=DirectionOnly`；同时观察末端前向是否指向目标、roll 是否能选择自然
  关节姿态、碰撞检查是否继续通过。

## 2026-08-14 - DirectionOnly 首次运行仍无解分析（只读）

### 本次任务目标

- 核对 DirectionOnly 上线后的首次实测，确认新模式是否真正生效，以及失败位于哪一层。

### 读取的关键文件

- `Logs/Log_2026-08-14_09-51-47.log`
- `Assets/MujocoStaticIKSolver.cs`
- `Assets/MissionController.cs`
- `Assets/ArmController.cs`
- `Assets/RobotData.cs`
- `Assets/BITStarPlanner.cs`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`

### 日志事实

- 两次单点任务均明确输出
  `[IK姿态] mode=DirectionOnly, targetRotation=True, rotWeight=0.100, rollFallback=False`，
  说明新脚本和场景配置已经生效，不是 Unity 没有重新加载修改。
- 目标仍是模型点 `(0.975,1.920,-0.024)`；实际观察位置为
  `(0.725,1.920,-0.024)`、目标旋转 `(0,90,0)`，即末端 Z 前向轴必须沿 Unity `+X`
  水平指向模型。
- 底盘仍因模型点水平距离 `0.975m < armReachDistance 1.15m` 保持原点，没有搜索备用
  站位或朝向。
- 两次 DirectionOnly 都先运行六轴、再释放升降轴，最终得到完全相同的最佳无碰撞候选：
  `bestPosErr=0.03178m`、`bestRotErr=0.06950rad`（约3.98度）。
- 场景近似验收上限仍是位置 `0.01m`、方向 `0.03rad`（约1.72度），因此该候选同时
  超出位置和方向阈值，在 BIT* 随机路径搜索开始前被拒绝。
- 相比旧 FullPose 单点结果 `0.03886m / 0.08360rad`，释放 roll 后位置和方向误差都
  有约17~18%的改善，但不足以达到验收范围；这证明旧 Up/roll 约束确实是一部分负担，
  但固定 `+X` 接近方向本身仍是主要约束。
- 同一点 PositionOnly 已实测可达到约 `0.77~0.99mm` 且升降轴为0，因此目标位置、底盘
  驱动和升降轴本身没有故障。

### 当前判断与下一步

- 失败层位于 DirectionOnly IK/候选验收，不是 BIT* 路径搜索失败。
- 当前 `bestObserved` 只统计通过最终碰撞检查的候选；日志尚未记录碰撞拒绝数量、碰撞前
  最优误差、失败候选 qpos/关节限位和最终前向，因此还不能区分：精确 `+X` 方向确实
  不可达、DLS陷入局部极小值，或更精确候选被碰撞过滤。
- 不建议只增加相同随机重试次数，也不建议直接放大位置误差上限到31.78mm。
- 推荐下一步先补失败候选诊断；若确认是数值局部极小，采用 PositionOnly 可行解预热后
  再执行位置优先 DirectionOnly；若确认精确方向几何不可达，再由用户确认是否接受约
  5度的方向锥容差，或改为搜索备用底盘站位/接近方向。

### 修改情况

- 本轮未修改业务代码、场景或参数。
- 仅更新项目内 `.codex/WORKLOG.md` 与 `.codex/PROJECT_CONTEXT.md`。

## 2026-08-14 - 后续 IK 方案含义说明（只读）

### 本次任务目标

- 向用户详细解释“PositionOnly 可行解预热 → 位置优先 DirectionOnly 精化”、失败诊断、
  5度方向锥和备用底盘站位分别解决什么问题，以及它们是否会降低精度。

### 说明要点

- 预热不是最终取消方向，而是先用已验证可达的纯位置解作为 DirectionOnly 的初始关节
  姿态，避免从默认/随机姿态同时追位置和方向而陷入局部极小。
- 位置优先表示把末端位置作为一级硬任务，方向只在不破坏位置精度的关节零空间中继续
  优化；这与当前把位置和方向加权折中不同，不需要放大10mm位置验收阈值。
- 补充诊断用于区分局部极小、关节限位、碰撞过滤和真实几何不可达，避免盲目修改权重。
- 约5度方向锥会真实降低方向精度，只是后备方案；备用底盘站位不降低末端精度，但会
  增加底盘搜索和运动。

### 修改情况

- 未修改业务代码、场景或参数；仅补充本项目工作记录。

## 2026-08-14 - PositionOnly 预热与位置优先 DirectionOnly 实现

### 本次任务目标

- 在不放宽位置、方向和碰撞验收标准的前提下，实现
  `PositionOnly 可行解预热 → 位置优先 DirectionOnly 精化`，并补齐失败候选诊断。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/MujocoStaticIKSolver.cs`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`

### 修改的业务文件

- `Assets/MujocoStaticIKSolver.cs`

### 具体修改

- DirectionOnly每个默认/随机候选先运行一次内部 PositionOnly DLS，得到已经靠近目标位置
  的关节构型，再从该状态精化方向；中间状态只写入临时 MuJoCo 状态，候选结束后仍由
  原有快照恢复，不会在 Game 画面执行，也不会下发给真实机器人。
- 新增 `enablePositionWarmStart=true`、`enablePositionPriorityDirectionSolve=true` 和
  `positionWarmStartMaxIterations=2000` 三个可回退配置；只影响DirectionOnly。
- DirectionOnly精化改用分层加权DLS：先计算位置一级任务；构造
  `N = I - Jp#Jp` 位置零空间；方向二级任务只通过 `JdN` 使用剩余自由度，并补偿一级
  位置步对方向的瞬时影响。
- DirectionOnly阶段不再叠加原有rest pose偏置，避免未投影的舒适姿态偏置重新破坏位置
  一级任务；PositionOnly和FullPose原路径保留。
- 候选先捕获数值结果、再执行碰撞检查，分别统计碰撞前最佳候选和通过碰撞后的最佳候选；
  不会让碰撞候选进入accepted列表。
- 新增诊断：
  - `[IK预热汇总]`：预热次数、严格收敛次数、最佳位置误差、分层开关；
  - `[IK碰撞汇总]`：计算候选数、碰撞拒绝数和通过数；
  - `[IK失败诊断]`：预热状态及候选来源；
  - `[IK方向诊断]`：目标/实际前向和夹角；
  - `[IK关节限位]`：各执行关节值、范围、最近限位余量；
  - `[IK碰撞候选]`：最深穿透、Geom ID及通过托管MjGeom解析的名称。
- 失败碰撞名称仍不调用有原生字符串所有权风险的 `mj_id2name`。
- 将旧的“每个随机候选都打印初始坐标”压缩为每轮首个候选打印，避免诊断刷屏。

### 保持不变的内容

- 本轮未修改 `RunScene.unity`；正式场景仍为 DirectionOnly、`enableLookAt=true`。
- `maxAcceptedPositionError=0.01m`、`maxAcceptedRotationError=0.03rad`、
  `checkCollision=true` 均未放宽。
- 未启用5度方向锥，未修改底盘站位搜索，也未改 BIT*、MissionController 或真实机器人
  下发逻辑。

### 验证

- `dotnet build "My project21.5.sln" --no-restore` 成功：0 error；17项均为项目原有
  warning。
- `MujocoStaticIKSolver.cs` 保持原CRLF格式，并通过带 `cr-at-eol` 的 diff whitespace检查。
- 尚需 Unity 运行回归当前高位单点。先确认 `[IK预热汇总]` 中存在严格收敛的约1mm
  PositionOnly预热，再查看最终是否返回候选；若仍失败，把新的方向、限位和碰撞诊断
  作为决定备用底盘站位或方向锥的依据。

### 当前状态

- 代码实现和静态编译已完成，等待 Unity 场景运行验收。

## 2026-08-14 - 分层 DirectionOnly 三次运行复测分析（只读）

### 本次任务目标

- 核对用户在启用 PositionOnly 预热和位置优先 DirectionOnly 后的多次失败日志，判断
  新实现是否生效，以及失败是否来自位置、碰撞、关节限位或固定方向约束。

### 日志证据

- 检查 `Logs/Log_2026-08-14_10-35-19.log`，其中包含 10:35:42、10:35:52、
  10:36:26 三次独立单点规划。
- 三次均为 `mode=DirectionOnly`，预热分别严格收敛 30/40、30/40、29/40；最佳预热
  位置误差为 `0.73~0.81mm`，且 `hierarchical=True`，说明新代码和两个关键开关确实
  已生效。
- 三次最终最佳无碰撞候选完全一致：位置误差 `2.90mm`，满足现有 `10mm` 近似验收
  上限；方向误差 `0.20826rad = 11.933°`，明显超过 `0.03rad = 1.719°` 上限，故在
  BIT* 路径搜索开始前由 IK 验收拒绝。
- 每轮40个候选中分别有24、18、25个被碰撞检查拒绝，但全局最优候选本身通过碰撞检查；
  因而当前失败不是碰撞过滤掉了更优候选。
- 六个转动关节均不接近限位；只有升降缸 `joint10=0m` 位于下限。求解器已先做不允许
  升降缸参与的六轴阶段，再释放升降缸，两阶段都未满足方向，因此不能把失败简单归因于
  升降缸下限。
- 底盘因目标模型点水平距离 `0.975m < 1.15m` 保持原位，没有搜索其他站位。

### 当前结论

- 分层求解把位置从旧 DirectionOnly 的 `31.78mm` 改善到 `2.90mm`，证明“先保证位置”
  已按预期工作；代价是当前固定站位下方向稳定停在约 `11.93°` 的残差平台。
- 位置可达、碰撞过滤和转动关节限位已基本排除。剩余主要可能性是：当前底盘站位下精确
  Unity `+X` 接近方向不在该末端点的可达方向集合内，或零空间精化在该构型附近进入稳定
  局部极小/奇异位形。
- 40次候选、约30次成功位置预热仍反复得到同一最佳方向残差，继续单纯增加相同随机重试
  次数的收益很低。
- 当前残差约 `11.93°`，所以直接采用先前讨论的 `5°` 方向锥仍不足以接纳这个候选；更稳妥
  的下一步是先做固定位置的方向可行性扫描，并让底盘在少量备用站位/朝向上重算，以区分
  真正几何不可达和现有零空间算法局部极小，同时保持现有方向精度要求。

### 修改情况

- 本轮未修改业务代码、场景或求解参数。
- 仅更新项目内诊断记录和迁移说明。

## 2026-08-14 - 以相机可见性为目标的方向锥方案确认（只读）

### 本次任务目标

- 根据用户“只要相机能看到接头即可”的任务要求，评估是否可用更大的DirectionOnly方向锥
  替代备用底盘站位搜索，并给出具体建议值和修改边界。

### 检查结果与建议

- 当前真实视频链路使用 `ExternalCode/realsense_image_server.py` 获取RealSense彩色图，历史
  运行日志确认分辨率为1280x720；RunScene内名为`fixed`/`track`的Unity Camera不是这一路
  真实相机画面，不能用其FOV代替RealSense视场。
- 本轮尝试只读查询在线RealSense的真实内参/FOV，但设备访问未获授权，因此没有假定具体
  相机型号或把Unity Camera参数当成硬件参数。
- 当前最佳DirectionOnly方向残差为11.933度。建议第一档采用“半角15度”的方向锥
  （完整锥角30度，弧度约0.261799），比当前候选留约3度余量；不建议一开始直接放到20度
  以上，避免接头落在画面边缘且受安装偏差影响后离开有效识别区。
- 实现时应只放宽DirectionOnly的最终近似验收上限；保持位置10mm、碰撞检查、方向优化
  权重和严格收敛阈值不变。这样求解器仍会在候选中优先选择方向误差更小的解，只在没有
  严格方向解时接纳15度锥内的候选。
- “夹角小于相机半视场”只保证理论上可能入画，不自动保证目标未遮挡、尺寸足够或视觉模型
  能识别。Unity复测时需要打开真实现场视频，确认接头位于画面中部而非刚好贴边；真机执行
  前仍需低速/仿真验证。

### 修改情况

- 本轮未修改业务代码或场景参数，只确认推荐方案并更新项目记录。

## 2026-08-14 - DirectionOnly角度误差几何含义说明（只读）

### 本次任务目标

- 解释方向误差是“末端到达观察点后的姿态误差”，还是“末端位置可以落在锥形区域”，并
  对照当前ArmController和IK代码明确观察点、接头和相机视线的关系。

### 代码链路与结论

- `ArmController.CalculateObservationPose()` 先根据接头位置、机器人位置和
  `observationDistance` 计算观察点：`p = target + dir * observationDistance`；当前复现
  日志中接头约为 `(0.975,1.920,-0.024)`，IK观察点约为
  `(0.725,1.920,-0.024)`，两者相距约0.25m。
- 同一方法用 `Quaternion.LookRotation(target - p)` 生成从观察点精确指向接头的理想朝向；
  当前 `faceAxis=(0,0,1)`，因此DirectionOnly比较的是`tip`的Z前向轴和这条理想视线。
- IK位置误差与方向误差独立计算：位置误差是实际`tip`位置到固定观察点`p`的欧氏距离；
  方向误差通过`Vector3.Angle(actualForward,targetForward)`等价的轴角计算得到。
- 所谓“半角15度方向锥”的锥顶在实际末端/相机附近，中心轴是“观察点→接头”的理想方向；
  被放宽的是前向向量可以在锥内偏转，不是末端位置可以在锥形体积内任意移动。末端位置
  仍需满足现有10mm上限（当前候选为2.90mm）。
- 接纳15度方向误差不代表实际光轴仍精确穿过接头，而是接头相对理想光轴最多可能偏约
  15度。若相机光心/光轴与`tip`完全一致，0.25m距离下11.933度约对应画面目标相对中心线
  5.3cm，15度约对应6.7cm；真实安装偏置会改变该数值。
- 当前代码没有把RealSense外参显式纳入IK；只有在真实相机光轴与`tip` Z轴已经通过机械
  安装或标定对齐时，IK的方向角才能直接等价为接头在真实相机画面中的离轴角。上线前仍
  应用现场视频确认。

### 修改情况

- 本轮未修改业务代码或场景参数，只补充几何定义和验证边界。

## 2026-08-14 - RunScene DirectionOnly方向锥放宽至半角15度

### 本次任务目标

- 按用户确认，将正式RunScene的DirectionOnly最终方向近似验收范围改为半角15度，使当前
  11.933度方向残差、2.90mm位置误差的无碰撞候选可以进入后续BIT*规划。

### 读取的关键文件

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `Assets/MujocoStaticIKSolver.cs`
- `Assets/ArmController.cs`
- `Assets/MissionController.cs`
- `Assets/SimulationPlatform/Scenes/RunScene.unity`

### 修改的业务文件

- `Assets/SimulationPlatform/Scenes/RunScene.unity`

### 具体修改

- 仅将RunScene中`MujocoStaticIKSolver.maxAcceptedRotationError`从`0.03rad`
  （约1.719度）改为`0.2617994rad`（15度）。
- 未修改`stopRotThreshold=0.005rad`、`rotWeight=0.1`或DirectionOnly分层求解过程，因此
  求解器仍继续优化方向并按现有评分选择误差更小的候选；15度只作为严格收敛失败后的最终
  近似验收上限。
- 未修改`maxAcceptedPositionError=0.01m`、`checkCollision=true`、接头观察距离、底盘
  搜索、BIT*路径规划或真实机器人下发逻辑。
- 本次只修改场景YAML中的一个现有标量，未覆盖RunScene内其他未提交修改。

### 验证与待测

- 文本检查确认RunScene现在保存`maxAcceptedRotationError: 0.2617994`，换算为15度。
- 本次没有修改C#，无需新增编译依赖；仍需用户在Unity中重新运行此前单点，确认IK返回
  候选并开始BIT*规划，然后打开真实现场视频检查接头是否位于可稳定识别区域。
- 真机执行前应先在仿真/低速条件验证；如果日志方向误差超过15度仍会按预期拒绝。

### 当前状态

- 场景参数修改完成，等待Unity运行回归。

## 2026-08-19 - SoftWare1.7 发布及 GitHub Release 流程固化

### 本次任务目标

- 将 `My project21.5` 中除已忽略大型资源外的当前改动发布为 `SoftWare1.7 / v1.7`。
- 在 GitHub 上为 `v1.7` 创建包含更新内容和验证结果的正式 Release。
- 将本次操作整理为后续版本可重复使用的发布流程。

### 读取和检查的关键内容

- `AGENTS.md`
- `.codex/WORKLOG.md`
- `.codex/PROJECT_CONTEXT.md`
- `.gitignore`
- `ProjectSettings/ProjectSettings.asset`
- Git 暂存区、分支、标签和远程跟踪状态

### 发布范围与大文件处理

- 用户确认本次上传整个非忽略工作区，共24个文件，提交统计为6687行新增、469行删除。
- 已确认以下大型资源未被Git跟踪，并继续由`.gitignore`排除：
  - `Assets/dipan2.fbx`（约186MB）；
  - `Assets/微软雅黑 SDF.asset`（约135MB）；
  - `Assets/机舱.fbx`（约102MB）。
- 暂存区中最大的文件为 `Assets/SimulationPlatform/Scenes/RunScene.unity`，约3.56MB，
  其余待提交文件均低于GitHub单文件限制。

### 版本和编译验证

- 将 `ProjectSettings/ProjectSettings.asset` 中 `bundleVersion` 从 `1.6.0` 更新为
  `1.7.0`；旧值来自2026-07-24的 `SoftWare1.6` 提交 `bae44a7`。
- 执行 `dotnet build "My project21.5.sln" --no-restore` 成功：0错误、26个既有警告。
- 编译警告主要来自旧API、隐藏继承成员、未使用字段、未等待异步调用和序列化状态字段，
  未阻止本次发布。

### Git 发布结果

- 发布分支：`release/software-v1.0`。
- 发布提交：`dad2be7521a875e0b9c6cdd6f1058dc70ba9b9c3`，提交说明 `SoftWare1.7`。
- 分支已从远程提交 `bae44a7` 推进到 `dad2be7` 并成功推送到
  `origin/release/software-v1.0`。
- 创建并推送带注释标签 `v1.7`。
- 最终 `HEAD`、`release/software-v1.0`、`origin/release/software-v1.0` 和 `v1.7`
  均指向 `dad2be7`，发布后的工作区为干净状态。

### GitHub Release

- 用户已在 GitHub `Releases` 中选择已有标签 `v1.7`，发布 `SoftWare1.7` Release。
- Release 内容按“新增功能、功能优化、工程调整、编译验证、注意事项”分节，包含内部
  版本号 `1.7.0` 和本次0错误、26警告的编译结果。
- GitHub Release 是面向使用者的版本说明；Git标签用于固定提交，二者不是同一个对象。

### 后续标准流程

1. 保存Unity场景并更新 `bundleVersion`。
2. `git fetch origin` 后用 `git status -sb` 检查本地与远程关系。
3. 确认提交范围和大文件排除规则；仅在整个非忽略工作区都属于本次发布时使用
   `git add -A`。
4. 用 `git --no-pager diff --cached --stat` 检查暂存内容，并完成编译验证。
5. `git commit` 后先推送发布分支，再创建并单独推送带注释版本标签。
6. 用 `git status -sb`、`git log -1 --decorate` 确认本地、远程和标签一致。
7. 在GitHub上基于该标签创建Release并填写更新说明。
8. 从下一个版本起，建议在打标签前同步更新仓库根目录 `CHANGELOG.md`，长期保留版本历史。

### 修改情况与当前状态

- 本轮仅更新 `.codex/WORKLOG.md` 和 `.codex/PROJECT_CONTEXT.md`，没有修改业务代码、
  Unity场景、Prefab或运行参数。
- `SoftWare1.7 / v1.7` 分支、标签及GitHub Release均已完成；本次新增的记录尚需另行提交
  才会同步到GitHub。

## 2026-08-19 - 伍老师21.4改动深度对比与第一批集成

### 本次任务目标

- 以已发布的21.5/v1.7为基线，深度对比`My project21.4-wulaoshi`，处理7月23日反馈的
  三类问题：视角和替换接头保存、替换后旧目标点残留、场景模型界面缺少网格切割。
- 不整目录覆盖、不回退21.5现有的IK、NavMesh、碰撞体诊断、视觉和真机控制改动。

### 对比结论

- 伍老师目录与21.5从较早历史独立演进，不能用普通快进合并；其工作区显示的243个修改
  文件经忽略行尾差异后均为0行真实修改，属于CRLF/LF噪声，真实功能改动都在提交历史中。
- 与本次反馈直接相关的提交为：`fae2e2e`（视角保存）、`dc4bcd5`（替换时清理旧选择）、
  `b820a07`（替换模型命名回退）、`4c26960`（替换记录回填和恢复）、`03c9c77`
  （MainScene网格切割入口）。
- 未整包移植`46a6269`的场景管理界面重排、`872ba48`的面板生命周期改动、`8f36012`的
  调试立方体/材质，以及`fae2e2e`中把项目记录上限从50改成1的无关变更。
- `9d35a04`的目标点持久化没有进入本批：原实现不能可靠恢复红色高亮，保存的点位语义和
  路径标记不完全一致，同名/替换兄弟节点也可能匹配错误。本批只修复反馈明确要求的
  “替换后旧接头仍作为隐藏目标参与规划”。

### 修改的业务文件

- `Assets/SimulationPlatform/Scripts/Behaviour/CameraController.cs`
- `Assets/SimulationPlatform/Scripts/Behaviour/ModelCollisionHighlighter.cs`
- `Assets/SimulationPlatform/Scripts/Function/ModelImport.cs`
- `Assets/SimulationPlatform/Scripts/Function/Simulation.cs`
- `Assets/SimulationPlatform/Scripts/Model/ProjectRecord.cs`
- `Assets/SimulationPlatform/Scripts/Model/SimulationParam.cs`
- `Assets/SimulationPlatform/Scenes/MainScene.unity`

### 具体集成内容

- `CameraController`新增受控视角状态读写，保存水平角、垂直角、缩放距离和平移偏移；
  项目保存时同时写入`ProjectRecord`兼容字段和`SimulationParam`统一字段，加载模型和
  自动取景完成后在帧末恢复视角，兼容伍老师版本已生成的XML。
- 项目记录加载时真正回填`Replaces`，模型加载后按层级索引定位原接头、按JointId加载
  对应GLB并逐条重放替换；全部完成后刷新替换记录列表、恢复视角并重建运行时NavMesh。
- 替换接头前通过21.5现有的“逻辑选择目标”解析清除高亮、静态目标列表和
  `PathPointManager`标记，保留21.5对自动生成`_MjRoot/_Hull_`碰撞代理的去重逻辑；
  替换模型缺少名称时使用“替换接头”回退名。
- `ModelImport`等待派生类`OnModelLoaded()`异步初始化完成，并在故障时终止后续初始化，
  避免恢复任务与模型基础组件创建相互抢跑。
- 仅在`MainScene`补充并绑定`ColliderBtn`，按钮文字为“网格切割”，放在保存按钮下方；
  继续复用21.5现有`SceneEdit.OnColliderBtnClick()`和`AutoColliderGen_Final`，未覆盖整场景。

### 验证

- `dotnet build "My project21.5.sln" --no-restore -v:minimal`成功：0错误、25个既有警告。
- `git -c core.whitespace=cr-at-eol diff --check`通过。
- MainScene新增的9个Unity YAML fileID均唯一，按钮、文字、父子RectTransform和
  `SceneEdit.ColliderBtn`引用已静态核对。
- 当前无法用命令行替代Unity运行态交互测试；仍需在Unity中依次验证：保存后改变视角并
  重新进入、保存替换接头后重新进入、替换已选目标后立即规划、MainScene点击网格切割。

### 分支与当前状态

- 集成在本地分支`integration/wulaoshi-21.4`进行，基点为`90e425b`；未提交、未推送，
  不会改变已发布的`v1.7`标签。
