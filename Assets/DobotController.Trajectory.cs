using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public partial class DobotController
{
    // 限制已接受但未确认执行完的指令，不无限堆积控制器队列。
    public const int FieldTrajectoryWindow = 16;
    private sealed class TrajectoryEndpoint
    {
        public ulong id;
        public long afterAck;
        public double[] joints;
        public int user, tool;
    }
    private TrajectoryEndpoint trajectoryEndpoint;

    public async Task ExecuteFieldTrajectoryAsync(IReadOnlyList<double[]> points, double[] start,
        FieldCommissioningSettings settings, ArmTrajectoryTiming timing, CancellationToken cancel,
        IReadOnlyList<double> sendIntervals = null, string trajectoryLabel = "机械臂轨迹")
    {
        if (!FieldWorkflowActive || ExternalControlActive) throw new InvalidOperationException("当前阶段不允许轨迹下发");
        if (points == null || points.Count == 0 || timing == null) throw new ArgumentException("轨迹或发送节奏为空");
        settings.CheckJoints(start);
        if (sendIntervals != null && sendIntervals.Count != points.Count) throw new ArgumentException("轨迹采样间隔与点数不一致");
        var intervals = new List<double>();
        var path = new List<double[]>();
        double[] previous = (double[])start.Clone();
        for (int p = 0; p < points.Count; p++)
        {
            var point = points[p];
            settings.CheckJoints(point);
            ArmTrajectorySampling.CheckStep(previous, point, settings.maxJointStepDeg, trajectoryLabel, p);
            double interval = sendIntervals == null ? timing.DelaySeconds(previous, point) : sendIntervals[p];
            if (double.IsNaN(interval) || double.IsInfinity(interval) || interval < 0)
                throw new ArgumentException($"{trajectoryLabel}：采样点{p+1}发送间隔不是非负有限数值");
            intervals.Add(interval);
            path.Add((double[])point.Clone()); previous = point;
        }
        RequireFieldJointsNear(start, settings.jointToleranceDeg);
        trajectoryEndpoint = null;
        cancel.ThrowIfCancellationRequested();
        int epoch = commandEpoch;
        await ExchangeDashboardAsync(SpeedCommand(settings.robotSpeedPercent), true);
        var pending = new List<ulong>();
        ulong initialCommandId = CopyRobotState().command_id;
        TrajectoryEndpoint endpoint = null;
        for (int i = 0; i < path.Count; i++)
        {
            var budget = System.Diagnostics.Stopwatch.StartNew();
            while (pending.Count >= FieldTrajectoryWindow)
            {
                CheckTrajectoryEpoch(epoch, cancel);
                TrimTrajectoryWindow(pending);
                if (pending.Count < FieldTrajectoryWindow) break;
                if (budget.Elapsed.TotalSeconds >= settings.jointTimeoutSeconds)
                    throw new TimeoutException("机械臂轨迹队列未推进，禁止继续堆积指令");
                await Task.Delay(50, cancel);
            }
            CheckTrajectoryEpoch(epoch, cancel);
            TrimTrajectoryWindow(pending);
            var sent = System.Diagnostics.Stopwatch.StartNew();
            string command = "MovJ(joint={" + Numbers(path[i]) + "}, cp=" + (i == path.Count - 1 ? "0" : "50") + ")";
            ulong id = ParseCommandId(await ExchangeDashboardAsync(command, true));
            if (endpoint == null && id == initialCommandId) throw new IOException("新轨迹复用了交接前命令编号");
            if (endpoint != null && id <= endpoint.id) throw new IOException("轨迹命令编号未递增，拒绝采用不明队列结果");
            pending.Add(id);
            var state = CopyRobotState();
            endpoint = new TrajectoryEndpoint { id = id, afterAck = state.sequence,
                joints = path[i], user = state.user, tool = state.tool };
            Debug.Log($"[半自动轨迹] {trajectoryLabel} point={i + 1}/{path.Count} command_id={id} cp={(i == path.Count - 1 ? 0 : 50)} pending={pending.Count} interval_s={(i == path.Count-1 ? 0 : intervals[i]):F6} target_deg=[{Numbers(path[i])}]");
            // 原段的角差/估速/缓冲预算分摊到补点。ACK耗时计入，不额外重复最小等待/缓冲。
            if (i < path.Count - 1)
            {
                double delay = intervals[i];
                while (sent.Elapsed.TotalSeconds < delay)
                {
                    CheckTrajectoryEpoch(epoch, cancel); TrimTrajectoryWindow(pending);
                    await Task.Delay((int)Math.Max(1, Math.Min(50, (delay - sent.Elapsed.TotalSeconds) * 1000)), cancel);
                }
            }
        }
        // 整段只有这里等待物理到位；段内不逐点等待空闲或调用Stop。
        var timer = System.Diagnostics.Stopwatch.StartNew();
        double stableSince = -1;
        while (timer.Elapsed.TotalSeconds < settings.jointTimeoutSeconds)
        {
            CheckTrajectoryEpoch(epoch, cancel);
            var state = CopyRobotState();
            bool reached = state.mode == 5 && state.sequence > endpoint.afterAck && state.command_id == endpoint.id;
            for (int j = 0; j < 6; j++) reached &= Math.Abs(state.joints[j] - endpoint.joints[j]) <= settings.jointToleranceDeg;
            if (reached)
            {
                if (stableSince < 0) stableSince = timer.Elapsed.TotalSeconds;
                if (timer.Elapsed.TotalSeconds - stableSince >= 0.3)
                {
                    trajectoryEndpoint = endpoint;
                    Debug.Log($"[半自动轨迹][END_CONFIRMED] command_id={endpoint.id} actual_deg=[{Numbers(state.joints)}]");
                    return;
                }
            }
            else stableSince = -1;
            await Task.Delay(50, cancel);
        }
        throw new TimeoutException("机械臂整段轨迹末点实际到位超时");
    }
    private void CheckTrajectoryEpoch(int epoch, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        if (!FieldWorkflowActive || ExternalControlActive || stopLatched || epoch != commandEpoch)
            throw new OperationCanceledException("机械臂轨迹阶段已停止或占用改变");
        RequireRobotReady(true); fieldSettings.CheckJoints(CopyRobotState().joints);
    }
    private void TrimTrajectoryWindow(List<ulong> pending)
    {
        var state = CopyRobotState();
        int current = pending.IndexOf(state.command_id);
        if (current >= 0)
        {
            int count = current + (state.mode == 5 ? 1 : 0);
            if (count > 0) pending.RemoveRange(0, count);
        }
    }
    public double[] RequireFieldJointsNear(double[] expected, double tolerance)
    {
        if (double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0)
            throw new ArgumentException("关节位姿确认容差无效");
        if (!FieldWorkflowActive || ExternalControlActive || stopLatched)
            throw new InvalidOperationException("当前阶段不能确认静止位姿");
        RequireRobotReady(false); fieldSettings.CheckJoints(expected);
        var state = CopyRobotState(); fieldSettings.CheckJoints(state.joints);
        for (int j = 0; j < 6; j++)
            if (Math.Abs(state.joints[j] - expected[j]) > tolerance)
                throw new InvalidOperationException("实际关节与本段起点/暂停位姿不一致，禁止自动连接未经验证的路径");
        return state.joints;
    }
    public double[] RequireFieldTrajectoryHold()
    {
        var endpoint = trajectoryEndpoint;
        if (endpoint == null) throw new InvalidOperationException("本次轨迹末点尚未确认");
        var joints = RequireFieldJointsNear(endpoint.joints, fieldSettings.jointToleranceDeg);
        var state = CopyRobotState();
        if (state.command_id != endpoint.id || state.sequence <= endpoint.afterAck ||
            state.user != endpoint.user || state.tool != endpoint.tool)
            throw new InvalidOperationException("人工等待期间命令或坐标系改变，继续许可失效");
        return joints;
    }
}
