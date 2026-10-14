using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public partial class DobotController
{
    public bool FieldWorkflowActive { get; private set; }
    private FieldCommissioningSettings fieldSettings;
    private bool OriginalContactTimingActive => FieldWorkflowActive && fieldSettings != null && fieldSettings.originalContactTiming;
    public void CheckFieldHealth()
    {
        if (!FieldWorkflowActive) return;
        if (stopLatched) throw new OperationCanceledException("机械臂停止已锁存");
        RequireRobotReady(true);
        fieldSettings.CheckJoints(CopyRobotState().joints);
    }
    public bool IsDashboardConnected => tcpClient != null && tcpClient.Connected;
    public bool FieldConnectedTo(FieldCommissioningSettings s)
    {
        try
        {
            var dashboard = tcpClient?.Client.RemoteEndPoint as System.Net.IPEndPoint;
            var feedback = feedbackClient?.Client.RemoteEndPoint as System.Net.IPEndPoint;
            var ip = System.Net.IPAddress.Parse(s.robotIp);
            return IsDashboardConnected && feedbackClient.Connected && dashboard != null && feedback != null &&
                dashboard.Address.Equals(ip) && dashboard.Port == s.dashboardPort && feedback.Address.Equals(ip) && feedback.Port == s.feedbackPort;
        }
        catch { return false; }
    }
    public string InspectFieldFeedback()
    {
        if (!controlModeConfirmed) throw new InvalidOperationException("RequestControl尚未成功，不能开始实物流程");
        if (ExternalControlActive) throw new InvalidOperationException("外部算法正在占用机械臂");
        RequireRobotReady(false);
        var s = CopyRobotState();
        return $"mode={s.mode} age_s={s.age_s:F3} id={s.command_id} user={s.user} tool={s.tool} six_force_online={s.six_force_online} joints_deg=[{Numbers(s.joints)}] pose_mm_deg=[{Numbers(s.pose)}] force_N_Nm=[{Numbers(s.force)}]";
    }
    public void BeginFieldWorkflow(FieldCommissioningSettings settings)
    {
        ContactAcquisitionError = null;
        ContactWorkflowError = null;
        if (FieldWorkflowActive || ExternalControlActive) throw new InvalidOperationException("机械臂已有任务占用");
        settings.Validate(true);
        InspectFieldFeedback();
        settings.CheckJoints(CopyRobotState().joints);
        fieldSettings = settings.Copy();
        contactTool = settings.toolNumber;
        contactUser = settings.userNumber;
        hardForceN = settings.forceLimitN; hardMomentNm = settings.momentLimitNm; lateralTriggerN = settings.lateralLimitN;
        allowExternalRealMotion = false; // 只在正式接触阶段开放
        stopLatched = false; commandEpoch++;
        FieldWorkflowActive = true;
    }
    public void PermitFieldContact(bool enabled)
    {
        if (enabled && !FieldWorkflowActive) throw new InvalidOperationException("未持有实物流程权限");
        if (enabled)
        {
            if (fieldSettings.feedbackPort != 30004)
                throw new InvalidOperationException("真实接触要求30004高频反馈，请修改现场JSON的feedbackPort并重新连接（30005为200ms周期）");
            RequireContactSensor();
        }
        allowExternalRealMotion = enabled;
    }
    public async Task ConfirmFieldIdleAsync(CancellationToken cancel)
    {
        if (!FieldWorkflowActive) throw new InvalidOperationException("未持有实物流程权限");
        // 与已有Dashboard请求串行排队，明确停止交接前已经送出的指令；不是只看一帧空闲反馈。
        Debug.Log("[实物联调][PRE_MOTION_STOP] 开始前停止确认；本次现场流程尚未发送MovJ偏移/路径指令");
        try { await StopOwnedRobotAsync(); }
        catch
        {
            Debug.LogError("[实物联调][PRE_MOTION_STOP_FAILED] 开始前停止确认失败；本次现场流程未发送MovJ偏移/路径指令");
            throw;
        }
        cancel.ThrowIfCancellationRequested();
        RequireRobotReady(false);
        Debug.Log("[实物联调] 开始前机器人停止/空闲已确认");
    }
    public async Task EndFieldWorkflowAsync()
    {
        allowExternalRealMotion = false;
        Debug.Log("[实物联调][END_STOP] 流程收尾停止确认");
        await StopFieldMotionAsync();
        if (ExternalControlActive) throw new InvalidOperationException("外部接触流程未释放，保持锁定");
        FieldWorkflowActive = false;
    }
    public async Task StopFieldMotionAsync()
    {
        stopLatched = true; commandEpoch++; allowExternalRealMotion = false;
        await StopOwnedRobotAsync();
    }
    public async Task MoveFieldJointsAsync(double[] target, FieldCommissioningSettings settings, CancellationToken cancel)
    {
        if (!FieldWorkflowActive || ExternalControlActive) throw new InvalidOperationException("当前阶段不允许关节运动");
        settings.CheckJoints(target);
        RequireRobotReady(false);
        var initial = CopyRobotState().joints;
        settings.CheckJoints(initial);
        for (int i = 0; i < 6; i++)
            if (Math.Abs(target[i] - initial[i]) > settings.maxJointStepDeg)
                throw new InvalidOperationException($"J{i+1}单步变化超过{settings.maxJointStepDeg}度，请使用已验证的细分路径");
        cancel.ThrowIfCancellationRequested();
        int epoch = commandEpoch;
        await ExchangeDashboardAsync(SpeedCommand(settings.robotSpeedPercent), true);
        cancel.ThrowIfCancellationRequested();
        if (epoch != commandEpoch || stopLatched) throw new OperationCanceledException("机械臂已停止");
        string moveCommand = "MovJ(joint={" + Numbers(target) + "}, cp=0)";
        Debug.Log("[实物联调][MOVJ_REQUEST] 即将请求下发（不是已到位）: " + moveCommand);
        ulong motionId = ParseCommandId(await ExchangeDashboardAsync(moveCommand, true));
        long afterAck = CopyRobotState().sequence;
        Debug.Log("[实物联调][机械臂] command_id=" + motionId + " actual_deg=[" + Numbers(initial) + "] target_deg=[" + Numbers(target) + "]");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        double stableSince = -1;
        while (timer.Elapsed.TotalSeconds < settings.jointTimeoutSeconds)
        {
            cancel.ThrowIfCancellationRequested();
            if (epoch != commandEpoch || stopLatched) throw new OperationCanceledException("机械臂阶段已停止");
            RequireRobotReady(true);
            var s = CopyRobotState();
            settings.CheckJoints(s.joints);
            bool reached = s.mode == 5 && s.sequence > afterAck && s.command_id == motionId;
            for (int i = 0; i < 6; i++)
                reached &= Math.Abs(s.joints[i] - target[i]) <= settings.jointToleranceDeg;
            if (reached)
            {
                if (stableSince < 0) stableSince = timer.Elapsed.TotalSeconds;
                if (timer.Elapsed.TotalSeconds - stableSince >= 0.3)
                { Debug.Log("[实物联调][机械臂] 到位 actual_deg=[" + Numbers(s.joints) + "]"); return; }
            }
            else stableSince = -1;
            await Task.Delay(50, cancel);
        }
        throw new TimeoutException("机械臂实际到位超时");
    }
}
