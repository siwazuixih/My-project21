using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// 编排与设备实现分离，允许无硬件验证失败/取消绝不推进下一阶段。
public static class FieldWorkflowSequence
{
    public static async Task ExecuteAsync(List<List<TrajectoryTask>> targets,
        Func<TrajectoryTask, Task> execute, Func<Task> vision, Func<Task> contact,
        Action<int> completed, CancellationToken cancel)
    {
        foreach (var group in targets)
        {
            foreach (var task in group)
                if (task.stage != TrajectoryStage.ReturnArm)
                { cancel.ThrowIfCancellationRequested(); await execute(task); }
            cancel.ThrowIfCancellationRequested(); await vision();
            cancel.ThrowIfCancellationRequested(); await contact();
            foreach (var task in group)
                if (task.stage == TrajectoryStage.ReturnArm)
                { cancel.ThrowIfCancellationRequested(); await execute(task); }
            cancel.ThrowIfCancellationRequested(); completed(group[0].targetIndex);
        }
    }
}
