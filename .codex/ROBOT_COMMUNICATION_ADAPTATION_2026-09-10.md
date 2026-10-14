# 机械臂统一通信与 0910 拧紧流程改造说明

## 2026-09-23 补充：original分步节奏测试模式

以新提供的`ExternalCode/original/cr10af_hybrid_x.py`核对，原算法每步一次ServoP再按t等待，
并非此前推测的高频持续发点。原件不修改。`originalContactTiming`默认false，现场显式true
才授权真实节奏测试。Python传递original_timing/stop_at_end，Unity按时长监控并只在原算法
要求时发步末Stop，返回interval_elapsed而非completed。HybridExit整段结束调用finish_stage，
失败不能进入拧紧；横向搜索主体通过AST对照测试保持一致。

网关仍有坐标系/传感器/力阈值和异常停止检查，段末使用新位置稳定窗口确认（仅此测试模式）；
这不是透明TCP转发或硬实时等价适配。详细差异、部署与撤回步骤见FIELD_COMMISSIONING最新节。
原文件后续更新必须重新对照并测试；当前不自动加载original代码，避免直接连接设备。

日期：2026-09-10。基于本地 `release/software-v1.0` 的 v1.10 开发。
本次没有提交、推送、打标签，也没有修改软件版本号或场景 YAML。

## 1. 最重要的变化

机械臂始终由 Unity 的 `DobotController` 连接。Python 不再直接连接机械臂，
只向 Unity 请求位姿、力反馈和相对运动。所谓“阶段占用”是软件内部的运动权限，
不是把机器人 TCP 连接交给 Python。

```text
Unity 任务编排（ConnectCommander）
    │ 本机 9100：启动/查询/停止接触装配流程
    ▼
Python V26 桥接服务 → ContactCycle → HybridExit 力控算法
    │ 本机 19120：读取数据、申请运动、等待完成
    ▼
Unity DobotController 统一入口 → 原有机器人 Dashboard / Feedback 连接

Python V26 → 电批连接（保留原方案）
Python V26 → 本机 9101 力矩曲线 HTTP（保留原方案）
```

注意：“机械臂统一通信”不等于本次把电批、底盘、升降机也全部改为 Unity 直连。
电批仍由原 Python 程序管理。

## 2. 每个文件具体改了什么

| 文件 | 改造内容 |
| --- | --- |
| `Assets/DobotController.cs` | 改为 partial 类；普通命令也使用串行请求/应答；取消旧的独立应答读取循环，避免两处抢读同一 TCP；反馈增加力、末端位姿及报警缓存；算法占用期间拒绝普通运动指令。 |
| `Assets/DobotController.Gateway.cs` | 新增同一个 DobotController 类的通信实现；本机监听、阶段占用、请求白名单、模拟后端、真实运动许可、停止/超时处理。无需额外挂组件。 |
| `ExternalCode/unity_robot_client.py` | 新增 Python 通信适配器；算法统一调用这个文件，不接触机器人 IP/端口或任意原始命令。 |
| `ExternalCode/cr10af_hybrid_x.py` | 移除原 Dashboard/Feedback 直连实现；替换读取和运动接口；保留工具 X 进退、Y/Z 搜索以及力阈值判断主体。这个文件原本是用户补齐的未跟踪文件，上传时应一起加入。 |
| `ExternalCode/servo_contact_cycle.py` | 新增卡入→等待→拧紧→等待→退出的阶段管理，记录流程编号、阶段、结果和错误。 |
| 正式 V26 Python 脚本 | 增加 `auto_cycle`、`auto_cycle_sim` 和流程状态字段；保留已有 Unity 桥接、曲线、保存与电批通信；自动流程期间拦截额外手动启动；停止会同时取消自动流程。 |
| `Assets/ServoTighteningController.cs` | 增加等待整个接触装配完成的 API；传递网关端口给 Python；退出服务前等待流程释放；原手动拧紧按钮仍保留原语义。 |
| `Assets/ConnectCommander.cs` | 离线全流程中原先“等待两秒假定拧紧完成”改为调用 Python 模拟接触装配，成功后才进入复位和下一目标。 |
| `tests/` | .NET 网关测试宿主和 Python 跨语言离线测试；直接编译实际网关源文件，但 Unity API/机器人反馈由宿主替代。 |

0910 原始拧紧脚本继续作为原始资料保存，不直接替换正式启动文件。
原因是原始版缺少我们已有的 Unity 桥接和曲线接口；本次把它新增的装配时序移入
`servo_contact_cycle.py`，接入现有 V26，而不是同时运行两套拧紧服务。

## 3. 一个目标现在怎么运行

离线全流程：原粗定位任务 → 模拟视觉轨迹 → 接触装配 → 机械臂复位 → 下一目标。

接触装配内部：

1. 申请 Unity 阶段权限。
2. `INSERTING`：工具 -X 卡入，默认计划 20 mm，轴向步长 2 mm、速度 1.2 mm/s。
3. `BEFORE_TIGHTEN`：等待 5 秒，持续查询状态维持会话。
4. `TIGHTENING`：真实模式启动电批并等终态；模拟模式等待 2 秒，不调用真实电批。
5. `BEFORE_EXIT`：等待 5 秒。
6. `EXITING`：工具 +X 退出，默认计划 20 mm。
7. 确认停止并释放阶段权限，再返回终态。

真实电批 `OK` 才能得到整体成功。沿用同学的策略：`NG_SLIP`/`JAM` 可进入退出阶段，
但退出完成后仍然是失败，不能当作合格装配继续下一目标。
其他错误、取消、超时或释放失败也不继续下一目标。

算法每步现在等待 Unity 的到位确认及停止确认，这是与原始连续发送方式的区别，
会影响节拍和接触行为，必须实机重新验证。`moved_mm` 仍是已完成请求的计划步长之和，
不是独立传感器测得的累计插入深度。

## 4. 本机接口与坐标约定

TCP `127.0.0.1:19120`，UTF-8 JSON，每条以换行结尾，一条请求对应一条应答。
`id` 用于对应请求，应答必须 `ok=true`；同一连接顺序执行请求。

| op | 含义 |
| --- | --- |
| `acquire` | 指定 `simulate`，申请占用并返回会话 token；同一时刻只允许一个持有者。 |
| `status` | 返回反馈来源、年龄、模式、报警、位姿、关节、力等。 |
| `pose` / `wrench` | 当前位姿 / 指定工具编号的六维力。 |
| `move_relative` | `delta=[dx,dy,dz]`、`duration`；完成后返回 `result=completed`。 |
| `stop` | 允许独立本机连接停止，避免排在当前算法运动请求后面；停止锁存后不能继续运动。 |
| `release` | 确认停止后释放占用。 |

真实模式：相对运动是**机器人当前工具坐标系、毫米**，时间为秒；位姿为机器人
GetPose 返回的 `[x,y,z,rx,ry,rz]`（毫米/角度），不是 Unity 世界坐标。
关节反馈为机器人六关节角度；不把底盘/升降轴冒充第七个机器人关节。
六维力顺序 `[Fx,Fy,Fz,Mx,My,Mz]`，单位 N、Nm；反馈字段的参考系需要实机核对。
Unity 通过 `RelPointTool` 求目标，`ServoP` 执行，再用同一配置下的 `GetPose` 检查位置。

模拟模式：仅维护内存位姿，工具坐标按单位旋转处理，力恒为零。
它验证通信与流程，不模拟接触力、摩擦、碰撞或插接效果，也不会把这段卡入/退出
映射成 Unity 中机械臂的逐帧运动。粗定位和视觉轨迹仍使用原有离线画面播放。

未来视觉程序可以复用 `UnityRobotClient`，但本次接口只实现相对 XYZ 运动，
**还没有实现任意六/七轴关节轨迹下发**。视觉协议确定后，需要单独增加轨迹校验和执行接口。
不允许视觉程序绕过此入口另开机器人连接。

## 5. 开关、错误处理与边界

- `DobotController.allowExternalRealMotion` 默认 false，Python 不能通过请求将它打开。
- 真实自动电批还需要原 `enableRealToolMotion` 许可。现有“全流程验证（模拟）”
  仍调用 `auto_cycle_sim`，本次没有把这个复选框改为自动运行实物。
- `contactTool` 默认 1，与算法参数一致；修改工具编号时双方必须一致。
- 网关默认单步最大 5 mm、速度最大 5 mm/s。这是单条接触指令的边界，
  不是整个视觉任务的 50 mm 距离限制；不会限定整个轨迹只能走 5 mm。
- 真实模式要求反馈年龄不超过 0.25 秒、允许的机器人模式、无报警/碰撞，
  且力/力矩有限且未超过配置阈值。默认 30 N / 10 Nm。
- 默认到位位置误差 0.2 mm；命令应答不等于到位，运动确认失败不会报成功。
- Dashboard 读写串行；应答超时关闭连接，防止迟到应答被当作下一条结果。
- 空闲请求超时 10 秒；运动有自身时间边界。断线清理由会话处理检测后触发，
  不是硬实时断线急停。
- 普通运动和算法阶段互斥；停止异常时保留占用，不能假装已释放。
- 应用退出、系统崩溃或网络断开不能保证软件停止命令送达；软件停止接口不是硬件急停。
  真实试验仍须现场人员、硬件急停和受控验证。

## 6. 现在怎么测试

先关闭旧版 Python 服务，退出 Unity Play Mode，等待脚本导入。
保留真实运动许可关闭，不需要连接机器人或电批。
进入原来的全流程验证页面，生成任务后勾选“全流程验证（模拟）”，开始运行。
日志应依次出现上述接触阶段，最后 `SIMULATED_OK`，再复位/下一目标。
这次单目标会比之前多等一段时间，因为真的执行了算法请求循环和两次 5 秒等待。
中途停止后不应出现该目标完成并继续下一目标。

自动测试（需要 .NET 8，仅测试工具需要；Unity 项目目标框架不变）：

```bash
dotnet build tests/RobotGatewayHarness/RobotGatewayHarness.csproj
PYTHONDONTWRITEBYTECODE=1 python3 tests/test_robot_gateway.py
```

本次结果：6 项通过。覆盖真实运动默认拒绝、占用互斥、相对运动完成、
越界与停止锁存、独立连接停止、模拟完整接触时序、运动中取消。
其中流程测试缩短位移和等待时间，不代表实际完整时长/真实力控已验证。
四个改动 Python 文件 AST 语法检查通过。
实际 Unity 引用的 `Assembly-CSharp.csproj` 编译 0 错误、28 警告（含 JSON 反序列化字段未显式赋值）。
由于生成的 csproj 尚未纳入新增 partial 文件，验证时通过 `/tmp` 的临时 MSBuild targets
显式加入该源文件；未永久修改生成的 csproj。Unity 批处理被已打开项目的锁阻止，
仍需当前 Editor 自动导入后确认 Console 和场景运行。

## 7. 实机接入前仍需确认

1. 机器人固件对应的 Dashboard 应答、ServoP 参数、工具编号及 User/Tool 坐标配置。
2. 当前反馈端口是否按 1440 字节布局发送，魔数、力/位姿、报警偏移及单位正确。
3. 低速空载检查每个相对方向、到位误差和 Stop 后模式；不能直接从完整插接开始试。
4. 校验接触力阈值、工具方向、插入深度、退出路径、每步停止对算法的影响。
5. 在当前 Unity Editor 回归普通机械臂命令、连接/断开、停止、曲线和多目标取消。
6. 当前真实接触 API 已实现，但整套实物粗定位→视觉→接触的现场联调尚未完成。

本轮完成的是统一通信改造和离线验证，不是实机验收。

## 8. 汇报用双端通信日志（2026-09-10 补充）

无需开关，执行请求时自动生成 `[RobotTrace]` 日志。先停止旧版Python服务，
退出Play等待编译，再进入软件，由软件重新启动V26并打开“展开日志”。
如果复用外部手动启动的Python进程，它的输出仍在原终端，软件无法接管其stdout。

- `[Python][TX]`：请求编号id、op、模拟/真实请求模式、delta毫米、duration秒及工具编号。
- `[Unity][RX]`：同一个id及收到的参数。
- `[Unity][TX]`：同一个id、是否成功、结果、返回values、单位和处理耗时handling_ms。
- `[Python][RX]`：同一个id、收到的结果/values和往返耗时roundtrip_ms；异常为`[FAIL]`。
- pose/运动返回values是毫米与角度，wrench返回values是N/Nm。
- status心跳不逐条输出；acquire/release/stop、运动、位姿和力读取有日志。
- token不输出。SIM是内存模拟数据，不是实物反馈。REAL_REQUEST只代表请求真实模式，
  不代表许可已通过或真实设备已运动。

Python stdout被软件启动器异步读取、主线程转发，所以界面中四条记录可能交错；
必须按id对照而不是仅按显示先后。队列最多500条、每帧最多转发20条，极端积压会丢弃
最早的待显示记录，这不是完整审计存储。Python stderr保持原输出方式。
Unity处理耗时从完整JSON解析后开始，Python往返耗时包含发送、读取及等待处理，
两者均不是纯网络延迟，也不能简单将差值归因为网络。

本次7项离线测试通过，C#编译0错误、27警告；Editor面板展示待用户实测。
此前完整模拟卡入在8mm触发30秒time_budget_reached的问题仍然存在，本次不改超时或算法。

## 9. 2026-09-16 联调改造（代码已接入，工控机/实物待验收）

网关已改为分块读取JSON、纯字节IO不捕获Unity帧上下文，仍保留30秒运行预算。
10项测试（含原始20mm卡入/退出、完整等待时长，不缩短参数）在无设备测试宿主通过；
仍需在Unity中对照roundtrip_ms复测。开发键盘运动快捷键默认关闭。

Linux环境检查可以在软件目录运行：

```bash
python3 ExternalCode/commissioning_preflight.py
```

输出JSON检查报告并以0代表环境检查通过；不连接设备，不代表机器人运动已通过验收。
若实际使用其他Python，应用其完整路径替换python3。

随后依据用户PDF加入底盘3055/3056应答+1020运行/完成确认，机械臂新增实际关节到位接口。
现场配置页面、参数持久化、报告导出、精调/接触分测和单/多目标编排已接入。
Python新增configure_contact配置回执，Unity核对API版本/电批端点/配置编号后才开放接触阶段。
实物过程中可启用只读反馈同步；默认运动锁定，失败/停止不复位或推进下一目标。
升降超时继续的旧规则按用户要求保留（不是到位保证），只修补停止后的延迟触发问题。
完整操作、参数单位、文件对应关系、未验收边界见
[FIELD_COMMISSIONING_2026-09-16.md](FIELD_COMMISSIONING_2026-09-16.md)。
本次无硬件测试通过不代表现场通过；未构建Linux包、未进行真实设备运动。

## 2026-10-08：末端实测与失败取证补充

- Unity新增MotionDiagnostics partial，仍统一持有机器人TCP；Python在同一网关会话发送begin_stage/end_stage标记，无新的机器人直连。GetPose和RelPointTool均由Unity发送，采样从已有反馈取得，不使用仿真Transform推算实物位移。
- Python服务新增robot_insert/robot_withdraw及模拟对应命令，共享ContactCycle/HybridExit，跳过电批拧紧和缓冲阶段；成功状态ROBOT_ONLY_DONE仅表示算法单测结束。原始脚本保持不动，原ServoP分步节奏保持，测量开销与真实位移需现场检查。
- 状态新增motion_diagnostics_supported/robot_only_supported及cycle_first_error/cycle_failure_json/cycle_cleanup_errors/cycle_workflow。Unity要求新能力标志，避免旧服务混用；首因在Python清理前保存，C#即使收到取消仍按本次cycle_id读取，停止/释放错误追加不覆盖。
- 报告motion.csv记录原始请求、目标、实际姿态/关节角/时间/坐标系及测量有效性，cycle-failure.json保存周期首因。原来的moved_mm与interval_elapsed依旧是请求累计/时间结束，不能当实测行程。具体字段、单测步骤和部署方法见现场操作说明2026-10-08段。
