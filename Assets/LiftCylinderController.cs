using UnityEngine;
using System;
using System.Net.Sockets;
using Modbus.Device;
using Modbus.Message;
using TMPro;

public class LiftCylinderController : MonoBehaviour
{
    [Header("Modbus TCP 配置")]
    public string ipAddress = "192.168.192.88";
    public int port = 502;
    public byte slaveId = 1; 

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

    public void Connect()
    {
        try {
            tcpClient = new TcpClient();
            tcpClient.Connect(ipAddress, port); 
            modbusMaster = ModbusIpMaster.CreateIp(tcpClient); 
            Debug.Log("<color=green>[升降缸] NModbus4 TCP 连接成功！</color>");
            StartMonitor();
        } catch (Exception e) {
            Debug.LogError($"[升降缸] 连接失败: {e.Message}");
        }
    }

    public void Disconnect()
    {
        StopMonitor();
        tcpClient?.Close();
        modbusMaster?.Dispose();
        Debug.Log("<color=yellow>[升降缸] 连接已断开。</color>");
    }

    public bool IsConnected() => tcpClient != null && tcpClient.Connected && modbusMaster != null;

    /// <summary>
    /// 使能电缸 (寄存器地址 100，对应表格 40101)
    /// </summary>
    public void EnableCylinder()
    {
        if (!IsConnected()) return;
        // 控制命令属于 INTEGER 类型，使用 WriteSingleRegister 发送标准功能码 0x06
        modbusMaster.WriteSingleRegister(slaveId, 100, 1); 
        Debug.Log("[升降缸] 已下发使能指令");
    }

    /// <summary>
    /// 急停电缸 (寄存器地址 105，对应表格 40106)
    /// </summary>
    public void StopCylinder()
    {
        if (!IsConnected()) return;
        // 使用 WriteSingleRegister 发送标准功能码 0x06
        modbusMaster.WriteSingleRegister(slaveId, 105, 1);
        Debug.Log("[升降缸] 已下发急停指令");
    }

    /// <summary>
    /// 发送绝对移动指令序列
    /// </summary>
    /// <param name="simHeightMeters">Unity仿真的高度(米)</param>
    /// <param name="velocity">速度(mm/s)</param>
    public async void MoveToPosition(float simHeightMeters, float velocity = 50.0f)
    {
        if (!IsConnected()) return;
        commandedVelocityMmPerSec = velocity;

        // 第一步：确保电缸处于使能状态 (向 101 写入 1)
        // modbusMaster.WriteSingleRegister(slaveId, 101, 1);
        modbusMaster.WriteMultipleRegisters(slaveId, 100, new ushort[] { 1 });
            
        // 等待 200 毫秒，给物理电机上电锁死的时间
        await System.Threading.Tasks.Task.Delay(200); 


        // 1. 位置计算 -> 转换为毫米并保持为 float (对应表格 402025: SINGLE)
        float targetRealPos = simHeightMeters * simToRealScale;

        // 2. 写入速度 (2001) -> SINGLE 类型，使用 CDAB 转换
        modbusMaster.WriteMultipleRegisters(slaveId, 2001, FloatToCDABRegisters(velocity));

        // 3. 写入目标位置 (2025) -> SINGLE 类型，使用 CDAB 转换
        modbusMaster.WriteMultipleRegisters(slaveId, 2025, FloatToCDABRegisters(targetRealPos));

        // yield return new WaitForSeconds(0.5f); // 确保前面两个寄存器写入完成
        

        // 4. 触发绝对运动指令 (寄存器地址 103，对应表格 40104)
        await System.Threading.Tasks.Task.Delay(100);

        modbusMaster.WriteSingleRegister(slaveId, 103, 0); // 先写入 0，确保每次都是从非触发状态开始

        await System.Threading.Tasks.Task.Delay(100);


        modbusMaster.WriteSingleRegister(slaveId, 103, 1); 

        await System.Threading.Tasks.Task.Delay(100);


        modbusMaster.WriteSingleRegister(slaveId, 103, 0); // 写回 0，完成触发
        
        Debug.Log($"<color=cyan>[升降缸] 移动 -> 目标位置(Float): {targetRealPos} mm, 速度(Float): {velocity:F1}</color>");
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
            return heightMm;
        } catch { 
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
            return currentVelocityMmPerSec;
        } catch {
            return 0f;
        }
    }

    public float ReadActualTorque()
    {
        if (!IsConnected()) return 0f;
        try {
            ushort[] vals = modbusMaster.ReadHoldingRegisters(slaveId, 2021, 2);
            actualTorque = CDABRegistersToFloat(vals);
            return actualTorque;
        } catch {
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
            // 第四步：制造一个完美的“上升沿”触发
            // 先复位为 0，确保等下能产生上升沿
            // modbusMaster.WriteSingleRegister(slaveId, 104, 0); 
            // await System.Threading.Tasks.Task.Delay(50);
            modbusMaster.WriteMultipleRegisters(slaveId, 103, new ushort[] { 0 }); 
            await System.Threading.Tasks.Task.Delay(50);
            modbusMaster.WriteMultipleRegisters(slaveId, 103, new ushort[] { 1 });
    }

    private void StartMonitor()
    {
        if (!autoMonitor || monitorCoroutine != null) return;
        monitorCoroutine = StartCoroutine(MonitorRoutine());
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
            RefreshUI();
            yield return wait;
        }
        monitorCoroutine = null;
    }

    private void UpdatePositionState(float heightMm)
    {
        currentHeightMm = heightMm;
        currentHeightMeters = heightMm / Mathf.Max(simToRealScale, 0.0001f);
    }

    private void RefreshUI()
    {
        if (heightTextDisplay != null)
            heightTextDisplay.text = $"Lift: {currentHeightMm:F1} mm";
        if (velocityTextDisplay != null)
            velocityTextDisplay.text = $"Lift Speed: {currentVelocityMmPerSec:F1} mm/s";
        if (torqueTextDisplay != null)
            torqueTextDisplay.text = $"Lift Torque: {actualTorque:F2}";
    }

    void OnDestroy()
    {
        StopMonitor();
    }
}
