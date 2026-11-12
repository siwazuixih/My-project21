using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using TMPro; 

// ==========================================
// 1. 数据结构定义区 (用于解析底盘返回的 JSON)
// ==========================================
[System.Serializable]
public class RobotLocationData
{
    public int ret_code;
    public float x;
    public float y;
    public float angle;
    public float confidence;
}

[System.Serializable]
public class NavigationStatus
{
    public int ret_code;
    public int task_status; // 0 NONE / 2 RUNNING / 3 SUSPENDED / 4 COMPLETED / 5 FAILED / 6 CANCELED
    public int task_type;
    public float distance;     // 剩余距离
    public string target_name; 
}

// ==========================================
// 2. 主控制器类
// ==========================================
public partial class RobokitController : MonoBehaviour
{
    #region 事件定义
    public event DobotEventHandler OnRobokitEvent;
    #endregion

    [Header("底盘网络配置")]
    public string robotIP = "192.168.1.100";
    [SerializeField] private bool enableDebugKeyboardShortcuts = false;
    [SerializeField] private float connectionTimeoutSeconds = 3f;

    [Header("底盘实时数据监测 (可在面板查看)")]
    public float currentX;
    public float currentY;
    public string currentNavState = "NONE";
    public float remainDistance;

    [Header("UI 输入框绑定")]
    public TMP_InputField inputDist;  // 平动距离
    public TMP_InputField inputVx;    // x平动线速度
    public TMP_InputField inputVy;    // y平动线速度
    public TMP_InputField inputTargetX; // 导航目标X
    public TMP_InputField inputTargetY; // 导航目标Y
    public TMP_InputField inputFieldAngle; // 导航目标角度

    // TCP 客户端与流
    private TcpClient statusClient, controlClient, navClient;
    private NetworkStream statusStream, controlStream, navStream;
    private bool isStatusOn, isControlOn, isNavOn;
    private System.Threading.CancellationTokenSource connectionCancellation;
    private Task connectionTask;
    private Task stopRequestTask;
    private bool connecting;
    public bool ViewConnecting => connecting;
    public bool ViewHasConnection => DeviceTcpConnection.IsOpen(statusClient) || DeviceTcpConnection.IsOpen(controlClient) || DeviceTcpConnection.IsOpen(navClient);
    public bool ViewEndpointsAligned => DeviceTcpConnection.AbsentOrMatches(statusClient, robotIP, 19204) &&
        DeviceTcpConnection.AbsentOrMatches(controlClient, robotIP, 19205) && DeviceTcpConnection.AbsentOrMatches(navClient, robotIP, 19206);
    public bool ViewAllPortsConnected => DeviceTcpConnection.Matches(statusClient, robotIP, 19204) &&
        DeviceTcpConnection.Matches(controlClient, robotIP, 19205) && DeviceTcpConnection.Matches(navClient, robotIP, 19206);
    public string ViewConnectionDescription => "状态 " + DeviceTcpConnection.Describe(statusClient) + "；控制 " +
        DeviceTcpConnection.Describe(controlClient) + "；导航 " + DeviceTcpConnection.Describe(navClient) +
        (ViewEndpointsAligned ? "" : "；实际连接与当前配置不一致");
    private long connectionGeneration;

    void Update()
    {
        if (!enableDebugKeyboardShortcuts) return;
        // 快捷键测试区 (绝对没有 [Header] 标签)
        
        // C键: 连接
        if (Input.GetKeyDown(KeyCode.C)) ConnectAll();
        
        // R键: 重定位 (初始化位置)
        if (Input.GetKeyDown(KeyCode.R)) 
            ResetPosition();

        // L键: 查位置 (刷新 currentX 和 currentY)
        if (Input.GetKeyDown(KeyCode.L)) 
            FindPositon();

        // S键: 查导航状态 (刷新 currentNavState)
        if (Input.GetKeyDown(KeyCode.S)) 
            TaskRequire();

        // N键: 站点导航 (去 Roboshop 设定的点)
        if (Input.GetKeyDown(KeyCode.N)) 
            NavigateToStation("LM1");

        // G键: 自由坐标导航 (不需要设站点，直接去绝对坐标！)
        if (Input.GetKeyDown(KeyCode.G)) 
            NavigateToCoordinate();

        // M键: 平动 (相对移动，如往前走1米)
        if (Input.GetKeyDown(KeyCode.M)) 
            MoveRelative();
    }

    // ==========================================
    // 3. 核心运动与控制方法
    // ==========================================

    public void ResetPosition()
    {
        OperationLogTool.RecordLog(OperationType.真实运行, "请求底盘重定位（非完成确认）");
        // 根据官方文档：当 isAuto 为 true 时，忽略 x, y, angle，底盘将执行自动重定位 
        string json = "{\"isAuto\":true}";
        
        Debug.Log($"<color=cyan>[底盘] 请求自动重定位...</color> JSON: {json}");
        SendCommand(2002, json); 
    }

    public async void StopMovement()
    {
        try { await StopMovementAsync(); }
        catch (Exception e) { Debug.LogError("底盘停止请求失败（不能认为已停稳）: " + e.Message); }
    }

    public Task StopMovementAsync()
    {
        CancelConfirmedMotion();
        if (stopRequestTask != null && !stopRequestTask.IsCompleted) return stopRequestTask;
        if (navStream == null) return Task.CompletedTask; // No connected command channel to stop.
        stopRequestTask = RequestConfirmedAsync(3003, "", System.Threading.CancellationToken.None);
        return stopRequestTask;
    }

    public void FindPositon()
    {
        SendCommand(1004, ""); 
    }

    public void TaskRequire()
    {
        SendCommand(1020, "{\"simple\":true}");
    }
    /// <summary>去指定的预设站点</summary>
    public void NavigateToStation(string stationId)
    {
        string json = $"{{\"id\":\"{stationId}\"}}";
        SendCommand(3051, json); 
    }

    /// <summary>去地图上的绝对坐标 (自由导航 FreeGo)</summary>
    public void NavigateToCoordinate()
    {
        float targetX, targetY, targetAngle;
        float.TryParse(inputTargetX.text, out targetX);
        float.TryParse(inputTargetY.text, out targetY);
        float.TryParse(inputFieldAngle.text, out targetAngle);
        // 使用仙工的 FreeGo 语法，id 必须是 SELF_POSITION
        string json = $"{{\"id\":\"SELF_POSITION\", \"freeGo\":{{\"x\":{targetX}, \"y\":{targetY}, \"theta\":{targetAngle}}}}}";
        SendCommand(3051, json);
    }

    /// <summary>相对移动 (平动)</summary>
    public void MoveRelative()
    {   
        float dist, vx, vy;
        float.TryParse(inputDist.text, out dist);
        float.TryParse(inputVx.text, out vx);
        float.TryParse(inputVy.text, out vy);
        string json = $"{{\"dist\":{dist}, \"vx\":{vx}, \"vy\":{vy}}}";
        SendCommand(3055, json);
    }

    // ==========================================
    // 4. 底层网络封装 (连接、路由、接收)
    // ==========================================

    public async void ConnectAll()
    {
        try { await ConnectAllAsync(); }
        catch (Exception e) { Debug.LogWarning("底盘连接未完成: " + e.Message); }
    }

    public Task ConnectAllAsync()
    {
        if (FieldWorkflowOwned || ConfirmedMotionActive || ManualJogActive || manualJogRecoveryBusy || manualJogStopBusy || (stopRequestTask != null && !stopRequestTask.IsCompleted))
        { Debug.LogWarning("底盘流程/停止确认中，禁止重连"); return Task.CompletedTask; }
        if (connecting) return connectionTask ?? Task.CompletedTask;
        if (DeviceTcpConnection.Matches(statusClient, robotIP, 19204) &&
            DeviceTcpConnection.Matches(controlClient, robotIP, 19205) && DeviceTcpConnection.Matches(navClient, robotIP, 19206))
            return Task.CompletedTask;
        connectionTask = ConnectChassisAsync();
        return connectionTask;
    }

    private async Task ConnectChassisAsync()
    {
        connecting = true;
        CloseChassisConnections();
        var lifetime = new System.Threading.CancellationTokenSource();
        connectionCancellation = lifetime;
        string host = robotIP;
        var attempts = new[] { ConnectPortAsync(host, 19204, lifetime.Token),
            ConnectPortAsync(host, 19205, lifetime.Token), ConnectPortAsync(host, 19206, lifetime.Token) };
        bool transferred = false;
        Debug.Log($"底盘三端口并行连接中（每端口超时{connectionTimeoutSeconds}秒）");
        try
        {
            var clients = await Task.WhenAll(attempts);
            lifetime.Token.ThrowIfCancellationRequested();
            statusClient = clients[0]; controlClient = clients[1]; navClient = clients[2];
            connectionGeneration++;
            statusStream = statusClient?.GetStream(); controlStream = controlClient?.GetStream(); navStream = navClient?.GetStream();
            isStatusOn = statusStream != null; isControlOn = controlStream != null; isNavOn = navStream != null;
            transferred = true; stopRequestTask = null;
            if (isStatusOn) _ = ReceiveLoopAsync(statusStream, 19204);
            if (isControlOn) _ = ReceiveLoopAsync(controlStream, 19205);
            if (isNavOn) _ = ReceiveLoopAsync(navStream, 19206);
            PublishChassisConnection();
        }
        catch (OperationCanceledException) { Debug.Log("底盘连接已取消"); }
        finally
        {
            if (!transferred) foreach (var attempt in attempts)
                if (attempt.Status == TaskStatus.RanToCompletion) attempt.Result?.Close();
            if (ReferenceEquals(connectionCancellation, lifetime)) connectionCancellation = null;
            lifetime.Dispose(); connecting = false;
        }
    }

    private async Task<TcpClient> ConnectPortAsync(string host, int port, System.Threading.CancellationToken cancel)
    {
        try { return await DeviceTcpConnection.OpenAsync(host, port, connectionTimeoutSeconds, cancel); }
        catch (OperationCanceledException) { return null; }
        catch (Exception e)
        {
            Debug.LogWarning($"底盘端口 {port} 连接失败: {e.Message}");
            OnRobokitEvent?.Invoke(this, DobotEventArgs.CreateErrorEvent(e.Message, $"ConnectPort_{port}"));
            return null;
        }
    }

    private void PublishChassisConnection()
    {
        bool complete = isStatusOn && isControlOn && isNavOn;
        string state = complete ? "底盘三端口已连接" :
            $"底盘部分连接/未连接：状态={isStatusOn}，控制={isControlOn}，导航={isNavOn}";
        OnRobokitEvent?.Invoke(this, DobotEventArgs.CreateConnectionEvent(complete, state, "Chassis"));
    }

    public async void SendCommand(ushort apiNum, string jsonPayload)
    {
        try {
            if (apiNum == 3003) CancelConfirmedMotion();
            if ((ConfirmedMotionActive || FieldWorkflowOwned || ManualJogLocked) && apiNum >= 2000 && apiNum != 3003)
                throw new InvalidOperationException("底盘闭环任务中，禁止额外运动或重定位");
            await RequestConfirmedAsync(apiNum, jsonPayload, System.Threading.CancellationToken.None);
        } catch (Exception e) {
            Debug.LogError($"发送失败: {e.Message}"); 
        }
    }

    private async Task ReceiveLoopAsync(NetworkStream stream, int port)
    {
        try {
            while (stream != null) {
                byte[] header = new byte[16];
                await ReadChassisExactAsync(stream, header);
                if (header[0] != 0x5A || header[1] != 1 || header[12] != 0 || header[13] != 0 || header[14] != 0)
                    throw new System.IO.IOException("不支持的底盘包头/路由/压缩，请核对固件协议");
                
                uint len = RobokitProtocol.ParsePayloadLength(header);
                
                if (len > 1024 * 1024) throw new System.IO.IOException("底盘响应超过1MiB");
                {
                    byte[] body = new byte[len];
                    await ReadChassisExactAsync(stream, body);
                    string jsonResponse = Encoding.UTF8.GetString(body);
                    
                    // 解析包头中的 API 编号 (第8-9字节，转小端读取)
                    byte[] apiBytes = new byte[2] { header[9], header[8] }; // 注意这里将大端翻转回小端
                    ushort resApiNum = BitConverter.ToUInt16(apiBytes, 0);

                    // 智能解析数据到 Unity 变量
                    ushort sequence = (ushort)((header[2] << 8) | header[3]);
                    if (CompleteChassisReply(stream, sequence, resApiNum, jsonResponse))
                        ParseRobotData(resApiNum, jsonResponse);
                }
            }
        } catch (Exception e) { FailChassisStream(stream, e); }
    }

    /// <summary>将收到的 JSON 翻译成 Unity 面板里的变量</summary>
    private void ParseRobotData(ushort apiNum, string json)
    {
        // 如果是查询位置的回复 (11004)
        if (apiNum == 11004)
        {
            RobotLocationData loc = JsonUtility.FromJson<RobotLocationData>(json);
            currentX = loc.x;
            currentY = loc.y;
            Debug.Log($"最新坐标更新: X={currentX}, Y={currentY}");
            
            // 触发底盘位置更新事件
            OnRobokitEvent?.Invoke(this, DobotEventArgs.CreateChassisPositionEvent(
                currentX, currentY, loc.angle));
        }
        // 如果是查询导航状态的回复 (11020)
        else if (apiNum == 11020)
        {
            NavigationStatus nav = JsonUtility.FromJson<NavigationStatus>(json);
            currentNavState = ChassisStateName(nav.task_status);
            remainDistance = nav.distance;
            Debug.Log($"导航状态更新: {currentNavState}, 剩余距离: {remainDistance}");
            
            // 触发底盘导航状态更新事件
            OnRobokitEvent?.Invoke(this, DobotEventArgs.CreateChassisNavStatusEvent(
                currentNavState, remainDistance));
        }
        else
        {
            // 其他动作的反馈直接打印
            Debug.Log($"<color=yellow>收到 API [{apiNum}] 回复:</color> {json}");
        }
    }

    public void SetRobotIP(string newIP)
    {
        robotIP = newIP;
        Debug.Log("底盘 IP 已被 UI 动态修改为: " + robotIP);
    }


    public void DisConnect()
    {
        if (ManualJogLocked)
        { _ = StopManualJogAsync(); Debug.LogWarning("底盘手动状态待核对，请停稳/解除手动锁后再断开"); return; }
        if (FieldWorkflowOwned)
        { FieldCommissioningPanel.Active?.RequestStop(); Debug.LogWarning("先停止实物流程并确认，再断开底盘"); return; }
        if (ConfirmedMotionActive || (stopRequestTask != null && !stopRequestTask.IsCompleted))
        { StopMovement(); Debug.LogWarning("底盘正在停止，请确认后再次断开"); return; }
        if (stopRequestTask != null && stopRequestTask.IsFaulted)
        { Debug.LogWarning("底盘停止请求未获确认，保留连接；请重试停止或检查现场急停"); return; }
        CloseChassisConnections();
        RunDataCapture.EndManualSession?.Invoke();
        Debug.Log("底盘所有连接已断开（不等于物理停止确认）");
        PublishChassisConnection();
    }

    private void CloseChassisConnections()
    {
        connectionCancellation?.Cancel();
        CancelConfirmedMotion();
        var status = statusStream; var control = controlStream; var nav = navStream;
        var statusSocket = statusClient; var controlSocket = controlClient; var navSocket = navClient;
        statusStream = controlStream = navStream = null;
        statusClient = controlClient = navClient = null;
        isStatusOn = isControlOn = isNavOn = false;
        FailChassisStream(status, new System.IO.IOException("底盘断开"));
        FailChassisStream(control, new System.IO.IOException("底盘断开"));
        FailChassisStream(nav, new System.IO.IOException("底盘断开"));
        statusSocket?.Close(); controlSocket?.Close(); navSocket?.Close();
    }
    void OnDestroy()
    {
        CloseChassisConnections();
        RunDataCapture.EndManualSession?.Invoke();
    }
}
