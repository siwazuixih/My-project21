# 真实运行连接按钮整理建议（仅审查）

日期：2026-10-28。依据当前工作区源码、RunScene.unity持久化按钮绑定，以及运行时生成的现场/底盘手动/相机/拧紧按钮；保留此前未提交修改。未修改运行代码、场景或配置，未启动Unity/相机/Python硬件服务、连接设备、编译或打包。表格是源码核对，不是运行时GUI/实机验收。

## 后续用户修订及实施范围

本文件下方保留的是修改前的审查和原建议。用户随后明确要求**现场联调面板不动，作为测试入口**，已按此范围实施：只整理正式工具栏到DeviceConnectionPanel；现场原连接/JSON/单测及底盘手动快捷连接保留。下方原“现场按钮改共用面板”的建议未采纳，不能作为当前操作说明。当前用法见FIELD_COMMISSIONING_2026-09-16.md开头“正式运行页设备连接入口整理”。新面板不改写现场JSON，保留正式流程原端点不一致拒绝规则；上电使能独立、重定位放高级，服务/预览只改明确名称。源码/无设备检查不代表GUI或实物验收。

## 核心结论

目前重复的是操作入口和概念，不应为了减少按钮删除必要的底层连接。机械臂控制和反馈、底盘各端口、电缸通信仍需保留。各层已有连接去重，因此多个入口不等于每点一次必然新增一组TCP。

应把“网络连接”“机械臂上电/使能”“本地服务启动”“流程执行/人工继续”“重定位”分清楚。建议统一设备连接面板/配置入口，保留按设备操作的能力；不要把所有设备、上电、使能和运动初始化塞进一个没有明确范围的连接按钮。

## 当前按钮与处理建议

| 位置/按钮 | 当前实际行为 | 建议 |
| --- | --- | --- |
| 底部“连接机器人” | Scene ConnectBtn顺序调用Dobot.Connect、Lift.Connect、Robokit.ConnectAll，实际尝试三种设备 | 现名称不准确；收口到“设备连接”，不能继续声称只连机械臂 |
| 底部“连接底盘” | Scene ConnectBaseBtn仅调用Robokit.ConnectAll | 与上项部分重复；移入设备连接面板，底盘独立连接能力保留 |
| 现场“连接机械臂” | 从现场JSON读取IP/控制端口/反馈端口，再调用Dobot.Connect | 去掉重复的单独实现，改共用设备面板/连接函数；不改变必要端口 |
| 现场“连接底盘/升降” | 从JSON读取两种设备地址，尝试连接底盘和电缸 | 两设备不应强制绑在一起；统一面板中各自一行，支持只调其中一种 |
| 底盘手动“连接底盘” | 当前底盘IP，复用同一Robokit.ConnectAllAsync，不连其他设备 | 可保留就近快捷按钮，但必须共用同一配置和处理入口，不另做一套通信 |
| 底部“断开” | Scene DisconnectBtn调用Dobot.Disconnect、Lift.Disconnect、Robokit.DisConnect；不包含相机和拧紧服务 | 明确为“断开设备”，或统一面板内按设备断开；不能把它当整机停止/关闭全部服务 |
| 底部“启动” | Dobot.PowerOn发送PowerOn() | 保留功能，改名“机械臂上电”，放机械臂准备分组，不与连接合并 |
| 底部“激活” | ClearError()、EnableRobot()，随后设速度3% | 保留功能，改名“机械臂使能”；当前带清错/速度副作用，不应静默并入连接 |
| “开启/关闭视觉” | 启停RealSense采集Python进程及HTTP预览，切页还会停止预览 | 改名“开启/关闭相机预览”，独立保留；不是启动视觉精调算法 |
| “启动/关闭程序” | 启动或附着本机拧紧Python服务，查询状态/曲线；正式流程等待时同一按钮又用来人工确认继续 | 服务启停移到“拧紧服务/调试”；人工确认继续独立保留为流程动作，不跟连接服务混用 |
| “重置底盘” | Robokit.ResetPosition发送2002，isAuto=true，请求自动重定位 | 不是重新连接、回原点或普通清零；改名“底盘自动重定位”，移入调试/高级操作，不跟连接自动执行 |

RunScene还有一个active=0的“Connect (1)”和active=0的“MoveRelative”对象，不是当前真实运行常用入口。暂不因本轮整理删除遗留对象或改场景；后续若清理需先核对引用。StatusReal本身初始inactive是仿真/真实模式切换安排，不能误判上述真实运行按钮都已禁用。

## 建议的最小布局

主界面只设一个“设备连接”入口，展开后机械臂、底盘、升降缸分别显示IP/状态/连接或断开。需要便利操作时，可增加范围明确的“连接全部设备/断开全部设备”，但只针对这三种直接通信设备，不偷偷启动相机、拧紧Python、上电使能或重定位；某个连接失败不阻塞界面，也不吞掉其他设备的独立结果。

机械臂上电/使能放在该面板的机械臂分组或独立“设备准备”区域，明确与网络连接不同。本轮不建议直接自动串成“连接→清错→上电→使能”。机械臂控制模式确认、反馈健康与上电/使能状态应分别展示，不能只有一个“已连接”就代表可运动。

相机预览、拧紧服务是本地服务管理，作为独立分组保留，不要求每次只挪底盘也要启动它们。拧紧服务在线与电批TCP在线应分开显示；相机进程存在不等于已收到新图像。无需新增手动“连接反馈端口/网关/曲线”按钮，这些仍由现有设备/服务内部管理。

现场面板保留参数、无设备环境检查、反馈诊断、分阶段单测和流程确认；连接按钮可以改成打开同一个设备面板。底盘手动允许保留一个就近“连接底盘”快捷入口，但它也必须共用同一地址/函数。主界面的“开始运行”和人工“确认并拧紧”是流程按钮，不合并到设备连接；原手动电批正转/反转/停止功能也不因连接整理删除。

## 合并前需处理的具体问题

1. **统一地址来源。** 底部Scene按钮直接使用当前控制器字段；现场ConnectDevices会从JSON覆盖IP/端口，底盘手动沿用当前字段。统一面板应显示与实际本次连接一致的值，并与正式运行冻结配置核对；不要让两处界面互相悄悄覆盖。已连接设备变更地址需走明确断开/重连，不用绿灯掩盖端点不一致。
2. **连接与执行分离。** Dobot.Connect内部还有RequestControl握手及反馈连接；EnableRobot有清错/速度动作。Python电批tcp_receive成功连接后会reset_driver（复位/停止）。因此服务启动不应伪装成无副作用的普通网络连接，更不能在新“连接全部”里顺便执行正转/反转或重定位。
3. **正式流程已准备拧紧服务。** FieldPanel.RunAsync非Vision阶段会调用PrepareFieldServiceAsync，后者在未就绪时StartProgramAsync，并核对服务版本、模式、电批地址与接触配置。因此日常全流程不必再要求先手工“启动程序”，该入口主要用于单独调试/查看服务状态。
4. **保留按设备故障定位。** 底盘单独挪车不应被机械臂/电缸不可达阻止；仅机械臂诊断也不应强求底盘成功。连接可汇总显示，但失败原因和重试对象必须分开。
5. **停止不能并入断开。** 当前“立即停止”由电批控制器处理，空闲独立模式主要停电批，正式流程占用时转给流程停止；新底盘手动另有底盘软件停止。不能称现有一个按钮在任意状态下都是整机急停。后续统一界面需明确停止范围，并保留停止/待核对时不切断必要通道、不断开后自动续跑的现有保护。

## 主要证据位置

- Assets/SimulationPlatform/Scenes/RunScene.unity：ConnectBtn约39194行三目标；DisconnectBtn约36997行三目标；ConnectBaseBtn约53735行；PowerOn/EnableRobot/ResetPosition持久化绑定。
- Assets/DobotController.cs：ConnectAsync/ConnectCoreAsync、PowerOn、EnableRobot、Disconnect。
- Assets/RobokitController.cs：ConnectAllAsync、ResetPosition、DisConnect；ManualJog partial保留锁定与恢复要求。
- Assets/LiftCylinderController.cs：ConnectAsync、Disconnect。
- Assets/FieldCommissioningPanel.cs：ConnectDevices、RunAsync的PrepareFieldServiceAsync调用；Assets/FieldCommissioningPanel.ChassisJog.cs：ConnectJogChassisAsync。
- Assets/ServoTighteningController.cs：ToggleProgram、PrepareFieldServiceAsync、HandleStopButton、UpdateButtonState。
- Assets/VisionImageReceiver.cs：StartVideo/StopVideo/EnsureServerStarted。
- ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py：tcp_receive连接成功后reset_driver。

本轮仅完成审查。待用户确认界面与合并范围后再实现，并补连接状态/单设备失败/配置一致性/流程占用/断开停止回归；GUI布局仍需Unity人工验收。
