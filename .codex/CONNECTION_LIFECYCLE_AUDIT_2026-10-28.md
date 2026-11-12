# 通信连接、断开与退出清理审查

日期：2026-10-28。审查基线：`e906217`，本地`release/software-v1.0` / `v1.12-test.20261028`。开始时工作区干净。

本轮只审查、验证和记录，不修改运行代码、场景、配置、协议或设备参数，不连接任何实物、不运行Python硬件服务，不提交/推送。结论来自源码、RunScene按钮绑定及无网络替身验证，不是现场事故或实物安全验收报告。

## 结论

正常情况下的连接/下发链路已有串行请求、应答匹配、占用和反馈保护，不需要推翻统一通信架构；主要缺口在**连接失败、重连、场景退出和软件退出的生命周期没有统一收口**。部分问题在上一个测试标签中已存在，不应归因于此次伍老师日志功能迁移，也不能仅靠一次正常连通测试证明这些路径正确。

优先修复：底盘断开先于停止确认；电缸重复连接/跨连接异步动作；退出时异步停止未等待；Python启动失败未回滚；电批无限等待与共用锁。后续完善反馈恢复、状态显示、UI与服务所有权、相机新鲜度和异常释放。

## 覆盖的实际链路

| 对象 | 当前通信/所有者 | 已审入口 |
| --- | --- | --- |
| 机械臂 | Unity→29999 Dashboard；30004/30005二进制反馈，端口按配置 | DobotController主类、Feedback/Gateway/Commissioning相关partial及界面调用 |
| 机器人算法网关 | Python→Unity回环19120，JSON-lines与会话token | Gateway监听/会话/停止；unity_robot_client与ContactCycle |
| 底盘 | Unity→19204状态、19205控制、19206导航，TCP | RobokitController与ConfirmedMotion、统一断开按钮 |
| 电缸 | Unity→502 Modbus TCP | LiftCylinderController、现场入口和统一按钮 |
| 电批及程序 | Unity→本机9100 TCP控制、9101 HTTP曲线；Python→电批1200 TCP | ServoTighteningController及当前V26脚本 |
| 相机及图像 | Python用RealSense SDK采集；Unity→本机8080 HTTP预览 | VisionImageReceiver与realsense_image_server |

电批TCP仍由Python持有，并不是Unity直接持有；机械臂才由Unity独占。相机预览不是“Unity转发图像给算法”的链路。original、旧0910副本、独立延迟/测量脚本不当作当前界面默认执行入口；厂家SDK/第三方DLL内部不属于本轮全量源码审查。

## 优先修复项

### C01 / 高：统一“断开”按钮可能提前关闭底盘停止通道

- 证据：`Assets/SimulationPlatform/Scenes/RunScene.unity:37000`附近，同一个按钮依次绑定Dobot.Disconnect、Lift.Disconnect、Robokit.DisConnect。
- 前两者遇到流程占用会RequestStop并返回；底盘`Assets/RobokitController.cs:278`却无占用检查，取消任务后立即关闭全部流。`Assets/FieldCommissioningPanel.cs:531`启动停止任务，但按钮不会等待这个Task完成。运行时被锁定的控件列表`Simulation.cs:71`不包含此断开按钮。
- 触发：任务运行或停止请求仍在等待底盘串行门/应答时点击断开。3003可能还没发出或尚未得到确认，通道就被关闭。并非每次都一定发不出去，取决于当时请求队列。
- 影响：软件取消等待不等于设备物理停止，不能把此按钮称为已安全断开。设备断线后的实际动作取决于控制器配置，本轮未实机验证。
- 建议：统一编排“撤销新动作→请求停止→保留反馈并确认→再断开”；底盘入口也必须服从占用/停止锁，不允许绕过总流程。

### C02 / 高：电缸重连既漏释放，也未隔离旧动作

- `Assets/LiftCylinderController.cs:45`每次Connect直接覆盖tcpClient和modbusMaster，没有已连复用、先关闭旧资源或重连代次更新。统一ConnectBtn确实直接调用它，现场面板的IsConnected检查不能覆盖这个旧按钮入口。
- 连接异常只刷新UI/打印错误，失败的新client没有明确关闭，旧master可能仍留在字段中。Disconnect也没有在finally里清空两个字段。
- `MoveToPosition:120`通过motionEpoch拦截停止/断开后的旧动作，但Connect不更新这个epoch；因此重连期间尚未结束的async void动作可以继续读取新modbusMaster并写入旧目标/触发寄存器。
- `OnDestroy:323`只停止协程、不关闭TCP或Dispose master；正常返回项目管理页也会销毁场景内控制器，不能只依赖退出整个进程释放。
- 无网络替身已复现重复连接未关闭旧资源、销毁未释放、失败重连残留对象和旧动作向新master写目标/触发。旧动作复现通过直接调用控制器完成，**不宣称已证明现场半自动入口允许同样重连**，其MotionLocked确有额外保护。
- 建议：单一连接状态机/串行门，重连先使旧epoch失效，释放旧master/client；失败回滚与销毁均幂等释放。不自动重发旧运动。

### C03 / 高：软件退出未等待整套停止完成

- `Assets/DobotController.cs:278`在OnApplicationQuit中ShutdownRobotGateway后马上关闭Dashboard和反馈。Gateway的`ShutdownRobotGateway:83`仅发起`_ = AbortOwnerAsync()`，没有等待停止应答和后续反馈确认。
- `Assets/ServoTighteningController.cs:1978`退出分支同步请求stop并可能Thread.Sleep，但不像常规StopProgramAsync那样等待cycle_active消失；它仅凭电批状态/未连接决定是否Kill进程。Python接触周期的停止又可能需要通过Unity主线程处理本机Gateway。
- 影响：界面直接退出时，异步停止/报告收尾可能来不及完成；同步阻塞还可能阻止Gateway处理停止请求。不能将关闭窗口或进程消失当作整机已停。
- 补充：`StopProgramAsync:1389`将`!ToolConnected`作为允许关闭条件；Python`_do_stop_driver:768`最终写STOP也没有依据hard_stop_driver返回值或设备端停止反馈。属于软件状态/发送结果，并非实际停止证明。
- 建议：在退出请求发生时先拦截/编排有界异步停止，保持主循环、反馈和服务可用，完成后再允许真正退出；无法确认时明确告警和保留锁。硬件急停仍独立必需，不在本轮猜测新增硬件协议。

### C04 / 高：Python服务启动失败未完整回滚，可能残留重复实例

- `Assets/ServoTighteningController.cs:953`任意status失败后尝试启动新Python，不能区分服务不存在、已有服务阻塞、错误版本或端口上的非目标服务。
- `:1026`写入programProcess；`:1046`后的提前退出、超时和模式不匹配仅ReportFailure，finally只复位lifecycleRunning，没有统一回收本次新进程。现场入口重试StartProgramAsync可能覆盖原进程引用，失去旧实例所有权。
- Python`:1766`先启动电批接收线程，接收线程连上后会reset_driver；`:1772`才启动控制口线程。`:1371`的bind失败没有通知主线程退出，嵌入主线程继续while True。因此端口冲突不保证失败实例退出，可能残留另一个连接电批的进程。
- 建议：先验证并绑定本机服务端口，再开放设备连接；携带实例/版本/模式握手，启动失败只回收自己启动的实例；保留已有活进程引用，不能把任意握手失败当作“不存在”。关闭现有外部实例必须遵守所有权，不用按进程名批量杀Python。

### C05 / 高：电批无限socket等待与共用锁会妨碍停止/状态查询

- `ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py:1178`连接后settimeout(None)，随后recv可无限等待；`:325`的sendall持有sock_lock。
- unity_status、close_current_socket、停止发送均需要该锁。若sendall因网络半开/不读数据等情况长期阻塞，状态和关闭也会排队；如果只是recv无数据，则可能长期保留非空client_socket而不重连，仍回报tool_connected=true。
- 程序虽返回feedback_age，C# BridgeStatus及RequireFieldToolReadyAsync没有消费它来区分“连接对象存在”和“设备正在回传”。这不是已证明实物会失控，而是无法给出可用性/停止时限保证。
- 替身验证确认sendall等待期间共用锁确实不可取得。连接失败的局部s也未在except中明确关闭；不能夸大为永久泄漏，Python GC有机会回收。
- 建议：有界读写/停止等待，反馈超时和显式重连状态，避免在不可中断IO期间长期占住状态/关闭所需的锁。断网或重连不能自动恢复旧运动。停止时限与设备端确认另需协议/实测支持。

## 后续完善项

### C06 / 中：机械臂退出场景没有对称关闭Dashboard和反馈

- Gateway的OnDisable只关闭监听/算法会话；Dobot主类没有OnDestroy。Dashboard/反馈关闭仅在显式Disconnect或OnApplicationQuit中。
- 正常返回MainScene只退订事件/取消流程，没有调用Dobot.Disconnect。反馈Task持有本次stream/client及控制器，旧连接可能持续接收，重进RunScene又创建新控制器。
- 已有的“流程占用时不允许离开”保护应保留；空闲状态离开也应释放场景所有的通信资源，而不是仅取消事件订阅。

### C07 / 中：连接显示、反馈可用和重连恢复没有统一状态

- 机械臂`DobotController.Connect:100`只要Dashboard已连且RequestControl确认就直接返回。反馈首次连接失败或后来失效时，再点连接不会进入ConnectFeedback修复，通常需手动断开整组再连接。
- 反馈异常`:345`更新失败/新鲜度，但不会发送ConnectionChanged(false)；`Simulation.HandleConnectionChanged:304`的Feedback分支为空。Dashboard应答超时关闭流时也没有统一更新显示。
- 底盘每个端口均发送同一种Chassis事件；Simulation`:307`用最后一次事件覆盖一个状态灯，所以状态口失败而导航口成功时仍可能显示“已连接”。接收异常FailChassisStream只关闭流，不同步广播整体健康状态。
- 电缸ReadCurrentPosition读取失败返回0，监控可能继续展示之前值；不是读取失败就确实到达0。此处只指出状态表达，不擅自改用户要求保留的电缸超时进入策略。
- 正式流程另有RequireRobotReady、端点/新鲜度/请求失败保护，所以**不等于绿灯错误就一定继续运动**；问题在用户看不到准确故障和恢复路径。
- 建议：区分Disconnected、Connecting、TransportConnected、FeedbackHealthy、ControlReady、Stopping/Fault；按设备实际需要汇总端口，失效撤销准备状态；重连握手可修复连接，但不得恢复旧任务。

### C08 / 中：隐藏UI直接关服务，未检查实物所有权

- `Assets/ServoTighteningController.cs:864`以StatusReal.activeInHierarchy或OfflineWorkflowActive决定可见性；隐藏且进程活跃时直接StopProgramAsync，没有排除FieldWorkflowOwned，也没通过整个现场流程RequestStop收口。
- 常规ToggleProgram有占用检查，但这个Update路径绕开它。条件被触发时可导致现场占用中的服务被停；没有证据表明此前某份现场报告必然由它触发，当前只能确认源码存在这条路径。
- 建议：UI只显示状态；服务生命周期由任务/实例所有权决定，离开界面统一先结束任务，不能由显示组件自行关服务。

### C09 / 中：相机掉线后可能一直显示旧图像

- `ExternalCode/realsense_image_server.py:602`捕获采集失败但保留latest图像；`get_image:659`只要缓存非空就返回，HTTP仍为200，没有按_last_frame_time检查帧是否过期。Unity当前只查看HTTP成功/模式，不校验帧时间。
- 因此相机拔出/采集阻塞后画面可能停在最后一帧，看起来仍是正常预览；HTTP通不等于相机在线。此结论来自缓存分支，未进行实际拔相机测试。
- `VisionImageReceiver.EnsureServerStarted:780`仅表示Process.Start成功，不做服务实例/就绪握手或进程中途退出恢复；StopServerProcess直接Kill，不优先调用Python已有的正常stop/finally路径。Kill结束进程后OS通常会释放句柄，本轮**不宣称它必然导致相机永久占用**。
- 建议：帧时间/序号与过期显示、实例握手、启动失败回滚及有界正常退出后再兜底Kill；不要因为预览HTTP成功就把它当作视觉任务可用许可。

### C10 / 中低：少数异常清理缺少独立finally/代次管理

- `ExternalCode/unity_robot_client.py:122`的reader.close若抛异常，后面的sock.close与字段清空不会执行。已用实际适配类+假reader复现。每项资源应独立try/finally，且保留release失败首因。
- Servo.OnDestroy仅退订并Dispose commandGate，不先结束/等待在途命令；已经进入SendCommandInternalAsync的finally稍后Release可能遇到已Dispose的门。销毁也没有与OnApplicationQuit同等服务处理。属于可达的异常生命周期风险，未做Unity销毁时序复现。
- Gateway Shutdown只主动关闭ownerClient，未统一跟踪所有已接入未获取权限的client；多数空闲会话在10秒读超时后清理，但不是立即完整收尾。启停监听也宜加代次隔离和会话集合。

### C11 / 中：连接与电缸轮询在Unity主线程同步等待

- Dobot.Connect虽然声明async void，但`:107`的new TcpClient(host, port)在首次await前同步连接，ConnectFeedback`:293`也同步连接；Robokit.ConnectPort`:176`及Lift.Connect`:51`同样使用同步Connect。
- 电缸MonitorRoutine`:282`每轮在协程中顺序执行三次同步Modbus读取，未在本类显式设置有界读写/重试策略。Unity协程不是后台线程，实际超时/重试取决于底层库默认值，本轮未把它猜成某个固定秒数或无限等待。
- 网络不可达/丢包时可阻塞画面、输入和主线程状态处理，包括停止按钮交互；不是每次设备关机都会卡顿，快速拒绝和黑洞超时不同。
- 建议：可取消、有明确期限的异步连接及串行设备IO；Unity对象更新回主线程。取消/超时后关闭本次连接并隔离迟到结果，不将旧动作带到重连会话。

## 已有正确设计，应保留

- 机械臂唯一Dashboard串行门，完整应答匹配；超时/错应答关闭连接，避免串包。
- 反馈固定1440字节读取与旧stream引用隔离；新鲜度检查/诊断计数，Unity事件回主线程派发。
- 本机机器人Gateway的回环监听、阶段token、独占权限、断线停止/锁存；未确认停止不随意释放运动权限。
- 底盘流水号与API应答匹配、受控串行/取消、状态确认与旧估时模式区分，失败不自动降级。
- Unity→Python每条命令使用有超时的using短连接；短连接本身不是错误，不必为了本轮审查改成长连接。
- 模拟/实物权限隔离、现场参数握手、人工确认关卡及已有的异常拒绝逻辑。

## 本轮验证及限制

临时目录：`/tmp/connection-audit-20261028-NcOfG3/`，未将测试替身加入项目。

- `dotnet run --project LiftProbe.csproj`：直接链接当前生产LiftCylinderController，TCP/Modbus/Unity均使用无网络替身；5项问题路径复现（重复连接、销毁释放、读失败但仍连、失败重连残留、旧异步动作跨新连接）。首轮缺Modbus.Message测试桩命名空间，补充临时测试桩后运行通过，不是生产代码编译错误。
- `PYTHONDONTWRITEBYTECODE=1 python3 python_probe.py`：AST只提取生产函数，不导入会启动硬件线程的V26模块；3项路径复现（失败连接没有明确关闭、阻塞发送共用锁、reader关闭异常跳过socket关闭）。未创建真实socket、进程或相机对象。
- 共8项针对性无网络验证；**不是此前327项全回归复跑，也不是Unity场景动态/硬件故障注入验收**。现有FieldRobotHarness主要链接相关partial，并没有覆盖完整Dobot主类Connect/Disconnect及真实场景按钮生命周期，不能用其通过证明本报告各路径无误。

下一步建议先改C01～C05及场景资源释放，补“重复点连接、连接中断开、运行中断开、服务端口冲突、半开连接、退出/重进场景、退出软件”的假服务回归，再由现场在明确监护和硬件急停可用条件下逐项验收。当前只报告，等待用户批准具体改造范围。
