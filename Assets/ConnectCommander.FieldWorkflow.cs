using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public partial class ConnectCommander
{
    public List<List<TrajectoryTask>> SnapshotFieldPlan(FieldCommissioningSettings settings, bool allTargets)
    {
        var result = new List<List<TrajectoryTask>>();
        var targets = GetTargetOrder();
        int first = targets.IndexOf(settings.firstTarget - 1);
        if (first < 0) throw new InvalidOperationException("找不到所选目标，请先生成任务并核对目标编号");
        for (int t = first; t < targets.Count && (allTargets || t == first); t++)
        {
            var group = new List<TrajectoryTask>();
            TrajectoryTask coarse = null, returning = null;
            int lastStage = 0;
            foreach (var original in taskPlaylist)
            {
                if (original.targetIndex != targets[t]) continue;
                if (original.stage == TrajectoryStage.Unspecified) throw new InvalidOperationException("旧任务缺少阶段信息，请重新生成");
                int stage = (int)original.stage;
                if (stage <= lastStage || stage > (int)TrajectoryStage.ReturnArm ||
                    (original.stage == TrajectoryStage.Navigation ? original.type != TaskType.Chassis :
                     original.stage == TrajectoryStage.Lift ? original.type != TaskType.Lift : original.type != TaskType.Arm))
                    throw new InvalidOperationException("目标任务阶段重复、顺序或类型错误，请重新规划");
                lastStage = stage;
                var copy = new TrajectoryTask { taskName = original.taskName, targetIndex = original.targetIndex,
                    type = original.type, stage = original.stage, initialForward = original.initialForward,
                    targetLiftHeight = original.targetLiftHeight,
                    armStartPose = original.armStartPose == null ? null : (double[])original.armStartPose.Clone(),
                    chassisPoints = original.chassisPoints == null ? new List<Vector3>() : new List<Vector3>(original.chassisPoints) };
                if (original.armPoints != null) foreach (var p in original.armPoints) copy.armPoints.Add(p == null ? null : (double[])p.Clone());
                if (copy.stage == TrajectoryStage.CoarseArm) coarse = copy;
                if (copy.stage == TrajectoryStage.ReturnArm) returning = copy;
                group.Add(copy);
            }
            if (coarse == null || coarse.armPoints.Count == 0 || coarse.armStartPose == null)
                throw new InvalidOperationException("目标缺少粗定位轨迹/起点，拒绝启动实物流程");
            string coarseLabel = $"目标{targets[t]+1}/{TrajectoryStage.CoarseArm}";
            ArmTrajectorySampling.ValidateControl(coarse.armStartPose, armJointStartIndex, -1, coarseLabel, null);
            for (int p = 0; p < coarse.armPoints.Count; p++)
                ArmTrajectorySampling.ValidateControl(coarse.armPoints[p], armJointStartIndex, p, coarseLabel, coarse.armStartPose.Length);
            if (returning == null)
            {
                returning = new TrajectoryTask { taskName = $"目标{targets[t]+1}: 原粗定位路径反向复位", type = TaskType.Arm,
                    stage = TrajectoryStage.ReturnArm, targetIndex = targets[t],
                    armStartPose = (double[])coarse.armPoints[coarse.armPoints.Count-1].Clone() };
                for (int i = coarse.armPoints.Count - 1; i >= 0; i--) returning.armPoints.Add((double[])coarse.armPoints[i].Clone());
                returning.armPoints.Add((double[])coarse.armStartPose.Clone());
                group.Add(returning);
            }
            // 必须先核对返程声明，不能靠插值掩盖接触退出后/规划拼接的起点不一致。
            var coarseEnd = coarse.armPoints[coarse.armPoints.Count-1];
            if (returning.armStartPose == null)
            {
                if (returning.armPoints.Count == 0 || !SameArmPose(returning.armPoints[0], coarseEnd, settings.jointToleranceDeg))
                    throw new InvalidOperationException($"目标{targets[t]+1}/返程缺少声明起点，且首点与粗定位终点不一致；拒绝自动补接路径");
                returning.armStartPose = (double[])coarseEnd.Clone();
            }
            if (!SameArmPose(returning.armStartPose, coarseEnd, settings.jointToleranceDeg))
                throw new InvalidOperationException($"目标{targets[t]+1}/返程声明起点与粗定位终点不一致");
            returning.armStartPose = (double[])coarseEnd.Clone();
            foreach (var task in group)
            {
                if (task.type == TaskType.Lift)
                {
                    if (liftTCP == null || settings.liftMaxMm <= settings.liftMinMm)
                        throw new InvalidOperationException("包含升降任务，必须绑定升降并填写实际行程上下限");
                    FieldCommissioningSettings.Range(task.targetLiftHeight * liftTCP.simToRealScale, settings.liftMinMm, settings.liftMaxMm, "升降目标毫米");
                }
                if (task.type == TaskType.Chassis)
                {
                    if (task.chassisPoints.Count < 2 || task.initialForward.sqrMagnitude < 0.00001f ||
                        float.IsNaN(task.initialForward.sqrMagnitude) || float.IsInfinity(task.initialForward.sqrMagnitude))
                        throw new InvalidOperationException("底盘缺少路径或初始朝向");
                    foreach (var p in task.chassisPoints)
                        if (float.IsNaN(p.sqrMagnitude) || float.IsInfinity(p.sqrMagnitude)) throw new InvalidOperationException("底盘路径包含非有限数值");
                }
                if (task.type == TaskType.Arm)
                {
                    string label = $"目标{task.targetIndex+1}/{task.stage}";
                    task.armSampling = ArmTrajectorySampling.Build(task.armPoints, task.armStartPose,
                        armJointStartIndex, settings.maxJointStepDeg, label);
                    // 线性细分不把非法端点变成合法路径，仍逐点执行实际配置的绝对限位检查。
                    double[] previous = ExtractArmJoints(task.armStartPose);
                    CheckPlannedJoints(settings, previous, label + "/起点");
                    for (int p = 0; p < task.armSampling.Points.Count; p++)
                    {
                        var joint = ExtractArmJoints(task.armSampling.Points[p]);
                        CheckPlannedJoints(settings, joint, label + $"/采样点{p+1}");
                        ArmTrajectorySampling.CheckStep(previous, joint, settings.maxJointStepDeg, label, p);
                        previous = joint;
                    }
                    task.armPoints = task.armSampling.Points;
                    Debug.Log($"[轨迹采样] {label} 原点={task.armSampling.OriginalCount} 采样点={task.armPoints.Count} 原最大角差={task.armSampling.MaximumSourceDeltaDeg:F4}°(J{task.armSampling.MaximumSourceAxis}) 步长={settings.maxJointStepDeg}°");
                }
            }
            if (result.Count > 0)
            {
                var prior = result[result.Count - 1];
                var reset = prior[prior.Count - 1];
                var finish = ExtractArmJoints(reset.armPoints[reset.armPoints.Count - 1]);
                var start = ExtractArmJoints(coarse.armStartPose);
                for (int j = 0; j < 6; j++)
                    if (Math.Abs(finish[j] - start[j]) > settings.jointToleranceDeg)
                        throw new InvalidOperationException("上一目标复位与下一目标起点不一致，请开启规划中的移动前复位并重新生成任务");
            }
            result.Add(group);
        }
        return result;
    }
    private bool SameArmPose(double[] left, double[] right, double tolerance)
    {
        if (left == null || right == null || armJointStartIndex < 0 ||
            armJointStartIndex > left.Length-6 || armJointStartIndex > right.Length-6) return false;
        var a = ExtractArmJoints(left); var b = ExtractArmJoints(right);
        for (int j = 0; j < 6; j++)
            if (double.IsNaN(a[j]) || double.IsInfinity(a[j]) || double.IsNaN(b[j]) || double.IsInfinity(b[j]) ||
                Math.Abs(a[j]-b[j]) > tolerance) return false;
        return true;
    }
    private static void CheckPlannedJoints(FieldCommissioningSettings settings, double[] joints, string label)
    {
        try { settings.CheckJoints(joints); }
        catch (ArgumentException e) { throw new ArgumentException(label + "：" + e.Message, e); }
    }
    public ArmTrajectoryTiming CaptureArmTrajectoryTiming() => new ArmTrajectoryTiming(
        armJointSpeedDeg, armCommandBufferTime, armFirstPointWaitTime, armMinWaitTime, armMaxWaitTime);

    public List<List<TrajectoryTask>> SnapshotSemiAutomaticPlan(FieldCommissioningSettings settings, bool allTargets)
    {
        var plan = SnapshotFieldPlan(settings, allTargets);
        foreach (var group in plan)
        {
            var coarse = group.Find(t => t.stage == TrajectoryStage.CoarseArm);
            var returning = group.Find(t => t.stage == TrajectoryStage.ReturnArm);
            var endpoint = coarse.armPoints[coarse.armPoints.Count - 1];
            var endJoints = ExtractArmJoints(endpoint);
            if (returning.armStartPose != null)
            {
                var declared = ExtractArmJoints(returning.armStartPose);
                for (int j = 0; j < 6; j++)
                    if (Math.Abs(declared[j] - endJoints[j]) > settings.jointToleranceDeg)
                        throw new InvalidOperationException("返程声明起点与粗定位终点不一致");
            }
            returning.armStartPose = (double[])endpoint.Clone();
            var first = ExtractArmJoints(returning.armPoints[0]);
            var finish = ExtractArmJoints(returning.armPoints[returning.armPoints.Count - 1]);
            var origin = ExtractArmJoints(coarse.armStartPose);
            for (int j = 0; j < 6; j++)
                if (Math.Abs(finish[j] - origin[j]) > settings.jointToleranceDeg)
                    throw new InvalidOperationException($"目标{coarse.targetIndex+1}/返程末点J{j+1}={finish[j]:F6}°，粗定位起点={origin[j]:F6}°，未复位，请重新规划");
            ArmTrajectorySampling.CheckStep(endJoints, first, settings.maxJointStepDeg,
                $"目标{coarse.targetIndex+1}/返程起点", 0);
        }
        return plan;
    }
    public async Task ExecuteSemiAutomaticTaskAsync(TrajectoryTask task, FieldCommissioningSettings settings,
        ArmTrajectoryTiming timing, CancellationToken cancel)
    {
        if (task.type != TaskType.Arm) { await ExecuteFieldTaskAsync(task, settings, cancel); return; }
        var points = new List<double[]>();
        foreach (var point in task.armPoints) points.Add(ExtractArmJoints(point));
        await dobotTCP.ExecuteFieldTrajectoryAsync(points, ExtractArmJoints(task.armStartPose), settings, timing, cancel,
            task.armSampling?.SendIntervals(timing), $"目标{task.targetIndex+1}/{task.stage}");
        task.isDispatched = true;
    }
    public async Task ExecuteFieldTaskAsync(TrajectoryTask task, FieldCommissioningSettings settings, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        Debug.Log($"[实物联调][目标{task.targetIndex+1}] {task.stage}: {task.taskName}");
        if (task.type == TaskType.Arm)
        {
            foreach (var point in task.armPoints)
                await dobotTCP.MoveFieldJointsAsync(ExtractArmJoints(point), settings, cancel);
            task.isDispatched = true;
        }
        else
        {
            var completion = new TaskCompletionSource<bool>();
            StartCoroutine(DriveFieldCoroutine(task.type == TaskType.Chassis ? SendChassisSegmentedRoutine(task, settings) : SendLiftRoutine(task, true), completion, cancel));
            await completion.Task;
            cancel.ThrowIfCancellationRequested();
            if (!task.isDispatched) throw new InvalidOperationException(task.dispatchError ?? "子任务未完成");
        }
    }
    private IEnumerator DriveFieldCoroutine(IEnumerator root, TaskCompletionSource<bool> done, CancellationToken cancel)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(root);
        try
        {
            while (stack.Count > 0)
            {
                object current = null; bool moved = false; Exception failure = null;
                try { cancel.ThrowIfCancellationRequested(); moved = stack.Peek().MoveNext(); if (moved) current = stack.Peek().Current; }
                catch (Exception e) { failure = e; }
                if (failure != null) { done.TrySetException(failure); yield break; }
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (current is IEnumerator nested) stack.Push(nested);
                else yield return current;
            }
            done.TrySetResult(true);
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            if (!done.Task.IsCompleted) done.TrySetCanceled();
        }
    }
}
