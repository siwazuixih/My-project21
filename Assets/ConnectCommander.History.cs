using System.Collections;

public partial class ConnectCommander
{
    private string offlineHistoryId;
    private string legacyHistoryId;

    private IEnumerator RecordLegacyTaskRoutine(TrajectoryTask task, IEnumerator work)
    {
        if (!RunRecorder.IsCurrent(legacyHistoryId))
            legacyHistoryId = RunHistoryCapture.Begin(RunType.REALTIME, "原通讯下发", false, GetTargetOrder().Count, true);
        string id = legacyHistoryId;
        if (RunRecorder.IsCurrent(id)) RunRecorder.RecordStep(task.taskName, "请求下发", id);
        bool ended = false;
        try
        {
            yield return work;
            ended = true;
            if (!RunRecorder.IsCurrent(id)) yield break;
            if (!task.isDispatched)
            {
                RunRecorder.RecordStep(task.taskName, "未完成：" + task.dispatchError, id);
                RunRecorder.FinishRun(RunRecordResult.Failed, task.dispatchError ?? "子任务未确认完成", id);
            }
            else
            {
                RunRecorder.RecordStep(task.taskName, task.type == TaskType.Chassis ? task.dispatchEvidence ?? "底盘未提供完成证据" :
                    task.type == TaskType.Arm ? "下发/预计等待结束，未按实际关节验收到位" : "升降等待结束，沿用原超时策略", id);
                if (taskPlaylist.TrueForAll(t => t.isDispatched))
                    RunRecorder.FinishRun(RunRecordResult.Dispatched, "任务清单下发/等待结束，不等于全设备实际到位与拧紧验收", id);
            }
        }
        finally
        {
            if (!ended && RunRecorder.IsCurrent(id)) RunRecorder.FinishRun(RunRecordResult.Interrupted, "下发协程中断", id);
        }
    }
    private void RecordOfflineStep(string name, string result)
    {
        if (RunRecorder.IsCurrent(offlineHistoryId)) RunRecorder.RecordStep(name, result, offlineHistoryId);
    }
}
