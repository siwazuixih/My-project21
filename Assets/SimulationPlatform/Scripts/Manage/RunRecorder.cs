using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using UnityEngine;

// Adapted from wlj RunRecorder. This class only observes workflows; it never controls hardware.
public static class RunRecorder
{
    private const string CollectionName = "RunRecords";
    private static readonly object Gate = new object();
    private static readonly Dictionary<string, RunRecord> Unsaved = new Dictionary<string, RunRecord>();
    private static RunRecord current;
    public static RunRecord Current => current;
    public static string LastError { get; private set; } = "";
    public static string DatabasePath => Path.Combine(PathTool.GetExecutableDirPath(), "Data", "Runs", "RunRecords.db");

    public static string BeginRun(RunType mode, SimulationParam simParams, int targetCount,
        int replaceCount, string realSummary, string source = "仿真", bool simulated = false, bool resume = true)
    {
        lock (Gate)
        {
            string projectId = RunManager.Project?.Id;
            if (string.IsNullOrEmpty(projectId))
            {
                Debug.LogWarning("[RunRecorder] 未选择项目，不创建项目运行记录");
                return null;
            }
            if (current != null)
            {
                if (resume && current.ProjectId == projectId && current.Mode == mode && current.Source == source)
                    return current.Id;
                FinishRun(RunRecordResult.Interrupted, "切换项目或启动另一流程");
            }
            current = new RunRecord { ProjectId = projectId, Mode = mode, Simulated = simulated,
                Source = source ?? "", StartTime = DateTime.Now, TargetCount = targetCount,
                ReplaceCount = replaceCount, SimParams = Snapshot(simParams), RealParamsSummary = realSummary ?? "" };
            Save(current); // A crash must leave a running/unconfirmed record, never a false success.
            Debug.Log($"[RunRecorder] 开始记录：{current.Source}，目标{targetCount}个");
            return current.Id;
        }
    }

    public static bool IsCurrent(string id) => !string.IsNullOrEmpty(id) && current?.Id == id;

    public static void RecordStep(RunStepRecord step, string runId = null)
    {
        lock (Gate)
        {
            if (current == null || step == null || (runId != null && !IsCurrent(runId))) return;
            step.Index = current.Steps.Count;
            current.Steps.Add(step);
            Save(current);
        }
    }

    public static void RecordStep(string targetName, string result, string runId = null)
        => RecordStep(new RunStepRecord(0, targetName) { Result = result ?? "" }, runId);

    public static void MarkFailure(string reason, bool collision = false, string runId = null)
    {
        lock (Gate)
        {
            if (current == null || (runId != null && !IsCurrent(runId))) return;
            // Keep the first failure; a later cancellation/success must not erase it.
            if (current.Result == RunRecordResult.Failed.ToString() || current.Result == RunRecordResult.Collision.ToString()) return;
            current.Result = (collision ? RunRecordResult.Collision : RunRecordResult.Failed).ToString();
            current.ResultReason = reason ?? "未记录原因";
            Save(current);
        }
    }

    public static void FinishRun(RunRecordResult result, string reason = null, string runId = null)
    {
        lock (Gate)
        {
            if (current == null || (runId != null && !IsCurrent(runId))) return;
            if (current.Result != RunRecordResult.Failed.ToString() && current.Result != RunRecordResult.Collision.ToString())
            {
                current.Result = result.ToString();
                current.ResultReason = reason ?? "";
            }
            current.EndTime = DateTime.Now;
            current.DurationSeconds = Math.Max(0, (current.EndTime - current.StartTime).TotalSeconds);
            current.Completed = true;
            Save(current);
            Debug.Log($"[RunRecorder] 结束记录：{current.Result}，{current.ResultReason ?? ""}，步骤{current.Steps.Count}条");
            current = null;
        }
    }

    public static void FinishRun() => FinishRun(RunRecordResult.Interrupted, "离开界面/流程未确认完成");

    public static void SetReportDirectory(string directory, string runId)
    {
        lock (Gate)
        {
            if (!IsCurrent(runId)) return;
            current.ReportDirectory = directory ?? "";
            Save(current);
        }
    }

    private static void Save(RunRecord record)
    {
        // Logging IO failure must not throw into movement/Stop cleanup. Retain retryable records.
        Unsaved[record.Id] = record;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath));
            using (var db = new LiteDatabase(DatabasePath))
            {
                var collection = db.GetCollection<RunRecord>(CollectionName);
                collection.EnsureIndex(x => x.ProjectId);
                collection.EnsureIndex(x => x.StartTime);
                foreach (RunRecord pending in Unsaved.Values) collection.Upsert(pending);
            }
            Unsaved.Clear(); LastError = "";
        }
        catch (Exception e)
        {
            if (LastError != e.Message) Debug.LogWarning("[RunRecorder] 历史记录未落盘，当前进程保留待重试：" + e.Message);
            LastError = e.Message;
        }
    }

    public static List<RunRecord> GetRecords(Project project) => QueryRecords(project?.Id, null, false);
    public static List<RunRecord> GetRecords(Project project, RunType mode) => QueryRecords(project?.Id, mode, false);
    public static List<RunRecord> GetRecords(string projectId, RunType? mode, bool ascending = true)
        => QueryRecords(projectId, mode, ascending);

    private static List<RunRecord> QueryRecords(string projectId, RunType? mode, bool ascending)
    {
        lock (Gate)
        {
            var records = new Dictionary<string, RunRecord>();
            if (string.IsNullOrEmpty(projectId)) return new List<RunRecord>();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath));
                using (var db = new LiteDatabase(DatabasePath))
                    foreach (var record in db.GetCollection<RunRecord>(CollectionName).Find(x => x.ProjectId == projectId))
                    {
                        // LiteDB can round-trip empty strings as null; old wlj rows also omit new fields.
                        record.ResultReason = record.ResultReason ?? "";
                        record.Source = record.Source ?? "";
                        record.ReportDirectory = record.ReportDirectory ?? "";
                        record.Steps = record.Steps ?? new List<RunStepRecord>();
                        records[record.Id] = record;
                    }
                if (Unsaved.Count == 0) LastError = "";
            }
            catch (Exception e) { LastError = e.Message; Debug.LogWarning("[RunRecorder] 查询历史失败：" + e.Message); }
            foreach (RunRecord record in Unsaved.Values)
                if (record.ProjectId == projectId) records[record.Id] = record;
            if (current?.ProjectId == projectId) records[current.Id] = current;
            var filtered = records.Values.Where(x => !mode.HasValue || x.Mode == mode.Value);
            return (ascending ? filtered.OrderBy(x => x.StartTime) : filtered.OrderByDescending(x => x.StartTime)).ToList();
        }
    }

    public static void DeleteProjectRecords(string projectId)
    {
        if (string.IsNullOrEmpty(projectId)) return;
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath));
                using (var db = new LiteDatabase(DatabasePath))
                    db.GetCollection<RunRecord>(CollectionName).DeleteMany(x => x.ProjectId == projectId);
                foreach (string id in Unsaved.Where(x => x.Value.ProjectId == projectId).Select(x => x.Key).ToArray()) Unsaved.Remove(id);
                if (current?.ProjectId == projectId) current = null;
            }
            catch (Exception e) { LastError = e.Message; Debug.LogWarning("[RunRecorder] 删除项目运行记录失败：" + e.Message); }
        }
    }

    private static Vector3D Copy(Vector3D value) => value == null ? null : new Vector3D { X = value.X, Y = value.Y, Z = value.Z };
    private static SimulationParam Snapshot(SimulationParam p) => p == null ? null : new SimulationParam {
        RobotXYZ = Copy(p.RobotXYZ), RobotRotation = Copy(p.RobotRotation), SceneXYZ = Copy(p.SceneXYZ), SceneRotation = Copy(p.SceneRotation),
        BaseSpeedValue = p.BaseSpeedValue, ArmSpeedValue = p.ArmSpeedValue, ObservationDistanceValue = p.ObservationDistanceValue,
        XYZPanelValue = Copy(p.XYZPanelValue), ShowTarget = p.ShowTarget, CameraCurrentX = p.CameraCurrentX,
        CameraCurrentY = p.CameraCurrentY, CameraCurrentDistance = p.CameraCurrentDistance, CameraPanOffset = Copy(p.CameraPanOffset)
    };
}
