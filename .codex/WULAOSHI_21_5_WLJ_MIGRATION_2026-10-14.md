# 伍老师21.5.wlj功能迁移说明

日期：2026-10-14。目标工程：My project21.5；来源：../My project21.5.wlj/My project21.5（wlj/acd0380，开发起点5c2ae4f）。

## 备份及当前状态

- 集成前测试源码已上传：release/software-v1.0，提交4f849ed，附注标签v1.12-test.20261014；远端分支及标签解引用已核对一致。
- 本轮在integration/wulaoshi-21.5-wlj工作。原测试标签未移动，发布分支指针未推进。
- 当前迁移代码尚未提交、推送或打新发布标签，也未构建Linux可执行包。未提交的文件是工作区改动，不能仅凭切换分支就当作已经隔离保存的提交；验收后再commit。
- 保留已有WORKLOG和TESTDATA分析文档；没有修改伍老师来源工程或testdata。

## 迁入的功能

### 操作日志

迁入JointEdit/JointList、SceneEdit/SceneList、ProjectEdit的字段前后值、模型文件名/大小、场景网格切割数量日志，修复新增接头/项目被记为编辑的判断。PathPointManager记录目标添加/删除，JointReplace记录替换请求。登录失败记录只含事件，不记录密码。

Simulation.History和PendingParamLog记录机器人/场景位置与旋转、观测方向、真实模式IP/速度、单步/反馈同步开关以及运行/退出请求。参数初值在加载后播种，拖动/连续改值在停止变化0.6秒后或开始/退出时汇总为旧值→最终值，避免首次修改遗漏和每个滑条回调都写库。ObserveParam退出也补写最后一条。

机械臂连接/启动/激活/断开、底盘重定位、电缸连接/断开、电批程序按钮/正反转/停止、视觉预览及连接/错误事件留痕。请求日志明确写“请求”，不冒充硬件成功。新增日志异常有捕获；停止链优先发起原停止逻辑，收尾后再记录结果。OperationLogTool目录初始化移入异常保护，避免首次写日志失败导致控制类调用被TypeInitializationException阻断。

操作日志仍在可执行目录`Data/Operations/OperationLog.db`，分类以名称字符串保存；原OperationType数值0～3保持，新增分类4/5，不改变已有系统操作的枚举值。

### 项目运行历史

迁入并适配RunRecord、RunStepRecord、RunRecorder，数据库位于可执行目录`Data/Runs/RunRecords.db`（Editor为当前项目根目录）。与用户参数快照ProjectRecords、现场诊断CommissioningReports、全局Logs均为不同数据。

记录项目、模式/来源、开始/结束时间、耗时、目标/替换数量、参数快照、步骤、结果和原因。仿真参数及嵌套Vector3D复制为快照，后续界面改值不会修改已开始记录。开始、逻辑步骤、结束分别Upsert；异常退出留下运行中/未确认结束，不假造成功。Completed只表示记录收尾，不等于硬件到位，必须同时看Result与说明。

同项目/模式/来源的单步继续复用记录，换项目或流程另建；旧异步回调按runId检查，不能修改新的运行。首次失败/碰撞原因不会被后续取消或成功覆盖。LiteDB空字符串读回null及旧记录缺字段均有兼容；写盘失败显示警告并在本进程保存待重试项，后续写入按Id重试，不重复插入。写盘失败后若进程退出，未落盘部分仍可能丢失，不是断电可靠存储。

| 入口 | 历史分类/来源 | 收尾依据 |
| --- | --- | --- |
| 开始仿真 | 仿真 / 仿真 | 仿真目标轨迹结束；预计算/IK失败保留失败 |
| 全流程验证（模拟） | 仿真筛选内 / 离线模拟 | 模拟粗定位、精调、接触周期和复位结束；非实物 |
| 原“开始运行”通讯下发 | 真实运行 / 原通讯下发 | 最多记已下发/未验收到位，不将旧预计等待当硬件成功 |
| 现场联调各模式 | 真实运行 / 现场联调+模式 | 沿用当前真实执行/周期和收尾停止确认，失败保持失败 |

现场记录关联CommissioningReports目录；模拟视觉偏移明确标为模拟视觉、实物机械臂运动。单独插入/退出保留MotionDiagnosticSummary和“不等于到位验收”说明。电缸继续沿用既有超时策略，不在本轮改变；历史步骤明确该边界。新鲜实物反馈collision标志才可分类为碰撞，不根据Unity模型接触或错误文字猜测。

### 列表、筛选和步骤详情

复用伍老师TaskList、RunTypeChose、RunRecordItem、StepDetailPanel、StepCellItem及TaskCell/StepCell和图标。两种prefab放入`Assets/SimulationPlatform/Resources/Run/History`，保留其GUID；脚本和图标沿用来源GUID，没有重复复制到原Prefab目录。

MainScene有大量无关YAML变化，因此没有替换或直接合并场景。新增RunHistoryPanel在点击项目“记录”时按需创建界面并绑定来源组件。继承中文字体、垂直滚动和窄屏横向拖动，列表每页50条，支持全部/仿真（含离线）/真实运行筛选、刷新、上一页/下一页、详情和返回。步骤详情首行显示完整运行摘要及报告目录。

没有实际采集的力矩/角度显示“未采集”，不把默认0当测量。现有Python状态接口没有本轮所需的完整逐步骤实测结果，所以没有编造实测数值或改动同学算法。历史详情仅查看文本，不会驱动设备重放。

## 明确保留、未迁入的内容

逐字节核对17个核心文件与集成前测试标签相同：Gateway、Commissioning、Feedback、MotionDiagnostics、FieldWorkflow/Sequence、底盘完成确认、碰撞生成/保存、IK、MainScene/RunScene、ProjectSettings和Python接触/客户端/算法/延迟工具。

已有统一通信、单一控制权、停止确认、实际到位判据、原节奏限制、末端测量、碰撞持久化、实时同步及现场安全许可均保留。涉及控制入口的旧文件只加记录或记录包装；ArmController仅在已有IK失败分支补上记录通知，不改规划或运动。

未迁入无调用闭环的SelectedObjects/SceneModel.Time结构，也未覆盖未使用的材质和模型列表外观资源。没有新增所有设备的原始报文/寄存器全量追踪，没有增加轨迹动画回放，没有修复本轮testdata中的机械臂直连延迟测试断线；这些不属于此次日志/历史迁移。

## 验证

- RunHistoryHarness：25项，使用项目实际LiteDB DLL及生产RunRecorder/数据模型，覆盖快照、跨项目隔离、迟到回调、失败优先、开始即持久化、分类、级联删除、IO失败/重试和参数日志合并。
- OperationLogHarness：7项，覆盖首次目录失败不抛初始化异常、恢复写入、新分类、旧枚举值和文件信息。
- FieldRobotHarness：77项（含新鲜/过期碰撞分类3项）；FieldWorkflowHarness：15项；ChassisHarness：13项。
- Python unittest：39项，其中38通过、1项默认长时测试跳过。合计175项无硬件回归通过，另1项跳过。
- Unity 2022.3.62f2c1批处理编译和WulaoshiMigrationValidation预览场景检查：资源、绑定、中文字体、窄屏滚动、类型/结果、详情和返回通过；验证不进入Play、不访问真实项目DB或设备。
- git diff --check通过。未做真实机器人运动、工控机部署、渲染截图或全套人工GUI验收。

## 操作及人工验收

1. Editor等待脚本导入。进入一个测试项目，正常执行少量仿真，返回项目管理后点击该项目“记录”。无需手动挂载新组件。
2. 确认新增记录的时间/项目/参数/结果；筛选仿真，打开详情，再返回记录和项目列表。无记录时应显示0条而不是报错。
3. 用现有全流程离线验证入口跑一轮；应显示“离线模拟”，不能出现在真实运行筛选里；未采集力矩/角度不得显示成0。
4. 测一次仿真停止和一个可控规划失败，确认不会误记成功；关闭软件后重开，再确认已结束历史仍在。
5. 检查实际屏幕字号、滚动和长错误说明是否可读。记录页是运行时生成的，未写入MainScene；预览验证不能代替这一步。
6. Linux打包完整拷贝新构建目录；若替换旧程序目录，备份并保留旧Data/Operations与Data/Runs，切勿用空Data覆盖历史。现场仍按已有FIELD_COMMISSIONING分阶段验收。

全部通过后再提交integration分支，合并到release/software-v1.0并新打标签；不得移动已上传的测试标签去指向迁移代码。
