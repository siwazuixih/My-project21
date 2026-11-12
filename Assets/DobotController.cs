using System;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using TMPro; // 如果你在 UI 中使用了 TextMeshPro 的 InputField
using System.Threading.Tasks; // 用于异步任务   
using System.Threading;

public partial class DobotController : MonoBehaviour
{
    #region 事件定义
    // 统一的事件委托和事件
    public event DobotEventHandler OnDobotEvent;
    #endregion

    [Header("TCP 配置")]
    public string robotIP = "127.0.0.1"; // 默认指向本机，配合虚拟服务端测试
    public int dashboardPort = 29999;
    public int feedbackPort = 30005;
    [SerializeField] private float connectionTimeoutSeconds = 3f;
    [Header("开发调试（实物部署保持关闭）")]
    [SerializeField] private bool enableDebugKeyboardShortcuts = false;
    [SerializeField] private bool enableLoopbackSimulatorCommands = false;
    private bool controlModeConfirmed, connecting;
    public bool ViewConnecting => connecting;
    public bool ViewHasConnection => DeviceTcpConnection.IsOpen(tcpClient) || DeviceTcpConnection.IsOpen(feedbackClient);
    public bool ViewEndpointsAligned => DeviceTcpConnection.AbsentOrMatches(tcpClient, robotIP, dashboardPort) &&
        DeviceTcpConnection.AbsentOrMatches(feedbackClient, robotIP, feedbackPort);
    public bool ViewControlReady => controlModeConfirmed && DeviceTcpConnection.Matches(tcpClient, robotIP, dashboardPort);
    public string ViewConnectionDescription => "控制 " + DeviceTcpConnection.Describe(tcpClient) +
        "；反馈 " + DeviceTcpConnection.Describe(feedbackClient) + "；" +
        (HasFreshFeedback(1f) ? "有效反馈正常" : "尚无新鲜有效反馈") + "；模式：" + currentRobotModeText +
        (ViewEndpointsAligned ? "" : "；实际连接与当前配置不一致");
    private CancellationTokenSource connectionCancellation;
    private Task connectionTask;
    private long displayedFeedbackSequence;
    private string feedbackFailure;
    private bool publishedFeedbackHealthy, publishedDashboardHealthy;
    private double feedbackClockOffset = double.PositiveInfinity;

    private TcpClient tcpClient;
    private NetworkStream networkStream;
    private TcpClient feedbackClient;
    private NetworkStream feedbackStream;
    private volatile bool feedbackLoopRunning;
    private readonly object feedbackLock = new object();

    [Header("6轴角度输入框")]
    public TMP_InputField[] jointInputs; // 声明一个数组来存放6个输入框

    [Header("UI 绑定")]
    public TextMeshProUGUI speedTextDisplay;
    public TextMeshProUGUI robotModeTextDisplay;
    public TextMeshProUGUI speedScalingTextDisplay;
    public TextMeshProUGUI velocityRatioTextDisplay;
    public TextMeshProUGUI[] actualJointTextDisplays;

    [Header("机械臂实时状态（端口按连接配置）")]
    public int currentRobotMode = 0;
    public string currentRobotModeText = "未知";
    public double speedScaling = 0;
    public byte velocityRatio = 0;
    public double[] actualJoints = new double[6];
    public double[] actualJointSpeeds = new double[6];
    public long feedbackSequence = 0;
    public double lastFeedbackUnixSeconds = 0;
    public int speed;



    void Update()
    {
        RefreshFeedbackUI(); // 后台反馈事件必须每帧在主线程派发，不能被鼠标悬停UI阻断。
        if (UIUtil.IsPointerOverUI()) return;
        if (!enableDebugKeyboardShortcuts) return;
        // 测试按键 1：连接机械臂并请求控制权
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            Connect();
        }

        // 测试按键 2：使能机械臂
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            EnableRobot();
        }

        // 测试按键 3：发送一个固定的 MovJ 运动指令
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            // 随便伪造一组 6 轴角度数据，单位：度
            double[] fakeJoints = new double[] { 10.5, 20.0, -15.2, 0, 90.0, 0 };
            MoveJoints(fakeJoints);
        }

    }

    // 专门给 InputField (TMP) 调用的动态传值方法
    public void SetRobotIP(string newIP)
    {
        robotIP = newIP;
        Debug.Log("后台 IP 已被 UI 动态修改为: " + robotIP);
    }
    public void SetRobotPort(int newPort)
    {
        dashboardPort = newPort;
        Debug.Log("后台 端口 已被 UI 动态修改为: " + dashboardPort);
    }


    public async void Connect()
    {
        try { await ConnectAsync(); }
        catch (Exception e) { Debug.LogWarning("机械臂连接未完成: " + e.Message); }
    }

    public Task ConnectAsync()
    {
        if (ExternalControlActive || FieldWorkflowActive) { Debug.LogWarning("流程正在执行，不能重新连接机械臂。"); return Task.CompletedTask; }
        if (connecting) return connectionTask ?? Task.CompletedTask;
        connectionTask = ConnectCoreAsync();
        return connectionTask;
    }

    private async Task ConnectCoreAsync()
    {
        connecting = true;
        var lifetime = new CancellationTokenSource();
        connectionCancellation = lifetime;
        CancellationToken cancel = lifetime.Token;
        try
        {
            string host = robotIP;
            int dashboard = dashboardPort, feedback = feedbackPort;
            if (!controlModeConfirmed || networkStream == null || !networkStream.CanRead || !networkStream.CanWrite ||
                !DeviceTcpConnection.Matches(tcpClient, host, dashboard))
            {
                CloseRobotConnections(false);
                OperationLogTool.RecordLog(OperationType.真实运行, $"请求连接机器人（IP：{host}，端口：{dashboard}）");
                Debug.Log($"机械臂正在连接 {host}:{dashboard}（超时{connectionTimeoutSeconds}秒，界面可继续操作）");
                var opened = await DeviceTcpConnection.OpenAsync(host, dashboard, connectionTimeoutSeconds, cancel);
                if (cancel.IsCancellationRequested) { opened.Close(); cancel.ThrowIfCancellationRequested(); }
                tcpClient = opened;
                dashboardConnectionGeneration++;
                networkStream = opened.GetStream();
                await ExchangeDashboardAsync("RequestControl()", false);
                cancel.ThrowIfCancellationRequested();
                controlModeConfirmed = true;
                OnDobotEvent?.Invoke(this, DobotEventArgs.CreateConnectionEvent(
                    true, $"TCP控制模式已确认 {host}:{dashboard}", "Dashboard"));
            }
            if (!DeviceTcpConnection.Matches(feedbackClient, host, feedback) || !HasFreshFeedback(1f))
                await ConnectFeedbackAsync(host, feedback, cancel);
        }
        catch (OperationCanceledException) { Debug.Log("机械臂连接已取消"); }
        catch (Exception e)
        {
            CloseRobotConnections(false);
            Debug.LogError("<color=red>TCP 连接失败: </color>" + e.Message);

            // 触发连接失败事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateConnectionEvent(
                false, $"连接失败: {e.Message}", "Dashboard"));

            // 触发错误事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateErrorEvent(e.Message, "Connect"));
        }
        finally
        {
            if (ReferenceEquals(connectionCancellation, lifetime)) connectionCancellation = null;
            lifetime.Dispose(); connecting = false;
        }
    }
    public void PowerOn()
    {
        OperationLogTool.RecordLog(OperationType.真实运行, "请求启动机器人（PowerOn）");
        SendCommand("PowerOn()");
    }


    public async void Disconnect()
    {
        if (FieldWorkflowActive) { FieldCommissioningPanel.Active?.RequestStop(); Debug.LogWarning("先停止实物流程并确认，再断开连接"); return; }
        if (ExternalControlActive)
        {
            RevokeExternalControl();
            await AbortOwnerAsync();
            if (ExternalControlActive)
            {
                Debug.LogError("未确认算法阶段停止，暂不主动断开机器人连接。请检查设备状态。");
                return;
            }
        }
        try
        {
            // 1. 先关闭数据流
            OperationLogTool.RecordLog(OperationType.真实运行, "请求断开机器人");
            CloseRobotConnections();
            RunDataCapture.EndManualSession?.Invoke();
            Debug.Log("<color=yellow>机械臂控制口与反馈口均已断开（断线本身不是停稳证明）。</color>");

            // 触发断开连接事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateConnectionEvent(
                false, "控制口与反馈口已断开（不等于停稳确认）", "Dashboard"));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"断开连接时发生错误: {e.Message}");

            // 触发错误事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateErrorEvent(e.Message, "Disconnect"));
        }
    }


    public async void EnableRobot()
    {
        try
        {
            if (!controlModeConfirmed) throw new InvalidOperationException("TCP控制模式尚未确认，请在未使能状态重新连接");
            OperationLogTool.RecordLog(OperationType.真实运行, "请求激活机器人（EnableRobot）");
            await ExchangeDashboardAsync("ClearError()", false);
            await ExchangeDashboardAsync("EnableRobot()", false);
            SetRobotSpeed(3);
        }
        catch (Exception e) { Debug.LogWarning("使能失败: " + e.Message); }
    }

    // 设置全局运行速度 (1 ~ 100)
    public void SetRobotSpeed(float speedRatio)
    {
        if (!Finite(speedRatio)) { Debug.LogWarning("速度必须为有限数值"); return; }
        // 安全保护：防止输入的数字超过 1-100 的范围
        int speed = Mathf.RoundToInt(Mathf.Clamp(speedRatio, 1, 100));     
        this.speed = speed;
        string cmd = $"SpeedFactor({speed})";
        SendCommand(cmd);
        if (speedTextDisplay != null)
        {
            speedTextDisplay.text = "速度: " + speed + "%";
        }
    }
    public void MoveJoints(double[] joints)
    {
        if (joints == null || joints.Length != 6 || Array.Exists(joints, j => !Finite(j)))
        { Debug.LogWarning("关节目标必须为6个有限角度，未发送运动"); return; }

        string jointStr = Numbers(joints);

        string cmd = $"MovJ(joint={{{jointStr}}}, cp=50)";
        SendCommand(cmd);
    }

    public void StartSimulatorFollowDemo()
    {
        if (!CanUseSimulatorCommands()) { Debug.LogWarning("此按钮仅供本机模拟器演示；实物请使用开始运行/现场联调。"); return; }
        SendCommand("StartFollowDemo()");
    }

    public void StopSimulatorFollowDemo()
    {
        SendCommand(CanUseSimulatorCommands() ? "StopFollowDemo()" : "Stop()");
    }
    private bool CanUseSimulatorCommands() => enableLoopbackSimulatorCommands &&
        tcpClient?.Client.RemoteEndPoint is System.Net.IPEndPoint endpoint && System.Net.IPAddress.IsLoopback(endpoint.Address);

    // 专门给 Unity UI 按钮调用的测试方法（无参数，按钮能识别它）
    public void TestMoveBtnClick()
    {
        double[] fakeJoints = new double[6];
        if (jointInputs == null || jointInputs.Length != 6) { Debug.LogWarning("缺少6轴输入框"); return; }
        for (int i = 0; i < 6; i++)
            if (jointInputs[i] == null || !double.TryParse(jointInputs[i].text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out fakeJoints[i]) || !Finite(fakeJoints[i]))
            { Debug.LogWarning($"J{i+1}输入无效，未发送运动"); return; }
        // 调用咱们真正发指令的方法
        MoveJoints(fakeJoints);
    }

    private async void SendCommand(string cmd)
    {
        try
        {
            if (!controlModeConfirmed && cmd != "Stop()") throw new InvalidOperationException("TCP控制模式未确认");
            if (FieldWorkflowActive && cmd == "Stop()")
            { FieldCommissioningPanel.Active?.RequestStop(); return; }
            if (ExternalControlActive && cmd == "Stop()")
            { RevokeExternalControl(); await AbortOwnerAsync(); return; }
            if ((ExternalControlActive || FieldWorkflowActive) && cmd != "Stop()")
                throw new InvalidOperationException("力控/视觉阶段正在占用运动权限");
            string response = await ExchangeDashboardAsync(cmd, false);
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateCommandEvent(cmd, true));
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateResponseEvent(response, "Dashboard"));
        }
        catch (Exception e)
        {
            Debug.LogWarning("机械臂命令失败: " + e.Message);
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateCommandEvent(cmd, false, e.Message));
        }
    }

    private void OnApplicationQuit()
    {
        ShutdownRobotGateway();
        CloseRobotConnections();
    }

    private void OnDestroy() { CloseRobotConnections(); RunDataCapture.EndManualSession?.Invoke(); }

    private void CloseRobotConnections(bool cancelConnecting = true)
    {
        if (cancelConnecting) connectionCancellation?.Cancel();
        commandEpoch++;
        controlModeConfirmed = false;
        var stream = networkStream; var client = tcpClient;
        networkStream = null; tcpClient = null;
        try { stream?.Close(); }
        finally { try { client?.Close(); } finally { CloseFeedbackConnection(); } }
    }

    private async Task ConnectFeedbackAsync(string host, int port, CancellationToken cancel)
    {
        try
        {
            CloseFeedbackConnection();
            var opened = await DeviceTcpConnection.OpenAsync(host, port, connectionTimeoutSeconds, cancel);
            if (cancel.IsCancellationRequested) { opened.Close(); cancel.ThrowIfCancellationRequested(); }
            feedbackClient = opened;
            feedbackStream = feedbackClient.GetStream();
            lock (feedbackLock)
            { feedbackGeneration++; feedbackEndpoint = feedbackClient.Client.RemoteEndPoint.ToString(); feedbackReadError = "none"; feedbackFailure = null; }
            feedbackLoopRunning = true;
            _ = FeedbackLoopAsync();
            Debug.Log($"<color=green>机械臂反馈端口 {port} 连接成功，等待有效反馈。</color>");

            // 触发反馈连接成功事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateConnectionEvent(
                true, $"成功连接到反馈端口 {robotIP}:{feedbackPort}", "Feedback"));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            CloseFeedbackConnection();
            Debug.LogError($"<color=red>机械臂反馈端口 {feedbackPort} 连接失败: </color>{e.Message}");

            // 触发反馈连接失败事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateConnectionEvent(
                false, $"反馈端口连接失败: {e.Message}", "Feedback"));

            // 触发错误事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateErrorEvent(e.Message, "ConnectFeedback"));
        }
    }

    private async Task FeedbackLoopAsync()
    {
        // 固定本次连接，后台只做IO和锁内数值更新；Unity事件在RefreshFeedbackUI派发。
        var client = feedbackClient;
        var stream = feedbackStream;
        byte[] packet = new byte[1440];
        try
        {
            await Task.Run(async () =>
            {
                while (feedbackLoopRunning && ReferenceEquals(stream, feedbackStream) && client.Connected)
                {
                    if (await ReadExactAsync(stream, packet, packet.Length) != packet.Length)
                        throw new System.IO.IOException("机械臂反馈断开/包不完整");
                    lock (feedbackLock) if (ReferenceEquals(stream, feedbackStream)) RecordFeedbackPacket(false);
                    // 丢弃已积压的完整旧包，只发布最后一包；不触碰Unity对象。
                    while (client.Available >= packet.Length)
                    {
                        if (await ReadExactAsync(stream, packet, packet.Length) != packet.Length)
                            throw new System.IO.IOException("机械臂反馈断开");
                        lock (feedbackLock) if (ReferenceEquals(stream, feedbackStream)) RecordFeedbackPacket(true);
                    }
                    lock (feedbackLock)
                        if (ReferenceEquals(stream, feedbackStream)) ParseFeedbackPacket(packet);
                }
            });
        }
        catch (Exception e)
        {
            lock (feedbackLock)
                if (ReferenceEquals(stream, feedbackStream))
                { lastFeedbackUnixSeconds = 0; feedbackFailure = e.Message; feedbackReadError = e.Message; }
        }
    }

    public bool HasFreshFeedback(float timeoutSeconds)
    {
        lock (feedbackLock)
        {
            if (lastFeedbackUnixSeconds <= 0) return false;
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            return now - lastFeedbackUnixSeconds <= timeoutSeconds;
        }
    }

    public bool TryCopyActualJoints(double[] destination, float timeoutSeconds)
    {
        if (destination == null || destination.Length < 6) return false;

        lock (feedbackLock)
        {
            if (lastFeedbackUnixSeconds <= 0) return false;
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            if (now - lastFeedbackUnixSeconds > timeoutSeconds) return false;

            EnsureFeedbackArrays();
            Array.Copy(actualJoints, destination, 6);
            return true;
        }
    }

    private void RefreshFeedbackUI()
    {
        RunDataCapture.CheckSaveError();
        DobotEventArgs feedbackEvent = null;
        string failure;
        lock (feedbackLock)
        {
            var data = RunDataCapture.Current;
            if (data != null && !data.Simulated)
            {
                double age = lastFeedbackUnixSeconds > 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000.0 - lastFeedbackUnixSeconds : double.NaN;
                data.Robot(feedbackEndpoint,feedbackGeneration,feedbackSequence,robotTimestamp,age,
                    feedbackClient != null && age >= 0 && age <= .25,currentRobotMode,actualUser,actualTool,actualJoints,actualJointSpeeds,actualToolPose,currentCommandId);
            }
            failure = feedbackFailure; feedbackFailure = null;
            if (displayedFeedbackSequence != feedbackSequence)
            {
                displayedFeedbackSequence = feedbackSequence;
                feedbackEvent = DobotEventArgs.CreateFeedbackEvent(currentRobotMode, currentRobotModeText,
                    speedScaling, velocityRatio, (double[])actualJoints.Clone(), (double[])actualJointSpeeds.Clone());
            }
        }
        if (feedbackEvent != null) OnDobotEvent?.Invoke(this, feedbackEvent);
        if (failure != null)
        {
            CloseFeedbackConnection();
            Debug.LogWarning("机械臂反馈失败: " + failure);
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateConnectionEvent(false, "反馈中断，请检查网络后重连", "Feedback"));
        }
        bool healthy = HasFreshFeedback(1f);
        if (healthy != publishedFeedbackHealthy)
        {
            publishedFeedbackHealthy = healthy;
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateConnectionEvent(healthy,
                healthy ? "有效反馈已就绪" : "反馈已过期，不能作为设备就绪依据", "Feedback"));
        }
        bool dashboardHealthy = controlModeConfirmed && tcpClient != null && tcpClient.Connected &&
            networkStream != null && networkStream.CanRead && networkStream.CanWrite;
        if (dashboardHealthy != publishedDashboardHealthy)
        {
            publishedDashboardHealthy = dashboardHealthy;
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateConnectionEvent(dashboardHealthy,
                dashboardHealthy ? "控制通道已就绪" : "控制通道不可用，请检查后重连", "Dashboard"));
        }
        int mode;
        string modeText;
        double scaling;
        byte ratio;
        double[] joints = new double[6];

        lock (feedbackLock)
        {
            EnsureFeedbackArrays();
            mode = currentRobotMode;
            modeText = RobotModeToText(mode);
            currentRobotModeText = modeText;
            scaling = speedScaling;
            ratio = velocityRatio;
            Array.Copy(actualJoints, joints, 6);
        }

        if (robotModeTextDisplay != null)
            robotModeTextDisplay.text = $"工作模式：{mode} {modeText}";
        if (speedScalingTextDisplay != null)
            speedScalingTextDisplay.text = $"速度缩放：{scaling:F2}";
        if (velocityRatioTextDisplay != null)
            velocityRatioTextDisplay.text = $"关节速度：{ratio}%";

        if (actualJointTextDisplays != null)
        {
            int count = Mathf.Min(actualJointTextDisplays.Length, 6);
            for (int i = 0; i < count; i++)
            {
                if (actualJointTextDisplays[i] != null)
                    actualJointTextDisplays[i].text = $"关节{i + 1}：{joints[i]:F2}°";
            }
        }
    }

    private string RobotModeToText(int mode)
    {
        switch (mode)
        {
            case 1: return "初始化";
            case 2: return "抱闸松开";
            case 3: return "已下电";
            case 4: return "未使能";
            case 5: return "已使能、空闲";
            case 6: return "拖拽模式";
            case 7: return "运行中";
            case 8: return "单次运动中";
            case 9: return "报警";
            case 10: return "已暂停";
            case 11: return "碰撞状态";
            default: return "未知";
        }
    }

    private void EnsureFeedbackArrays()
    {
        if (actualJoints == null || actualJoints.Length != 6)
            actualJoints = new double[6];
        if (actualJointSpeeds == null || actualJointSpeeds.Length != 6)
            actualJointSpeeds = new double[6];
    }

    private void CloseFeedbackConnection()
    {
        feedbackLoopRunning = false;
        var stream = feedbackStream; var client = feedbackClient;
        feedbackStream = null; feedbackClient = null;
        lock (feedbackLock)
        { lastFeedbackUnixSeconds = 0; robotTimestamp = 0; protocolFeedbackSequence = 0; feedbackClockOffset = double.PositiveInfinity; }
        try { stream?.Close(); } finally { client?.Close(); }
    }
}
