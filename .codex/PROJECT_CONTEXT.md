# Project Context

### 2026-11-12 合入新管理UI前的GitHub备份教学（未代执行上传）

- 用户要求继续手动操作并理解每条命令。当前已在release/software-v1.0，HEAD及只读核对的GitHub实际同名分支均为e9062176009174a2a4eb813f09cd4d405497ed20；v1.12-test.20261028已经存在，不能继续照10月28日教程切分支/重复打旧标签。
- 新教程PUBLISH_2026-11-12.md，配套RELEASE_v1.12-test.20261112.md；建议新标签v1.12-test.20261112（核对时本地和远端均不存在）。备份对象为当前未合入根SimulationPlatform新UI的开发版，包含10月30日修复，不是仅重推旧e906217。
- 当前业务修改及新增文件均在.codex/Assets/ExternalCode/tests；教程指定这些目录暂存，保留ExternalCode原版脚本参考，因为tests/test_contact_upgrade.py会读取这些目录做算法对照。现有忽略的大模型/字体不在源码备份内，测试bin/obj及运行数据继续忽略。
- 仅编写教学与记录、只读远端核对和add dry-run；未实际暂存/提交/fetch/推送/打标签/发布Release，未重跑编译或设备测试。源码阶段备份不改Player内部1.9.0，不表示正式安装包版本统一或实机验收完成。用户完成远端分支/标签与本地HEAD哈希一致核对后再继续UI集成。

### 2026-10-30 现场回归修复：普通仿真控制权、V2预算分阶段、预检留证

- 用户实测确认“仿真底盘动、仿真机械臂不动”由实时反馈跟随开启导致。Simulation新增PrepareSimulationPlayback，在普通开始仿真/切回仿真/全流程模拟启用时通过DisableRealRobotFollow和SetExternalControlActive(false)完整释放模型，不能仅followEnabled=false；实物/离线任务占用则拒绝。加载项目不在默认仿真页自动恢复实物跟随。只操作模型，不断端口/发实物运动。Arm/Mission增加停靠、手动等待、轨迹播放及冲突日志；不改IK/BIT*/底盘路径或关节映射。
- V2旧JSON插入20mm/30s小于有效0.4mm/s理论50s，截图中的下发/插入是预检拒绝。Settings.Validate保留默认严格兼容，新增contactBudget=false用于保存/连接/服务配置/低层关节流程；Field按所选模式单/双方向ValidateContactBudget，Python真实start在任何副作用前同样按workflow检查。仅J1/J2和只读恢复不查未用预算，实际速度/力/反馈/方向/接触互锁不变。
- 面板显示理论时间/当前预算及显式“补足V2预算并保存”按钮：仅增加到max(已有值,90,ceil(行程/有效速度+30))，超过600拒绝，不改行程/速度/力限/方向，取消本次许可。没有自动改现场JSON；90s不是20mm完成保证，查询/停止/减载/搜索开销仍需实测。预检失败现在创建报告保存实际草稿/原因，preflightOnly不会混用上次周期失败或结束其他任务诊断。指南已更新。
- 无设备测试：Python125运行124通过1跳过；C#模型跟随/缓存播放10、现场配置编排120、机器人129、离线22、日志23，共304通过；主+Editor0错误68警告。实际Python+生产网关+回环假设备冒烟新增旧预算配置/插入无副作用拒绝/独立退出/旧预算恢复，通过原完整周期/持久互锁。未生产Play/实物/打包/提交；下一步完整构建后手验模型模式切换、新预算按钮、预检报告，再监护短行程。

### 2026-10-30 柔顺V2已迁入源码（接口5，未实物/GUI/打包验收）

- 用户批准后，当前改为contact-compliant-v2-20261029/接口5。两向统一调用V2优化类：卡入≤0.4mm/s/0.10mm、退出≤0.6mm/s/0.15mm、连续载荷减速/有载≤0.05mm、12/18N轴向软停、8轮有改善搜索与载荷转移拒绝、实测进度/0.30mm偏移。保留Unity独占IO、共享client/cancel、停止/释放失败不成功及10-30空值/IO诊断修复；6个核心数值方法AST对V2原件一致。未改电批28Nm判定/协议或粗定位/底盘/升降/视觉节奏。
- 新contact_session.py固定使用C#传入persistentDataPath/contact-session.json，独占创建/原子有界记录身份/配置/阶段/起点/参考轴/实测/受力，覆盖插入→等待→拧紧→退出。完整安全退出/停止/释放后才清；仅插入成功/异常/取消/重启保留。模拟不触碰真实记录；遗留记录拒绝新实物目标/偏移/粗定位、手动电批正反转及快捷键。仅插入后不能盲点仅退出，先人工解除接触并复核。
- 现场面板增加人工解除接触复核：要求无流程占用、同一设备/User/Tool、旧操作进程不在、机器人停止及只读全局/原始力复核、电批新鲜无错误低速稳定0.3s。只停止/检查，不运动/切工具/使能/清零/续跑；原本机停止解锁不清持久记录。Unity用GetForce()读前后核对实际Tool/传感器/新鲜反馈，contact_probe不申请运动租约。带参API也合法，此次是兼容新现场用法，不做零力/坐标回退。
- 新建插入预算90s，旧JSON不自动覆盖；按有效≤0.4速度校验最低理论预算，移除不适用的未封顶step/speed时长拒绝（实际V2受速度×0.25s约束）。面板/日志显示有效参数；90s不保证20mm，旧退出查询调度开销未直接优化。服务启动/附加/准备/轮询检查接口5/策略/持久目录；预检包含新模块，构建复制全部顶层.py及新指南。见TIGHTENING_V2_MIGRATION_2026-10-30.md，原操作指南顶部已同步。
- 隔离服务的状态/报告也不读取真实接触文件；实物服务内的模拟动作不修改/清除记录，状态仍可显示真实互锁。Python122运行121通过1跳过（新增29）；C#284通过（Robot129/Field110/Offline22/Console23），主+Editor0错误68警告，预检/diff通过，原件16哈希一致。实际进程+生产网关+本机假机器人/电批端到端完整周期、仅插入/重启互锁/显式只读恢复通过；另假电批正反转/停转/CSV和隔离冒烟通过。未连实物/生产Play/实际Linux打包/提交推送。下一步先安全关闭旧服务，导入验收面板再完整构建；核对真实工具/两类力及短行程，不把离线结果当工艺验收。

### 2026-10-30 同学柔顺V2已审查（未迁入运行版）

- 新包ExternalCode/柔顺卡入与退出_V2完整程序及使用方法_20261029/ubuntu20_04_compliant_v2_20261029，compliant-v2-20261029。核心改动是两向共用实测小步：卡入≤0.4mm/s及0.10mm、退出≤0.6mm/s及0.15mm、连续载荷减速/有载≤0.05mm、卡入/退出轴向软停12/18N、最多八轮有改善搜索及拒绝向轴向/力矩转移、0.30mm偏移、跨进程contact_session与只读probe。电批28Nm判定/帧协议/滤波相对20261023原件未变，主文件仅行程显式确认/预算联动更新。完整对照见TIGHTENING_V2_REVIEW_2026-10-30.md。
- V2无参GetForce并读前后核对实际Tool；用户V4.6PDF与官方SDK同时允许带工具号和无参，不能认定当前GetForce(tool)违反API，现场兼容须只读确认。软件适配仍应Unity独占机器人、ContactCycle唯一调度，不照搬原件真实socket/子进程/GUI；现有search_rounds=2不改会让V2八轮无效，旧插入30s不适配20mm/≤0.4mm/s，原退出90s查询调度问题也没有被V2解决。
- 新原件stop吞异常，假IO复现Stop失败仍completed且guard删除异常状态；blocked状态复位不检查仍存活PID，也已复现。必须保留现有接口4版停止失败不成功/取消分离/null契约/IO诊断，并把持久接触状态接完整装配周期而非仅CLI单阶段；记录放固定persistentDataPath、人工解除接触与停止复核后才能清。随包16哈希一致，Ubuntu/Python3.8.10离线46全过；未连设备、改运行源码/JSON、Play/打包/提交。当前仍运行contact-20261023/接口4，下一步待用户批准算法迁移。

### 2026-10-30 拧紧适配已修空值/取消冲突并补反馈证据（接口4，未打包/实机验收）

- 用户继续批准修拧紧程序，保留上一阶段JSON连接来源统一。UnityRobotClient无preview时省略expected_target而非发null；有六维preview仍发送/消费并接受现有网关长度、有限值与0.01漂移校验。真实Unity2022.3.62f2c1独立无设备DTO探针6项复核（省略为null、旧null变空、畸形数组仍拒绝、六值和运动标志保留）；没有放宽C#网关或改同学数值运动算法。
- 通用hard_stop_driver及反转前quick_stop_for_home/反转后fast_stop_after_home_reverse不再设置stop_requested，也不清该事件；明确停止/故障中止保留锁存和发送边界拒绝正反转，显式新操作才按原入口清锁。组合验证卡入前hold→正转、正常结果停止→新鲜停转确认、反转回位不再误当取消；准备期间用户停止仍禁止晚到正转。
- 电批增加独立tool_io_lock下的连接级有界计数/证据：connection_id、rx_bytes、feedback_frames、framing_rejects/parse_rejects/filter_rejects、last_rx_hex前96字节、last_tx_hex前32字节、last_error最多240字符及收字节年龄。状态与失败报告带tool_io，连接/断线及约1Hz[ToolTrace]进入Unity主线程/现场报告日志，不受机械臂周期过滤隐藏。记录不增加硬件查询/改变协议或伪造live_speed；真实零反馈根因仍待现场新报告，不能拿收字节/缺省device_error=-1当停转/真实故障。
- ContactCycle接口升4、策略contact-20261023不变；Servo附加到现有服务/准备/启动和轮询检查4，拒绝旧接口3混用，须先通过旧软件安全停服务再完整部署。停转、力限、取消、方向、超时和机器人独占IO保留；退出20mm/90s耗尽的查询/调度节奏本轮未改，不能直接以旧20mm全周期验收。
- Python完整93运行92通过1长时跳过（含新增16，原缺陷11项子集修前6失败/修后通过）；C#针对性FieldRobot121/Offline22/Console23共166通过；真实UnityJSON6通过；实际进程＋本机假41字节电批冒烟验证正转OK/新鲜停转/CSV、反转释放、手动停止及坏CRC计数，隔离进程冒烟验证6实物命令拒绝/完成取消/零假电批连接。主+Editor独立编译0错误64警告、diff通过，原件SHA256不变。没有连接真实设备/生产Play或打包/提交推送；操作与改造指南顶部已同步，下一步先安全关闭旧服务、完整构建新目录，核对端点/模式/有效反馈后监护短距离逐项验收。

### 2026-10-30 机械臂两个正式连接入口统一JSON（仅连接项修复，未打包/实机验收）

- 用户仅批准先修端口来源。DeviceConnectionPanel机械臂连接/补连与FieldCommissioningPanel连接机械臂统一读取persistentDataPath/field-commissioning.json已保存的robotIp/dashboardPort/feedbackPort。Settings增加统一FileName/ReadRobotConnectionFile/端点专用校验，正式连接不依赖Python/算法参数校验；缺文件需先保存，解析/端点无效拒绝，不回退场景30005，也不写死30004。不改Inspector/场景旧默认或其他底盘/电缸来源。
- Device面板机械臂IP只读、显示JSON端口，打开/重读不修改控制器或建连，每次显式连接再读文件；底盘/升降当前地址编辑仍保留。Field连接若草稿连接参数与文件不同，先提示保存/从外部重读；新增“从文件重新读取JSON（覆盖未保存内容）”，成功清旧安全确认、不写文件/换连接，任务/机械臂连接在途禁止重载。外部改文件不会改变已冻结的运行配置或现有socket。
- Dobot.ConfigureFieldConnection共用端点应用/占用保护，使用实际控制+反馈socket比较；有任一不同端点必须先人工断开，部分连接也检查，不自动热切或关闭通道。FieldConnectedTo复用DeviceTcpConnection规范IP匹配，错误包含期望/实际双端点；原ConnectAsync/取消/关闭双端口/请求控制/反馈解析/模式/停止保护和运动包不变。真实接触仍要求30004，读JSON不等于允许任意端口接触。
- 回归342通过：DeviceUI81（生产读取器/面板＋假设备）、FieldRobot121（含13项新生产配置器/实际loopback端点）、FieldWorkflow107、Lifecycle33。独立主+Editor编译0错误55项既有/序列化警告，输出/tmp/robot-json-build-*，diff检查通过；两套socket测试经批准只访问自有127.0.0.1，不连接硬件。操作指南顶部已更新；未Play/真实设备/打包/提交推送。待人工验收JSON读取/新按钮布局和双端口实际显示。此连接阶段尚未修卡入/取消问题；后续修复见上，零反馈根因及退出节奏仍待现场核对。

### 2026-10-30 新实测发现适配缺陷（首次只诊断；连接项后续修复见上）

- 10-29的12份新报告已逐项核对，详见TESTDATA_DIAGNOSIS_2026-10-30.md。三次仅卡入均在ServoP前被“预计算目标必须为有限六维数值”挡住：客户端preview缺失时显式发送expected_target:null，真实Unity2022.3.62f2c1 JsonUtility将null数组变成长度0，C#只按!=null检查误拒。/tmp独立Unity探针已复现；原Harness用System.Text.Json未覆盖该差异，不可归咎同学运动算法。
- 两次SemiSingle实际反馈30005、现场配置30004，mode5且反馈有效，首因为连接配置不一致而非模式/运动拒绝；新连接面板沿控制器/场景旧30005，未与现场JSON统一。另一次InsertOnly的mode6是实际拖拽，禁止自动运动正确。
- 一次Contact卡入前电批停转门槛失败：TCP已连但samples0/feedback_time0/live_speed空，未有有效反馈；原始电批stdout/帧未包含于现场run.log，底层原因待取Player.log/原始收发，不能据缺省device_error=-1断言设备报警。复核还发现当前通用hard_stop_driver设置stop_requested而新原件没有，正常复位/hold/结果停止误当用户取消，假IO组合执行证明启动及新鲜停转确认均被自锁，原件不复现；虽不是日志首因，后续必须一并修。
- 退出分别触发受力保护或90s耗尽，最后70步实走约10.586mm/20mm，相邻步起点中位1.284s而单段记录约0.3s，多轮串行查询/调度等开销待优化，不等于纯TCP延迟；不能仅放宽力/超时。首次诊断时以上均未修；随后用户只批准连接来源修复，最新状态见上，其他适配契约/停止语义、电批证据和退出预算仍待后续授权处理；诊断未接设备/改配置/打包提交。

### 2026-10-29 新拧紧适配源码完成（10-28开始；未打包/GUI/实机验收）

- 当前运行源码已迁移完整20261023基座并叠加退出优化，替代下方“仅审查/未集成”状态。策略contact-20261023、Python接口3，+X卡入/-X退出；Python算法不直连机械臂，Unity继续独占机器人，电批仍由Python连接。原件/补丁文件SHA256不变；实现及操作见TIGHTENING_ADAPTATION_2026-10-28.md，FIELD指南顶部优先于旧版接触说明。
- 新Field配置positiveXInsertionConfirmed默认false，面板显式方向框后保存/重做本次确认；旧JSON不自动授权反向运动。Parse通过new+FromJsonOverwrite保留新增默认值，拒绝空/null/数组。新exitStageTimeoutSeconds默认90；contactStageTimeoutSeconds对应卡入，完整周期预算联动。contactLateralTrigger/Clear/Slow默认18/12/12，原lateralLimitN硬上限20不放宽；明确解除<触发<min(26,硬限)，原加权评价非纯N。originalContactTiming保留兼容，接触固定用同学时长节奏；新许可不改变粗定位/返程停止规则。
- cr10af_hybrid_x保留新版EMA/滞回/限次搜索/自适应步长；cr10af_exit_optimized保留小步/减载/实测进度与跟踪保护，IO/共享会话改Unity适配。ContactCycle唯一编排；电批结果线程先停止/确认再保存，前后停转门槛使用新鲜live_speed、run_id和result_processing/stop_confirmed，不拿曲线最后speed或OK标签当停稳。取消/通信/设备NG/超时不退出；滑牙/卡滞即使退出仍保留失败、不进下一目标。运动写边界仍拒绝停止锁存后的正反转，入队被拒不假成功。
- 网关新增有权限的只读relative_target，移动可核对预计算目标漂移≤0.01；不是任意原始命令代理。段末停止和全局锁存分开，收尾错误不吞成成功；服务、网关、配置/周期回执核对版本。服务停止/结果处理中不直接关闭Python。旧GetForce/位姿/ServoP通讯沿现有路径，没有新猜测硬件API。
- C#取本次cycle_report一次，将计划/实际位移/搜索/载荷结果通过RunDataSession.Evidence写commands.jsonl的algorithm_result事件，旧会话不串新会话。电批CSV/PNG由SERVO_LOG_DIR落persistentDataPath/ServoLogs，报告带路径，仍不在运行数据ZIP白名单内。Linux构建自动复制顶层新模块，并附TIGHTENING_ADAPTATION.md；预检仅编译包含新模块，不导入启动电批服务。
- 全无设备回归620通过+1长时跳过：DeviceUI63/Lifecycle33/Chassis27/Manual57/JogUI28/Robot108/Field107/Offline22/Data38/History32/Console22/Operation7/Python76（77运行/1跳过）；另实际隔离子进程冒烟验证完成、取消、六实物命令拒绝、零假电批连接。独立主+Editor编译0错误55项警告，8个数值方法AST对原件不变，四Python模块字节码全局符号检查通过。没有实物或Unity渲染验收、打包/提交/推送；下一步先验收新方向框/布局，再完整新目录构建、监护短距离测试，不能照旧版方向或默认20mm盲执行。

### 2026-10-28 同学完整拧紧新基座与退出补丁已核对（仅审查，未集成）

- 新输入为ExternalCode/程序和使用文档_X方向修订版_192.168.192.19/ubuntu20_04及先前退出受力优化补丁_20261023。两基础入口与补丁纯文本变换匹配，缺参数/基座问题已解决；完整包本身尚未应用退出补丁。原件与临时组合副本离线测试通过（组合共26=原11+补丁15），没有硬件或Unity适配验收。详情见TIGHTENING_EXIT_PATCH_REVIEW_2026-10-28.md文末。
- **新原件明确+X卡入/-X退出，当前项目运行版仍-X卡入/+X退出，尚未改方向或现场JSON。** 实施须同步ContactCycle/单测/模拟/测量参考轴/提示与配置回执，旧确认不能静默授权相反方向。实际Tool方向仍需现场核对。
- 不再仅迁移退出：完整基座还含EMA/滞回/分层阈值/自适应段长/限次搜索、接收持续运行的后台结果处理、动作前后电批停转确认、看门狗/单调时钟、拆包与保存修复。应保留同学算法并适配Unity独占通信及现有9100/9101服务、19120网关/权限/停止锁定，ContactCycle保持唯一编排，避免原自动周期二次运动。新原件无本机桥接/模拟隔离且无参只check退出，不能直接覆盖运行文件。
- 现有lateralLimitN旧硬停与新版搜索触发/解除/硬停必须显式分层匹配；原件24/18/26不自动放宽现场已有20限值。结果OK/NG锁存与后台停止完成/新鲜停转证明分开、保留NG结果；monotonic不可与旧桥time.time混算。下一步需用户批准适配并补契约测试，当前仅审查文档更新，无实物/打包/提交。

### 2026-10-28 正式设备连接面板（用户要求现场测试面板原样保留）

- 新DeviceConnectionPanel由Simulation.Awake运行时挂载，Start在StatusReal内定位ConnectBtn，用新的ButtonClickedEvent替换整个事件（RemoveAllListeners不能移除持久化三设备回调），底部靠右152×38按钮“设备连接”，打开不联网。当前实例隐藏ConnectBaseBtn/DisconnectBtn/PowerOnBtn/EnableBtn/ResetBaseBtn，移入新面板分组；不编辑Scene/Prefab绑定，原开始运行/手动电批和动态日志布局保留。
- 机械臂/底盘/电缸可独立连接/断开，另有明确范围的三设备并行连接/逐项断开，失败独立显示。复用原ConnectAsync/ConnectAllAsync与断开/停止入口，不另建socket、不自动上电/使能/重定位/启动Python/相机。原右侧IP改只读镜像（SetTextWithoutNotify），面板读取当前控制器字段，只在显式连接时应用经校验的数字IP；活动端点不同必须先断开。ViewConnecting/ViewHasConnection/ViewEndpointsAligned/ViewConnectionDescription等只读属性显示实际远端及已有反馈，DeviceTcpConnection.Describe规范IPv4-mapped地址，不新增硬件查询。
- 现场FieldCommissioningPanel、其ChassisJog partial、Settings及用户现场JSON完全不改；旧连接/单测/许可/停止原样保留。新面板不加载/保存/覆盖JSON。端口显示沿用当前控制器设置；正式RunAsync仍核对实际端点与现场本次配置，不一致拒绝，不自动切地址。连接中正式StartRun新增等待提示，现场测试入口行为不变；跨入口配置在连接过程中变化会显示核对提示而不回写旧值。
- 任务/算法/底盘手动/停止核对占用时禁止改地址、连接及设备准备；断开先走已有停止/提示，不直接切断通道，连接中断开可取消，revision隔离迟到提示。确认/权限不因隐藏界面或重连自动恢复；机械臂上电/使能独立人工许可（使能沿用清错/速度3%），底盘2002自动重定位在高级区并单独许可，不声称完成/回原点。只读状态是最近已知端点/已有反馈，不是安全认证。
- 相机动态按钮空闲文字改开启/关闭相机预览；拧紧动态按钮空闲文字改启动/关闭拧紧服务并适配文字缩放，正式阶段继续/确认底盘/确认并拧紧及手动正反转/停止行为不变。指南FIELD_COMMISSIONING开头已更新，REAL_CONNECTION_BUTTON_AUDIT添加用户修订提示，原审查中“改现场入口”建议不再适用。待Unity布局/不同分辨率及实物验收，旧包需完整重构建，无新Inspector绑定。
- 无设备回归590通过+1长时跳过（新DeviceUI63、Lifecycle33、Chassis27、Manual57、JogUI28、Robot108、Field101、Offline22、Data37、History32、Console22、Operation7、Python53）；主+Editor独立编译0错误55项既有警告。现场core/Jog partial/Settings SHA256与本阶段修改前一致；meta唯一、diff检查通过，无打包/提交推送/实物或Unity Play操作。

### 2026-10-28 底盘手动定距/定角点动（源码实现，待GUI/实物验收）

- 用户确认实车支持横移，选择点击一次固定距离/角度，不做按住连续遥控。真实运行页左上角x170/y115增加“底盘手动”，与x10现场参数入口并列/面板互斥；流程状态条移到x330避免遮挡。面板默认收起，参数只在当前面板实例内保留，不改现场JSON/场景；红色停止在滚动区外，使用车体方向，不随镜头变化。
- ChassisJogCommand不可变生成3055/3056，界面mm/deg转m/rad，dist/angle正值，vx正前负后、vy正左负右、vw正逆时针，mode=0。默认50mm/50mm-s、5°/5°-s；软件范围1～500mm、10～100mm-s、0.1～45°、1～15°-s，不是硬件额定/安全认证。原规划路径负vx/旋转映射没有改。
- RobokitController.ManualJog复用当前IP的状态/导航通道、唯一匹配请求和原1020 RUNNING→COMPLETED，不创建新SDK/线程或自动重发。ManualJogLocked含在途/需人工核对/停止及恢复中；单点击不排队。短动作漏RUNNING、错误/手动停止保留锁，人工观察停稳后3003应答+1020无活动任务再解锁，绝不以应答或估时当物理停车保证。连接丢失可在无运动/停止在途时人工重连，但不自动清锁。
- FieldPanel.MotionLocked和Commander.AnyTaskRunning纳入手动锁；NonManualTaskRunning保留原运行条件以区分占用，自动/离线/原下发与手动互斥。手动动作撤销原现场确认，锁住IP/模式等输入，成功或明确恢复后归还；挪车后须重新核对规划起点，不自动改模型位姿或旧规划。Selected project的手动档案沿用RunDataCapture入口/阶段；停止即使无连接也先取消本机待续动作，普通Player退出仍经已有锁定停止入口，强杀/Editor停止不保证。
- 操作详见FIELD_COMMISSIONING_2026-09-16.md开头。本阶段无设备假服务/处理器验证，不代表GUI/实物验收；旧包需完整重构建，无须Inspector新绑定。

### 2026-10-28 运行指令/反馈独立文本档案（源码和无设备验证完成，未打包/GUI验收）

- RunDataSession/RunDataCapture负责有界后台文本追加，不控制硬件；RunDataFiles负责白名单ZIP。RunRecorder在Begin/Finish跟随运行创建/结束档案，RunRecord仅新增DataDirectory/DataError，不塞高频行到DB。现场入口先建原CommissioningReports再将同目录传入历史；其他运行用persistentDataPath/RunData/<runId>。完整现场配置另传metadata，不膨胀RealParamsSummary/列表；SimulationParam是属性，用既有LiteDB映射序列化，不能用Unity JsonUtility得到空对象。
- metadata.json schema=device-run-data-v1，含项目/运行ID、来源/模式/simulated、版本/buildId、快照、行数/丢失/结束时在途、完整性。commands.jsonl记录实际Dashboard/底盘/Modbus入口，区分attempt/write/library-return/reply/accepted/failed；机械臂排队ID、底盘流水号/hex及实际socket端点，不把查询值误当队列ID；NModbus不伪造OS写入时刻/原响应。原trajectory-plan、motion.csv/停止诊断保留，accepted仍非到位/停稳。
- Dobot现有状态上限10Hz记六轴/速度/末端/User/Tool/模式/设备时间/年龄/序号/new_sample/active_command_id；电缸Update不发查询，上限5Hz记已有三寄存器读数，各自成功时刻/有效性，无效留空不假0，转矩保留raw单位。应用时间不是硬件同步/网络延迟；采样非全量包，模拟只记参数/阶段不混实物。
- 每秒后台刷新、文件目标20MiB完整行间分卷/CSV每卷表头、队列4096行及2Mi字符，满/写失败/关闭时在途明确标不完整，不等待磁盘或恢复旧运动。参数/UTC和单调时间、目标/阶段关联；旧异步句柄持有旧会话，不串新runId。手动首次普通通信在已选项目下自动建明确调试记录，主动断开/销毁/正式任务替换/换项目结束；无会话孤立停止不先初始化DB。原摘要数据库仍用原保存方式，数据失败不覆盖任务首因。
- 运行详情动态添加打开目录/导出ZIP/保存状态，不改Prefab/Scene；小索引后台读取，核对schema/runId，活动/未刷盘/异常退出不假完整导出，旧记录/目录缺失兼容。ZIP仅新档案及分卷、排除其他文件/符号链接、重复导出新文件，输出persistentDataPath/RunDataExports；不含原motion.csv/run.log，异常仍需整份报告。Linux构建附带RUN_DATA_STORAGE.md。
- 回归433通过+1跳过（Data37/History32/Robot108/Chassis26/Lifecycle25/Field101/Offline22/Operation7/Console22/Python53）；仅临时文件/自有loopback假服务。主+Editor独立编译0错误，未Play/GUI或实机。指南RUN_DATA_STORAGE_2026-10-28.md及FIELD_COMMISSIONING已更新；未改场景/版本/持久JSON/本轮Python算法、旧包、Git提交/标签。下一步退出Play导入、验证历史入口/旧记录/中文路径/ZIP及模拟隔离，再完整Linux新目录构建和监护单目标对照；硬件急停优先，强杀/断电可丢尾部，不以日志当安全认证。

### 2026-10-28 通信连接/断开简化改进（源码/无设备回归完成，未打包/实机验收）

- 本阶段覆盖下方“仅审查/尚未改连接”的历史状态；说明见COMMUNICATION_IMPROVEMENTS_2026-10-28.md，旧CONNECTION_LIFECYCLE_AUDIT保留为修改前证据。继续release/software-v1.0/e906217，保留前轮RuntimeConsole过滤与记录，未改索引/提交/标签/远端。
- DeviceTcpConnection用Unity2022兼容ConnectAsync+WhenAny+关闭取消资源，各设备TCP默认3秒；保留原public void/async void UI入口，新增Task入口供调用。底盘三端口并行/按整体状态发布，重复连接去重/健康复用，取消后的迟到应答不能重接。Dobot空闲反馈坏可单独恢复，状态灯要求控制+新鲜反馈，不改运动/坐标/端口。
- Dobot销毁/普通断开关闭控制与反馈；Gateway跟踪全部已接入会话并隔离旧监听。底盘流程/受控动作/停止在途时先请求停止保留通道，停止失败不假断开；FieldPanel总停止现在等待3003匹配应答，但它不是物理停稳证明。wantsToQuit仅拦截正式流程锁中的Player普通退出，停后需再次退出；Editor/强杀/断电不受保护。
- 电缸连接替换先motionEpoch失效并释放master/client，失败/销毁也释放；IO默认读写1000ms、Retries=0，读取失败撤销反馈新鲜度/关闭。仍是主线程同步IO，可能短顿；返回0与原到位超时继续规则保留，不当严格完成保证。
- Python服务UI可见性考虑FieldWorkflowOwned；本次活进程控制口不响应时保留引用且不重复启动，启动轮询约15秒，未盲Kill。V26先bind本机控制口再启动电批线程；socket IO1秒、空闲recv超时不重连、失败局部socket显式关闭，不改原运动算法/节奏。适配reader异常也确保关socket，Servo销毁不Dispose在途仍需Release的门。
- 相机get_image对采集超过2秒旧帧拒绝返回；未重做实例/优雅退出或处理结果年龄。未把TCP连通、软件STOP、3003应答当整机安全到位/停稳证据。未新增运动自动重试/断网续跑、框架或后台Modbus队列。
- 本轮回归382通过+1跳过：ConnectionLifecycle23/Chassis24/FieldRobot105/FieldWorkflow101/Offline22/History25/OperationLog7/Console22/Python53；仅自建loopback或无网络替身。独立main+Editor编译0错误47既有警告，未Unity Play/GUI、硬件连接或构建Linux。下一步按现场文档先空闲连接/取消/状态灯/过滤验证，再完整构建至新目录并监护单目标验收；不先运动拔线。

### 2026-10-28 日志面板反馈过滤（已改源码，未打包/GUI验收）

- RuntimeConsole在展开日志窗口上方动态创建“过滤机械臂反馈（仅日志）”Toggle，默认关闭、收起同步隐藏；SetRobotFeedbackLogFilter立即重建可见文本。只匹配Log类型且以反馈更新:（兼容中文冒号）及Mode=开头的周期消息，警告/错误/连接/StopTrace/人工检查等保留，不改变Dobot事件、通信/同步/到位/停止和文件/报告记录。
- 新RuntimeConsoleLogBuffer按普通/周期两类各保留maxLines、合计最多2倍，显示仍最多maxLines且保持时间顺序，反馈洪流不挤掉普通日志；隐藏反馈入缓存但不刷新文本，取消能恢复尚保留消息。纯逻辑RuntimeConsoleHarness22项通过，主/Editor独立编译通过，实际UI仍待验收。
- 已向用户核对卡顿来自统一按钮多设备同步TCP连接/底盘顺序端口尝试，尚未改连接逻辑；正常Dobot.Disconnect同时关Dashboard与当前反馈，流程占用时先停止保留连接，残留角度不等于新反馈。保留上一轮审查文档/记录，不把过滤当作通信修复。

### 2026-10-28 通信生命周期审查（仅诊断，未改运行代码）

- 当前本地发布分支/标签已到e906217（v1.12-test.20261028），开始时工作区干净；上文/历史中的“尚未提交”为当时状态，不再代表当前提交状态。本轮未联网重新核验发布附件。
- 详见CONNECTION_LIFECYCLE_AUDIT_2026-10-28.md：统一断开按钮顺序调用三设备，底盘直接DisConnect可先于排队停止确认关闭通道；电缸重复Connect覆盖旧资源且不更新motionEpoch，OnDestroy不释放，替身已复现旧异步动作跨新master继续写入；Dobot退出场景只关Gateway不对称关闭Dashboard/反馈；退出程序异步停止未等待且Servo退出分支不等cycle_active结束。
- 其余重点：Python启动失败无完整回滚，电批线程先于本机端口成功绑定启动；电批连接后取消socket超时、sendall持共用锁，状态/停止/关闭可能被阻塞；反馈恢复/多端口状态灯不完整、UI隐藏关服务未检查FieldWorkflowOwned、相机缓存旧帧仍可HTTP200、Python适配reader关闭异常跳过socket关闭。部分为既有问题，不能归咎于迁移日志，也未证明是此前现场日志唯一原因。
- /tmp无网络替身验证5项电缸+3项Python路径，不启动真实服务/Unity Play或连接设备，不修改运行代码/协议/配置。仅新增审查报告并更新上下文/WORKLOG；待用户批准后优先统一停止→确认→断开、连接代次/释放、服务启动回滚与有界IO，保留统一通信、反馈/停止/权限和用户原电缸超时策略。

### 2026-10-28 发布准备（仅教学与说明，未执行提交/构建/上传）

- 只读GitHub核对发布分支及旧标签v1.12-test.20261014仍指向4f849ed；本地integration/wulaoshi-21.5-wlj保留未提交迁移/半自动/采样改动。本轮新增PUBLISH_2026-10-28.md操作教程及RELEASE_v1.12-test.20261028.md预发布说明，建议新标签v1.12-test.20261028，继续原release/software-v1.0，不创建新的远端integration分支、不移动旧标签。
- 指导保存/提交integration→ff-only合入发布分支→完整Linux构建至TEST1028-v1.12-test→推送分支/标签→Release附完整tar.gz；源码push、Git标签、可执行包和Release是四种不同产物。Player内部版本仍1.9.0，本轮未改，不因标签自动升级。此前327项无硬件测试是10月15日记录，并非本轮复跑；现场待验收和未解决问题已如实列入说明。

### 2026-10-15 稀疏机械臂路径采样适配已实施（源码，未打包/实机验收）

- 新增ArmTrajectorySampling：compact路径保持原单位/轴顺序，用六转动轴的最大角差计算ceil(delta/maxJointStepDeg)，逐原线段线性细分，保留原拐点与精确端点，不折返角度、不重求IK。maxJointStepDeg通常保留5，仍可配置0.01～10，现在为预规划路径采样密度而非总转角门槛；现场单步微调原门槛不变。非有限数值/维度/索引错误拒绝，单任务最多100000采样点防爆量。
- SnapshotFieldPlan仅修改冻结副本，同时适配粗定位、显式返程、自动逆向返程；返程声明须先与粗定位终点匹配，在既有容差内统一到实际计划端点后再采样，缺声明且首点不匹配拒绝补接。真实绝对限位、起点吻合、多目标复位、反馈/停止和段末到位保护保留。ExtractArmJoints改双精度rad→deg，避免float常数放大边界误差。执行器仍拒绝未经采样的大步跳变，错误含目标/阶段/点/轴/前后角及差值。
- SendIntervals按原规划段计算估速/缓冲/最小最大等待一次，再按补点份额分摊，避免固定缓冲随补点倍增；ACK耗时计入，队列背压仍可延长。中间cp50、末点cp0、16条窗口及仅段末新反馈/命令ID/空闲/六轴误差稳定0.3秒不变，不逐点等待静止或Stop。不保证真实速度、CP平滑后的TCP轨迹或总耗时与旧包完全相同。
- 离线全流程复用采样检查（默认5°，不依赖实物许可/软限位），动画仍沿原线段播放，不将每个补点重复计为0.25秒。FieldPanel解释maxJointStepDeg语义；正式半自动报告新增trajectory-plan.json，保存原始/采样六轴角度、来源点/份额与发送节流，build-info诊断标识sampled-trajectory-v2-2026-10-15。操作说明开头已更新；Editor预览补Unity JSON序列化断言但本轮未执行预览。
- 回归101项FieldWorkflow（含51项采样）、105项假机械臂、22项原生产离线协程、21项假底盘、46项Python、25项历史、7项操作日志通过，共327通过，另1项长时跳过。独立main+Editor编译0错误47既有警告；socket仅127.0.0.1假服务，无设备连接。未改Python算法/工具标定、底盘协议、电缸超时规则、场景/版本号、来源工程、持久JSON或旧TEST包；未提交推送。用户需退出Play等待导入、重新生成任务并完整构建，核对真实限位/起点后监护单目标验收。

### 2026-10-15 本机轨迹步长拦截已用具体数据复现（只诊断，未改运行代码）

- 用户表示末端TCP问题已解决，本轮仅核对规划输出/角度转换/步长拦截。本机Editor.log最近一组目标1：q_start前六轴约[0,45,-90,-90,-90,0]°，q_goal约[48.7931,-32.9966,34.6410,-1.7361,-254.3646,-41.5681]°（原日志弧度四位小数，换算为近似值）；a0～a5明确为joint1～6，a6为升降joint10，不是完整qpos索引误取或TCP的XYZ。
- BITStarPlanner.Plan直连成功只返回两个compact端点；AddArmTask只对path.Count==1插值，多点路径原样复制，故本机该粗定位任务相邻点1→2首个被拦的是J1约48.79°，最大J5约164.36°。离线ExecuteActuatorPlanRoutine逐帧插值只改Control，不把画面的中间帧写回任务轨迹，因此模拟可动与真实预检失败并不矛盾。固定10点的单解/显式返程分支也不保证每步≤5/10°。
- /tmp临时诊断链接当前生产SnapshotSemiAutomaticPlan/Settings及现有Harness纯替身，用日志端点重放：±360绝对范围通过；5/10均准确复现相邻差错误，100先报参数上限；仅在临时内存把同一关节线段均分33段后通过5°预检。这个比较只证明点密度与保护策略不匹配，不证明真实MovJ/cp轨迹等同仿真或已安全验收，也不能把±360配置当实际软限位。未连接设备、改缓存/限位/节奏或取消保护。
- 后续获准实施时应统一规划→执行的轨迹表示：沿已校验关节线段自适应采样，或将粗定位已规划段与微调步长策略分离；同步粗定位/返程/离线预检和详细点轴诊断，并检查发送节奏与平滑影响。不应把视觉微调的小步长直接当粗定位端点总角差上限。现场截图是否与此具体数值完全相同仍需现场计划导出。

### 2026-10-15 testdata/1现场首因（不是步长拦截）

- 新报告位于wulaoshi/testdata/1，两份均InsertOnly且maxJointStepDeg=10。074611首因是Tool(1)返回-1；用户Dobot V4.6.0 TCP PDF第21页明确此返回表示工具坐标索引不存在。10月8日同编号Tool(1)成功并有确认，需核对控制器工具坐标1的存在/保存/工程配置；不能改0或零标定绕过。当前未进入插入运动，motion.csv仅表头。
- 074545服务ready后约81ms被关闭，随后配置Connection refused；UpdateButtonVisibility隐藏真实UI即关闭程序且不排除FieldWorkflowOwned是可疑路径，未有调用来源证明，尚未修代码。两新报告assemblyId相同，用户所述回退旧版失败报告未提供；共享控制器配置不会因软件回退还原。另见OperationLog LiteDB PAGE_SIZE错误，非Tool失败首因，未动数据库。

### 2026-10-15 相邻步长校验的现场问题（待进一步改造，未放宽）

- maxJointStepDeg默认5，Settings.Validate另硬编码范围0.01～10；FieldWorkflow规划预检与DobotController.Trajectory执行器再次比较同轴相邻角差。100触发参数范围错误，当前源码有限值10应通过参数范围校验；现场若10仍报范围错误，实际读取值/构建尚未核对，不应猜测具体点差。此阈值是应用防跳变策略，非厂家API限制。
- 用户确认现场与本机同一组目标点位，但离线StartRun分流只回放，未走真实计划的相同步长预检，故不能以模拟可动推断该预检已通过，也不能认定选点有错。尚未掌握现场具体触发目标/点/轴及前后角度。用户在追问限制必要性，本轮仅诊断记录；调整限制需要明确方案/批准，保留真实轴范围、起点/坐标映射一致性、反馈/故障/停止及阶段末到位检查。

### 2026-10-14 底盘新/旧完成判断开关（源码，未实机验收）

- 现场联调/参数新增“底盘旧模式：定时等待”开关，保存在FieldCommissioningSettings.useLegacyChassisTiming；false默认状态模式，旧JSON缺字段仍false。legacyChassisTiming为可选嵌套配置，缺失补默认0.5秒通用缓冲/5秒旋转/5秒平移/0.2秒最小/0无配置上限；正上限不得截短预计时间，各段硬边界600秒。运行复制/冻结，切换撤销安全确认，执行/占用中不能切换，无自动降级。
- ConnectCommander真实底盘路径共用几何、3055/3056方向，按模式选择ExecuteRelativeConfirmedAsync或ExecuteRelativeLegacyTimedAsync；旧路径核对应答和原估时，不查询1020，捕捉取消/断线/拒绝并请求停止。模式/速度/路径/等待复制，dispatchEvidence区分证据。现场旧模式整段底盘之后ManualTargetGate等待人工核对位置/朝向/停稳，底部程序按钮为“确认底盘”；不同于粗定位之后“确认并拧紧”，停止/重复/旧代次/反馈不符不放行。离线模拟不受影响。
- 两种模式仍保留现有包/流水号/错误码校验，不退回fire-and-forget或忽略断线。旧模式只需导航连接端点，新模式仍要状态/导航端点；兼容IPv4-mapped IPv6表示。不能将旧模式等待/人工确认当作自动到位证据；3003应答不代表物理停止，切换/开始前需现场停稳确认。
- 操作详见FIELD_COMMISSIONING_2026-09-16.md及CHASSIS_COMPLETION_2026-09-16.md开头。21项假底盘、50项FieldWorkflow、19项原离线/路径Harness和原机械臂/Python/日志回归通过。独立main+Editor编译通过，Unity批处理仍因项目占用未执行预览；GUI/真实底盘状态链路待验收，未改场景/版本号/旧TEST包或提交发布。

### 2026-10-14 离线模拟同步人工关卡（当前源码，GUI待验收）

- 全流程验证（模拟）现为粗定位动画→等待人工继续→小幅模拟视觉→auto_cycle_sim→模拟精调反向撤回→原返程→下一目标重新等待。只读/写MuJoCo模型控制量和本机模拟服务，不放行任何真实许可；动画结束不是实物到位确认。默认模拟偏移峰值J1 0.8°、J2 0.5°、第七轴2mm，支持原Inspector配置；每次运行复制模拟点与任务，不随重规划/编辑改变。
- OfflineTargetSession复用ManualTargetGate并额外检查第七轴，代次/目标绑定、一次性继续、取消失效；等待期间模型姿态改变即失败。Servo原程序按钮在等待时显示“继续（模拟）”，未启动Python也能继续/停止；正反转禁用。FieldPanel显示轻量模拟状态/继续/停止，不展示实物许可大面板。异常统一收尾，启动过程中取消等待启动收尾再关闭本次进程；离开场景也取消并清理迟到启动。
- StartSimulationProgramAsync显式设置SERVO_SIMULATION_ONLY=1，V26回报simulation_only；隔离模式不启动电批接收/实物命令/终端按键线程，发送底层也拒绝，命令白名单只准模拟周期/状态/配置/取消。拒绝复用实物服务；PrepareFieldService拒绝模拟服务。ContactCycle模拟取消不发无主的Gateway emergency_stop，真实停止行为不变；仍保留本机网关的带token模拟会话通信和算法原有流程，非纯计时假成功。
- 回归：42项FieldWorkflow、12项直接提取生产离线协程的Harness、96项假机械臂、13项假底盘、46项Python、25项历史及7项操作日志通过，另1项长时跳过；真实V26隔离子进程+实际C#假Gateway完成SIMULATED_OK/取消并证明假电批零连接。独立main+Editor编译0错误；Editor预览和实际Play界面仍待人工验收，旧TEST包未更新。

### 2026-10-14 真实运行半自动人工关卡（当前源码，未实机验收）

- 已修复“单目标”父标签漏改：RunScene标签Text在父StepRun、Toggle在子ToggleTarget，UpdateModeUI现只向匹配StepRun的父层搜索，不扫描整个面板；真实页单目标、仿真页单步运行。Editor预览增加对应层级和无关文字不变的检查，仍需用户运行菜单验收。

- Simulation.StartRun保留原离线验证分流；真实入口改走FieldCommissioningPanel的SemiSingle/SemiMultiple，同一占用会话粗定位→人工等待→完整接触→返程→下一目标，每点重新确认，无模拟J1/J2视觉。单步选项在真实页显示单目标，取消则从firstTarget逐目标；现场安全确认仍必须人工勾选，未确认只打开参数提示。
- 离线模拟原来自动执行不暂停的问题已按本页开头同步人工关卡；真实入口仍是人工跳过视觉，不执行模拟偏移。两种模式只统一交互编排，不把模拟结果当作实机验收。
- 原程序启停按钮在半自动会话期间复用为“确认并拧紧”，仅等待时可点；手动电批正反转互斥禁用，停止在服务未就绪时仍可用并路由总停止。模式切换不能隐藏正在占用的实物控制。ManualTargetGate以会话延续代次/目标绑定一次性许可，取消/重复/旧点击或姿态改变不能推进。
- DobotController.Trajectory与ArmTrajectoryTiming新增独立段执行器：冻结原角差/估速/缓冲发送节奏，逐条ACK、中间cp50/末点cp0、有界16条待确认窗口；仅段末检查本次末条ID+新反馈+空闲+六轴误差持续0.3秒，段内不逐点Stop/停稳。轨迹起点须匹配实际关节，返程显式连接粗定位终点且须回原起点，接触后不匹配则失败不自动补偿。旧现场③④原节奏限制未解除，Python/网关协议未改。
- 新报告trajectory-timing.json及semi-automatic-trajectory-v1-2026-10-14标识；历史明确人工跳过视觉与电缸沿用超时策略。底盘13项假服务状态完成回归通过，未找到完成链路实机验收证据；电缸虽读位置，读取失败返回0/超时仍继续的边界保留，不能泛称整机可靠到位。
- 操作说明见FIELD_COMMISSIONING_2026-09-16.md开头。新增Editor只读预览菜单Semi automatic toolbar；Unity批处理因已有实例占用未执行，独立编译/无设备回归不替代GUI与现场验收。未改场景/版本号、现有TEST构建、Git标签或发布分支。

### 2026-10-14 伍老师21.5.wlj日志/运行历史迁移

- 集成前备份4f849ed/v1.12-test.20261014已在GitHub；当前开发分支integration/wulaoshi-21.5-wlj。迁移说明见WULAOSHI_21_5_WLJ_MIGRATION_2026-10-14.md，尚未发布迁移版本。
- RunRecorder使用Data/Runs/RunRecords.db按项目保存运行历史；开始/步骤/收尾Upsert，runId隔离迟到回调，首因优先、参数深快照、空字段/IO异常兼容。OperationLogTool仍用Data/Operations/OperationLog.db；原枚举值保持，新分类仿真操作/真实运行为4/5。
- 项目记录按钮调用RunHistoryPanel，按需创建TaskList/StepDetailPanel，复用Resources/Run/History两种wlj prefab和Run/Task图标，不改MainScene。历史50条分页、模式筛选/详情/返回，中文字体与窄屏滚动；未测力矩/角度显示未采集，非设备运动重放。
- Simulation.History/PendingParamLog按0.6秒空闲或关键动作合并参数日志；MissionController/ConnectCommander.History/FieldCommissioningPanel接实际流程终态。离线全流程属仿真且标离线模拟；旧通讯下发只能记Dispatched，不能当实际到位；现场停止确认未通过不可记成功。DobotController.History只读新鲜实物collision标志作历史分类，不改变控制。
- 核心Gateway/反馈/停止/测量、FieldWorkflow、底盘确认、碰撞/IK、场景和Python保持测试标签内容，未迁入SelectedObjects等无闭环结构。生产控制参数/端口/安全许可不因历史功能改变。GUI渲染/实物现场仍需人工验收。

### 2026-10-08 通信延迟独立小测试

- ExternalCode/communication_latency_test.py有camera/robot子命令；当前相机Python直接采集/推理，Unity只显示预览，故camera模式不测试Unity图像转发。帧等待/对齐/拷贝和SDK主机可对应时间戳年龄估算分开，硬件时钟/负值/时钟跳变不伪称有效延迟。
- Gateway新增latency_echo/latency_query，不获取运动会话，白名单GetPose/GetAngle/GetForce，任务占用时拒绝；复用生产主线程/串行Dashboard路径但关闭每请求trace日志。返回实际设备端点及本地持续时间，测试端校验设备一致。
- robot模式在同一Python计完整RTT，直连/Unity/Unity/直连分时采样，各路径500次并预热，报告统计量及差值；不是运动完成或整算法耗时。SDK若拒绝第二个只读连接，结果无效，脚本不发RequestControl。
- Build后处理自动复制独立操作说明COMMUNICATION_LATENCY.md和顶层Python脚本；报告写到终端当前目录LatencyReports，当前未运行真实设备或打包。操作见COMMUNICATION_LATENCY_2026-10-08.md。

### 2026-10-08 实际末端测量测试版

- 新增DobotController.MotionDiagnostics.cs partial：以显式GetPose和RelPointTool计算段起终点及初始工具-X参考方向，记录请求/实际位移、横偏、目标误差及平均/窗口速度。约10Hz采已有反馈，时间戳/User/Tool/新鲜度校验；起点GetPose与原始反馈不相符时反馈推导标无效。
- Panel新增InsertOnly/WithdrawOnly，Python ContactCycle共享同一HybridExit但不调用电批运动回调；机械臂单测不要求电批连接，仍要求机械臂/力传感器和现场许可。服务自身启动可能连接/复位电批。
- 新协议begin_stage/end_stage与robot_insert/robot_withdraw；Python能力标志motion_diagnostics_supported、robot_only_supported，contact_api_version仍2并要求新标志，旧服务拒绝混用。原算法/旧节奏/原停止保护与参数默认值保持。
- 报告新增motion.csv（100000普通采样上限，约每秒/边界刷新）、cycle-failure.json（有首因时）、result测量/IO/清理信息；构建标识motion-measurement-v1-2026-10-08。原始异常在停止前冻结，C#取消后仍按同一cycle_id取回，清理不覆盖。
- 已通过100项本机无硬件测试（网关测试另跳过1项长时测试），普通解决方案0错误44警告；Unity已自动把新partial纳入生成csproj。没有构建Linux可执行包、部署、改版本号或提交；GUI与实际速度仍待现场验收。操作见FIELD_COMMISSIONING_2026-09-16.md最新段。

### 2026-09-23 现场应答兼容补充

- User/Tool现场可返回空ResultID。Gateway仅对坐标系选择兼容该情况，以应答后连续3个新鲜空闲且实际编号正确的反馈确认；非空ID仍匹配，运动命令仍要求ID。
- ContactAcquisitionError在现场流程开始清空，acquire失败时先保存并记录，再停止清理；Panel最终报告优先保留该根因，避免取消异常覆盖。未实机验收。

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

## 全流程离线验证（2026-09-03）

- 真实运行右侧面板的“实时同步”下方由`Simulation` 动态创建
  “全流程验证（模拟）”勾选框；不要为此直接修改`RunScene.unity` YAML。
- 勾选后`Simulation.StartRun()`只调用
  `ConnectCommander.DispatchFullProcessValidation()`，不进入原真机`DispatchTask`/
  `DispatchNextTargetSequence`。开关默认false、不持久化，开启时禁用单步和实时同步。
- `TrajectoryTask.targetIndex`是零基目标编号，`TrajectoryStage`分为
  `Navigation/Lift/CoarseArm/ReturnArm`。`MissionController.DeepPrecomputeAll()`创建任务时
  必须同时写这两项；新调度不应再靠`StartsWith("目标 1")`这种会误匹配目标10的方式分组。
- 视觉轨迹通过`IVisionFineTuneTrajectoryProvider`进入编排层，统一输出
  `FineTuneTrajectoryPlan`。当前`MockVisionFineTuneTrajectoryProvider`使用7维数组：前6轴
  是CR10关节弧度，第7轴是升降米数。Inspector中模拟偏移为前6轴度数+第7轴米数，
  provider负责转换到内部单位。
- 未来视觉可返回连续笛卡尔点或七执行器轨迹，但都应在新provider中转换，
  不要把视觉协议细节写进编排协程。不设50mm最大距离和姿态不变硬限制；真机接入前
  必须补齐坐标系/单位、关节范围、连续性、碰撞和超时/中止校验。
- 离线时序为每个目标的原粗定位任务 -> 模拟视觉轨迹 -> 启动/复用本机V26
  bridge -> 等待模拟拧紧并输出`Result=SIMULATED_OK` -> 机械臂复位 -> 下一目标。
  真实电批安全开关始终不解锁，离线流程不调用任何真机`Send*Routine`。
- 离线流程不污染真机任务进度：退出时所有`isDispatched`恢复false。按R、界面重置、
  取消勾选或退出场景都必须取消离线流程；由本次流程启动的V26进程在结束时关闭。
- 当前工作分支为`feature/full-process-validation`。用户未提交的
  `ProjectSettings/ProjectSettings.asset` 1.9.0修改属于工作区变更，不要在本功能的提交中意外遗漏。

### 2026-09-10 更新：统一机械臂入口（覆盖上文旧分支/模拟拧紧状态）

- 当前工作分支 release/software-v1.0，基于v1.10/57f96bc；本次适配尚未提交。
- DobotController 为partial类，Gateway文件在同一组件启动回环TCP19120；机器人
  Dashboard/Feedback只由Unity持有，普通指令与算法共用串行请求应答入口。
- Python UnityRobotClient 申请阶段占用，读取status/pose/wrench，发送相对XYZ毫米运动；
  阶段互斥，不是移交机器人TCP连接。真实模式默认关闭，仅允许回环请求。
- 正式启动仍是V26非0910文件，保留9100控制与9101曲线、电批Python通信。
  ContactCycle负责卡入→5秒→拧紧→5秒→退出；HybridExit经Unity入口执行算法。
- 全流程模拟现在调用auto_cycle_sim；模拟接触为内存位姿和零力，不是物理接触仿真。
  整体成功并释放后才复位/下一目标；NG退出完成仍为失败。
- 不支持任意七轴视觉轨迹网关；后续必须明确轴/单位/坐标及执行校验后扩展。
- 实机协议/反馈布局与现场力控尚未验证。详细文件映射、限制、验收步骤见
  `.codex/ROBOT_COMMUNICATION_ADAPTATION_2026-09-10.md`；测试位于tests/。
- 通信展示：RobotTrace日志始终启用，Python/Unity用相同请求id记录收发、参数、
  结果和耗时，不打印token/status心跳。软件启动的Python stdout由主线程队列转发至
  RuntimeConsole；外部已启动进程仍需在其终端查看Python侧。日志不是完整审计存储。
- 当前Editor完整模拟曾在卡入8mm触发30秒预算超时，尚未修复；缩短时长的测试通过
  不代表完整Editor流程通过。新增日志用于对照和进一步定位。

### 2026-09-16 联调版开发中

- 基线已是v1.11/4b8f839。网关纯字节IO取消Unity上下文捕获、JSON按块缓存读取，
  避免逐字节帧等待；完整时长模拟宿主10项测试通过，Editor仍待复测。
- 机械臂和底盘键盘调试快捷键默认关闭；正式UI接口保留。
- ExternalCode/commissioning_preflight.py仅做无设备环境检查（Python/numpy/Matplotlib/
  脚本/字体/Agg），不会连接实物。当前还没有完整现场配置/实物编排面板。
- 完整真机流程接入前必须解决底盘相对运动完成反馈协议、升降异步停止/超时判据、
  机械臂路径实际到位判据；已询问用户现场范围和底盘型号/协议，不猜测底盘状态语义。
- 用户随后提供PDF，已确认1020完整状态task_type=7为平动/旋转；新增
  RobokitController.ConfirmedMotion.cs串行请求及完成轮询，task_status改int。
  每段先运动应答，再观察RUNNING→COMPLETED；失败/超时不下发后续段。
  假底盘13项测试通过，现场仍待验收。短动作未观察RUNNING保守失败，不采信旧完成。
  升降缸超时逻辑按用户明确要求暂不改。细节见CHASSIS_COMPLETION_2026-09-16.md。

### 2026-09-16 续：现场面板与实物编排已实现（覆盖上文“尚无面板”状态）

- Simulation.Awake动态挂FieldCommissioningPanel，左上“现场联调 / 参数”，不改场景YAML。
  JSON配置保存在Application.persistentDataPath/field-commissioning.json，现场确认不持久化。
  默认六轴/升降范围为0，必须填写真实设备范围；参数校验后冻结本次快照。
- 新增FieldCommissioningSettings、DobotController.Commissioning、ConnectCommander.FieldWorkflow、
  FieldWorkflowSequence。真实路径使用MovJ cp=0 +新鲜反馈/空闲且稳定0.3秒判到位，不按估时完成。
  模拟视觉从实测六轴的J1/J2加配置度数偏移；不是离线7维动画，也不是视觉真正识别结果。
- 精调单测/接触单测/单目标/多目标入口分离，原离线入口保留。目标计划深复制/提前校验，
  缺少ReturnArm时逆序粗定位路径回其起点；跨目标起点不一致拒绝运行。
- 现场流程独占普通下发；接触阶段临时授权Python真机运动。Python API v2接收
  configure_contact:<JSON>，回configuration_id；Unity校验版本/电批地址/回执。真实cycle必须先配置。
- 反馈跟随允许在真实执行中切换，只写仿真执行器；不产生反向运动指令。面板允许单轴源独立跟随。
- Stop/故障/反馈异常中止，停止未确认保持锁。升降超时继续按用户要求保留，但Stop会使旧异步
  写寄存器代际失效；不能宣称无人值守安全到位。详情FIELD_COMMISSIONING_2026-09-16.md。
- 每次测试自动导出CommissioningReports下配置、2MB内会话日志和结果。Linux后处理附带操作说明，
  顶层Python和字体复制沿用原逻辑。未打包、未实物验收、未提交推送。
- tests新增FieldWorkflowHarness(15项)、FieldRobotHarness(12项)、Python配置(6项)；既有网关10项
  完整时长模拟、假底盘13项通过。实际GUI/硬件/协议运动结果仍需人工验证。

### TCP文档审查补充（2026-09-16，尚未修复）

- 用户V4.6.0/2024.12.26 PDF审查见DOBOT_TCP_AUDIT_2026-09-16.md。现场SpeedFactor浮点文本
  为确定错误；原测试Number替身与生产F6不同，56项无硬件测试不等于协议兼容。
- Tool为队列指令，接触前需要ID/实际Tool反馈确认；不能只ACK后立即默认GetPose。
  SixForceOnline=1037未接入，CurrentCommandId=1112未使用；Stop/到位应关联本次命令后的新反馈。
- 现有已读取反馈偏移正确。30005反馈200ms，250ms保护余量小；ServoP不受全局速率控制。
  0.2mm容差大于0.1mm搜索步长；自动接触应先修复并验收。RunScene尚有模拟专用演示命令绑定。
- 本轮仅检查/报告，没有修改运行代码；这些不是“已修复”状态。

### TCP审查后修复（2026-09-16，覆盖上面的“尚未修复”状态）

- 现场SpeedFactor独立整数构造，配置拒绝小数；普通MovJ使用InvariantCulture且拒绝无效输入。
- 接触新增userNumber(默认0)，User/Tool等待ResultID+新反馈+实际编号；GetPose显式指定双方。
- 读取SixForceOnline、CurrentCommandId、User/Tool、机器人时间戳；接触要求传感器在线，
  Python同步检查。方向负载改用GetForce(tool)，不再假定反馈力的YZ等同于工具YZ。
- MovJ/ServoP要求命令ID与应答后的新反馈；微步容差≤步长25%，姿态0.1°；Stop须3次新静止帧。
- 反馈后台只更新锁内数据、主线程发布事件；丢弃积压完整包/重复时间戳，检查相对到达延迟。
- 新现场配置默认30004；旧配置不覆盖，真实接触拒绝30005。RequestControl失败不开放实物流程。
- 详见FIELD_COMMISSIONING_2026-09-16.md新增部署说明。未修改场景、UI位置、版本号或现有打包目录。
- ServoP仍为分段验证而非33Hz连续伺服；实物运动、反馈字节流和Linux构建仍需现场验收。

### Stop诊断补充（2026-09-17）

- Gateway停止检查新增StopTrace独立编号、请求/应答/限频采样/通过/失败日志；状态和六轴速度
  在同一锁内快照读取。TIMEOUT异常包含最后快照与新样本/不合格/连续合格计数。
- Commissioning区分PRE_MOTION_STOP(_FAILED)、END_STOP和MOVJ_REQUEST，避免把
  开始前被拦截误判为“运动后没有停止”。现场运行日志沿用面板run.log/result.json收集。
- 不放宽阈值、不解锁、不改变端口/运动参数；具体根因仍待现场实际QDActual等数据，非已修复。

### 停止诊断v2（2026-09-23）

- DobotController.Feedback.cs承载原1440字节解析/完整读取，主接收循环统计累计完整块、
  接受、坏头、旧时间戳、相对延迟与积压；StopTrace开始/结束包含连接代次和计数快照。
- StopTrace汇总新采样失败原因与无新帧轮询，TRANSPORT区分排队、流写入、应答收齐和派发。
  既有0.01/250ms/3次/50次条件不变；本轮非实机根因修复。
- FieldCommissioningPanel从开始建立报告，build-info含stop-v2-2026-09-23和程序集MVID；
  result.completed=false表示尚未结束。CommissioningJournal增量刷新关键诊断，保留两份
  各约100万字符文件；普通run.log结束存最近200万字符。非无限/断电可靠日志。
- 独立Recovery报告关联parentReport，不覆盖原运行；操作步骤见FIELD_COMMISSIONING。
- 无设备测试78项通过，生产解析现由合成字节包覆盖，仍需Unity界面/现场固件反馈验收。

### original分步节奏试验模式（2026-09-23）

- 原件保留ExternalCode/original，不作为运行入口。真实原逻辑是每步一次ServoP+t监控，
  轴向不步末Stop、横向搜索Stop、整段结束Stop；不是高频轨迹流。
- originalContactTiming默认false；启用后Python传递original_timing/stop_at_end，网关返回
  interval_elapsed（不是到位），finish_stage在段末确认停止，失败不进入电批。
  新Python支持标志和配置模式回执均校验，防旧服务混用。
- 此模式交接停止用新鲜空闲无报警+位置跨度≤0.001°持续0.5秒且至少3新样本，
  QDActual只诊断；未做实机验收，不是安全认证。旧模式停止策略不变。
- 新模式只开放现场①②，③④明确禁用；粗定位连续执行未改。操作与差异见FIELD_COMMISSIONING。
- 验证累计92项无设备回归，编译0错误；未打包/部署。构建标识original-timing-test-2026-09-23。
