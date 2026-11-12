using System;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

public sealed partial class FieldCommissioningPanel
{
    private bool chassisJogVisible, chassisJogConfirmed, chassisJogObservedStopped, chassisJogUiBusy;
    private string chassisJogDistance = "50", chassisJogSpeed = "50", chassisJogAngle = "5", chassisJogAngularSpeed = "5";
    private string chassisJogUiMessage = "打开此面板不会运动；点击方向按钮只发送一次固定距离/角度任务。";
    private Vector2 chassisJogScroll;
    private RobokitController JogChassis => commander != null ? commander.chassisTCP : null;
    private bool OtherJogWorkflowBusy => Busy || connectionBusy || (robot != null && robot.FieldWorkflowActive) ||
        (servo != null && servo.FieldWorkflowOwned) || (JogChassis != null && JogChassis.FieldWorkflowOwned) ||
        (commander != null && (commander.NonManualTaskRunning || commander.OfflineValidationActive));

    private void DrawChassisJogEntry()
    {
        if (simulation == null || !simulation.IsRealRunPage) { chassisJogVisible = false; chassisJogConfirmed = false; return; }
        if (GUI.Button(new Rect(170, 115, 150, 32), "底盘手动"))
        {
            chassisJogVisible = !chassisJogVisible || (JogChassis != null && JogChassis.ManualJogLocked);
            chassisJogConfirmed = false; chassisJogObservedStopped = false;
            if (chassisJogVisible) visible = false;
        }
        if (!chassisJogVisible) return;
        var chassis = JogChassis;
        float width = Mathf.Min(460, Mathf.Max(260, Screen.width - 360));
        float height = Mathf.Min(620, Screen.height - 160);
        bool originalEnabled = GUI.enabled;
        var wrappedLabel = new GUIStyle(GUI.skin.label) { wordWrap = true };
        var wrappedBox = new GUIStyle(GUI.skin.box) { wordWrap = true };
        var wrappedToggle = new GUIStyle(GUI.skin.toggle) { wordWrap = true };
        GUILayout.BeginArea(new Rect(10, 150, width, height), GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Label("底盘手动挪车（实物）");
        GUI.enabled = chassis == null || !chassis.ManualJogLocked;
        if (GUILayout.Button("关闭", GUILayout.Width(60))) { chassisJogVisible = false; chassisJogConfirmed = false; }
        GUI.enabled = originalEnabled;
        GUILayout.EndHorizontal();
        GUILayout.Label(chassis == null ? "未绑定底盘" : "底盘IP：" + chassis.robotIP +
            (chassis.FieldConnectedTo(chassis.robotIP) ? "　状态/导航已连接" : "　状态/导航未就绪"), wrappedLabel, GUILayout.Height(36));
        GUILayout.Label(chassis != null ? chassis.ManualJogStatus : chassisJogUiMessage, wrappedBox, GUILayout.Height(68));
        chassisJogScroll = GUILayout.BeginScrollView(chassisJogScroll, GUILayout.Height(Mathf.Max(80, height - 220)));
        GUILayout.Label(chassisJogUiMessage, wrappedLabel);
        GUI.enabled = !OtherJogWorkflowBusy && !chassisJogUiBusy && chassis != null && !chassis.ManualJogActive;
        if (GUILayout.Button("连接底盘（复用当前IP，不连接其他设备）")) _ = ConnectJogChassisAsync();
        GUI.enabled = !OtherJogWorkflowBusy && !chassisJogUiBusy && chassis != null && !chassis.ManualJogLocked && !chassis.ConfirmedMotionActive;
        DrawJogNumber("单次距离 mm（1～500）", ref chassisJogDistance);
        DrawJogNumber("平移速度 mm/s（10～100）", ref chassisJogSpeed);
        DrawJogNumber("单次转角 °（0.1～45）", ref chassisJogAngle);
        DrawJogNumber("角速度 °/s（1～15）", ref chassisJogAngularSpeed);
        chassisJogConfirmed = GUILayout.Toggle(chassisJogConfirmed,
            "本次允许点动：机械臂/工具已收妥，周围无人、急停可用；底盘已停稳且无其他控制源", wrappedToggle, GUILayout.MinHeight(48));
        GUI.enabled = GUI.enabled && chassisJogConfirmed && chassis.FieldConnectedTo(chassis.robotIP);
        GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
        DrawJogButton("前进", ChassisJogDirection.Forward); GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        DrawJogButton("左移", ChassisJogDirection.Left); DrawJogButton("后退", ChassisJogDirection.Backward); DrawJogButton("右移", ChassisJogDirection.Right);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        DrawJogButton("逆时针旋转", ChassisJogDirection.Counterclockwise); DrawJogButton("顺时针旋转", ChassisJogDirection.Clockwise);
        GUILayout.EndHorizontal();
        GUI.enabled = originalEnabled;
        GUILayout.Label("方向按实车车头，不随镜头变化。使用里程模式；挪车后须重新核对自动运行的规划起点。\n移动中不累计连点；很短的动作若未捕获RUNNING，会保留手动锁，请核对停稳。", wrappedLabel);
        if (chassis != null && chassis.ManualJogNeedsReview)
        {
            GUI.enabled = !chassis.ManualJogActive && !chassisJogUiBusy && !OtherJogWorkflowBusy;
            chassisJogObservedStopped = GUILayout.Toggle(chassisJogObservedStopped, "我已现场观察确认底盘实际停稳");
            GUI.enabled = GUI.enabled && chassisJogObservedStopped;
            if (GUILayout.Button("核对状态并解除手动锁（不续跑）")) _ = RecoverJogAsync();
            GUI.enabled = originalEnabled;
        }
        GUILayout.EndScrollView();
        var previousColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(1f, .3f, .2f);
        GUI.enabled = chassis != null && (chassis.ManualJogLocked || chassis.FieldConnectedTo(chassis.robotIP, false));
        if (GUILayout.Button("停止底盘（软件停止，非硬件急停）", GUILayout.Height(42)))
        {
            chassisJogConfirmed = false; chassisJogObservedStopped = false; confirmed = false;
            if (OtherJogWorkflowBusy) RequestStop();
            else { simulation.SetRealRunInputsLocked(true); _ = chassis.StopManualJogAsync(); }
        }
        GUI.backgroundColor = previousColor; GUI.enabled = originalEnabled;
        GUILayout.EndArea();
    }

    private void DrawJogNumber(string label, ref string value)
    {
        GUILayout.BeginHorizontal(); GUILayout.Label(label);
        string edited = GUILayout.TextField(value, 16, GUILayout.Width(100));
        if (edited != value) { value = edited; chassisJogConfirmed = false; }
        GUILayout.EndHorizontal();
    }
    private void DrawJogButton(string label, ChassisJogDirection direction)
    { if (GUILayout.Button(label, GUILayout.Height(36))) _ = StartJogAsync(direction); }
    private static double ParseJogNumber(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            throw new ArgumentException("参数必须是数字，小数请用英文句点，不要写单位或逗号");
        return number;
    }
    private async Task StartJogAsync(ChassisJogDirection direction)
    {
        var chassis = JogChassis;
        if (chassis == null || chassisJogUiBusy || OtherJogWorkflowBusy || !chassisJogConfirmed || chassis.ManualJogLocked) return;
        try
        {
            bool turn = direction == ChassisJogDirection.Counterclockwise || direction == ChassisJogDirection.Clockwise;
            var command = ChassisJogCommand.Create(direction,
                ParseJogNumber(turn ? chassisJogAngle : chassisJogDistance), ParseJogNumber(turn ? chassisJogAngularSpeed : chassisJogSpeed));
            simulation.PrepareFieldMode();
            confirmed = false; chassisJogObservedStopped = false;
            chassisJogUiBusy = true; simulation.SetRealRunInputsLocked(true);
            chassisJogUiMessage = "已请求" + command.Description + "；挪车后原现场流程许可失效，需重新核对规划起点。";
            await chassis.ExecuteManualJogAsync(command, chassisJogConfirmed);
        }
        catch (Exception e) { chassisJogUiMessage = e.Message; Debug.LogWarning("[底盘手动] " + e.Message); }
        finally
        {
            chassisJogUiBusy = false;
            if (chassis.ManualJogNeedsReview) chassisJogConfirmed = false;
            if (!chassis.ManualJogLocked) simulation.SetRealRunInputsLocked(false);
        }
    }
    private async Task ConnectJogChassisAsync()
    {
        if (JogChassis == null || chassisJogUiBusy || OtherJogWorkflowBusy || JogChassis.ManualJogActive) return;
        chassisJogUiBusy = true; chassisJogConfirmed = false;
        try { await JogChassis.ConnectAllAsync(); chassisJogUiMessage = "连接尝试结束，请检查当前IP及状态/导航端口。连接不表示已停稳。"; }
        catch (Exception e) { chassisJogUiMessage = "连接失败：" + e.Message; }
        finally { chassisJogUiBusy = false; }
    }
    private async Task RecoverJogAsync()
    {
        if (JogChassis == null || chassisJogUiBusy || OtherJogWorkflowBusy) return;
        chassisJogUiBusy = true;
        try
        {
            if (await JogChassis.AcknowledgeManualJogStoppedAsync(chassisJogObservedStopped))
            { simulation.SetRealRunInputsLocked(false); chassisJogConfirmed = false; confirmed = false; }
        }
        finally { chassisJogUiBusy = false; chassisJogObservedStopped = false; }
    }
}
