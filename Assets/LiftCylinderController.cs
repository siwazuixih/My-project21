using UnityEngine;
using System;
using System.Net.Sockets;
using Modbus.Device;
using Modbus.Message;
using TMPro;
using System.Threading;
using System.Threading.Tasks;

public class LiftCylinderController : MonoBehaviour
{
    [Header("Modbus TCP 配置")]
    public string ipAddress = "192.168.192.88";
    public int port = 502;
    public byte slaveId = 1; 
    [SerializeField] private float connectionTimeoutSeconds = 3f;
    [SerializeField] private int ioTimeoutMilliseconds = 1000;

    [Header("仿真与真机单位转换")]
    public float simToRealScale = 1000f; // 1米 = 1000毫米

    [Header("升降缸实时状态")]
    public bool autoMonitor = true;
    public float monitorInterval = 0.2f;
    public float currentHeightMm = 0f;
    public float currentHeightMeters = 0f;
    public float estimatedVelocityMmPerSec = 0f;
    public float currentVelocityMmPerSec = 0f;
    public float actualTorque = 0f;
    public float commandedVelocityMmPerSec = 0f;
    public float lastFeedbackTime = -1f;

    [Header("UI 显示绑定")]
    public TextMeshProUGUI heightTextDisplay;
    public TextMeshProUGUI velocityTextDisplay;
    public TextMeshProUGUI torqueTextDisplay;

    private TcpClient tcpClient;
    private ModbusIpMaster modbusMaster; 
    private Coroutine monitorCoroutine;
    private int motionEpoch;
    private CancellationTokenSource connectionCancellation;
    private Task connectionTask;
    private bool connecting;
    public bool ViewConnecting => connecting;
    public bool ViewHasConnection => DeviceTcpConnection.IsOpen(tcpClient);
    public bool ViewEndpointsAligned => DeviceTcpConnection.AbsentOrMatches(tcpClient, ipAddress, port);
    public string ViewConnectionDescription => "Modbus " + DeviceTcpConnection.Describe(tcpClient) + "；" +
        (HasFreshFeedback(1f) ? "高度反馈正常" : "尚无新鲜高度反馈") + (ViewEndpointsAligned ? "" : "；实际连接与当前配置不一致");
    private long connectionGeneration, dataSequence;
    private long positionObserved = -1, velocityObserved = -1, torqueObserved = -1;
    public int StopVersion { get; private set; }

    private void Awake()
    {
        RefreshDisconnectedUI();
    }

    public async void Connect()
    {
        try { await ConnectAsync(); }
        catch (Exception e) { Debug.LogWarning("升降缸连接未完成: " + e.Message); }
    }

    public Task ConnectAsync()
    {
        if (FieldCommissioningPanel.Active != null && FieldCommissioningPanel.Active.MotionLocked) return Task.CompletedTask;
        if (connecting) return connectionTask ?? Task.CompletedTask;
        if (FieldConnectedTo(ipAddress, port)) return Task.CompletedTask;
        connectionTask = ConnectCoreAsync();
        return connectionTask;
    }

    private async Task ConnectCoreAsync()
    {
        connecting = true;
        CloseLiftConnection(); // Invalidate pending motion before replacing the master.
        var lifetime = new CancellationTokenSource();
        connectionCancellation = lifetime;
        OperationLogTool.RecordLog(OperationType.真实运行, $"请求连接升降缸（IP：{ipAddress}）");
        try {
            Debug.Log($"升降缸正在连接（超时{connectionTimeoutSeconds}秒）");
            var opened = await DeviceTcpConnection.OpenAsync(ipAddress, port, connectionTimeoutSeconds, lifetime.Token);
            if (lifetime.IsCancellationRequested) { opened.Close(); lifetime.Token.ThrowIfCancellationRequested(); }
            tcpClient = opened;
            connectionGeneration++;
            int timeout = Math.Max(100, ioTimeoutMilliseconds);
            tcpClient.ReceiveTimeout = timeout; tcpClient.SendTimeout = timeout;
            modbusMaster = ModbusIpMaster.CreateIp(tcpClient); 
            modbusMaster.Transport.ReadTimeout = timeout;
            modbusMaster.Transport.WriteTimeout = timeout;
            modbusMaster.Transport.Retries = 0; // Never silently retry a motion write.
            Debug.Log("<color=green>[升降缸] NModbus4 TCP 连接成功！</color>");
            RefreshUI();
            StartMonitor();
        } catch (OperationCanceledException) { Debug.Log("升降缸连接已取消"); }
        catch (Exception e) {
            CloseLiftConnection();
            RefreshDisconnectedUI();
            Debug.LogError($"[升降缸] 连接失败: {e.Message}");
        }
        finally
        {
            if (ReferenceEquals(connectionCancellation, lifetime)) connectionCancellation = null;
            lifetime.Dispose(); connecting = false;
        }
    }

    public void Disconnect()
    {
        OperationLogTool.RecordLog(OperationType.真实运行, "请求断开升降缸");
        if (FieldCommissioningPanel.Active != null && FieldCommissioningPanel.Active.MotionLocked)
        { FieldCommissioningPanel.Active.RequestStop(); return; }
        CloseLiftConnection();
        RefreshDisconnectedUI();
        RunDataCapture.EndManualSession?.Invoke();
        Debug.Log("<color=yellow>[升降缸] 连接已断开。</color>");
    }

    public bool IsConnected() => tcpClient != null && tcpClient.Connected && modbusMaster != null;
    public bool FieldConnectedTo(string ip, int targetPort)
    {
        try
        {
            return IsConnected() && DeviceTcpConnection.Matches(tcpClient, ip, targetPort);
        }
        catch { return false; }
    }

    /// <summary>
    /// 使能电缸 (寄存器地址 100，对应表格 40101)
    /// </summary>
    public void EnableCylinder()
    {
        if (!IsConnected()) return;
        // 控制命令属于 INTEGER 类型，使用 WriteSingleRegister 发送标准功能码 0x06
        WriteRegisterLogged(100, new ushort[] {1}, true);
        Debug.Log("[升降缸] 已下发使能指令");
    }

    /// <summary>
    /// 急停电缸 (寄存器地址 105，对应表格 40106)
    /// </summary>
    public void StopCylinder()
    {
        StopVersion++;
        motionEpoch++;
        if (!IsConnected()) return;
        // 使用 WriteSingleRegister 发送标准功能码 0x06
        WriteRegisterLogged(105, new ushort[] {1}, true);
        Debug.Log("[升降缸] 已下发急停指令");
    }

    /// <summary>
    /// 发送绝对移动指令序列
    /// </summary>
    /// <param name="simHeightMeters">Unity仿真的高度(米)</param>
    /// <param name="velocity">速度(mm/s)</param>
    public void MoveToPosition(float simHeightMeters, float velocity = 50.0f)
    {
        MoveToPosition(simHeightMeters, velocity, false);
    }

    public async void MoveToPosition(float simHeightMeters, float velocity, bool fieldWorkflow)
    {
        if (!fieldWorkflow && FieldCommissioningPanel.Active != null && FieldCommissioningPanel.Active.MotionLocked) return;
        if (!IsConnected()) return;
        int epoch = ++motionEpoch;
        try
        {
        commandedVelocityMmPerSec = velocity;

        // 第一步：确保电缸处于使能状态 (向 101 写入 1)
        // modbusMaster.WriteSingleRegister(slaveId, 101, 1);
        WriteRegisterLogged(100, new ushort[] {1}, false);
            
        // 等待 200 毫秒，给物理电机上电锁死的时间
        await System.Threading.Tasks.Task.Delay(200); 
        if (epoch != motionEpoch || !IsConnected()) return;


        // 1. 位置计算 -> 转换为毫米并保持为 float (对应表格 402025: SINGLE)
        float targetRealPos = simHeightMeters * simToRealScale;

        // 2. 写入速度 (2001) -> SINGLE 类型，使用 CDAB 转换
        WriteRegisterLogged(2001, FloatToCDABRegisters(velocity), false);

        // 3. 写入目标位置 (2025) -> SINGLE 类型，使用 CDAB 转换
        WriteRegisterLogged(2025, FloatToCDABRegisters(targetRealPos), false);

        // yield return new WaitForSeconds(0.5f); // 确保前面两个寄存器写入完成
        

        // 4. 触发绝对运动指令 (寄存器地址 103，对应表格 40104)
        await System.Threading.Tasks.Task.Delay(100);
        if (epoch != motionEpoch || !IsConnected()) return;

        WriteRegisterLogged(103, new ushort[] {0}, true); // 先写入 0，确保每次都是从非触发状态开始

        await System.Threading.Tasks.Task.Delay(100);
        if (epoch != motionEpoch || !IsConnected()) return;


        WriteRegisterLogged(103, new ushort[] {1}, true);

        await System.Threading.Tasks.Task.Delay(100);
        if (epoch != motionEpoch || !IsConnected()) return;


        WriteRegisterLogged(103, new ushort[] {0}, true); // 写回 0，完成触发
        
        Debug.Log($"<color=cyan>[升降缸] 移动 -> 目标位置(Float): {targetRealPos} mm, 速度(Float): {velocity:F1}</color>");
        }
        catch (Exception e) { HandleLiftIoFailure(e); Debug.LogError("[升降缸] 移动序列失败：" + e.Message); }
    }

    /// <summary>
    /// 读取当前位置 (寄存器地址 2013，对应表格 402013)
    /// </summary>
    /// <returns>当前高度(mm)</returns>
    public float ReadCurrentPosition()
    {
        if (!IsConnected()) return 0f;
        try {
            // 读取 2013 (连续2个寄存器)，返回的是 SINGLE 浮点数格式
            ushort[] vals = modbusMaster.ReadHoldingRegisters(slaveId, 2013, 2);
            float heightMm = CDABRegistersToFloat(vals);
            UpdatePositionState(heightMm);
            lastFeedbackTime = Time.realtimeSinceStartup;
            positionObserved = System.Diagnostics.Stopwatch.GetTimestamp(); dataSequence++;
            return heightMm;
        } catch (Exception e) {
            HandleLiftIoFailure(e);
            return 0f; 
        }
    }

    public bool HasFreshFeedback(float timeoutSeconds)
    {
        return lastFeedbackTime >= 0f &&
               Time.realtimeSinceStartup - lastFeedbackTime <= timeoutSeconds;
    }

    public float ReadCurrentVelocity()
    {
        if (!IsConnected()) return 0f;
        try {
            ushort[] vals = modbusMaster.ReadHoldingRegisters(slaveId, 2017, 2);
            currentVelocityMmPerSec = CDABRegistersToFloat(vals);
            estimatedVelocityMmPerSec = currentVelocityMmPerSec;
            velocityObserved = System.Diagnostics.Stopwatch.GetTimestamp(); dataSequence++;
            return currentVelocityMmPerSec;
        } catch (Exception e) {
            HandleLiftIoFailure(e);
            return 0f;
        }
    }

    public float ReadActualTorque()
    {
        if (!IsConnected()) return 0f;
        try {
            ushort[] vals = modbusMaster.ReadHoldingRegisters(slaveId, 2021, 2);
            actualTorque = CDABRegistersToFloat(vals);
            torqueObserved = System.Diagnostics.Stopwatch.GetTimestamp(); dataSequence++;
            return actualTorque;
        } catch (Exception e) {
            HandleLiftIoFailure(e);
            return 0f;
        }
    }

    // ==========================================
    // 核心转换工具：全面采用符合地址表的 SINGLE (Float) CDAB 字节序
    // ==========================================
    
    // 浮点数转 Modbus 双寄存器 (CDAB 字节序)
    private ushort[] FloatToCDABRegisters(float value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        ushort lowRegister = BitConverter.ToUInt16(bytes, 0);
        ushort highRegister = BitConverter.ToUInt16(bytes, 2);
        return new ushort[] { highRegister, lowRegister }; 
    }

    // Modbus 双寄存器转浮点数 (CDAB 字节序)
    private float CDABRegistersToFloat(ushort[] registers)
    {
        if (registers == null || registers.Length < 2) return 0f;
        byte[] bytes = new byte[4];
        byte[] highBytes = BitConverter.GetBytes(registers[0]); // 取出 HighRegister
        byte[] lowBytes = BitConverter.GetBytes(registers[1]);  // 取出 LowRegister
        
        // 按照 CDAB 顺序还原底层 4 个字节
        bytes[0] = lowBytes[0];
        bytes[1] = lowBytes[1];
        bytes[2] = highBytes[0];
        bytes[3] = highBytes[1];
        
        return BitConverter.ToSingle(bytes, 0);
    }

    public async void startmove()
    {
            if (FieldCommissioningPanel.Active != null && FieldCommissioningPanel.Active.MotionLocked) return;
            int epoch = ++motionEpoch;
            if (!IsConnected()) return;
            try {
            // 第四步：制造一个完美的“上升沿”触发
            // 先复位为 0，确保等下能产生上升沿
            // modbusMaster.WriteSingleRegister(slaveId, 104, 0); 
            // await System.Threading.Tasks.Task.Delay(50);
            WriteRegisterLogged(103, new ushort[] {0}, false);
            await System.Threading.Tasks.Task.Delay(50);
            if (epoch != motionEpoch || !IsConnected()) return;
            WriteRegisterLogged(103, new ushort[] {1}, false);
            } catch (Exception e) { Debug.LogError("[升降缸] 启动失败：" + e.Message); }
    }

    private void StartMonitor()
    {
        if (!autoMonitor || monitorCoroutine != null) return;
        monitorCoroutine = StartCoroutine(MonitorRoutine());
        if (!IsConnected()) StopMonitor(); // The first synchronous read may fail before StartCoroutine returns.
    }

    private void StopMonitor()
    {
        if (monitorCoroutine != null) StopCoroutine(monitorCoroutine);
        monitorCoroutine = null;
    }

    private System.Collections.IEnumerator MonitorRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(Mathf.Max(monitorInterval, 0.05f));
        while (IsConnected())
        {
            ReadCurrentPosition();
            ReadCurrentVelocity();
            ReadActualTorque();
            if (!IsConnected()) break; // Do not overwrite the failure UI with cached measurements.
            RefreshUI();
            yield return wait;
        }
        monitorCoroutine = null;
        RefreshDisconnectedUI();
    }

    private void UpdatePositionState(float heightMm)
    {
        currentHeightMm = heightMm;
        currentHeightMeters = heightMm / Mathf.Max(simToRealScale, 0.0001f);
    }

    private void RefreshUI()
    {
        if (heightTextDisplay != null)
            heightTextDisplay.text = $"升降缸高度：{currentHeightMm:F1} mm";
        if (velocityTextDisplay != null)
            velocityTextDisplay.text = $"升降缸速度：{currentVelocityMmPerSec:F1} mm/s";
        if (torqueTextDisplay != null)
            torqueTextDisplay.text = $"升降缸转矩：{actualTorque:F2}";
    }

    private void RefreshDisconnectedUI()
    {
        if (heightTextDisplay != null)
            heightTextDisplay.text = "升降缸连接：未连接";
        if (velocityTextDisplay != null)
            velocityTextDisplay.text = "升降缸高度：-- mm";
        if (torqueTextDisplay != null)
            torqueTextDisplay.text = "速度：-- mm/s  转矩：--";
    }

    void OnDestroy()
    {
        CloseLiftConnection();
        RunDataCapture.EndManualSession?.Invoke();
    }

    private void WriteRegisterLogged(ushort address, ushort[] values, bool single)
    {
        string decoded = values.Length == 2 ? "; float_CDAB=" + CDABRegistersToFloat(values).ToString("R",System.Globalization.CultureInfo.InvariantCulture) : "";
        string payload = "FC=" + (single ? "0x06" : "0x10") + "; slave=" + slaveId + "; address=" + address +
            "; registers=[" + string.Join(",",values) + "]" + decoded;
        using (var record = RunDataCapture.Command("lift",RunDataCapture.Endpoint(tcpClient,ipAddress + ":" + port),connectionGeneration,
            payload,"2025:height_mm; 2001:velocity_mm/s; 100:enable; 103:trigger; 105:stop",ensureManual:address!=105))
        {
            try
            {
                if (single) modbusMaster.WriteSingleRegister(slaveId,address,values[0]);
                else modbusMaster.WriteMultipleRegisters(slaveId,address,values);
                // NModbus returns only after checking its response; no raw response bytes are exposed here.
                record?.Emit("library_call_returned","Synchronous NModbus response completed; exact OS write time unavailable");
                record?.Reply("NModbus call returned; response validated by library, raw bytes unavailable"); record?.Accepted();
            }
            catch (Exception e) { record?.Failed(e); throw; }
        }
    }
    private static double DataAge(long tick) => tick < 0 ? double.NaN :
        (System.Diagnostics.Stopwatch.GetTimestamp()-tick)/(double)System.Diagnostics.Stopwatch.Frequency;
    private void Update()
    {
        RunDataCapture.CheckSaveError();
        RunDataCapture.Current?.Lift(RunDataCapture.Endpoint(tcpClient,ipAddress + ":" + port),connectionGeneration,dataSequence,
            currentHeightMm,currentVelocityMmPerSec,actualTorque,DataAge(positionObserved),DataAge(velocityObserved),DataAge(torqueObserved),IsConnected());
    }

    private void HandleLiftIoFailure(Exception error)
    {
        CloseLiftConnection(); RefreshDisconnectedUI();
        Debug.LogWarning("[升降缸] 通信失败，连接已释放，请检查后重连（不会自动续跑）: " + error.Message);
    }

    private void CloseLiftConnection()
    {
        connectionCancellation?.Cancel();
        motionEpoch++; lastFeedbackTime = -1f;
        positionObserved = velocityObserved = torqueObserved = -1;
        StopMonitor();
        var client = tcpClient; var master = modbusMaster;
        tcpClient = null; modbusMaster = null;
        try { master?.Dispose(); } finally { client?.Close(); }
    }
}
