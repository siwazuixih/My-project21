using System;
using System.Threading;
using System.Threading.Tasks;

// 离线动画关卡，只比较模型控制量；绝不表示实物反馈到位。
public sealed class OfflineTargetSession : IDisposable
{
    private readonly ManualTargetGate gate = new ManualTargetGate();
    private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
    private double[] paused;
    public CancellationToken Token => cancellation.Token;
    public bool Waiting => gate.Waiting && !Token.IsCancellationRequested;
    public int Target => gate.Target;
    public int Generation => gate.Generation;

    public Task WaitAsync(int target, double[] controls)
    {
        Validate(controls);
        paused = (double[])controls.Clone();
        return gate.WaitAsync(target, ToDegrees(controls), Token);
    }

    public bool Continue(int generation, int target, double[] controls)
    {
        if (!Waiting || generation != Generation || target != Target) return false;
        try
        {
            RequireUnchanged(controls);
            return gate.TryContinue(generation, target, ToDegrees(controls), .01);
        }
        catch { Cancel(); throw; }
    }

    public void RequireUnchanged(double[] controls)
    {
        Validate(controls);
        if (paused == null) throw new InvalidOperationException("尚未进入模拟等待关卡");
        for (int i = 0; i < 7; i++)
            if (Math.Abs(controls[i] - paused[i]) > (i < 6 ? .01 * Math.PI / 180 : .00001))
                throw new InvalidOperationException("等待期间模拟姿态被改变，请停止后重新开始");
    }

    private static void Validate(double[] controls)
    {
        if (controls == null || controls.Length != 7) throw new ArgumentException("离线关卡需要七个控制量");
        foreach (var value in controls)
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("模拟控制量无效");
    }
    private static double[] ToDegrees(double[] controls)
    {
        var result = new double[6];
        for (int i = 0; i < 6; i++) result[i] = controls[i] * 180 / Math.PI;
        return result;
    }
    public void Cancel() { cancellation.Cancel(); gate.Cancel(); }
    public void Dispose() { Cancel(); cancellation.Dispose(); }
}
