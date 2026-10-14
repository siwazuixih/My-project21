# Dobot TCP/IP V4.6.0 对照审查（2026-09-16）

> 后续状态：用户授权后已实施协议/完成确认修复，见WORKLOG同日“TCP审查修复”和
> FIELD_COMMISSIONING_2026-09-16.md部署说明。下文保留修复前审查证据；
> “未改代码”指审查当时。ServoP连续轨迹设计与硬件验收仍未完成，不等于实物安全认证。

## 范围和结论

依据用户提供的《Dobot TCP_IP二次开发接口文档V4.6.0_2024.12.26_cn.pdf》，
路径 `/data/SCU/xiangmu/chengfei/资料/技术文档/`。以下页码为文档印刷页码。
审查本地21.5源码，并检查RunScene按钮绑定、Python接触适配、测试替身；未连接设备。
这是审查记录，不是修复交付：本轮仅新增本报告、更新记录，没有改运行代码/场景/构建包。
保留现有未提交修改（包括用户的面板位置和字体资源）；不据本地源码推断工控机包完全一致。

结论：SpeedFactor有确定的参数类型错误；另有工具队列确认、反馈时序、传感器有效性、
细小位移到位判断等问题。不能只修SpeedFactor就宣称接触装配流程已可靠。

## A. 修复优先项

### A1. SpeedFactor必须用整数，当前现场接口用F6输出（确定错误）

- 文档p18：`SpeedFactor(ratio)`，ratio为int，范围[1,100]。
- `Assets/DobotController.Commissioning.cs:88`调用Number；Gateway.cs:360的Number用F6，
  实际发出`SpeedFactor(1.000000)`。用户现场回执-30001与p128的参数类型错误吻合。
- `FieldCommissioningSettings.robotSpeedPercent`为float，校验仅限范围[1,10]，允许小数。
- 修复应在配置层明确整数百分比、拒绝不支持的小数，并单独整数序列化。
  不要全局更改Number为整数，否则会损坏位姿/关节角/时间的精度。
- 旧`DobotController.SetRobotSpeed`先转int，整数格式符合文档，但不能用旧async void接口
  直接替代新await流程，丢掉应答失败传播和阶段控制。

### A2. Tool是队列指令，ACK后立即读GetPose存在坐标系交接竞争（高优先）

- 文档p17、p21：Tool(index)是队列指令，返回ResultID；p104解释ACK只表示接受。
- Gateway.cs:215等待Tool应答后就返回acquired，后续GetPose()是立即指令（p39）。
  尚未确认队列执行到了Tool，也未核对反馈1013的Tool索引。
- GetPose()默认读取全局User/Tool；ServoP也使用全局User/Tool（p90）。即使TCP请求串行，
  也不能据此证明Tool队列已执行。旧工具的起始位姿可能参与随后新工具下的运动计算。
- 建议解析Tool的ResultID，结合队列ID、反馈Tool和新反馈确认生效再开放算法。
  同时锁定/核对User（1012），避免依赖未知的全局用户坐标系；Tool和User必须已标定。
  不是说默认GetPose()语法错误，而是缺少坐标系一致性保证。

### A3. 没有接入六维力传感器在线/异常标志（高优先）

- 文档p125：SixForceOnline位于1037，0离线、1在线、2异常。
- DobotController读取1029报警、1038碰撞及576处ActualTCPForce，但跳过1037。
  Gateway/Python保护只检查反馈年龄、模式、报警、碰撞及力数值，没有传感器在线条件。
- 有限且很小/全零的力不能单独证明传感器有效。文档没有承诺离线时力字段一定为NaN。
- 接触实物阶段应要求在线且数值有效，异常/掉线时停止；单纯关节空载测试是否要求传感器
  应单独定义，不要把零力模拟结果当实物传感器验收。
- 576处是“由六维力原始数据计算的TCP各轴受力”，不是偏移读错，也不是720处的电流估力。
  文档未在反馈表明确保证它与指定工具的GetForce(tool)逐分量完全一致；横向Y/Z判断的
  坐标系/补偿应核实。原始传感器值1304处也不能直接替代工具坐标系力。

### A4. 0.2mm到位容差大于0.1mm横向搜索步长（确定逻辑缺口）

- Gateway.positionToleranceMm默认0.2；servo_contact_cycle的search_step_mm固定0.1。
- MoveRelativeAsync在时长等待后仅计算XYZ距离，误差<=0.2即接受到位。
  数值上即使0.1mm目标尚未走到、仍处于起点，也满足该条件。
- 加上未验证运动ResultID，不能可靠证明这次微小搜索已经执行完毕。
- 应结合当前命令ID、命令后新反馈、实际到位/停止状态；容差应按实际设备精度和搜索步长
  一起设计，且补充姿态偏差检查（当前只检查XYZ，目标虽保持姿态仍需验证）。
  不能凭文档直接许诺更小容差一定可达，也不能只靠增加等待掩盖。

### A5. 到位/停止缺少本次命令及命令后反馈关联（高优先）

- 文档p80/p90-91：MovJ/ServoP返回ResultID；p104-105给出当前ID结合RobotMode=5的判断方法；
  p125的CurrentCommandId位于1112~1119。
- 当前运动只检查ACK、关节/位姿和mode，不解析ResultID，也不读取CurrentCommandId。
- StopOwnedRobotAsync发送Stop后，可能立即接受Stop之前、年龄仍<=250ms的mode=5缓存。
  这不能严格证明停止后的实际状态；存在交接时间窗口。
- 建议解析队列ID，并要求与本次执行相关的新反馈。Stop应单独做命令后反馈和静止确认，
  不应机械套用“Stop返回ResultID”（Stop返回{}，p12）。

## B. 需要一起处理/确认的问题

### B1. 默认反馈周期与250ms保护预算相近

- p4/p123：30004为8ms，30005为200ms；默认软件选择30005。
- Gateway/Commissioning/Python多处固定0.25s保护，只有约50ms额外余量，受Unity帧及网络抖动影响。
  当前属于可能误停/保护响应偏慢的风险，不是非法端口或已证实每次超时。
- 建议现场确认使用30004高频反馈，并同时处理积压；不能只换端口或粗暴扩大超时。
- 代码用本机解析时刻更新lastFeedbackUnixSeconds，没有读取机器人TimeStamp(32~39)/RunTime。
  主线程延迟后从TCP队列读出的旧包可能被记作“刚产生”；应区分接收新鲜度、机器人时间推进
  和本次命令后的样本。不要直接相减两台未同步机器的Unix时间。
- 文档对30006默认周期自身存在两处差异：p4说50ms，p123说1000ms，不能擅自选一个值。
  当前未默认用30006；如改用，应向厂家/现场确认实际配置。

### B2. ServoP的格式及控制语义

- p90：六个坐标double，t float，[0.004,3600]秒；aheadtime float，[20,100]，无单位；
  gain float，[200,1000]。当前t为0.05~5秒，数值在协议范围内。
- 当前aheadtime=50、gain=500使用整数文本。文档示例中的坐标也用整数文本，不能仅凭
  参数标float断言控制器一定拒绝整数；但建议按声明类型统一成50.0/500.0并做严格协议测试。
  这与已获真实-30001回执的SpeedFactor不能混为“同样已确认报错”。
- 文档明确ServoP不受全局速率影响。界面1%不是接触ServoP的限速保证；当前delta/t检查
  是目标平均速度约束，不是硬件瞬时速度和加速度保证。
- 文档建议33Hz/30ms循环，先进行速度规划。当前是每小段一条ServoP、等待、GetPose、Stop，
  不是连续33Hz跟踪。该用法不是根据文档即可判为语法非法，但平滑性/实际速度/力控行为未验收。
  不应未经规划直接改成高频下发；先明确分段点动还是轨迹跟随及设备能力。

### B3. 两个模拟专用命令还挂在RunScene按钮上

- DobotController.StartSimulatorFollowDemo/StopSimulatorFollowDemo发StartFollowDemo()/StopFollowDemo()。
- 全文没有这两个Dobot标准指令；RunScene.unity:16081、42339仍保留这两个方法的按钮绑定。
- 在真机器人上不能当作有效启动/停止命令；预期可能返回命令不存在，而非有效停止。
  需隔离为模拟后端专用或核实并改绑正确功能，尤其不能让“停止演示”被误认为真机Stop。
- RealRobotFollower本身仅更新仿真执行器，不发送这两个命令，两者应明确区分。

### B4. 旧MovJ受系统小数格式影响

- DobotController.cs:204使用j.ToString("F2")，未指定InvariantCulture。
  采用逗号小数点的系统区域下，坐标小数逗号会与协议参数分隔符混淆。
- 新现场路径使用Number/F6+InvariantCulture，不存在这项区域格式问题（SpeedFactor类型另算）。
- 旧TestMoveBtnClick解析失败填0也应拒绝，而非补零运动；属于输入校验问题，不是协议允许行为。

### B5. 控制模式请求及应答验证

- RequestControl()语法正确，p7要求未上电或下使能（非暂停/松抱闸）才可切换TCP模式。
  Connect建立socket并上报“连接成功”不代表RequestControl成功；当前async void失败仅日志提示。
  应区分连接建立/控制权确认/使能就绪，失败不继续使能和运动步骤。
- ExchangeDashboardAsync串行化、非零报错、超时断开避免迟到包误用的方向正确。
  目前只检查开头0,，未验证回显命令，也不提取通用错误码/队列ID。协议p4有回显命令字段。
  需结构化解析完整分号终止回执，错误信息保留原文。当前读到换行也结束，健壮性可加强。

## C. 已核对匹配的内容（不应无依据修改）

| 项目 | 对照结果 |
| --- | --- |
| RequestControl/PowerOn/ClearError/EnableRobot/Stop无参调用 | 文档有对应原型；无参EnableRobot合法，但沿用既有负载配置，仍须现场核实 |
| MovJ(joint={六轴角},cp=0) | p79-80形式正确，cp在[0,100]内；旧cp=50亦合法，非严格停点 |
| GetPose() | p39允许User和Tool都省略；若指定必须同时传二者 |
| RelPointTool(pose={六值},{六偏移}) | p102形式正确；属于计算偏移，不是运动本身 |
| Tool(int)/GetForce(int) | 工具编号0~50符合该版文档；编号存在且已标定另需确认 |
| GetForce及GetPose返回六值 | 当前按首个{}解析6个有限数值与格式匹配；不能拿这个解析器读队列ID |
| 29999/30005与1440字节 | 端口和包长度合法，完整读取循环能处理TCP拆包 |
| 反馈大小0、Magic48、Mode24、SpeedScaling64 | 与p123一致；Magic前导0不影响数值比较 |
| QActual432、QDActual480、ActualTCPForce576、ToolVectorActual624 | 与p124一致 |
| VelocityRatio1016、ErrorStatus1029、CollisionState1038 | 与p124-125一致 |
| little-endian反馈 | 文档规定小端；BitConverter在目标常见x86 Linux符合，非跨架构显式保证 |

## D. 测试漏洞及建议验收顺序

FieldRobotHarness替身Number使用普通ToString，生产Number却用F6；替身Dashboard无协议类型
校验且直接更新目标关节。因此原“56项通过”不能证明真实Dobot协议兼容，尤其漏掉这次错误。

下一轮修复测试应：

1. 直接复用生产命令构造/解析代码，模拟控制器按本PDF严格检查int/float/命名参数、回执。
2. 覆盖Tool延迟执行、旧mode5、旧队列ID、重复/积压反馈、SixForceOnline=0/2、错误回显。
3. 覆盖0.1mm目标未移动不能过关、非零姿态误差、Stop后必须有新反馈，250ms边界及抖动。
4. 覆盖区域小数格式、真实命令与演示命令隔离；不是仅把测试预期改成错误格式让测试过关。
5. 工控机先只读核实协议/固件、工具和User、传感器，再在现场监护下小角度/无接触验证；
   接触相关阻断项修复和验收前暂停自动卡入/拧紧/退出。协议对照不能保证不存在其他实现缺陷。

本轮未修改运行代码，未打包或发布；现场现有软件仍包含以上问题。
