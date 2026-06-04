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
    public string task_status; // 状态如: "RUNNING", "COMPLETED", "FAILED"
    public float distance;     // 剩余距离
    public string target_name; 
}

// ==========================================
// 2. 主控制器类
// ==========================================
public class RobokitController : MonoBehaviour
{
    [Header("底盘网络配置")]
    public string robotIP = "192.168.1.100";

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

    void Update()
    {
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
        // 根据官方文档：当 isAuto 为 true 时，忽略 x, y, angle，底盘将执行自动重定位 
        string json = "{\"isAuto\":true}";
        
        Debug.Log($"<color=cyan>[底盘] 请求自动重定位...</color> JSON: {json}");
        SendCommand(2002, json); 
    }

    public void StopMovement()
    {
        SendCommand(3003, ""); 
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

    public  void ConnectAll()
    {
        ConnectPort(ref statusClient, ref statusStream, 19204, out isStatusOn);
        ConnectPort(ref controlClient, ref controlStream, 19205, out isControlOn);
        ConnectPort(ref navClient, ref navStream, 19206, out isNavOn);

        if (isStatusOn) _ = ReceiveLoopAsync(statusStream, 19204);
        if (isControlOn) _ = ReceiveLoopAsync(controlStream, 19205);
        if (isNavOn) _ = ReceiveLoopAsync(navStream, 19206);
    }

    private void ConnectPort(ref TcpClient c, ref NetworkStream s, int p, out bool ok)
    {
        try {
            c = new TcpClient();  c.Connect(robotIP, p); s = c.GetStream(); ok = true;
            Debug.Log($"<color=green>已连接底盘端口: {p}</color>");
        } catch { 
            ok = false; Debug.LogError($"端口 {p} 连接失败"); 
        }
    }

    public void SendCommand(ushort apiNum, string jsonPayload)
    {
        NetworkStream targetStream = null;

        if (apiNum >= 1000 && apiNum < 2000) targetStream = statusStream;
        else if (apiNum >= 2000 && apiNum < 3000) targetStream = controlStream;
        else if (apiNum >= 3000 && apiNum < 4000) targetStream = navStream;

        if (targetStream == null) return;

        try {
            byte[] packet = RobokitProtocol.Pack(apiNum, jsonPayload);
            targetStream.Write(packet, 0, packet.Length);
        } catch (Exception e) { 
            Debug.LogError($"发送失败: {e.Message}"); 
        }
    }

    private async Task ReceiveLoopAsync(NetworkStream stream, int port)
    {
        try {
            while (stream != null) {
                byte[] header = new byte[16];
                int r = await stream.ReadAsync(header, 0, 16);
                if (r < 16) break;
                
                uint len = RobokitProtocol.ParsePayloadLength(header);
                
                if (len > 0) {
                    byte[] body = new byte[len];
                    await stream.ReadAsync(body, 0, (int)len);
                    string jsonResponse = Encoding.UTF8.GetString(body);
                    
                    // 解析包头中的 API 编号 (第8-9字节，转小端读取)
                    byte[] apiBytes = new byte[2] { header[9], header[8] }; // 注意这里将大端翻转回小端
                    ushort resApiNum = BitConverter.ToUInt16(apiBytes, 0);

                    // 智能解析数据到 Unity 变量
                    ParseRobotData(resApiNum, jsonResponse);
                }
            }
        } catch { }
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
        }
        // 如果是查询导航状态的回复 (11020)
        else if (apiNum == 11020)
        {
            NavigationStatus nav = JsonUtility.FromJson<NavigationStatus>(json);
            currentNavState = nav.task_status;
            remainDistance = nav.distance;
            Debug.Log($"导航状态更新: {currentNavState}, 剩余距离: {remainDistance}");
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
        statusStream?.Close(); statusClient?.Close();
        controlStream?.Close(); controlClient?.Close();
        navStream?.Close(); navClient?.Close();
        Debug.Log("<color=red>已断开底盘所有连接</color>");
    }
    void OnDestroy()
    {
        // 脚本销毁时，切断所有连接，安全退出协程
        statusStream?.Close(); statusClient?.Close();
        controlStream?.Close(); controlClient?.Close();
        navStream?.Close(); navClient?.Close();
    }
}