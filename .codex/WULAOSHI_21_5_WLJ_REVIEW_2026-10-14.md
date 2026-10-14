# 伍老师21.5.wlj主要改动分析

检查日期：2026-10-14。仅静态对比，没有启动Unity、连接设备或修改业务代码。

## 对比对象与依据

- 新版：`../My project21.5.wlj/My project21.5`，自带Git仓库，分支`wlj`，HEAD `acd0380`（运行详情）。
- 新版开发起点：其仓库`5c2ae4f`（20260918 qq 21.5），已经含我们之前做的碰撞持久化和全流程离线原型。
- 新增提交：`6ca90de`同步21.4、`bef9a92`模型日志、`c7d2186`项目日志、`2437a6b`仿真日志、`0dd5331`真实运行日志、`2d67a9c`记录调整、`fb62e13/9fec8e2`任务记录、`18062e7/acd0380`运行详情，共10个，历史日期9月19日至23日。
- 我们当前代码：本目录My project21.5，HEAD `4b8f839`/v1.11并叠加本地未提交的现场联调、通信适配、末端实测和延迟测试等改动。
- 不能直接把21.4-wulaoshi与新版所有差异都归为这次伍老师的成果；他的21.5底稿已经继承我们的多个功能。以其开发起点对HEAD隔离增量，再核对实际工作区。此次提交增量涉及72个Assets文件、32个C#脚本，其中新增C#10个；MainScene大量YAML变更不等于算法改写。
- 新版仍有其自带未提交文件：WORKLOG、部分资源meta/材质/SelectedObjectRecord及Packages等，未清理或覆盖。忽略行尾后Packages差异未发现实质版本变化；Unity版本仍2022.3.62f2c1，bundleVersion仍1.9.0，不可据包名推断发布版本较新。

## 主要改进

### 1. 模型管理操作日志更详细

JointEdit/JointList/SceneEdit/SceneList记录新增、编辑、删除的具体内容。接头编辑对比名称、型号、备注、导程、节圆直径、转矩、阈值、角度、行程；场景编辑对比名称/备注/模型文件。OperationLogTool.FormatModelFileInfo格式化模型文件名和大小，SceneEdit网格切割后记录Root与网格数量。

JointEdit原来保存后根据已有Id判断，新增也会写成编辑；新版在保存前保留isNewJoint，ProjectEdit也作同类修正。用途是回答“哪个用户把哪个参数从什么改成什么”，不是提高模型生成或机械臂算法速度。

### 2. 项目管理和仿真过程留痕

- ProjectEdit记录创建/修改字段；ProjectList记录查看历史、删除项目，删除时调用DeleteProjectRecords级联清理该项目运行历史。
- PathPointManager记录添加/删除目标点，JointReplace记录原物体与新接头名称。
- Simulation/PendingParamLog记录机器人/场景位置与旋转，ObserveParam增加观测方向变更；开始仿真、开始运行、停止和退出增加操作日志。
- 真实运行记录IP输入、速度、单步/实时同步切换，设备连接/断开/启动/激活、底盘重定位、视觉开关和电批按钮操作。
- 这些是用户操作/事件日志，多处在请求前记录动作，不能一律解读为硬件执行成功。IP和速度加载时有_isLoadingParams抑制，XYZ的延迟日志仍需运行检查是否产生加载或恢复事件的额外记录。

### 3. 新增按项目归档的运行历史

RunRecord/RunStepRecord描述运行与步骤，RunRecorder管理当前运行、分步追加、收尾持久化与按项目查询。数据库路径由PathTool.GetExecutableDirPath生成`Data/Runs/RunRecords.db`，有ProjectId与StartTime索引；独立于原Data/Operations/OperationLog.db。

记录时间、耗时、模式、目标数、替换接头数、仿真参数快照、真实运行IP/速度摘要及步骤；支持成功/急停/碰撞状态的数据结构。仿真在OnArmTaskFinished追加目标步骤，全部完成时立即保存并防重复；单步同模式继续沿用记录。真实路径在DispatchTask追加下发步骤。

### 4. 项目“记录”按钮与筛选/详情界面落地

ProjectList.OnRecordProjectClick打开TaskList；RunTypeChose可选全部/仿真/真实运行；TaskList按时间倒序显示本项目历史；RunRecordItem填充类型、开始时间、结果图标/颜色、原因、参数摘要、耗时。点击详情进入StepDetailPanel，StepCellItem显示序号、目标名称、力矩、角度和结果，再返回原列表。

MainScene、TaskCell/StepCell prefab和相关图标/材质配套变更。静态检查MainScene的TaskListPanel、DetailPanel和两种prefab引用存在，prefab GUID与当前meta相符；界面可用性仍需Unity运行验证。代码中称“回放”，实际没有轨迹动画/机器人运动重放逻辑，是历史记录与文本步骤详情。

## 已有功能与本次新增要分开

### 用户追问：是否已经保存真实设备下发指令

- 新的RunRecords运行历史目前只保存DispatchTask的taskName/步骤摘要，没有命令文本、报文、寄存器地址和值、每条应答或命令与结果关联字段。
- 旧调试日志是另一条链：DobotController.SendCommand在写流后Debug.Log原始指令，接收端打印服务端应答；已有LogToFile监听Unity日志并写入可执行目录Logs/Log_日期.log。因此不能说所有指令完全没保存，但这些不是新增项目记录列表中的数据。
- 底盘通用SendCommand只Pack/Write，不统一记录发送API和JSON；ConnectCommander路径调度会记录部分平动/转动JSON，通用应答打印API回复。电缸MoveToPosition写速度/目标/触发寄存器，但没有逐次记录地址/寄存器值/Modbus应答，调度仅有目标高度摘要，故并非全设备完整指令追踪。
- 若后续做可追溯通讯记录，应在真正发送/接收处统一记录设备、时间、关联ID、项目/任务、命令或寄存器、写入结果/应答/执行终态，区分请求/已写入/应答成功/实际完成，不能用界面操作日志代替。

以其开发起点对HEAD比较：AutoColliderGen_Final、ColliderManager、MujocoStaticIKSolver、BITStarPlanner和ExternalCode没有本次提交改动。另与我们当前目录逐文件比对，AutoColliderGen_Final、ColliderManager、MujocoStaticIKSolver、realsense_image_server.py内容一致（忽略CRLF）。碰撞保存/恢复、碰撞显示、视角/接头替换保存和离线全流程原型主要是已继承的功能。

MissionController、DobotController、RobokitController、LiftCylinderController、ServoTighteningController与VisionImageReceiver此次增量主要加记录，不构成新通信协议、控制精度、相机处理或规划性能优化。

## 当前实现边界与明显问题

1. **真机“成功”依据不足**：ConnectCommander.cs:425-431在无未下发任务时直接FinishRun(Success)，不是设备执行到位/拧紧成功确认；DispatchTask还在组件检查前追加步骤，任务结果为空。因此列表成功不可作现场验收证据。我们的确认流程如果后续接入，应由实际终态驱动。
2. **力矩/角度未接实测**：RunStepRecord定义Torque/Angle，StepCellItem直接格式化显示；查生产代码未发现运行步骤这两个字段、JointId/JointParams的采集赋值。默认0不代表测得0，真机步骤Result也为空；需要接电批/目标实际数据。
3. **碰撞/力超限原因尚未连通**：RunRecorder.MarkCollision/SetReason只有定义，没有生产调用。支持图标和状态模型不等于已能自动捕获碰撞与力超限；按钮停止则明确记录了软急停原因。
4. **离线与实物分类混淆**：Simulation.StartRun在fullProcessValidationEnabled时也BeginRun(REALTIME)，文字还称实时同步；离线验证可能被放到“真实运行”历史中。并且离线协程的完成/异常没有配套新增RunRecorder收尾调用，不能宣称所有入口闭环。
5. **成功日志可能误报保存失败**：RunRecorder.FinishRun在数据库Insert后访问ResultReason.Length，但成功调用未保证ResultReason非null，可能已经入库却因日志格式化空引用报“运行记录保存失败”。这是静态确定的空值风险，未在Unity实跑。
6. **选择目标保存仅有结构**：ProjectRecord新增SelectedObjects、SelectedObjectRecord含路径/索引/位置；全局检索只有模型定义，尚未看到Simulation保存或恢复调用。SceneModel.Time也只见字段，不应算已完成目标点恢复/时间功能。
7. **参数日志合并未完全兑现**：PendingParamLog在下一次值变化时逐次写上一项，初值需先播种，ObserveParam的pending日志没有看到Flush调用；不等于滑条每次拖动只写一条最终值。还需核对第一次改值、退出时最后一次日志。

## 与我们当前21.5的关系和后续方向

这版适合作为“操作日志完善＋项目运行历史界面”的来源。它没有我们当前的DobotController.Gateway/Commissioning/Feedback/MotionDiagnostics、FieldCommissioningPanel、servo_contact_cycle/unity_robot_client、机械臂/底盘到位确认和通信延迟工具。不能用整包覆盖当前开发目录，否则会丢掉已有联调成果。

若后续集成，宜保留我们当前控制链路，移植记录模型、历史/详情界面与需要的操作日志；记录事件接到我们的真实完成/异常结果，明确模拟与实物标识、真实数据与空值。MainScene大改应逐项核对UI引用后集成，不直接整场景覆盖。当前未进行代码移植、分支切换、编译、部署或设备测试。
