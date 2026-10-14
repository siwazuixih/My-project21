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
            if (returning == null)
            {
                returning = new TrajectoryTask { taskName = $"目标{targets[t]+1}: 原粗定位路径反向复位", type = TaskType.Arm,
                    stage = TrajectoryStage.ReturnArm, targetIndex = targets[t] };
                for (int i = coarse.armPoints.Count - 1; i >= 0; i--) returning.armPoints.Add((double[])coarse.armPoints[i].Clone());
                returning.armPoints.Add((double[])coarse.armStartPose.Clone());
                group.Add(returning);
            }
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
                    if (task.armPoints.Count == 0) throw new InvalidOperationException("机械臂路径为空");
                    if (task.armStartPose != null && (armJointStartIndex < 0 || task.armStartPose.Length < armJointStartIndex + 6))
                        throw new InvalidOperationException("机械臂路径起点维度错误");
                    double[] previous = task.armStartPose == null ? null : ExtractArmJoints(task.armStartPose);
                    if (previous != null) settings.CheckJoints(previous);
                    foreach (var point in task.armPoints)
                    {
                        if (point == null || armJointStartIndex < 0 || point.Length < armJointStartIndex + 6)
                            throw new InvalidOperationException("机械臂路径维度错误");
                        var joint = ExtractArmJoints(point); settings.CheckJoints(joint);
                        if (previous != null) for (int j = 0; j < 6; j++)
                            if (Math.Abs(joint[j] - previous[j]) > settings.maxJointStepDeg)
                                throw new InvalidOperationException("规划路径相邻关节差超限，请重新规划/细分后再执行");
                        previous = joint;
                    }
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
            StartCoroutine(DriveFieldCoroutine(task.type == TaskType.Chassis ? SendChassisSegmentedRoutine(task) : SendLiftRoutine(task, true), completion, cancel));
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
