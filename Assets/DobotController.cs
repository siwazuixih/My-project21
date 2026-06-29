using System;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using TMPro; // 如果你在 UI 中使用了 TextMeshPro 的 InputField
using System.Threading.Tasks; // 用于异步任务   

public class DobotController : MonoBehaviour
{
    #region 事件定义
    // 统一的事件委托和事件
    public event DobotEventHandler OnDobotEvent;
    #endregion

    [Header("TCP 配置")]
    public string robotIP = "127.0.0.1"; // 默认指向本机，配合虚拟服务端测试
    public int dashboardPort = 29999;
    public int feedbackPort = 30005;

    private TcpClient tcpClient;
    private NetworkStream networkStream;
    private TcpClient feedbackClient;
    private NetworkStream feedbackStream;
    private bool feedbackLoopRunning;
    private readonly object feedbackLock = new object();

    [Header("6轴角度输入框")]
    public TMP_InputField[] jointInputs; // 声明一个数组来存放6个输入框

    [Header("UI 绑定")]
    public TextMeshProUGUI speedTextDisplay;
    public TextMeshProUGUI robotModeTextDisplay;
    public TextMeshProUGUI speedScalingTextDisplay;
    public TextMeshProUGUI velocityRatioTextDisplay;
    public TextMeshProUGUI[] actualJointTextDisplays;

    [Header("机械臂实时状态 (30005反馈)")]
    public int currentRobotMode = 0;
    public string currentRobotModeText = "UNKNOWN";
    public double speedScaling = 0;
    public byte velocityRatio = 0;
    public double[] actualJoints = new double[6];
    public double[] actualJointSpeeds = new double[6];
    public long feedbackSequence = 0;
    public double lastFeedbackUnixSeconds = 0;



    void Update()
    {
        if (UIUtil.IsPointerOverUI()) return;
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

        RefreshFeedbackUI();
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


    public void Connect()
    {
        try
        {
            tcpClient = new TcpClient(robotIP, dashboardPort);
            networkStream = tcpClient.GetStream();
            Debug.Log("<color=green>TCP 连接成功！</color>");

            // 触发连接成功事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateConnectionEvent(
                true, $"成功连接到 {robotIP}:{dashboardPort}", "Dashboard"));


            // 🌟 新增：连接成功后，启动独立的异步监听任务！
            _ = ReceiveLoopAsync();

            SendCommand("RequestControl()");
            ConnectFeedback();
        }
        catch (Exception e)
        {
            Debug.LogError("<color=red>TCP 连接失败: </color>" + e.Message);

            // 触发连接失败事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateConnectionEvent(
                false, $"连接失败: {e.Message}", "Dashboard"));

            // 触发错误事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateErrorEvent(e.Message, "Connect"));
        }
    }
    public void PowerOn()
    {
        SendCommand("PowerOn()");
    }


    public void Disconnect()
    {
        try
        {
            // 1. 先关闭数据流
            if (networkStream != null)
            {
                networkStream.Close();
                networkStream = null;
            }

            // 2. 再关闭 TCP 客户端
            if (tcpClient != null)
            {
                tcpClient.Close();
                tcpClient = null;
            }

            CloseFeedbackConnection();

            bool isConnected = false;
            Debug.Log("<color=yellow>TCP 连接已安全断开。</color>");

            // 触发断开连接事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateConnectionEvent(
                false, "连接已安全断开", "Dashboard"));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"断开连接时发生错误: {e.Message}");

            // 触发错误事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateErrorEvent(e.Message, "Disconnect"));
        }
    }


    public void EnableRobot()
    {
        SendCommand("ClearError()"); 
        SendCommand("EnableRobot()"); 
        SetRobotSpeed(3); // 默认10速度

    }

    // 设置全局运行速度 (1 ~ 100)
    public void SetRobotSpeed(float speedRatio)
    {
        // 安全保护：防止输入的数字超过 1-100 的范围
        int speed = Mathf.RoundToInt(Mathf.Clamp(speedRatio, 1, 100));        
        string cmd = $"SpeedFactor({speed})";
        SendCommand(cmd);
        if (speedTextDisplay != null)
        {
            speedTextDisplay.text = "速度: " + speed + "%";
        }
    }
    public void MoveJoints(double[] joints)
    {
        if (joints.Length < 6) return;

        string jointStr = string.Join(",", Array.ConvertAll(joints, j => j.ToString("F2")));

        string cmd = $"MovJ(joint={{{jointStr}}}, cp=50)";
        SendCommand(cmd);
    }

    public void StartSimulatorFollowDemo()
    {
        SendCommand("StartFollowDemo()");
    }

    public void StopSimulatorFollowDemo()
    {
        SendCommand("StopFollowDemo()");
    }

    // 专门给 Unity UI 按钮调用的测试方法（无参数，按钮能识别它）
    public void TestMoveBtnClick()
    {
        double[] fakeJoints = new double[6];
        // 使用 TryParse 进行安全转换：
        // 如果框里有正常数字，它就会把数字塞进对应的数组位置里；
        // 如果框是空的，或者填了乱码，它也不会报错，那个位置就保持默认的 0.0
        double.TryParse(jointInputs[0].text, out fakeJoints[0]);
        double.TryParse(jointInputs[1].text, out fakeJoints[1]);
        double.TryParse(jointInputs[2].text, out fakeJoints[2]);
        double.TryParse(jointInputs[3].text, out fakeJoints[3]);
        double.TryParse(jointInputs[4].text, out fakeJoints[4]);
        double.TryParse(jointInputs[5].text, out fakeJoints[5]);
        // 调用咱们真正发指令的方法
        MoveJoints(fakeJoints);
    }

    private void SendCommand(string cmd)
    {
        if (networkStream == null || !tcpClient.Connected)
        {
            Debug.LogWarning("未连接到 TCP 服务端，请先按 1 建立连接！");

            // 触发命令发送失败事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateCommandEvent(
                cmd, false, "未连接到 TCP 服务端"));
            return;
        }
        
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(cmd + "\n"); // 越疆指令通常需要带换行符作为结束
            networkStream.Write(data, 0, data.Length);
            Debug.Log($"<color=#00FFFF>Unity 发送指令:</color> {cmd}");

            // 触发命令发送成功事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateCommandEvent(cmd, true));

        }
        catch (Exception e)
        {
            Debug.LogError("发送指令失败: " + e.Message);

            // 触发命令发送失败事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateCommandEvent(cmd, false, e.Message));

            // 触发错误事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateErrorEvent(e.Message, "SendCommand"));
        }
    }

    private void OnApplicationQuit()
    {
        // 确保退出 Unity 时释放端口
        if (networkStream != null) networkStream.Close();
        if (tcpClient != null) tcpClient.Close();
        CloseFeedbackConnection();
    }

    // 需要在脚本顶部确保引入了 System.Threading.Tasks;

    private async System.Threading.Tasks.Task ReceiveLoopAsync()
    {
        byte[] buffer = new byte[1024];
        try
        {
            // 只要连接还在，就一直死循环监听
            while (tcpClient != null && tcpClient.Connected && networkStream != null)
            {
                // 异步等待，这里不会卡死主线程。如果没有数据，它就在这里安静等待
                int bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length);
                
                if (bytesRead > 0)
                {
                    string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    // 去掉末尾多余的换行符，保持日志干净
                    response = response.TrimEnd('\r', '\n'); 
                    Debug.Log($"<color=yellow>服务端返回:</color> {response}");

                    // 触发响应接收事件
                    OnDobotEvent?.Invoke(this, DobotEventArgs.CreateResponseEvent(response, "Dashboard"));
                }
                else
                {
                    // 如果读到 0 字节，说明服务端主动断开了连接
                    Debug.LogWarning("服务端主动断开了连接。");

                    // 触发连接断开事件
                    OnDobotEvent?.Invoke(this, DobotEventArgs.CreateConnectionEvent(
                        false, "服务端主动断开了连接", "Dashboard"));
                    break;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("接收任务结束或连接异常: " + e.Message);

            // 触发错误事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateErrorEvent(e.Message, "ReceiveLoopAsync"));
        }
    }

    private void ConnectFeedback()
    {
        try
        {
            CloseFeedbackConnection();
            feedbackClient = new TcpClient(robotIP, feedbackPort);
            feedbackStream = feedbackClient.GetStream();
            feedbackLoopRunning = true;
            _ = FeedbackLoopAsync();
            Debug.Log($"<color=green>机械臂反馈端口 {feedbackPort} 连接成功。</color>");

            // 触发反馈连接成功事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateConnectionEvent(
                true, $"成功连接到反馈端口 {robotIP}:{feedbackPort}", "Feedback"));
        }
        catch (Exception e)
        {
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
        byte[] packet = new byte[1440];

        try
        {
            while (feedbackLoopRunning && feedbackClient != null && feedbackClient.Connected && feedbackStream != null)
            {
                int bytesRead = await ReadExactAsync(feedbackStream, packet, packet.Length);
                if (bytesRead != packet.Length)
                {
                    Debug.LogWarning("机械臂反馈数据长度异常，反馈循环结束。");

                    // 触发错误事件
                    OnDobotEvent?.Invoke(this, DobotEventArgs.CreateErrorEvent(
                        "机械臂反馈数据长度异常", "FeedbackLoopAsync"));
                    break;
                }

                ParseFeedbackPacket(packet);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("机械臂反馈监听结束或连接异常: " + e.Message);

            // 触发错误事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateErrorEvent(e.Message, "FeedbackLoopAsync"));
        }
    }

    private async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int length)
    {
        int total = 0;
        while (total < length)
        {
            int read = await stream.ReadAsync(buffer, total, length - total);
            if (read <= 0) break;
            total += read;
        }
        return total;
    }

    private void ParseFeedbackPacket(byte[] packet)
    {
        ushort messageSize = BitConverter.ToUInt16(packet, 0);
        if (messageSize != 1440) return;

        lock (feedbackLock)
        {
            EnsureFeedbackArrays();
            currentRobotMode = (int)BitConverter.ToUInt64(packet, 24);
            currentRobotModeText = RobotModeToText(currentRobotMode);
            speedScaling = BitConverter.ToDouble(packet, 64);
            velocityRatio = packet[1016];

            for (int i = 0; i < 6; i++)
            {
                actualJoints[i] = BitConverter.ToDouble(packet, 432 + i * 8);
                actualJointSpeeds[i] = BitConverter.ToDouble(packet, 480 + i * 8);
            }

            feedbackSequence++;
            lastFeedbackUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

            // 触发反馈数据更新事件
            OnDobotEvent?.Invoke(this, DobotEventArgs.CreateFeedbackEvent(
                currentRobotMode, currentRobotModeText, speedScaling, velocityRatio,
                (double[])actualJoints.Clone(), (double[])actualJointSpeeds.Clone()));
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
        int mode;
        string modeText;
        double scaling;
        byte ratio;
        double[] joints = new double[6];

        lock (feedbackLock)
        {
            EnsureFeedbackArrays();
            mode = currentRobotMode;
            modeText = currentRobotModeText;
            scaling = speedScaling;
            ratio = velocityRatio;
            Array.Copy(actualJoints, joints, 6);
        }

        if (robotModeTextDisplay != null)
            robotModeTextDisplay.text = $"Mode: {mode} {modeText}";
        if (speedScalingTextDisplay != null)
            speedScalingTextDisplay.text = $"SpeedScaling: {scaling:F2}";
        if (velocityRatioTextDisplay != null)
            velocityRatioTextDisplay.text = $"JointSpeed: {ratio}%";

        if (actualJointTextDisplays != null)
        {
            int count = Mathf.Min(actualJointTextDisplays.Length, 6);
            for (int i = 0; i < count; i++)
            {
                if (actualJointTextDisplays[i] != null)
                    actualJointTextDisplays[i].text = $"J{i + 1}: {joints[i]:F2}°";
            }
        }
    }

    private string RobotModeToText(int mode)
    {
        switch (mode)
        {
            case 1: return "初始化";
            case 2: return "抱闸松开";
            case 3: return "下电";
            case 4: return "未使能";
            case 5: return "使能空闲";
            case 6: return "拖拽模式";
            case 7: return "运行中";
            case 8: return "单次运动";
            case 9: return "报警";
            case 10: return "暂停";
            case 11: return "碰撞";
            default: return "UNKNOWN";
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
        feedbackStream?.Close();
        feedbackClient?.Close();
        feedbackStream = null;
        feedbackClient = null;
    }
}
