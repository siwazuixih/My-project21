using System;
using System.Threading;
using System.Threading.Tasks;

// 不依赖Unity/设备的逐目标人工关卡；旧点击、重复点击、停止后的点击不能放行。
public sealed class ManualTargetGate
{
    private readonly object sync = new object();
    private TaskCompletionSource<bool> completion;
    private double[] pausedJoints;
    private bool waiting;
    private int generation, target;
    public bool Waiting { get { lock (sync) return waiting; } }
    public int Generation { get { lock (sync) return generation; } }
    public int Target { get { lock (sync) return target; } }

    public async Task WaitAsync(int targetIndex, double[] joints, CancellationToken cancellation)
    {
        ValidateJoints(joints);
        cancellation.ThrowIfCancellationRequested();
        int ticket;
        TaskCompletionSource<bool> current;
        lock (sync)
        {
            if (completion != null && !completion.Task.IsCompleted)
                throw new InvalidOperationException("已有目标等待人工确认");
            ticket = ++generation; target = targetIndex;
            pausedJoints = (double[])joints.Clone(); waiting = true;
            completion = current = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        using (cancellation.Register(() => Cancel(ticket)))
        {
            try { await current.Task; cancellation.ThrowIfCancellationRequested(); }
            finally { lock (sync) if (generation == ticket) waiting = false; }
        }
    }
    public bool TryContinue(int ticket, int targetIndex, double[] actualJoints, double tolerance)
    {
        lock (sync)
        {
            if (!waiting || generation != ticket || target != targetIndex) return false;
            ValidateJoints(actualJoints);
            if (double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0)
                throw new ArgumentException("人工确认关节容差无效");
            for (int i = 0; i < 6; i++)
                if (Math.Abs(actualJoints[i] - pausedJoints[i]) > tolerance)
                {
                    var error = new InvalidOperationException("暂停后机械臂位姿改变，原人工继续许可失效");
                    waiting = false; completion.TrySetException(error);
                    throw error;
                }
            waiting = false;
            return completion.TrySetResult(true);
        }
    }
    public void Cancel() { lock (sync) Cancel(generation); }
    private void Cancel(int ticket)
    {
        lock (sync)
        {
            if (generation != ticket) return;
            waiting = false; completion?.TrySetCanceled();
        }
    }
    private static void ValidateJoints(double[] values)
    {
        if (values == null || values.Length != 6) throw new ArgumentException("人工关卡需要六轴实际角度");
        foreach (double value in values)
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("人工关卡角度无效");
    }
}
