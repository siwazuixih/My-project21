using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;

// Production recorder + shipped LiteDB DLL, with no Unity scene/device initialization.
public static class Program
{
    private static int passed;
    private static void Check(bool value, string name)
    { if (!value) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
    public static void Main()
    {
        string temp = Path.Combine(Path.GetTempPath(), "unity-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp); PathTool.Root = temp;
        var a = new Project { Id = "project-a", Name = "甲" }; var b = new Project { Id = "project-b", Name = "乙" };
        RunManager.Project = a;
        var parameters = new SimulationParam { RobotXYZ = new Vector3D { X = 1 }, BaseSpeedValue = .3f };
        string id = RunRecorder.BeginRun(RunType.SIMULATION, parameters, 2, 1, "", "仿真");
        Check(RunRecorder.IsCurrent(id), "begin session");
        parameters.RobotXYZ.X = 99; parameters.BaseSpeedValue = 99;
        Check(RunRecorder.Current.SimParams.RobotXYZ.X == 1 && RunRecorder.Current.SimParams.BaseSpeedValue == .3f, "deep parameter snapshot");
        Check(RunRecorder.BeginRun(RunType.SIMULATION, parameters, 2, 1, "", "仿真") == id, "step-mode resumes same project/source");
        using (var db = new LiteDatabase(RunRecorder.DatabasePath))
        {
            var stored = db.GetCollection<RunRecord>("RunRecords").FindById(id);
            Check(stored != null && stored.Result == "Running" && !stored.Completed, "running state is durable before finish");
        }
        RunRecorder.RecordStep("目标甲", "执行结束", id);
        Check(!RunRecorder.Current.Steps[0].HasTorque && !RunRecorder.Current.Steps[0].HasAngle, "missing measurements are explicitly unknown");
        RunRecorder.FinishRun(RunRecordResult.Success, null, id);
        var first = RunRecorder.GetRecords(a).Single();
        Check(first.Result == "Success" && first.Completed && first.ResultReason == "", "success with null reason safely persists");
        Check(first.Steps.Count == 1 && first.Steps[0].Index == 0, "steps persist and index correctly");
        id = RunRecorder.BeginRun(RunType.SIMULATION, null, 1, 0, null, "全流程离线验证", true, false);
        Check(RunRecorder.Current.Simulated && RunRecorder.Current.Mode == RunType.SIMULATION, "offline never classified as physical");
        RunRecorder.MarkFailure("首次错误", false, id);
        RunRecorder.MarkFailure("取消覆盖", false, id);
        RunRecorder.FinishRun(RunRecordResult.Success, "后续误报成功", id);
        Check(RunRecorder.GetRecords(a).Any(x => x.Id == id && x.Result == "Failed" && x.ResultReason == "首次错误"), "first failure survives later success/cancellation");
        string old = RunRecorder.BeginRun(RunType.SIMULATION, null, 1, 0, null, "仿真");
        RunManager.Project = b;
        string next = RunRecorder.BeginRun(RunType.SIMULATION, null, 1, 0, null, "仿真");
        Check(next != old && RunRecorder.Current.ProjectId == b.Id, "same mode across projects creates separate record");
        Check(RunRecorder.GetRecords(a).Single(x => x.Id == old).Result == "Interrupted", "superseded session cannot be success");
        RunRecorder.RecordStep("迟到步骤", "不应写入", old);
        RunRecorder.FinishRun(RunRecordResult.Success, "迟到回调", old);
        Check(RunRecorder.IsCurrent(next) && RunRecorder.Current.Steps.Count == 0, "stale callbacks cannot alter another run");
        RunRecorder.FinishRun(RunRecordResult.Dispatched, "仅下发未验收", next);
        Check(RunRecorder.GetRecords(b).Single().Result == "Dispatched", "dispatched is not success");
        RunManager.Project = a;
        id = RunRecorder.BeginRun(RunType.REALTIME, null, 1, 0, "硬件参数", "现场联调/Contact", false, false);
        RunRecorder.SetReportDirectory("/test/report", id);
        RunRecorder.MarkFailure("碰撞反馈", true, id);
        RunRecorder.FinishRun(RunRecordResult.Interrupted, null, id);
        Check(RunRecorder.GetRecords(a, RunType.REALTIME).Single().Result == "Collision", "collision cause retained");
        Check(RunRecorder.GetRecords(a, RunType.REALTIME).Single().ReportDirectory == "/test/report", "field report linked");
        Check(RunRecorder.GetRecords(a, RunType.SIMULATION).All(x => x.Mode == RunType.SIMULATION), "mode filtering");
        var descending = RunRecorder.GetRecords(a);
        Check(descending.SequenceEqual(descending.OrderByDescending(x => x.StartTime)), "latest records first");
        RunRecorder.DeleteProjectRecords(b.Id);
        Check(RunRecorder.GetRecords(b).Count == 0 && RunRecorder.GetRecords(a).Count > 0, "delete cascades only selected project");

        string blocked = Path.Combine(temp, "blocked"); File.WriteAllText(blocked, "not a directory"); PathTool.Root = blocked;
        id = RunRecorder.BeginRun(RunType.REALTIME, null, 1, 0, "", "IO失败测试", false, false);
        Check(RunRecorder.IsCurrent(id) && !string.IsNullOrEmpty(RunRecorder.LastError), "IO failure cannot throw into workflow");
        RunRecorder.FinishRun(RunRecordResult.Failed, "测试结束", id);
        Check(RunRecorder.GetRecords(a).Any(x => x.Id == id), "failed IO record remains available in process");
        PathTool.Root = temp;
        string retry = RunRecorder.BeginRun(RunType.SIMULATION, null, 1, 0, "", "重试", false, false);
        RunRecorder.FinishRun(RunRecordResult.Success, null, retry);
        Check(RunRecorder.GetRecords(a).Count(x => x.Id == id) == 1 && RunRecorder.LastError == "", "next save retries idempotent pending records");
        UnityEngine.Time.unscaledTime = 0;
        var log = new PendingParamLog("测试参数"); log.Reset(new UnityEngine.Vector3(1, 2, 3));
        log.OnValueChanged(new UnityEngine.Vector3(2, 2, 3)); log.OnValueChanged(new UnityEngine.Vector3(3, 2, 3));
        Check(OperationLogTool.Logs.Count == 0, "parameter edit burst not logged per callback");
        UnityEngine.Time.unscaledTime = 1; log.FlushIfIdle();
        Check(OperationLogTool.Logs.Count == 1 && OperationLogTool.Logs[0].Contains("1.00") && OperationLogTool.Logs[0].Contains("3.00"), "idle flush captures first old to final new value");
        log.Flush(); Check(OperationLogTool.Logs.Count == 1, "flush does not duplicate last edit");
        log.OnValueChanged(new UnityEngine.Vector3(4, 2, 3)); log.Reset(new UnityEngine.Vector3(5, 2, 3)); log.Flush();
        Check(OperationLogTool.Logs.Count == 1, "programmatic load resets baseline without operation log");
        Console.WriteLine($"RunHistoryHarness: {passed} passed. Temporary reports: {temp}");
    }
}
public sealed class Project { public string Id { get; set; } public string Name { get; set; } }
public static class RunManager { public static Project Project { get; set; } }
public static class PathTool { public static string Root; public static string GetExecutableDirPath() => Root; }
public static class OperationLogTool { public static List<string> Logs = new List<string>(); public static void RecordLog(OperationType type, string value) => Logs.Add(value); }
namespace UnityEngine
{
    public static class Debug { public static void Log(object text) { } public static void LogWarning(object text) { } }
    public static class Time { public static float unscaledTime; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static bool operator ==(Vector3 a, Vector3 b) => a.x==b.x && a.y==b.y && a.z==b.z;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a==b);
        public override bool Equals(object other) => other is Vector3 v && this==v;
        public override int GetHashCode() => HashCode.Combine(x,y,z);
    }
}
