using System;

// 每次任务冻结发送节奏；估算速度仅用于节流，不作为机器人实际到位依据。
public sealed class ArmTrajectoryTiming
{
    public readonly double EstimatedSpeedDeg, BufferSeconds, FirstSeconds, MinSeconds, MaxSeconds;
    public ArmTrajectoryTiming(double speed, double buffer, double first, double min, double max)
    {
        double[] values = { speed, buffer, first, min, max };
        foreach (double value in values)
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentException("机械臂发送间隔参数必须为非负有限数值");
        if (speed <= 0 || (max > 0 && max < min))
            throw new ArgumentException("机械臂估算速度须大于0，最大等待不得小于最小等待");
        EstimatedSpeedDeg = speed; BufferSeconds = buffer; FirstSeconds = first;
        MinSeconds = min; MaxSeconds = max;
    }
    public double DelaySeconds(double[] previous, double[] next)
    {
        double delay = FirstSeconds;
        if (next == null || next.Length != 6 || (previous != null && previous.Length != 6))
            throw new ArgumentException("发送节奏需要六轴角度");
        double difference = 0;
        for (int i = 0; i < 6; i++)
        {
            if (double.IsNaN(next[i]) || double.IsInfinity(next[i]) ||
                (previous != null && (double.IsNaN(previous[i]) || double.IsInfinity(previous[i]))))
                throw new ArgumentException("发送节奏角度无效");
            if (previous != null) difference = Math.Max(difference, Math.Abs(next[i] - previous[i]));
        }
        if (previous != null) delay = difference / EstimatedSpeedDeg + BufferSeconds;
        delay = Math.Max(MinSeconds, delay);
        return MaxSeconds > 0 ? Math.Min(MaxSeconds, delay) : delay;
    }
}
