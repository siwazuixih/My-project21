using System;
using System.Collections.Generic;

// 规划器compact控制量保持原单位；只用指定的六个转动关节（rad）决定采样密度。
// 不改分支、不跨原路径拐点、不做±360折返，也不授予实际运动权限。
public sealed class ArmTrajectorySampling
{
    public const double DefaultStepDeg = 5;
    public const int MaximumPoints = 100000;
    private const double NumericToleranceDeg = 1e-7;
    public readonly List<double[]> Points = new List<double[]>();
    private readonly List<double[]> originals = new List<double[]>();
    private readonly List<int> sourceIndices = new List<int>();
    private readonly List<double> fractions = new List<double>();
    private readonly double[] start;
    private readonly int jointStartIndex;
    public readonly double StepDeg;
    public readonly string Label;
    public double MaximumSourceDeltaDeg { get; private set; }
    public int MaximumSourceAxis { get; private set; }
    public int OriginalCount => originals.Count;

    private ArmTrajectorySampling(double[] start, int jointStartIndex, double stepDeg, string label)
    {
        this.start = (double[])start.Clone(); this.jointStartIndex = jointStartIndex;
        StepDeg = stepDeg; Label = label ?? "机械臂轨迹";
    }

    public static ArmTrajectorySampling Build(IReadOnlyList<double[]> points, double[] start,
        int jointStartIndex, double stepDeg, string label)
    {
        if (double.IsNaN(stepDeg) || double.IsInfinity(stepDeg) || stepDeg < (double)0.01f || stepDeg > 10)
            throw new ArgumentException($"{label}：采样步长{stepDeg}°必须在[0.01,10]内；这不是整段转角上限");
        if (points == null || points.Count == 0 || points.Count > MaximumPoints)
            throw new ArgumentException($"{label}：规划路径为空或点数超过{MaximumPoints}");
        ValidateControl(start, jointStartIndex, -1, label, null);
        var result = new ArmTrajectorySampling(start, jointStartIndex, stepDeg, label);
        double[] previous = result.start;
        for (int p = 0; p < points.Count; p++)
        {
            ValidateControl(points[p], jointStartIndex, p, label, start.Length);
            var target = (double[])points[p].Clone(); result.originals.Add(target);
            double maximum = 0;
            for (int j = 0; j < 6; j++)
            {
                double delta = Math.Abs(target[jointStartIndex+j] - previous[jointStartIndex+j]) * 180 / Math.PI;
                maximum = Math.Max(maximum, delta);
                if (delta > result.MaximumSourceDeltaDeg) { result.MaximumSourceDeltaDeg = delta; result.MaximumSourceAxis = j+1; }
            }
            double required = Math.Max(1, Math.Ceiling(maximum / stepDeg));
            if (required > MaximumPoints - result.Points.Count)
                throw new ArgumentException($"{label}：原点{p+1}角差{maximum:F4}°按{stepDeg}°细分后超过{MaximumPoints}点，请核对路径/采样步长");
            int count = (int)required;
            for (int k = 1; k <= count; k++)
            {
                double[] sample = (double[])target.Clone();
                if (k != count)
                    for (int axis = 0; axis < sample.Length; axis++)
                        sample[axis] = previous[axis] * (1.0-(double)k/count) + target[axis] * ((double)k/count);
                result.Points.Add(sample); result.sourceIndices.Add(p); result.fractions.Add(1.0/count);
            }
            previous = target;
        }
        return result;
    }

    internal static void ValidateControl(double[] value, int offset, int point, string label, int? dimension)
    {
        string where = point < 0 ? "起点" : $"原点{point+1}";
        if (value == null || offset < 0 || offset > value.Length-6 || (dimension.HasValue && value.Length != dimension.Value))
            throw new ArgumentException($"{label}：{where}控制量维度/机械臂起始索引错误");
        for (int i = 0; i < value.Length; i++)
            if (double.IsNaN(value[i]) || double.IsInfinity(value[i]))
                throw new ArgumentException($"{label}：{where}控制量[{i}]不是有限数值");
    }

    public static double[] JointsDeg(double[] compact, int offset)
    {
        var result = new double[6];
        for (int j = 0; j < 6; j++) result[j] = compact[offset+j] * 180 / Math.PI;
        return result;
    }

    public static void CheckStep(double[] previous, double[] target, double maximum, string label, int point)
    {
        if (previous == null || target == null || previous.Length != 6 || target.Length != 6 ||
            double.IsNaN(maximum) || double.IsInfinity(maximum) || maximum < (double)0.01f || maximum > 10)
            throw new ArgumentException($"{label}：六轴采样检查的维度/步长无效");
        for (int j = 0; j < 6; j++)
        {
            if (double.IsNaN(previous[j]) || double.IsInfinity(previous[j]) || double.IsNaN(target[j]) || double.IsInfinity(target[j]))
                throw new ArgumentException($"{label}：采样点{point}→{point+1}，J{j+1}不是有限数值");
            double delta = Math.Abs(target[j]-previous[j]);
            if (delta > maximum + NumericToleranceDeg)
                throw new InvalidOperationException($"{label}：采样点{point}→{point+1}，J{j+1} {previous[j]:F6}°→{target[j]:F6}°，差{delta:F6}°超过采样步长{maximum:F6}°；尚未下发本段运动");
        }
    }

    // 缓冲/最小/最大等待只对原规划段算一次，再分摊到新增采样点，避免补点倍增等待。
    // 这是下发节流，不是物理到位判据；ACK/队列背压仍可能延长实际间隔。
    public List<double> SendIntervals(ArmTrajectoryTiming timing)
    {
        if (timing == null) throw new ArgumentNullException(nameof(timing));
        var budgets = new double[originals.Count];
        var previous = JointsDeg(start, jointStartIndex);
        for (int i = 0; i < originals.Count; i++)
        {
            var target = JointsDeg(originals[i], jointStartIndex);
            budgets[i] = timing.DelaySeconds(previous, target); previous = target;
        }
        var result = new List<double>(Points.Count);
        for (int i = 0; i < Points.Count; i++) result.Add(budgets[sourceIndices[i]] * fractions[i]);
        return result;
    }

    [Serializable] public sealed class PointReport
    {
        public int point, sourcePoint;
        public double fraction, sendIntervalSeconds;
        public double[] jointsDeg;
    }
    [Serializable] public sealed class Report
    {
        public string label;
        public double sampleStepDeg, maximumSourceDeltaDeg;
        public int maximumSourceAxis, originalCount, sampledCount;
        public double[] startDeg;
        public List<PointReport> original = new List<PointReport>(), sampled = new List<PointReport>();
    }
    public Report CaptureReport(ArmTrajectoryTiming timing)
    {
        var report = new Report { label = Label, sampleStepDeg = StepDeg,
            maximumSourceDeltaDeg = MaximumSourceDeltaDeg, maximumSourceAxis = MaximumSourceAxis,
            originalCount = originals.Count, sampledCount = Points.Count, startDeg = JointsDeg(start, jointStartIndex) };
        var intervals = SendIntervals(timing);
        for (int i = 0; i < originals.Count; i++) report.original.Add(new PointReport {
            point = i+1, sourcePoint = i+1, fraction = 1, jointsDeg = JointsDeg(originals[i], jointStartIndex) });
        for (int i = 0; i < Points.Count; i++) report.sampled.Add(new PointReport {
            point = i+1, sourcePoint = sourceIndices[i]+1, fraction = fractions[i],
            sendIntervalSeconds = i == Points.Count-1 ? 0 : intervals[i], jointsDeg = JointsDeg(Points[i], jointStartIndex) });
        return report;
    }
}
