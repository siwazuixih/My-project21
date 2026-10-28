using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

// 运行时入口，不依赖Editor/场景YAML；所有实物按钮与原离线入口明确分开。
public sealed class FieldCommissioningPanel : MonoBehaviour
{
    public static FieldCommissioningPanel Active { get; private set; }
    public bool Busy { get; private set; }
    public bool MotionLocked => Busy || (robot != null && robot.FieldWorkflowActive);
    private Simulation simulation;
    private ConnectCommander commander;
    private DobotController robot;
    private ServoTighteningController servo;
    private CancellationTokenSource cancellation;
    private Task stopTask;
    private int chassisStopVersion, liftStopVersion;
    private bool visible, confirmed, connectionBusy;
    private Vector2 scroll, panelScroll;
    private string monitorFailure;
    private string configText, message = "默认锁定真实运动。请先保存参数、环境检查，再连接和分阶段验证。";
    private string configPath, reportPath;
    private FieldCommissioningSettings sessionSettings;
    private readonly StringBuilder sessionLog = new StringBuilder();
    private CommissioningJournal journal;
    private bool captureSession, logTruncated;
    private string historyId;
    private bool historyWasUserStop;
    private bool historyCollision;
    private int historyTargetNumber;
    private readonly ManualTargetGate manualGate = new ManualTargetGate();
    private readonly ManualTargetGate legacyChassisGate = new ManualTargetGate();
    private bool semiAutomatic, continueRequestPending;
    private ArmTrajectoryTiming trajectoryTiming;
    public bool SemiAutomaticActive => Busy && semiAutomatic;
    public bool WaitingForContinue => SemiAutomaticActive && manualGate.Waiting && !continueRequestPending &&
        cancellation != null && !cancellation.IsCancellationRequested;
    public bool WaitingForChassisConfirmation => Busy && legacyChassisGate.Waiting &&
        cancellation != null && !cancellation.IsCancellationRequested;
    private enum RunMode { Vision, Contact, Single, Multiple, InsertOnly, WithdrawOnly, SemiSingle, SemiMultiple }

    public Task StartSemiAutomaticAsync(bool singleTarget)
    {
        if (MotionLocked || connectionBusy) { SetMessage("已有流程占用，不能重复开始"); return Task.CompletedTask; }
        if (!confirmed)
        {
            visible = true;
            SetMessage("请先核对现场参数并勾选本次安全确认，再点击开始运行。半自动模式不执行视觉。");
            return Task.CompletedTask;
        }
        return RunAsync(singleTarget ? RunMode.SemiSingle : RunMode.SemiMultiple);
    }

    private void Start()
    {
        Active = this;
        simulation = GetComponent<Simulation>();
        commander = simulation.ConnectCommander;
        robot = commander != null ? commander.dobotTCP : simulation.Dobot;
        servo = FindObjectOfType<ServoTighteningController>();
        configPath = Path.Combine(Application.persistentDataPath, "field-commissioning.json");
        configText = JsonUtility.ToJson(new FieldCommissioningSettings(), true);
        try { if (File.Exists(configPath)) configText = File.ReadAllText(configPath); }
        catch (Exception e) { SetMessage("读取配置失败：" + e.Message); }
        Application.logMessageReceived += CaptureLog;
    }
    private void CaptureLog(string text, string stack, LogType type)
    {
        if (!captureSession) return;
        string line = DateTime.UtcNow.ToString("O") + " " + type + " " + text;
        sessionLog.AppendLine(line);
        if (sessionLog.Length > 2000000)
        {
            sessionLog.Remove(0, sessionLog.Length - 2000000);
            logTruncated = true;
        }
        if (text.Contains("StopTrace") || text.Contains("[实物联调]") || text.Contains("[MotionTrace]") || text.Contains("[ContactCycle]") || text.Contains("[半自动轨迹]") || text.Contains("[底盘") ||
            text.Contains("PRE_MOTION_STOP") || text.Contains("MOVJ_REQUEST") || text.Contains("END_STOP") ||
            type == LogType.Error || type == LogType.Exception || type == LogType.Warning)
            journal?.Append(line);
    }
    private void SetMessage(string value) { message = value; Debug.Log("[实物联调] " + value); }
    private void Update()
    {
        if (Busy) robot?.SampleMotionDiagnostics();
        if (!Busy || robot == null || !robot.FieldWorkflowActive || cancellation == null || cancellation.IsCancellationRequested) return;
        try
        {
            robot.CheckFieldHealth();
            if (SemiAutomaticActive && manualGate.Waiting) robot.RequireFieldTrajectoryHold();
            if (legacyChassisGate.Waiting && (commander.chassisTCP == null || !commander.chassisTCP.FieldConnectedTo(sessionSettings.chassisIp, false)))
                throw new IOException("等待人工确认时底盘连接已断开");
            if ((commander.chassisTCP != null && commander.chassisTCP.CancellationVersion != chassisStopVersion) ||
                (commander.liftTCP != null && commander.liftTCP.StopVersion != liftStopVersion))
                throw new OperationCanceledException("底盘/升降收到停止请求");
        }
        catch (Exception e)
        {
            historyCollision |= robot.HistoryCollisionObserved;
            monitorFailure = monitorFailure ?? e.Message; Debug.LogError("[实物联调][监控] " + e.Message); RequestStop();
        }
    }
    private FieldCommissioningSettings ReadSettings(bool motion)
    {
        var settings = JsonUtility.FromJson<FieldCommissioningSettings>(configText);
        if (settings == null) throw new ArgumentException("配置为空");
        settings.Validate(motion);
        return settings;
    }
    private void OnGUI()
    {
        if (commander != null && commander.OfflineValidationActive)
        {
            GUI.Box(new Rect(170, 115, Mathf.Max(200, Screen.width - 510), 32), commander.OfflineStatus);
            if (commander.OfflineWaitingForContinue && GUI.Button(new Rect(170, 150, 220, 32), "继续（模拟精调与拧紧）"))
                commander.ContinueOfflineValidation();
            if (GUI.Button(new Rect(400, 150, 150, 32), "停止离线模拟")) commander.CancelFullProcessValidation();
            return; // 不把实物参数/安全许可面板混进离线演示。
        }
        if (GUI.Button(new Rect(10, 115, 150, 32), "现场联调 / 参数")) visible = !visible;
        if (SemiAutomaticActive || WaitingForChassisConfirmation) GUI.Box(new Rect(170, 115, Mathf.Max(200, Screen.width - 510), 32), message);
        if (!visible) return;
        float width = Mathf.Min(780, Screen.width - 20), height = Mathf.Min(850, Screen.height - 160);
        GUILayout.BeginArea(new Rect(10, 150, width, height), GUI.skin.box);
        panelScroll = GUILayout.BeginScrollView(panelScroll);
        GUILayout.Label("现场联调：实物执行（不是离线模拟）");
        GUILayout.Label(message, GUI.skin.box);
        if (robot != null) GUILayout.Label("末端测量：" + robot.MotionDiagnosticSummary, GUI.skin.box);
        bool follow = simulation.RealRobotFollower != null && simulation.RealRobotFollower.followEnabled;
        bool next = GUILayout.Toggle(follow, "实时反馈 → 仿真显示（只读；缺失反馈的轴不跟随）");
        if (next != follow)
        {
            if (next && !MotionLocked && commander != null && commander.AnyTaskRunning)
                SetMessage("已有任务运行，不切换显示模式");
            else
            {
                if (next && !MotionLocked) simulation.PrepareFieldMode();
                if (simulation.RealRobotFollower != null)
                {
                    simulation.RealRobotFollower.requireBothFeedbackSources = false;
                    simulation.RealRobotFollower.SetRealRobotFollow(next);
                    if (simulation.SyncToggle != null) simulation.SyncToggle.SetIsOnWithoutNotify(next);
                }
            }
        }
        GUI.enabled = !MotionLocked && !connectionBusy;
        DrawChassisModeSelector();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("保存参数")) TryAction(() => { var s = ReadSettings(false); AtomicWrite(configPath, JsonUtility.ToJson(s, true)); SetMessage("参数已保存：" + configPath); });
        if (GUILayout.Button("环境检查（无设备）")) _ = CheckEnvironmentFromUIAsync();
        if (GUILayout.Button("连接机械臂")) TryAction(() => ConnectDevices(false));
        if (GUILayout.Button("连接底盘/升降")) TryAction(() => ConnectDevices(true));
        if (GUILayout.Button("检查机械臂反馈")) TryAction(() => SetMessage(robot.InspectFieldFeedback()));
        GUILayout.EndHorizontal();
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Clamp(height - 350, 120, 470)));
        GUILayout.Label("JSON参数：角度deg，距离mm（底盘速度m/s），时间s。六轴限位必须现场填写。\nmaxJointStepDeg是规划采样步长（通常5°），不是整段转角上限。\n下方确认不保存；修改参数后请重新核对。firstTarget为从1开始的目标编号。");
        float textHeight = GUI.skin.textArea.CalcHeight(new GUIContent(configText), Mathf.Max(100, width - 65));
        string edited = GUILayout.TextArea(configText, GUILayout.Height(Mathf.Max(680, textHeight + 20)));
        if (edited != configText) { configText = edited; confirmed = false; }
        GUILayout.EndScrollView();
        confirmed = GUILayout.Toggle(confirmed, "本次确认：人员远离/急停可用；底盘当前停稳/无其他任务；已核对限位、工具方向及路径；接受升降超时仍继续的既有规则");
        GUI.enabled = !MotionLocked && !connectionBusy && confirmed;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("仅机械臂插入（电批不转）")) _ = RunAsync(RunMode.InsertOnly);
        if (GUILayout.Button("仅机械臂退出（电批不转）")) _ = RunAsync(RunMode.WithdrawOnly);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("① 仅J1/J2精调")) _ = RunAsync(RunMode.Vision);
        if (GUILayout.Button("② 仅卡入/拧紧/退出")) _ = RunAsync(RunMode.Contact);
        if (GUILayout.Button("③ 单目标全流程")) _ = RunAsync(RunMode.Single);
        if (GUILayout.Button("④ 从目标连续运行")) _ = RunAsync(RunMode.Multiple);
        GUILayout.EndHorizontal();
        GUI.enabled = true;
        if (WaitingForChassisConfirmation && GUILayout.Button("确认底盘位置/朝向正确且已停稳，继续升降/机械臂（旧模式）", GUILayout.Height(36))) ConfirmLegacyChassis();
        if (WaitingForContinue && GUILayout.Button("确认已对位，跳过视觉并执行卡入—拧紧—退出", GUILayout.Height(36))) ContinueSemiAutomatic();
        if (GUILayout.Button("停止本次流程（非硬件急停）", GUILayout.Height(36))) RequestStop();
        if (!Busy && robot != null && robot.FieldWorkflowActive && GUILayout.Button("重试停止确认并解除本机流程锁")) _ = RecoverAsync();
        GUILayout.Label("配置：" + configPath + "\n最近报告：" + (reportPath ?? "尚无") + "\n真实开始运行：粗定位末点确认→人工继续→拧紧→返程；每个目标重新确认。\n单目标选项开启只做一个目标；关闭则逐目标运行。离线验证仍使用全流程验证（模拟）。");
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
    private void DrawChassisModeSelector()
    {
        try
        {
            var s = JsonUtility.FromJson<FieldCommissioningSettings>(configText);
            if (s == null) throw new ArgumentException("JSON为空");
            bool legacy = GUILayout.Toggle(s.useLegacyChassisTiming, "底盘旧模式：定时等待（不查询任务完成状态）");
            if (legacy != s.useLegacyChassisTiming) TryAction(() => SetLegacyChassisMode(legacy));
            GUILayout.Label(s.useLegacyChassisTiming ? "旧模式：估时后须确认底盘停稳；修改后点保存参数。" : "新模式：查询1020的RUNNING→COMPLETED；失败不自动切旧模式。");
        }
        catch (Exception e) { GUILayout.Label("底盘模式暂不可编辑，请先修正JSON：" + e.Message); }
    }
    private void SetLegacyChassisMode(bool legacy)
    {
        if (MotionLocked || connectionBusy || (commander != null && commander.AnyTaskRunning))
            throw new InvalidOperationException("流程执行中不能切换底盘模式，请先停止并确认");
        var s = JsonUtility.FromJson<FieldCommissioningSettings>(configText);
        if (s == null) throw new ArgumentException("配置为空");
        s.useLegacyChassisTiming = legacy; s.Validate(false);
        configText = JsonUtility.ToJson(s, true); confirmed = false;
        SetMessage(legacy ? "已选择底盘旧模式：估时后须人工确认停稳；请保存参数并重新安全确认" : "已选择底盘新模式：任务状态确认；请保存参数并重新安全确认");
    }
    private void TryAction(Action action) { try { action(); } catch (Exception e) { SetMessage(e.Message); } }
    private void ConnectDevices(bool mobile)
    {
        var s = ReadSettings(false);
        if (commander == null || robot == null) throw new InvalidOperationException("ConnectCommander/机械臂未绑定");
        if (commander.AnyTaskRunning) throw new InvalidOperationException("已有任务运行");
        if (!mobile)
        {
            if (robot.IsDashboardConnected && (robot.robotIP != s.robotIp || robot.dashboardPort != s.dashboardPort || robot.feedbackPort != s.feedbackPort))
                throw new InvalidOperationException("先通过原界面断开旧机械臂连接，再连接新地址");
            robot.robotIP = s.robotIp; robot.dashboardPort = s.dashboardPort; robot.feedbackPort = s.feedbackPort;
            robot.Connect(); // 不自动上电/使能；保留原界面的人工操作。
        }
        else
        {
            if (commander.chassisTCP == null || commander.liftTCP == null) throw new InvalidOperationException("底盘/升降未绑定");
            commander.chassisTCP.robotIP = s.chassisIp; commander.chassisTCP.ConnectAll();
            if (commander.liftTCP.IsConnected())
            {
                if (commander.liftTCP.ipAddress != s.liftIp || commander.liftTCP.port != s.liftPort)
                    throw new InvalidOperationException("先断开旧升降连接再修改地址");
            }
            else { commander.liftTCP.ipAddress = s.liftIp; commander.liftTCP.port = s.liftPort; commander.liftTCP.Connect(); }
        }
        SetMessage("已尝试连接，请检查设备连接状态；连接不代表到位或已使能。");
    }
    private async Task CheckEnvironmentFromUIAsync()
    {
        connectionBusy = true;
        try { SetMessage(await CheckEnvironmentAsync(ReadSettings(false))); }
        catch (Exception e) { SetMessage("环境检查失败：" + e.Message); }
        finally { connectionBusy = false; }
    }
    private async Task<string> CheckEnvironmentAsync(FieldCommissioningSettings s)
    {
        string script = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ExternalCode", "commissioning_preflight.py"));
        if (!File.Exists(script)) throw new FileNotFoundException("缺少打包的环境检查脚本", script);
        using (var process = new System.Diagnostics.Process())
        {
            process.StartInfo = new System.Diagnostics.ProcessStartInfo(s.pythonExecutable, "\"" + script + "\"")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!process.HasExited && timer.Elapsed.TotalSeconds < 20) await Task.Delay(100);
            if (!process.HasExited) { process.Kill(); throw new TimeoutException("Python环境检查超时"); }
            string report = await stdout; string error = await stderr;
            Debug.Log("[实物联调][环境] " + report + error);
            if (process.ExitCode != 0) throw new IOException(report + error);
            return "环境检查通过（未连接设备）。";
        }
    }
    private async Task RunAsync(RunMode mode)
    {
        if (MotionLocked || connectionBusy || !confirmed) return;
        historyId = null; monitorFailure = null;
        bool acquired = false, contactPrepared = false, success = false;
        string outcome = "未启动";
        try
        {
            if (commander == null || robot == null) throw new InvalidOperationException("运行组件未绑定");
            if (commander.AnyTaskRunning) throw new InvalidOperationException("已有任务运行");
            if (servo == null) servo = FindObjectOfType<ServoTighteningController>();
            if (servo == null) throw new InvalidOperationException("未找到电批控制器");
            servo.ClearCycleFailure();
            sessionSettings = ReadSettings(true).Copy();
            var s = sessionSettings;
            bool semi = mode == RunMode.SemiSingle || mode == RunMode.SemiMultiple;
            var frozenTiming = semi ? commander.CaptureArmTrajectoryTiming() : null;
            if (s.originalContactTiming && (mode == RunMode.Single || mode == RunMode.Multiple))
                throw new InvalidOperationException("原节奏测试版先使用①精调/②接触单测；粗定位连续轨迹尚未改造，不开放③④");
            List<List<TrajectoryTask>> plan = semi ? commander.SnapshotSemiAutomaticPlan(s, mode == RunMode.SemiMultiple) :
                mode == RunMode.Single || mode == RunMode.Multiple ? commander.SnapshotFieldPlan(s, mode == RunMode.Multiple) : null;
            simulation.PrepareFieldMode();
            Busy = true; commander.isSequenceRunning = true; servo.FieldWorkflowOwned = true;
            semiAutomatic = semi; trajectoryTiming = frozenTiming; continueRequestPending = false;
            if (semi) simulation.SetRealRunInputsLocked(true);
            cancellation = new CancellationTokenSource(); stopTask = null; monitorFailure = null; servo.ClearCycleFailure();
            historyId = RunHistoryCapture.Begin(RunType.REALTIME, semi ? "真实运行/半自动人工跳过视觉" : "现场联调/" + mode, false, plan?.Count ?? 1, false,
                $"机器人:{s.robotIp}，User:{s.userNumber}，Tool:{s.toolNumber}，接触速度:{s.contactSpeedMmS}mm/s，" +
                (s.useLegacyChassisTiming ? "底盘旧模式：估时+人工确认；" : "底盘新模式：任务状态确认；") +
                (semi ? $"视觉由人工确认跳过；发送估速:{frozenTiming.EstimatedSpeedDeg}°/s，缓冲:{frozenTiming.BufferSeconds}s，最小/最大:{frozenTiming.MinSeconds}/{frozenTiming.MaxSeconds}s" : $"模拟视觉J1/J2:{s.visionJ1Deg}/{s.visionJ2Deg}°"));
            historyWasUserStop = historyCollision = false; historyTargetNumber = s.firstTarget;
            OperationLogTool.RecordLog(OperationType.真实运行, "请求实物流程 - " + mode + (semi ? "（视觉人工跳过，每目标暂停）" : "（视觉为模拟偏移）"));
            BeginReport(mode.ToString(), null, plan);
            RunRecorder.SetReportDirectory(reportPath, historyId);
            robot.BeginMotionDiagnostics(reportPath);
            SetMessage("启动 " + mode + "；参数已冻结");
            await CheckEnvironmentAsync(s);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!robot.FieldConnectedTo(s))
                throw new InvalidOperationException("机械臂连接与本次配置不一致");
            if (plan != null)
            {
                foreach (var group in plan) foreach (var task in group)
                {
                    if (task.type == TaskType.Chassis && (commander.chassisTCP == null || !commander.chassisTCP.FieldConnectedTo(s.chassisIp, !s.useLegacyChassisTiming)))
                        throw new InvalidOperationException(s.useLegacyChassisTiming ? "底盘导航连接或IP不匹配" : "底盘状态/导航连接或IP不匹配");
                    if (task.type == TaskType.Lift && (commander.liftTCP == null || !commander.liftTCP.FieldConnectedTo(s.liftIp, s.liftPort)))
                        throw new InvalidOperationException("升降连接/IP不匹配");
                }
            }
            robot.BeginFieldWorkflow(s); acquired = true;
            chassisStopVersion = commander.chassisTCP != null ? commander.chassisTCP.CancellationVersion : 0;
            liftStopVersion = commander.liftTCP != null ? commander.liftTCP.StopVersion : 0;
            if (commander.chassisTCP != null)
            {
                commander.chassisTCP.FieldWorkflowOwned = true;
                commander.chassisTCP.motionTimeoutSeconds = s.chassisTimeoutSeconds;
                commander.chassisTCP.motionStartTimeoutSeconds = s.chassisStartTimeoutSeconds;
            }
            commander.chassisMoveSpeedX = s.chassisSpeedMS; commander.chassisTurnSpeedW = s.chassisTurnRadS; commander.liftVelocity = s.liftSpeedMmS;
            await robot.ConfirmFieldIdleAsync(cancellation.Token);
            if (mode != RunMode.Vision)
            {
                await servo.PrepareFieldServiceAsync(s);
                contactPrepared = true;
                cancellation.Token.ThrowIfCancellationRequested();
                await servo.RequireFieldToolReadyAsync(mode != RunMode.InsertOnly && mode != RunMode.WithdrawOnly);
            }
            if (plan == null)
            {
                if (mode == RunMode.Vision) await VisionAsync(s, cancellation.Token);
                else if (mode == RunMode.InsertOnly || mode == RunMode.WithdrawOnly)
                    await RobotOnlyAsync(s, mode == RunMode.InsertOnly, cancellation.Token);
                else await ContactAsync(s, cancellation.Token);
            }
            else await FieldWorkflowSequence.ExecuteAsync(plan,
                task => ExecuteRecordedFieldTaskAsync(task, s, cancellation.Token),
                () => semi ? WaitForManualContinueAsync(s, cancellation.Token) : VisionAsync(s, cancellation.Token), () => ContactAsync(s, cancellation.Token),
                index => SetMessage("目标 " + (index + 1) + " 已完成"), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            outcome = mode == RunMode.InsertOnly || mode == RunMode.WithdrawOnly
                ? "机械臂单测执行结束；实际行程见末端测量（不等于到位验收）" : "流程完成";
            success = true;
        }
        catch (Exception e)
        {
            outcome = "流程中止：" + FailureCause(e.Message, acquired);
            if (RunRecorder.IsCurrent(historyId) && !(historyWasUserStop && monitorFailure == null &&
                (e is OperationCanceledException || servo?.LastCycleFirstError == "Cycle cancelled")))
                RunRecorder.MarkFailure(outcome, historyCollision || robot.HistoryCollisionObserved, historyId);
            Debug.LogError("[实物联调] " + outcome);
        }
        finally
        {
            if (Busy)
            {
                manualGate.Cancel();
                legacyChassisGate.Cancel();
                servo.SetRealToolMotionEnabled(false);
                if (acquired)
                {
                    cancellation.Cancel();
                    try
                    {
                        if (!success) { if (stopTask == null) stopTask = StopDevicesAsync(contactPrepared); await stopTask; }
                        if (contactPrepared) await servo.WaitFieldCycleIdleAsync();
                        if (!success && !string.IsNullOrEmpty(servo.LastCycleFirstError)) outcome = "流程中止：" + FailureCause(servo.LastCycleFirstError, acquired);
                        await robot.EndFieldWorkflowAsync();
                    }
                    catch (Exception e)
                    {
                        success = false; outcome += "；停止未确认，保持锁定：" + e.Message;
                        if (RunRecorder.IsCurrent(historyId)) RunRecorder.MarkFailure(outcome, false, historyId);
                    }
                }
                if (commander.chassisTCP != null) commander.chassisTCP.FieldWorkflowOwned = robot.FieldWorkflowActive;
                servo.FieldWorkflowOwned = robot.FieldWorkflowActive;
                commander.isSequenceRunning = robot.FieldWorkflowActive;
                simulation.SetRealRunInputsLocked(robot.FieldWorkflowActive);
                confirmed = false;
                SetMessage(outcome);
                ExportReport(success, mode.ToString(), outcome);
                if (RunRecorder.IsCurrent(historyId))
                {
                    RunRecorder.RecordStep("流程收尾", outcome, historyId);
                    RunRecorder.FinishRun(success ? RunRecordResult.Success :
                        historyWasUserStop && monitorFailure == null ? RunRecordResult.EmergencyStop : RunRecordResult.Failed, outcome, historyId);
                }
                OperationLogTool.RecordLog(OperationType.真实运行, "现场联调结束 - " + mode + "：" + outcome);
                Busy = false; semiAutomatic = false; continueRequestPending = false;
                cancellation.Dispose(); cancellation = null;
            }
            else SetMessage(outcome);
        }
    }
    private async Task VisionAsync(FieldCommissioningSettings s, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        double[] target = new double[6];
        if (!robot.TryCopyActualJoints(target, 0.25f)) throw new IOException("模拟视觉缺少新鲜实际关节反馈");
        target[0] += s.visionJ1Deg; target[1] += s.visionJ2Deg;
        SetMessage($"模拟视觉：实际J1/J2加偏移({s.visionJ1Deg}, {s.visionJ2Deg})度");
        await robot.MoveFieldJointsAsync(target, s, cancel);
        if (RunRecorder.IsCurrent(historyId)) RunRecorder.RecordStep($"目标{historyTargetNumber} / 模拟视觉精调（实物J1/J2偏移）", "关节目标已确认完成", historyId);
    }
    private async Task ExecuteRecordedFieldTaskAsync(TrajectoryTask task, FieldCommissioningSettings s, CancellationToken cancel)
    {
        historyTargetNumber = task.targetIndex + 1;
        string stage = task.stage == TrajectoryStage.Navigation ? "底盘运动" : task.stage == TrajectoryStage.Lift ? "升降运动" :
            task.stage == TrajectoryStage.ReturnArm ? "机械臂返程" : "机械臂粗定位";
        SetMessage($"目标{historyTargetNumber}：{stage}执行中");
        if (SemiAutomaticActive) await commander.ExecuteSemiAutomaticTaskAsync(task, s, trajectoryTiming, cancel);
        else await commander.ExecuteFieldTaskAsync(task, s, cancel);
        if (task.type == TaskType.Chassis && s.useLegacyChassisTiming)
        {
            if (RunRecorder.IsCurrent(historyId)) RunRecorder.RecordStep(task.taskName, task.dispatchEvidence, historyId);
            await WaitForLegacyChassisConfirmationAsync(s, cancel);
        }
        if (RunRecorder.IsCurrent(historyId)) RunRecorder.RecordStep(task.taskName,
            task.type == TaskType.Chassis ? s.useLegacyChassisTiming ? "底盘停稳由人工确认（非自动完成状态验收）" : task.dispatchEvidence :
            task.type == TaskType.Lift ? "升降等待结束（沿用原超时策略，非独立到位验收）" : "设备执行完成已确认", historyId);
    }
    private async Task WaitForLegacyChassisConfirmationAsync(FieldCommissioningSettings s, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested(); robot.CheckFieldHealth();
        var joints = new double[6];
        if (!robot.TryCopyActualJoints(joints, .25f)) throw new IOException("底盘旧模式确认缺少机械臂新鲜反馈");
        SetMessage($"目标{historyTargetNumber}：底盘旧模式估时结束；请核对位置/朝向并确认已停稳，再点“确认底盘”");
        if (RunRecorder.IsCurrent(historyId)) RunRecorder.RecordStep($"目标{historyTargetNumber} / 等待底盘人工确认", "仅估时结束，不代表底盘到位", historyId);
        // 关节快照只核对继续时机械臂姿态，不构成底盘到位证据。
        await legacyChassisGate.WaitAsync(historyTargetNumber, joints, cancel);
        cancel.ThrowIfCancellationRequested(); robot.CheckFieldHealth();
        if (commander.chassisTCP.CancellationVersion != chassisStopVersion || !commander.chassisTCP.FieldConnectedTo(s.chassisIp, false))
            throw new OperationCanceledException("底盘确认期间停止或断线，原确认失效");
    }
    public void ConfirmLegacyChassis()
    {
        if (!WaitingForChassisConfirmation) return;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested(); robot.CheckFieldHealth();
            if (commander.chassisTCP == null || commander.chassisTCP.CancellationVersion != chassisStopVersion ||
                !commander.chassisTCP.FieldConnectedTo(sessionSettings.chassisIp, false)) throw new IOException("底盘已停止/断线，不能继续");
            var joints = new double[6];
            if (!robot.TryCopyActualJoints(joints, .25f)) throw new IOException("机械臂反馈不新鲜，不能继续");
            legacyChassisGate.TryContinue(legacyChassisGate.Generation, legacyChassisGate.Target, joints, sessionSettings.jointToleranceDeg);
        }
        catch (Exception e) { monitorFailure = monitorFailure ?? "底盘人工确认失败：" + e.Message; SetMessage(monitorFailure); RequestStop(); }
    }
    private async Task WaitForManualContinueAsync(FieldCommissioningSettings s, CancellationToken cancel)
    {
        var actual = robot.RequireFieldTrajectoryHold();
        SetMessage($"目标{historyTargetNumber}：机械臂粗定位末点已确认；等待人工对位确认（视觉未执行）");
        if (RunRecorder.IsCurrent(historyId)) RunRecorder.RecordStep($"目标{historyTargetNumber} / 等待人工继续", "仅关节到位，不代表末端已对准实物", historyId);
        await manualGate.WaitAsync(historyTargetNumber, actual, cancel);
        cancel.ThrowIfCancellationRequested();
        robot.RequireFieldTrajectoryHold();
        SetMessage($"目标{historyTargetNumber}：人工已确认，跳过视觉，开始卡入—拧紧—退出");
        if (RunRecorder.IsCurrent(historyId)) RunRecorder.RecordStep($"目标{historyTargetNumber} / 人工跳过视觉", "本次人工确认，不是真实视觉精调成功", historyId);
    }
    public async void ContinueSemiAutomatic()
    {
        if (!WaitingForContinue) return;
        continueRequestPending = true;
        int generation = manualGate.Generation, target = manualGate.Target;
        var owner = cancellation;
        var token = owner.Token;
        try
        {
            token.ThrowIfCancellationRequested(); robot.RequireFieldTrajectoryHold();
            await servo.RequireFieldToolReadyAsync();
            token.ThrowIfCancellationRequested();
            var actual = robot.RequireFieldTrajectoryHold();
            if (manualGate.TryContinue(generation, target, actual, sessionSettings.jointToleranceDeg))
                OperationLogTool.RecordLog(OperationType.真实运行, $"目标{target}人工确认并继续（跳过视觉）");
        }
        catch (Exception e)
        {
            if (cancellation == owner && Busy && !token.IsCancellationRequested)
            {
                monitorFailure = monitorFailure ?? "人工继续检查失败：" + e.Message;
                SetMessage(monitorFailure); RequestStop();
            }
        }
        finally { if (cancellation == owner) continueRequestPending = false; }
    }
    private async Task ContactAsync(FieldCommissioningSettings s, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        robot.PermitFieldContact(true); servo.SetRealToolMotionEnabled(true);
        try
        {
            var result = await servo.RunContactCycleAsync(false, cancel,
                2 * s.contactStageTimeoutSeconds + s.tightenTimeoutSeconds + s.beforeTightenSeconds + s.beforeExitSeconds + 20);
            if (!result.Success) throw new IOException(result.Error);
            if (RunRecorder.IsCurrent(historyId)) RunRecorder.RecordStep($"目标{historyTargetNumber} / 卡入—拧紧—退出", "周期成功返回；力矩/角度未采集到历史", historyId);
        }
        finally { servo.SetRealToolMotionEnabled(false); robot.PermitFieldContact(false); }
    }
    private string FailureCause(string fallback, bool acquired)
    {
        if (!string.IsNullOrEmpty(servo?.LastCycleFirstError))
            return servo.LastCycleFirstError == "Cycle cancelled" && monitorFailure != null ? monitorFailure : servo.LastCycleFirstError;
        if (acquired && !string.IsNullOrEmpty(robot.ContactWorkflowError)) return robot.ContactWorkflowError;
        if (acquired && !string.IsNullOrEmpty(robot.ContactAcquisitionError)) return robot.ContactAcquisitionError;
        return monitorFailure ?? fallback;
    }
    private async Task RobotOnlyAsync(FieldCommissioningSettings s, bool insert, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        robot.PermitFieldContact(true); servo.SetRealToolMotionEnabled(false);
        try
        {
            var result = await servo.RunRobotOnlyCycleAsync(insert, cancel, s.contactStageTimeoutSeconds + 20);
            if (!result.Success) throw new IOException(result.Error);
            if (RunRecorder.IsCurrent(historyId)) RunRecorder.RecordStep(insert ? "机械臂单独插入" : "机械臂单独退出", robot.MotionDiagnosticSummary, historyId);
        }
        finally { robot.PermitFieldContact(false); }
    }
    public void RequestStop()
    {
        if (!Busy && (robot == null || !robot.FieldWorkflowActive)) return;
        if (monitorFailure == null) historyWasUserStop = true;
        cancellation?.Cancel();
        manualGate.Cancel();
        legacyChassisGate.Cancel();
        SetMessage("已请求停止；停止确认前不复位、不启动下一目标。必要时使用硬件急停。");
        if (stopTask == null && robot.FieldWorkflowActive) stopTask = StopDevicesAsync(true);
        OperationLogTool.RecordLog(OperationType.真实运行, "请求停止现场流程（非硬件急停）");
    }
    private async Task StopDevicesAsync(bool stopTool)
    {
        var failures = new List<string>();
        // 先启动机器人/电批异步停止，再执行同步Modbus，避免它阻塞前两者。
        Task arm = robot.StopFieldMotionAsync();
        Task<ServoTighteningController.CommandResult> tool = stopTool && servo.ProgramReady ? servo.StopToolAsync() : null;
        try { commander.chassisTCP?.StopMovement(); } catch (Exception e) { failures.Add(e.Message); }
        try { commander.liftTCP?.StopCylinder(); } catch (Exception e) { failures.Add(e.Message); }
        try { await arm; } catch (Exception e) { failures.Add(e.Message); }
        if (tool != null) { try { var r = await tool; if (!r.Success) failures.Add(r.Error); } catch (Exception e) { failures.Add(e.Message); } }
        if (failures.Count > 0) throw new IOException(string.Join("; ", failures));
    }
    private async Task RecoverAsync()
    {
        if (Busy || connectionBusy) return;
        connectionBusy = true;
        bool success = false;
        string outcome = "恢复未完成";
        BeginReport("Recovery", reportPath);
        try
        {
            SetMessage("开始重试停止确认；仅停止与解锁，不续跑运动");
            await StopDevicesAsync(true); if (servo.ProgramReady) await servo.WaitFieldCycleIdleAsync();
            await robot.EndFieldWorkflowAsync(); commander.isSequenceRunning = false; servo.FieldWorkflowOwned = false;
            simulation.SetRealRunInputsLocked(false);
            if (commander.chassisTCP != null) commander.chassisTCP.FieldWorkflowOwned = false;
            stopTask = null; confirmed = false; SetMessage("机械臂停止已确认。本次任务不会续跑；底盘/升降实际停止仍须现场确认。");
            success = true; outcome = message;
        }
        catch (Exception e) { outcome = "仍保持锁定：" + e.Message; SetMessage(outcome); }
        finally { ExportReport(success, "Recovery", outcome); connectionBusy = false; }
    }
    [Serializable] private sealed class RunReport { public string timeUtc, mode, outcome, motionSummary, motionIoError, cleanupErrors; public bool success, completed; }
    [Serializable] private sealed class BuildReport { public string diagnosticSchema, applicationVersion, unityVersion, assemblyId, parentReport; }
    [Serializable] private sealed class TimingReport
    {
        public double estimatedSpeedDeg, bufferSeconds, firstFallbackSeconds, minSeconds, maxSeconds;
        public int intermediateCp = 50, finalCp = 0, maximumPending = DobotController.FieldTrajectoryWindow;
        public string completion = "末点命令ID+新鲜反馈+空闲+六轴误差合格持续0.3秒；间隔不是到位依据";
        public string samplingTiming = "原规划段的估算等待/缓冲/最小最大值只计算一次，按细分份额分摊；末点不额外等待间隔；ACK/队列背压可延长间隔";
    }
    [Serializable] private sealed class TrajectoryPlanReport
    {
        public string schema = "sampled-arm-plan-v1-2026-10-15";
        public string units = "jointsDeg=六轴角度（度）；sourcePoint=原点编号；fraction=该原段分摊份额；sendIntervalSeconds=发出该点后的节流，非到位时间";
        public List<ArmTrajectorySampling.Report> tasks = new List<ArmTrajectorySampling.Report>();
    }
    private void BeginReport(string mode, string parent, List<List<TrajectoryTask>> plan = null)
    {
        journal?.Dispose(); journal = null;
        sessionLog.Clear(); logTruncated = false; captureSession = true;
        reportPath = null;
        try
        {
            string directory = Path.Combine(Application.persistentDataPath, "CommissioningReports", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(directory);
            reportPath = directory;
            File.WriteAllText(Path.Combine(directory, "configuration.json"), JsonUtility.ToJson(sessionSettings, true));
            if (semiAutomatic && trajectoryTiming != null)
                File.WriteAllText(Path.Combine(directory, "trajectory-timing.json"), JsonUtility.ToJson(new TimingReport {
                    estimatedSpeedDeg = trajectoryTiming.EstimatedSpeedDeg, bufferSeconds = trajectoryTiming.BufferSeconds,
                    firstFallbackSeconds = trajectoryTiming.FirstSeconds, minSeconds = trajectoryTiming.MinSeconds,
                    maxSeconds = trajectoryTiming.MaxSeconds
                }, true));
            if (semiAutomatic && trajectoryTiming != null && plan != null)
            {
                var pathReport = new TrajectoryPlanReport();
                foreach (var group in plan) foreach (var task in group)
                    if (task.armSampling != null) pathReport.tasks.Add(task.armSampling.CaptureReport(trajectoryTiming));
                File.WriteAllText(Path.Combine(directory, "trajectory-plan.json"), JsonUtility.ToJson(pathReport, true));
            }
            File.WriteAllText(Path.Combine(directory, "build-info.json"), JsonUtility.ToJson(new BuildReport {
                diagnosticSchema = "sampled-trajectory-v2-2026-10-15", applicationVersion = Application.version,
                unityVersion = Application.unityVersion, assemblyId = typeof(FieldCommissioningPanel).Assembly.ManifestModule.ModuleVersionId.ToString(), parentReport = parent
            }, true));
            AtomicWrite(Path.Combine(directory, "result.json"), JsonUtility.ToJson(new RunReport { timeUtc = DateTime.UtcNow.ToString("O"), mode = mode, outcome = "运行中；若程序异常退出，此结果不代表完成", completed = false }, true));
            journal = new CommissioningJournal(directory);
        }
        catch (Exception e) { SetMessage("诊断报告初始化失败：" + e.Message); }
    }
    private void ExportReport(bool success, string mode, string outcome)
    {
        try
        {
            if (reportPath == null) throw new IOException("报告目录未创建");
            robot?.EndMotionDiagnostics();
            journal?.Dispose();
            if (journal?.Error != null) SetMessage("增量诊断写入失败：" + journal.Error);
            File.WriteAllText(Path.Combine(reportPath, "run.log"), (logTruncated ? "[日志超过200万字符，仅保留尾部]\n" : "") + sessionLog);
            if (!string.IsNullOrEmpty(servo?.LastCycleFailureJson)) AtomicWrite(Path.Combine(reportPath, "cycle-failure.json"), servo.LastCycleFailureJson);
            AtomicWrite(Path.Combine(reportPath, "result.json"), JsonUtility.ToJson(new RunReport { timeUtc = DateTime.UtcNow.ToString("O"), mode = mode, outcome = outcome, success = success, completed = true,
                motionSummary = robot?.MotionDiagnosticSummary, motionIoError = robot?.MotionIoError, cleanupErrors = servo?.LastCycleCleanupErrors }, true));
        }
        catch (Exception e) { SetMessage(message + "；报告导出失败：" + e.Message); }
        finally { captureSession = false; journal?.Dispose(); journal = null; }
    }
    private static void AtomicWrite(string path, string text)
    {
        File.WriteAllText(path + ".tmp", text);
        if (File.Exists(path)) File.Replace(path + ".tmp", path, path + ".bak");
        else File.Move(path + ".tmp", path);
    }
    private void OnDisable() { RequestStop(); }
    private void OnDestroy()
    {
        RequestStop(); Application.logMessageReceived -= CaptureLog;
        robot?.EndMotionDiagnostics();
        captureSession = false; journal?.Dispose(); journal = null;
        if (Active == this) Active = null;
    }
}
