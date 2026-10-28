using UnityEngine;

public static class RunHistoryCapture
{
    public static string Begin(RunType mode, string source, bool simulated, int targetCount, bool resume = false, string summary = null)
    {
        // One lookup per logical run, never per trajectory point or feedback sample.
        Simulation simulation = Object.FindObjectOfType<Simulation>();
        simulation?.FlushAuditLogs();
        return RunRecorder.BeginRun(mode, simulation != null ? simulation.CaptureHistoryParameters() : RunManager.Project?.SimulationParam,
            targetCount, simulation?.JointReplace?.ReplaceRecords?.Count ?? RunManager.Project?.Replaces?.Count ?? 0,
            summary ?? (simulation == null ? "" : $"机器人IP:{simulation.RobotIpInput?.text}，底盘IP:{simulation.ChassisIpInput?.text}，速度(%):{simulation.SpeedSlider?.value}"),
            source, simulated, resume);
    }
}
