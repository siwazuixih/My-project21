using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 机械臂IP/端口只读已保存现场JSON；不写配置，不拥有连接或启动任何运动服务。
public sealed partial class DeviceConnectionPanel : MonoBehaviour
{
    private Simulation simulation;
    private DobotController robot;
    private RobokitController chassis;
    private LiftCylinderController lift;
    private ServoTighteningController servo;
    private Button entry;
    private bool visible, positioned, prepareConfirmed, relocationConfirmed, advanced;
    private Rect window;
    private Vector2 scroll;
    private readonly string[] drafts = new string[3];
    private readonly string[] notes = { "尚未操作", "尚未操作", "尚未操作" };
    private readonly bool[] connecting = new bool[3];
    private readonly int[] revision = new int[3];
    private readonly bool[] bindingConflict = new bool[3];
    private static readonly string[] Names = { "机械臂", "底盘", "升降缸" };
    private string message = "只建立/断开设备通信，不自动上电、使能、重定位或启动相机/拧紧服务。";
    private FieldCommissioningSettings robotConfiguration;
    private string RobotConfigurationPath => Path.Combine(Application.persistentDataPath, FieldCommissioningSettings.FileName);

    private bool WorkflowBusy => (FieldCommissioningPanel.Active != null && FieldCommissioningPanel.Active.MotionLocked) ||
        (simulation != null && simulation.ConnectCommander != null && simulation.ConnectCommander.AnyTaskRunning) ||
        (robot != null && (robot.FieldWorkflowActive || robot.ExternalControlActive)) ||
        (chassis != null && (chassis.FieldWorkflowOwned || chassis.ManualJogLocked || chassis.ConfirmedMotionActive)) ||
        (servo != null && servo.FieldWorkflowOwned);
    private bool AnyConnecting => DeviceConnecting(0) || DeviceConnecting(1) || DeviceConnecting(2);
    public bool IsConnectionOperationInProgress => AnyConnecting;

    private void Start()
    {
        simulation = GetComponent<Simulation>();
        if (simulation == null) return;
        var commander = simulation.ConnectCommander;
        robot = commander != null && commander.dobotTCP != null ? commander.dobotTCP : simulation.Dobot;
        chassis = commander != null && commander.chassisTCP != null ? commander.chassisTCP : simulation.Robokit;
        lift = commander != null ? commander.liftTCP : null;
        servo = FindObjectOfType<ServoTighteningController>();
        bindingConflict[0] = robot != null && simulation.Dobot != null && robot != simulation.Dobot;
        bindingConflict[1] = chassis != null && simulation.Robokit != null && chassis != simulation.Robokit;
        InitializeToolbar();
        ReloadDrafts();
        RefreshAddressDisplays();
    }
    private Button ToolbarButton(string name)
    {
        if (simulation.StatusReal == null) return null;
        foreach (var button in simulation.StatusReal.GetComponentsInChildren<Button>(true))
            if (button.name == name) return button;
        return null;
    }
    private void InitializeToolbar()
    {
        entry = ToolbarButton("ConnectBtn");
        if (entry == null) { Debug.LogWarning("[设备连接] 未找到正式ConnectBtn，未改动原工具栏"); return; }
        // RemoveAllListeners不能移除场景持久化回调；必须替换整个事件，防止点击面板入口就连接三设备。
        entry.onClick = new Button.ButtonClickedEvent();
        entry.onClick.AddListener(TogglePanel);
        foreach (var label in entry.GetComponentsInChildren<TMP_Text>(true)) label.text = "设备连接";
        foreach (var label in entry.GetComponentsInChildren<Text>(true)) label.text = "设备连接";
        var rect = entry.GetComponent<RectTransform>();
        if (rect != null) { rect.anchoredPosition = new Vector2(-264f, -12f); rect.sizeDelta = new Vector2(152f, 38f); }
        foreach (string name in new[] { "ConnectBaseBtn", "DisconnectBtn", "PowerOnBtn", "EnableBtn", "ResetBaseBtn" })
        {
            var old = ToolbarButton(name);
            if (old != null) old.gameObject.SetActive(false); // 只改本次运行实例，不编辑场景/持久化绑定。
        }
    }
    private void Update()
    {
        RefreshAddressDisplays();
        if (WorkflowBusy || AnyConnecting) prepareConfirmed = relocationConfirmed = false;
    }
    private void RefreshAddressDisplays()
    {
        if (simulation == null || entry == null) return;
        // 原右侧IP只显示当前控制器；机械臂配置来自现场JSON，这里不反写文件。
        if (simulation.RobotIpInput != null && robot != null)
        {
            simulation.RobotIpInput.readOnly = true;
            if (simulation.RobotIpInput.text != robot.robotIP) simulation.RobotIpInput.SetTextWithoutNotify(robot.robotIP);
        }
        if (simulation.ChassisIpInput != null && chassis != null)
        {
            simulation.ChassisIpInput.readOnly = true;
            if (simulation.ChassisIpInput.text != chassis.robotIP) simulation.ChassisIpInput.SetTextWithoutNotify(chassis.robotIP);
        }
    }
    private void TogglePanel()
    {
        visible = !visible;
        prepareConfirmed = relocationConfirmed = false;
        if (visible) ReloadDrafts(); // 只读配置；打开面板不改控制器或建立网络连接。
    }
    private void ReloadRobotConfiguration()
    {
        robotConfiguration = null;
        drafts[0] = "JSON读取失败";
        robotConfiguration = FieldCommissioningSettings.ReadRobotConnectionFile(RobotConfigurationPath);
        drafts[0] = robotConfiguration.robotIp;
    }
    private void ReloadDrafts()
    {
        try { ReloadRobotConfiguration(); }
        catch (Exception e) { drafts[0] = "JSON读取失败"; notes[0] = e.Message; }
        for (int i = 1; i < 3; i++) drafts[i] = CurrentAddress(i);
    }
    private void OnGUI()
    {
        if (simulation == null || !simulation.IsRealRunPage) { visible = false; prepareConfirmed = relocationConfirmed = false; return; }
        if (!visible) return;
        float width = Mathf.Min(660, Screen.width - 20), height = Mathf.Min(760, Screen.height - 180);
        if (!positioned) { window = new Rect(Mathf.Max(10, Screen.width - width - 350), 150, width, height); positioned = true; }
        window.width = width; window.height = height;
        window.x = Mathf.Clamp(window.x, 10, Mathf.Max(10, Screen.width - width - 10));
        window.y = Mathf.Clamp(window.y, 110, Mathf.Max(110, Screen.height - height - 70));
        window = GUI.Window(GetInstanceID(), window, DrawWindow, "设备连接 / 准备（正式运行）");
    }
    private void DrawWindow(int id)
    {
        bool original = GUI.enabled;
        var wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
        var box = new GUIStyle(GUI.skin.box) { wordWrap = true };
        var toggle = new GUIStyle(GUI.skin.toggle) { wordWrap = true };
        GUILayout.BeginHorizontal();
        GUILayout.Label("现场联调与底盘手动测试入口保持原样。", wrap);
        if (GUILayout.Button("关闭", GUILayout.Width(60))) { visible = false; prepareConfirmed = relocationConfirmed = false; }
        GUILayout.EndHorizontal();
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Max(130, window.height - 85)));
        GUILayout.Label(message, box);
        GUILayout.Label("机械臂IP、控制端口和反馈端口统一读取已保存的现场JSON（每次点击连接都会重读），此处不另设机械臂地址，不覆盖文件。修改后请先保存；已有不同连接须先断开。底盘/升降沿用原连接设置。\n配置：" + RobotConfigurationPath, wrap);
        if (WorkflowBusy) GUILayout.Label("有任务/停止核对占用：禁止连接、改地址或设备准备。点断开仅请求安全停止/提示，不直接切断通道。", box);
        for (int i = 0; i < 3; i++) DrawDevice(i, wrap);
        GUILayout.BeginHorizontal();
        GUI.enabled = !WorkflowBusy && !AnyConnecting;
        if (GUILayout.Button("连接三种设备（不启动服务）")) _ = ConnectAllAsync();
        GUI.enabled = original;
        if (GUILayout.Button("断开三种设备")) DisconnectAll();
        if (GUILayout.Button("重读JSON/当前地址（不重连）")) { ReloadDrafts(); prepareConfirmed = relocationConfirmed = false; }
        GUILayout.EndHorizontal();
        GUILayout.Label("机械臂准备（与连接分开）", box);
        GUI.enabled = !WorkflowBusy && !AnyConnecting && robot != null && !bindingConflict[0] && robot.ViewControlReady;
        prepareConfirmed = GUILayout.Toggle(prepareConfirmed, "人工确认允许上电/使能；使能沿用原逻辑，会清错并设置速度3%。", toggle);
        GUI.enabled = GUI.enabled && prepareConfirmed;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("机械臂上电")) PrepareRobot(false);
        if (GUILayout.Button("机械臂使能")) PrepareRobot(true);
        GUILayout.EndHorizontal();
        GUI.enabled = original;
        advanced = GUILayout.Toggle(advanced, "展开高级操作：底盘自动重定位");
        if (advanced)
        {
            GUILayout.Label("不是回原点或清零。仅在地图/定位环境正确、底盘实际停稳且无其他控制源时使用。", wrap);
            GUI.enabled = !WorkflowBusy && !AnyConnecting && chassis != null && !bindingConflict[1] && chassis.ViewAllPortsConnected;
            relocationConfirmed = GUILayout.Toggle(relocationConfirmed, "已核对现场条件，允许请求自动重定位", toggle);
            GUI.enabled = GUI.enabled && relocationConfirmed;
            if (GUILayout.Button("请求底盘自动重定位")) RelocateChassis();
            GUI.enabled = original;
        }
        GUILayout.Label("相机预览、拧紧服务由各自按钮管理，不包含在上述连接/断开内。服务在线不等于电批在线；连接、上电请求或使能应答都不是实际到位证明。停止必须使用对应停止入口，不能用断线代替硬件急停。", wrap);
        GUILayout.EndScrollView();
        GUI.enabled = original;
        GUI.DragWindow(new Rect(0, 0, window.width, 22));
    }
    private void DrawDevice(int i, GUIStyle wrap)
    {
        bool original = GUI.enabled;
        GUILayout.Label(Names[i] + "　" + Ports(i), GUI.skin.box);
        if (bindingConflict[i]) GUILayout.Label("正式界面与任务控制器绑定不一致；请检查Inspector，本行拒绝操作。", wrap);
        GUILayout.BeginHorizontal();
        GUILayout.Label("IP", GUILayout.Width(24));
        if (i == 0) GUILayout.Label(drafts[0] ?? "", GUILayout.MinWidth(130)); // 单一来源，避免另一个IP输入覆盖JSON。
        else
        {
            GUI.enabled = !WorkflowBusy && !DeviceConnecting(i) && HasDevice(i) && !HasConnection(i) && !bindingConflict[i];
            drafts[i] = GUILayout.TextField(drafts[i] ?? "", 64, GUILayout.MinWidth(130));
        }
        GUI.enabled = !WorkflowBusy && !DeviceConnecting(i) && HasDevice(i) && !bindingConflict[i];
        if (GUILayout.Button(DeviceConnecting(i) ? "连接中…" : "连接/补连", GUILayout.Width(90))) _ = ConnectDeviceAsync(i);
        GUI.enabled = HasDevice(i) && !bindingConflict[i];
        if (GUILayout.Button("断开", GUILayout.Width(65))) DisconnectDevice(i);
        GUI.enabled = original;
        GUILayout.EndHorizontal();
        GUILayout.Label(Description(i), wrap);
        GUILayout.Label(notes[i], wrap);
    }

    private bool HasDevice(int i) => i == 0 ? robot != null : i == 1 ? chassis != null : lift != null;
    private string CurrentAddress(int i) => i == 0 ? robot != null ? robot.robotIP : "" : i == 1 ? chassis != null ? chassis.robotIP : "" : lift != null ? lift.ipAddress : "";
    private bool HasConnection(int i) => i == 0 ? robot != null && robot.ViewHasConnection : i == 1 ? chassis != null && chassis.ViewHasConnection : lift != null && lift.ViewHasConnection;
    private bool Aligned(int i) => i == 0 ? robot.ViewEndpointsAligned : i == 1 ? chassis.ViewEndpointsAligned : lift.ViewEndpointsAligned;
    private bool DeviceConnecting(int i) => connecting[i] || (i == 0 ? robot != null && robot.ViewConnecting : i == 1 ? chassis != null && chassis.ViewConnecting : lift != null && lift.ViewConnecting);
    private string Ports(int i) => i == 0 ? robotConfiguration != null ? $"JSON控制{robotConfiguration.dashboardPort} / 反馈{robotConfiguration.feedbackPort}" : "JSON尚未读取/无效" : i == 1 ? "状态19204 / 控制19205 / 导航19206" : lift != null ? "Modbus " + lift.port : "未绑定";
    private string Description(int i) => !HasDevice(i) ? "未绑定设备" : i == 0 ? robot.ViewConnectionDescription : i == 1 ? chassis.ViewConnectionDescription : lift.ViewConnectionDescription;
    private static string ParseAddress(string value)
    {
        string text = (value ?? "").Trim();
        if (!IPAddress.TryParse(text, out var ip) || (!text.Contains(":") && text.Split('.').Length != 4))
            throw new ArgumentException("请填写有效的数字IP地址，不接受主机名或缩写地址");
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return ip.ToString();
    }
    private static void CheckPort(int port)
    { if (port < 1 || port > 65535) throw new ArgumentException("端口必须在1～65535内，请先核对当前端口配置"); }
    private async Task ConnectDeviceAsync(int i)
    {
        if (!HasDevice(i) || bindingConflict[i] || DeviceConnecting(i)) return;
        if (WorkflowBusy) { notes[i] = "已有任务占用，不连接或修改地址"; return; }
        int ticket = ++revision[i];
        try
        {
            if (i == 0) ReloadRobotConfiguration();
            string address = ParseAddress(i == 0 ? robotConfiguration.robotIp : drafts[i]);
            if (i != 0 && HasConnection(i) && (!Aligned(i) || address != ParseAddress(CurrentAddress(i))))
                throw new InvalidOperationException("已有不同端点连接，请先明确断开，再修改地址/重连；不会自动切换现场连接");
            if (i == 0) robot.ConfigureFieldConnection(robotConfiguration);
            if (i == 2) CheckPort(lift.port);
            drafts[i] = address; // 显式显示规范化地址，不能让输入缩写与真实端点看起来不同。
            connecting[i] = true;
            prepareConfirmed = relocationConfirmed = false;
            notes[i] = "正在连接 " + address + (i == 0 ? $"（JSON控制{robotConfiguration.dashboardPort}/反馈{robotConfiguration.feedbackPort}）" : "") + "；界面可继续操作，可点断开取消";
            Task work;
            if (i == 0) { robot.robotIP = address; work = robot.ConnectAsync(); }
            else if (i == 1) { chassis.robotIP = address; work = chassis.ConnectAllAsync(); }
            else { lift.ipAddress = address; work = lift.ConnectAsync(); }
            await work;
            if (ticket == revision[i]) notes[i] = CurrentAddress(i) == address && Aligned(i) ?
                "连接尝试结束，请看实际通道/反馈状态；连接不表示已上电、使能或到位。" : "连接期间参数发生变化/实际端点不一致，请核对；不会自动覆盖配置。";
        }
        catch (Exception e) { if (ticket == revision[i]) notes[i] = "连接未完成：" + e.Message; }
        finally { connecting[i] = false; RefreshAddressDisplays(); }
    }
    private async Task ConnectAllAsync()
    {
        if (WorkflowBusy || AnyConnecting) return;
        await Task.WhenAll(ConnectDeviceAsync(0), ConnectDeviceAsync(1), ConnectDeviceAsync(2));
        message = "三种设备连接尝试已结束，请逐行核对状态；某项失败不会替其他设备报告成功。";
    }
    private bool RequestStopInsteadOfDisconnect()
    {
        if (!WorkflowBusy) return false;
        if (FieldCommissioningPanel.Active != null && FieldCommissioningPanel.Active.MotionLocked) FieldCommissioningPanel.Active.RequestStop();
        else if (chassis != null && chassis.ManualJogLocked) _ = chassis.StopManualJogAsync();
        else if (robot != null && robot.ExternalControlActive) robot.Disconnect(); // 原入口先撤销权限/等停止，不是直接关TCP。
        message = "当前任务/停止核对仍占用；已请求已有停止入口处理（如适用），未直接断开设备。请核对停稳/解除流程锁后再点断开。";
        return true;
    }
    private void DisconnectDevice(int i)
    {
        if (!HasDevice(i) || bindingConflict[i] || RequestStopInsteadOfDisconnect()) return;
        ++revision[i]; prepareConfirmed = relocationConfirmed = false;
        try
        {
            if (i == 0) robot.Disconnect(); else if (i == 1) chassis.DisConnect(); else lift.Disconnect();
            notes[i] = "已请求断开/取消连接，请核对实际通道；断线不是物理停止证明。";
        }
        catch (Exception e) { notes[i] = "断开未完成：" + e.Message; }
    }
    private void DisconnectAll()
    {
        if (RequestStopInsteadOfDisconnect()) return;
        for (int i = 0; i < 3; i++) DisconnectDevice(i);
        message = "已逐项请求断开设备；相机预览和拧紧服务未关闭，请看各项实际状态。";
    }
    private void PrepareRobot(bool enable)
    {
        if (WorkflowBusy || AnyConnecting || robot == null || bindingConflict[0] || !prepareConfirmed || !robot.ViewControlReady) return;
        prepareConfirmed = false;
        if (enable) robot.EnableRobot(); else robot.PowerOn();
        message = enable ? "已请求机械臂使能（含原清错/速度3%逻辑），请核对设备反馈。" : "已请求机械臂上电；不会自动使能，请核对设备反馈。";
    }
    private void RelocateChassis()
    {
        if (WorkflowBusy || AnyConnecting || chassis == null || bindingConflict[1] || !relocationConfirmed || !chassis.ViewAllPortsConnected) return;
        relocationConfirmed = false; chassis.ResetPosition();
        message = "已请求底盘自动重定位（非回原点），不等于定位成功；请检查现场定位状态。";
    }
    private void OnDestroy() { if (entry != null) entry.onClick.RemoveListener(TogglePanel); }
}
