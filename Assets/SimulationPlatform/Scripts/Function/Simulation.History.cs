using UnityEngine;

public partial class Simulation
{
    private bool auditReady;
    private readonly PendingParamLog robotPositionLog = new PendingParamLog("机器人位置(m)");
    private readonly PendingParamLog robotRotationLog = new PendingParamLog("机器人旋转(°)");
    private readonly PendingParamLog scenePositionLog = new PendingParamLog("场景位置(m)");
    private readonly PendingParamLog sceneRotationLog = new PendingParamLog("场景旋转(°)");
    private readonly PendingParamLog realSpeedLog = new PendingParamLog("真实运行速度(%)", OperationType.真实运行);
    public SimulationParam CaptureHistoryParameters() => SaveSimulationParamsToRecord();

    private void InitializeAuditLogging()
    {
        ObserveParam.Instance?.ResetDirectionLog();
        robotPositionLog.Reset(RobotXYZ != null ? RobotXYZ.Value : Vector3.zero);
        robotRotationLog.Reset(RobotRotation != null ? RobotRotation.Value : Vector3.zero);
        scenePositionLog.Reset(SceneXYZ != null ? SceneXYZ.Value : Vector3.zero);
        sceneRotationLog.Reset(SceneRotation != null ? SceneRotation.Value : Vector3.zero);
        realSpeedLog.Reset(new Vector3(SpeedSlider != null ? SpeedSlider.value : 0, 0, 0));
        if (RobotIpInput != null) RobotIpInput.onEndEdit.AddListener(LogRobotIp);
        if (ChassisIpInput != null) ChassisIpInput.onEndEdit.AddListener(LogChassisIp);
        if (SpeedSlider != null) SpeedSlider.onValueChanged.AddListener(LogRealSpeed);
        if (StepRunToggle != null) StepRunToggle.onValueChanged.AddListener(LogStepRun);
        if (SyncToggle != null) SyncToggle.onValueChanged.AddListener(LogSync);
        auditReady = true;
    }
    private void DisposeAuditLogging()
    {
        FlushAuditLogs(); auditReady = false;
        if (RobotIpInput != null) RobotIpInput.onEndEdit.RemoveListener(LogRobotIp);
        if (ChassisIpInput != null) ChassisIpInput.onEndEdit.RemoveListener(LogChassisIp);
        if (SpeedSlider != null) SpeedSlider.onValueChanged.RemoveListener(LogRealSpeed);
        if (StepRunToggle != null) StepRunToggle.onValueChanged.RemoveListener(LogStepRun);
        if (SyncToggle != null) SyncToggle.onValueChanged.RemoveListener(LogSync);
    }
    public void FlushAuditLogs()
    {
        robotPositionLog.Flush(); robotRotationLog.Flush(); scenePositionLog.Flush(); sceneRotationLog.Flush(); realSpeedLog.Flush();
        ObserveParam.Instance?.FlushDirectionLog();
    }
    private void TickAuditLogging()
    {
        robotPositionLog.FlushIfIdle(); robotRotationLog.FlushIfIdle(); scenePositionLog.FlushIfIdle(); sceneRotationLog.FlushIfIdle(); realSpeedLog.FlushIfIdle();
    }
    private void LogRobotIp(string value) => OperationLogTool.RecordLog(OperationType.真实运行, "设置机器人IP：" + value);
    private void LogChassisIp(string value) => OperationLogTool.RecordLog(OperationType.真实运行, "设置底盘IP：" + value);
    private void LogRealSpeed(float value) => realSpeedLog.OnValueChanged(new Vector3(value, 0, 0));
    private void LogStepRun(bool value) => OperationLogTool.RecordLog(OperationType.真实运行, "单步运行：" + (value ? "开" : "关"));
    private void LogSync(bool value) => OperationLogTool.RecordLog(OperationType.真实运行, "实时反馈同步显示：" + (value ? "开" : "关"));
}
