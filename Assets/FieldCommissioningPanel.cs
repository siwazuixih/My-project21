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
    private enum RunMode { Vision, Contact, Single, Multiple, InsertOnly, WithdrawOnly }

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
        if (text.Contains("StopTrace") || text.Contains("[实物联调]") || text.Contains("[MotionTrace]") || text.Contains("[ContactCycle]") ||
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
            if ((commander.chassisTCP != null && commander.chassisTCP.CancellationVersion != chassisStopVersion) ||
                (commander.liftTCP != null && commander.liftTCP.StopVersion != liftStopVersion))
                throw new OperationCanceledException("底盘/升降收到停止请求");
        }
        catch (Exception e) { monitorFailure = monitorFailure ?? e.Message; Debug.LogError("[实物联调][监控] " + e.Message); RequestStop(); }
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
        if (GUI.Button(new Rect(10, 115, 150, 32), "现场联调 / 参数")) visible = !visible;
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
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("保存参数")) TryAction(() => { var s = ReadSettings(false); AtomicWrite(configPath, JsonUtility.ToJson(s, true)); SetMessage("参数已保存：" + configPath); });
        if (GUILayout.Button("环境检查（无设备）")) _ = CheckEnvironmentFromUIAsync();
        if (GUILayout.Button("连接机械臂")) TryAction(() => ConnectDevices(false));
        if (GUILayout.Button("连接底盘/升降")) TryAction(() => ConnectDevices(true));
        if (GUILayout.Button("检查机械臂反馈")) TryAction(() => SetMessage(robot.InspectFieldFeedback()));
        GUILayout.EndHorizontal();
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Clamp(height - 350, 120, 470)));
        GUILayout.Label("JSON参数：角度deg，距离mm（底盘速度m/s），时间s。六轴限位必须现场填写。\n下方确认不保存；修改参数后请重新核对。firstTarget为从1开始的目标编号。");
        float textHeight = GUI.skin.textArea.CalcHeight(new GUIContent(configText), Mathf.Max(100, width - 65));
        string edited = GUILayout.TextArea(configText, GUILayout.Height(Mathf.Max(680, textHeight + 20)));
        if (edited != configText) { configText = edited; confirmed = false; }
        GUILayout.EndScrollView();
        confirmed = GUILayout.Toggle(confirmed, "本次确认：人员远离/急停可用；已核对限位、工具方向及路径；接受升降超时仍继续的既有规则");
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
        if (GUILayout.Button("停止本次流程（非硬件急停）", GUILayout.Height(36))) RequestStop();
        if (!Busy && robot != null && robot.FieldWorkflowActive && GUILayout.Button("重试停止确认并解除本机流程锁")) _ = RecoverAsync();
        GUILayout.Label("配置：" + configPath + "\n最近报告：" + (reportPath ?? "尚无") + "\n离线验证仍使用原来的“全流程验证（模拟）”及“开始运行”。");
        GUILayout.EndScrollView();
        GUILayout.EndArea();
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
        bool acquired = false, contactPrepared = false, success = false;
        string outcome = "未启动";
        try
        {
            if (commander == null || robot == null) throw new InvalidOperationException("运行组件未绑定");
            if (commander.AnyTaskRunning) throw new InvalidOperationException("已有任务运行");
            if (servo == null) servo = FindObjectOfType<ServoTighteningController>();
            if (servo == null) throw new InvalidOperationException("未找到电批控制器");
            sessionSettings = ReadSettings(true).Copy();
            var s = sessionSettings;
            if (s.originalContactTiming && (mode == RunMode.Single || mode == RunMode.Multiple))
                throw new InvalidOperationException("原节奏测试版先使用①精调/②接触单测；粗定位连续轨迹尚未改造，不开放③④");
            List<List<TrajectoryTask>> plan = mode == RunMode.Single || mode == RunMode.Multiple
                ? commander.SnapshotFieldPlan(s, mode == RunMode.Multiple) : null;
            simulation.PrepareFieldMode();
            Busy = true; commander.isSequenceRunning = true; servo.FieldWorkflowOwned = true;
            cancellation = new CancellationTokenSource(); stopTask = null; monitorFailure = null; servo.ClearCycleFailure();
            BeginReport(mode.ToString(), null);
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
                    if (task.type == TaskType.Chassis && (commander.chassisTCP == null || !commander.chassisTCP.FieldConnectedTo(s.chassisIp)))
                        throw new InvalidOperationException("底盘状态/导航连接或IP不匹配");
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
                task => commander.ExecuteFieldTaskAsync(task, s, cancellation.Token),
                () => VisionAsync(s, cancellation.Token), () => ContactAsync(s, cancellation.Token),
                index => SetMessage("目标 " + (index + 1) + " 已完成"), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            outcome = mode == RunMode.InsertOnly || mode == RunMode.WithdrawOnly
                ? "机械臂单测执行结束；实际行程见末端测量（不等于到位验收）" : "流程完成";
            success = true;
        }
        catch (Exception e) { outcome = "流程中止：" + FailureCause(e.Message, acquired); Debug.LogError("[实物联调] " + outcome); }
        finally
        {
            if (Busy)
            {
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
                    catch (Exception e) { success = false; outcome += "；停止未确认，保持锁定：" + e.Message; }
                }
                if (commander.chassisTCP != null) commander.chassisTCP.FieldWorkflowOwned = robot.FieldWorkflowActive;
                servo.FieldWorkflowOwned = robot.FieldWorkflowActive;
                commander.isSequenceRunning = robot.FieldWorkflowActive;
                confirmed = false;
                SetMessage(outcome);
                ExportReport(success, mode.ToString(), outcome);
                Busy = false; cancellation.Dispose(); cancellation = null;
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
        }
        finally { robot.PermitFieldContact(false); }
    }
    public void RequestStop()
    {
        if (!Busy && (robot == null || !robot.FieldWorkflowActive)) return;
        cancellation?.Cancel();
        SetMessage("已请求停止；停止确认前不复位、不启动下一目标。必要时使用硬件急停。");
        if (stopTask == null && robot.FieldWorkflowActive) stopTask = StopDevicesAsync(true);
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
            if (commander.chassisTCP != null) commander.chassisTCP.FieldWorkflowOwned = false;
            stopTask = null; confirmed = false; SetMessage("机械臂停止已确认。本次任务不会续跑；底盘/升降实际停止仍须现场确认。");
            success = true; outcome = message;
        }
        catch (Exception e) { outcome = "仍保持锁定：" + e.Message; SetMessage(outcome); }
        finally { ExportReport(success, "Recovery", outcome); connectionBusy = false; }
    }
    [Serializable] private sealed class RunReport { public string timeUtc, mode, outcome, motionSummary, motionIoError, cleanupErrors; public bool success, completed; }
    [Serializable] private sealed class BuildReport { public string diagnosticSchema, applicationVersion, unityVersion, assemblyId, parentReport; }
    private void BeginReport(string mode, string parent)
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
            File.WriteAllText(Path.Combine(directory, "build-info.json"), JsonUtility.ToJson(new BuildReport {
                diagnosticSchema = "motion-measurement-v1-2026-10-08", applicationVersion = Application.version,
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
