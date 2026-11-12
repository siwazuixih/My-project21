using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public partial class RobokitController
{
    public bool ManualJogActive { get; private set; }
    public bool ManualJogNeedsReview { get; private set; }
    public bool ManualJogLocked => ManualJogActive || ManualJogNeedsReview || manualJogStopBusy || manualJogRecoveryBusy;
    public string ManualJogStatus { get; private set; } = "未发送手动运动。方向以实车车头为准。";
    private bool manualJogStopBusy, manualJogRecoveryBusy;

    public async Task<bool> ExecuteManualJogAsync(ChassisJogCommand command, bool operatorConfirmed)
    {
        if (command == null || !operatorConfirmed) return RejectManualJog("请先确认机械臂/工具已收妥、周围无人、底盘停稳且无其他控制源");
        if (ManualJogLocked || ConfirmedMotionActive || FieldWorkflowOwned || connecting ||
            manualJogStopBusy || manualJogRecoveryBusy || (stopRequestTask != null && !stopRequestTask.IsCompleted))
            return RejectManualJog("已有运动/流程/停止或待核对状态，不累计手动点击");
        if (!FieldConnectedTo(robotIP)) return RejectManualJog("底盘状态/导航端口未连接到当前IP，请先连接底盘");
        // 在第一次await之前占用；复用原1020闭环，不根据估计时间冒充完成。
        ManualJogActive = true;
        ManualJogStatus = "正在" + command.Description + "；等待底盘任务状态确认";
        Debug.Log("[底盘手动] " + ManualJogStatus);
        try
        {
            try { RunDataCapture.EnsureManualSession?.Invoke(); RunDataCapture.Current?.Phase("底盘手动/" + command.Description); }
            catch (Exception e) { Debug.LogWarning("[底盘手动] 数据记录初始化失败：" + e.Message); }
            bool success = await ExecuteRelativeConfirmedAsync(command.Api, command.Json);
            if (!success || ManualJogNeedsReview)
            {
                ManualJogNeedsReview = true;
                ManualJogStatus = "未确认本次点动完成，请检查实车并核对停稳：" + LastConfirmedMotionError;
                return false;
            }
            ManualJogStatus = command.Description + "：已观察RUNNING→COMPLETED（控制器任务完成，非独立位置测量）";
            Debug.Log("[底盘手动] " + ManualJogStatus);
            return true;
        }
        catch (Exception e)
        {
            ManualJogNeedsReview = true;
            ManualJogStatus = "点动异常，保持手动锁：" + e.Message;
            await StopManualJogAsync();
            return false;
        }
        finally { ManualJogActive = false; }
    }

    public async Task StopManualJogAsync()
    {
        if (manualJogStopBusy) return;
        ManualJogNeedsReview = true; // 即使停止应答成功，也不能当作物理停稳。
        manualJogStopBusy = true;
        CancelConfirmedMotion(); // 即使停止通道已经断开，也先阻止本机继续查询/下发。
        ManualJogStatus = "已请求停止底盘；保持锁定，必要时使用硬件急停";
        try
        {
            if (navStream == null || !navStream.CanWrite) throw new IOException("导航停止通道不可用");
            await StopMovementAsync();
            ManualJogStatus = "停止请求已获应答；仍须人工确认底盘停稳后解除手动锁";
        }
        catch (Exception e) { ManualJogStatus = "停止未获确认，保持锁定；检查现场/硬件急停：" + e.Message; }
        finally { manualJogStopBusy = false; Debug.LogWarning("[底盘手动] " + ManualJogStatus); }
    }

    // 只能由明确的人工停稳确认入口调用；不是断线后自动复位/恢复运动。
    public async Task<bool> AcknowledgeManualJogStoppedAsync(bool operatorObservedStopped)
    {
        if (!operatorObservedStopped || ManualJogActive || ConfirmedMotionActive || manualJogStopBusy || manualJogRecoveryBusy || FieldWorkflowOwned)
            return RejectManualJog("请等停止请求结束并人工确认底盘停稳，再解除手动锁");
        if (!FieldConnectedTo(robotIP)) return RejectManualJog("解除手动锁前需要恢复当前底盘状态/导航连接");
        manualJogRecoveryBusy = true; ManualJogNeedsReview = true;
        try
        {
            await StopMovementAsync();
            int version = CancellationVersion;
            var status = DecodeChassisReply(await RequestConfirmedAsync(1020, "{\"simple\":false}", CancellationToken.None));
            if (version != CancellationVersion) throw new OperationCanceledException("核对期间又收到停止/连接变更，保持手动锁");
            if (status.task_status < 0 || status.task_status > 6 || status.task_status == 1 || status.task_status == 2 || status.task_status == 3)
                throw new IOException("底盘仍有活动任务或状态无效，不能解除手动锁");
            ManualJogNeedsReview = false;
            ManualJogStatus = "人工已核对停稳且当前无活动任务，手动锁已解除；不会续跑旧动作。请重新核对规划起点。";
            Debug.Log("[底盘手动] " + ManualJogStatus);
            return true;
        }
        catch (Exception e) { ManualJogStatus = "仍保持手动锁：" + e.Message; Debug.LogWarning("[底盘手动] " + ManualJogStatus); return false; }
        finally { manualJogRecoveryBusy = false; }
    }
    private bool RejectManualJog(string reason)
    {
        if (!ManualJogLocked) ManualJogStatus = reason; // 拒绝新请求不覆盖在途/故障的首要提示。
        Debug.LogWarning("[底盘手动] " + reason); return false;
    }
}
