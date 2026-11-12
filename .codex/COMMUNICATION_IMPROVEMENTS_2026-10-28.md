# 通信连接与断开：本次改造说明

日期：2026-10-28。基于本地`release/software-v1.0`、`e906217`继续修改源码；未改变已发布标签或旧运行包。此前问题清单见[通信生命周期审查](CONNECTION_LIFECYCLE_AUDIT_2026-10-28.md)，那是修改前的历史诊断，不代表本次全部仍未处理。

## 改造原则

不增加通信框架，不修改厂家命令、端口、坐标映射或同学的拧紧运动算法。只采用常见的几项处理：异步连接、明确等待期限、重复连接复用、取消后关闭本次资源、旧动作不得进入新连接、流程占用时先停止而不抢先断线。

TCP连上、命令被接受、收到有效反馈、实物到位/停稳是不同证据。本次没有把它们合并成一个“成功”，也没有增加自动重发运动或断网续跑。

实现参考了[Microsoft TcpClient.ConnectAsync](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.tcpclient.connectasync?view=net-6.0)的非阻塞连接方式与[Python socket](https://docs.python.org/3/library/socket.html)的超时/显式释放方式。按[Unity 2022的.NET环境](https://docs.unity3d.com/2022.3/Documentation/Manual/overview-of-dot-net-in-unity.html)选择兼容重载，未直接套用较新.NET才有的ConnectAsync(token)/Task.WaitAsync。

## 用户会看到的变化

1. 点连接后会显示连接中/失败原因；缺一台设备不再在同步TCP连接中卡住Unity主线程。底盘三个端口并行尝试，不再逐个叠加TCP超时。已有连接且端点一致时复用，连点不会开出多组连接。
2. 正常断开机械臂会同时关控制口和反馈口；断开后的迟到连接/控制权应答不能把它重新接回去。反馈失效、控制口仍健康时，空闲下再点连接可以只修复反馈口，不必先把控制口一起断开。
3. 机器人状态灯要控制口正常且反馈新鲜才显示完整已连接；控制已连但无有效反馈显示未就绪。底盘按三端口汇总，不再用最后一个成功端口覆盖失败结果。旧底盘模式仍按原条件只要求所需端口，部分连接提示不会自动放宽流程检查。
4. 正式流程占用时点断开会先请求停止、保留通信。等流程结束/停止确认解除锁后，需要再点一次断开；不会自动断线或自动续跑。底盘独立受控动作/停止请求在途时也不提前关闭停止通道。
5. 电缸替换连接前先使旧动作失效，释放旧Modbus master和TCP。旧动作不会在新连接上继续写目标/触发。读取异常后关闭失效连接、撤销反馈新鲜度，而不是继续拿旧值显示正常。
6. Python控制口先绑定成功，再启动电批连接线程；端口冲突时新实例在接触电批前退出。软件已经持有仍活着但控制口不响应的Python进程时，会报错保留该引用，不重复启动第二个。场景中隐藏真实状态UI不会自行关闭正式流程占用的服务。
7. 相机采集超过2秒没有新帧时，HTTP返回既有错误路径，不再把最后一帧反复当实时画面成功返回。这只检查采集新鲜度，不是算法结果精度/处理延迟证明。
8. 展开日志可勾选“过滤机械臂反馈（仅日志）”；它仅过滤周期文字，不断开反馈，也不影响到位/停止判断或文件报告。

## 等待时间与配置位置

| 项目 | 当前默认/规则 | 修改位置 |
| --- | --- | --- |
| 每个设备TCP建连 | 3秒，可取消；底盘三端口并行 | DobotController、RobokitController、LiftCylinderController的Inspector：connectionTimeoutSeconds |
| 机械臂控制握手/命令应答 | 沿用原3秒交换期限 | DobotController.Gateway，不因连接修改而取消应答检查 |
| 电缸Modbus读/写 | 各1000ms，最小100ms；库内Retries=0 | LiftCylinderController的Inspector：ioTimeoutMilliseconds |
| 电批Python套接字IO | 1秒；接收暂时无数据继续等，不立即重连 | 当前V26的tcp_receive |
| Python启动轮询 | 约15秒上限，单次状态请求可能使总时长稍延长 | ServoTighteningController |
| 相机采集旧帧 | 超过2秒拒绝作为新画面返回 | realsense_image_server.py的get_image |

3秒是每次TCP尝试，不是“整套连接/握手/所有设备就绪总共3秒”。电缸第一次监控读取和其他已有业务也各有开销。电缸读写仍在主线程同步执行：本次用有界IO和失败后关闭限制卡顿，没有承诺界面任何情况下都零停顿。若现场仍明显受1秒等待影响，再按实测决定是否单独改为串行后台IO，不在这版引入队列框架。

socket超时不证明设备停止；取消等待也不会撤回已经被设备接受的运动。电批接收无数据可能是反转/空闲模式的正常行为，因此没有添加“一秒没包就自动重启/复位”的策略。

## 退出、断线与未完成边界

- 正式现场流程锁仍持有时，使用[Application.wantsToQuit](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Application-wantsToQuit.html)暂缓Player普通退出，先请求停止并提示；停止确认后再次退出。保留主循环处理反馈，不在退出拦截中同步阻塞等待。
- 这不保护强杀、断电、崩溃、Editor直接停止Play或强制场景卸载。Unity编辑器忽略该事件返回值，必须先从界面停止；软件退出绝不能代替硬件急停。
- 空闲销毁控制器时释放机械臂双端口、电缸资源和全部网关会话。异常强制销毁时不承诺异步停止一定完成。
- Python启动超时后若进程尚活，优先保留本次进程以便安全停止，不盲目Kill，也不覆盖旧引用。未新增通用服务实例认证、按进程名清理或自动修复功能。
- 底盘3003匹配应答仍只是停止请求被接受，不是物理停稳证明。电缸原有“到位超时仍继续”规则按此前要求保留；电批STOP软件状态也未升级成设备端停止证据。不能把本次称为整机安全认证或无人值守验收。
- 相机进程的完整就绪握手、优雅退出/自动恢复，以及处理结果自身的年龄判断未在这次重做；不要把采集正常等同于视觉算法已就绪。

对应原审查清单：C01/C02/C06/C08及C10的已列清理缺口已处理；C03补普通退出拦截但仍有强制退出边界；C04补绑定顺序、实例引用与禁止重复启动，没有扩成通用进程管理器；C05补有界IO但仍依赖原工具状态模型；C07改善连接/反馈状态，不将所有运行故障都包装成新状态机；C09补采集过期拒绝；C11解决同步TCP建连，电缸同步IO仅有界化。

## 修改文件

- `Assets/DeviceTcpConnection.cs`及meta：小型兼容连接工具；只有超时/取消、端点核对和迟到失败收尾，不是运动控制层。
- `Assets/DobotController.cs`、`DobotController.Gateway.cs`：连接去重、反馈修复、双端口/全会话释放和旧监听隔离。
- `Assets/RobokitController.cs`、`RobokitController.ConfirmedMotion.cs`：并行建连、整体状态、停止在途断开保护和有界异步写入。
- `Assets/LiftCylinderController.cs`：连接替换、动作代次、Modbus期限/不隐式重试、失败和销毁释放。
- `Assets/FieldCommissioningPanel.cs`：等待底盘停止应答、普通退出拦截；`Simulation.cs`：状态灯汇总。
- `Assets/ServoTighteningController.cs`：在途服务所有权、启动失败进程引用、避免销毁门与在途Release竞争。
- `ExternalCode/unity_robot_client.py`、当前V26、`realsense_image_server.py`：独立资源释放、绑定前置/有界IO、采集旧帧拒绝。
- `tests/ConnectionLifecycleHarness/`、`tests/test_connection_lifecycle.py`及现有ChassisHarness：新回归；原日志过滤代码沿用上一轮未提交内容。

## 验证与部署

本轮无实物回归：连接生命周期23、底盘24、机械臂105、现场流程101、离线流程22、运行历史25、操作日志7、日志过滤22、Python 53通过另1跳过，合计**382通过、1跳过**。新增生命周期测试链接完整生产Dobot主类及反馈解析、电缸类，网络只连自建127.0.0.1假服务，Modbus/Unity仍是替身；Python选取函数做无硬件测试，不导入会启动设备线程的原件。

独立主工程+Editor编译0错误、47项既有警告；临时编译targets纳入新文件并跳过本机缺失分析器，未手改Unity生成的csproj。上述结果不代表Unity Play、真实按钮时序、全部黑洞/半开网络行为或设备停止已验收。

下一步按[现场操作文档](FIELD_COMMISSIONING_2026-09-16.md)开头验证：先退出Play让新脚本导入，空闲下检查连接/取消/断开/反馈恢复/状态灯和日志过滤；完整构建Linux到新目录，再在硬件急停可用、有人监护下单目标验收。不要先用运动中的拔网线测试来验证这版。未修改Scene/Prefab、ProjectSettings、持久JSON、旧TEST包、Git索引/提交/标签/远端。
