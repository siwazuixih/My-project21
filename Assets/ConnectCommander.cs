using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum TaskType { Chassis, Arm, Lift }

[System.Serializable]
public class TrajectoryTask 
{
    public string taskName;
    public TaskType type;
    public List<double[]> armPoints = new List<double[]>();
    public double[] armStartPose;
    public List<Vector3> chassisPoints = new List<Vector3>();
    
    // 💡 新增：记录这个导航任务开始时，车头的初始朝向
    public Vector3 initialForward; 
    
    public bool isDispatched = false; 

    public float targetLiftHeight;
}

public class ConnectCommander : MonoBehaviour
{
    [Header("真机通讯组件绑定")]
    public DobotController dobotTCP;
    public RobokitController chassisTCP; 
    public LiftCylinderController liftTCP; // 🌟 新增：绑定你的升降缸驱动

    [Header("升降缸真机控制设置")]
    [Tooltip("下发给电缸的运行速度 (根据你的表格，真机接收的是 Float)")]
    public float liftVelocity = 30.0f;

    [Header("底盘分段控制设置")]
    [Tooltip("底盘直线平动时的线速度 (m/s)")]
    public float chassisMoveSpeedX = 0.3f;
    [Tooltip("底盘原地旋转时的角速度 (rad/s)")]
    public float chassisTurnSpeedW = 0.5f;
    [Tooltip("指令间缓冲时间 (秒)，确保上一个动作做完")]
    public float commandBufferTime = 0.5f;
    [Tooltip("底盘旋转动作完成后的额外等待时间 (秒)")]
    public float rotTimeBuffer = 10.0f;
    [Tooltip("底盘平移动作完成后的额外等待时间 (秒)")]
    public float moveTimeBuffer = 5.0f;
    [Tooltip("底盘单段动作最小等待时间 (秒)")]
    public float chassisMinWaitTime = 0.2f;
    [Tooltip("底盘单段动作最大等待时间 (秒)，小于等于 0 表示不限制")]
    public float chassisMaxWaitTime = 0f;

    [Header("真实机械臂插值设置")]
    public int safeInterpolationSteps = 10;
    [Tooltip("机械臂默认关节运行速度 (度/秒)，用于估算等待时间")]
    public float armJointSpeedDeg = 3.0f;
    [Tooltip("IK求解器执行器列表中，机械臂J1所在的起始索引")]
    public int armJointStartIndex = 0;
    [Tooltip("机械臂每段关节指令后的额外缓冲时间 (秒)")]
    public float armCommandBufferTime = 0.5f;
    [Tooltip("机械臂第一个点的默认等待时间 (秒)")]
    public float armFirstPointWaitTime = 20.0f;
    [Tooltip("机械臂单段动作最小等待时间 (秒)")]
    public float armMinWaitTime = 0.2f;
    [Tooltip("机械臂单段动作最大等待时间 (秒)，小于等于 0 表示不限制")]
    public float armMaxWaitTime = 0f;

    [Header("升降缸等待设置")]
    [Tooltip("升降缸启动后首次读位置前的等待时间 (秒)")]
    public float liftStartupWaitTime = 0.5f;
    [Tooltip("升降缸到位容差，按 LiftCylinderController 反馈单位填写")]
    public float liftPositionTolerance = 2.0f;
    [Tooltip("升降缸位置轮询间隔 (秒)")]
    public float liftPollInterval = 0.2f;
    [Tooltip("升降缸最大闭环等待时间 (秒)")]
    public float liftTimeout = 10.0f;
    [Tooltip("升降缸到位后的额外缓冲时间 (秒)")]
    public float liftCompletionBufferTime = 0.5f;
    [Header("📂 动作任务库 (预计算缓存)")]
    public List<TrajectoryTask> taskPlaylist = new List<TrajectoryTask>();

    [HideInInspector] public bool isSequenceRunning = false; // 流水线状态锁

    public void ClearAllTasks() => taskPlaylist.Clear();

    public void SetChassisMoveSpeed(string value) =>
        SetPositiveFloat(value, ref chassisMoveSpeedX, "底盘平移速度 chassisMoveSpeedX");

    public void SetChassisTurnSpeed(string value) =>
        SetPositiveFloat(value, ref chassisTurnSpeedW, "底盘旋转速度 chassisTurnSpeedW");

    public void SetCommandBufferTime(string value) =>
        SetNonNegativeFloat(value, ref commandBufferTime, "通用指令缓冲 commandBufferTime");

    public void SetChassisRotTimeBuffer(string value) =>
        SetNonNegativeFloat(value, ref rotTimeBuffer, "底盘旋转额外等待 rotTimeBuffer");

    public void SetChassisMoveTimeBuffer(string value) =>
        SetNonNegativeFloat(value, ref moveTimeBuffer, "底盘平移额外等待 moveTimeBuffer");

    public void SetChassisMinWaitTime(string value) =>
        SetNonNegativeFloat(value, ref chassisMinWaitTime, "底盘最小等待 chassisMinWaitTime");

    public void SetChassisMaxWaitTime(string value) =>
        SetNonNegativeFloat(value, ref chassisMaxWaitTime, "底盘最大等待 chassisMaxWaitTime");

    public void SetArmJointSpeedDeg(string value) =>
        SetPositiveFloat(value, ref armJointSpeedDeg, "机械臂估算关节速度 armJointSpeedDeg");

    public void SetArmCommandBufferTime(string value) =>
        SetNonNegativeFloat(value, ref armCommandBufferTime, "机械臂指令缓冲 armCommandBufferTime");

    public void SetArmFirstPointWaitTime(string value) =>
        SetNonNegativeFloat(value, ref armFirstPointWaitTime, "机械臂首点兜底等待 armFirstPointWaitTime");

    public void SetArmMinWaitTime(string value) =>
        SetNonNegativeFloat(value, ref armMinWaitTime, "机械臂最小等待 armMinWaitTime");

    public void SetArmMaxWaitTime(string value) =>
        SetNonNegativeFloat(value, ref armMaxWaitTime, "机械臂最大等待 armMaxWaitTime");

    public void SetSafeInterpolationSteps(string value)
    {
        if (!int.TryParse(value, out int parsed))
        {
            Debug.LogWarning($"机械臂插值步数 safeInterpolationSteps 输入无效: {value}");
            return;
        }

        safeInterpolationSteps = Mathf.Max(1, parsed);
        Debug.Log($"机械臂插值步数 safeInterpolationSteps 已设置为: {safeInterpolationSteps}");
    }

    // 💡 修改：接收第三个参数，即底盘开始这段路径时的初始车头朝向
    public void AddChassisTask(string name, Vector3[] corners, Vector3 startForward)
    {
        TrajectoryTask newTask = new TrajectoryTask {
            taskName = name, 
            type = TaskType.Chassis,
            chassisPoints = new List<Vector3>(corners),
            initialForward = startForward // 存入初始朝向
        };
        taskPlaylist.Add(newTask);
    }

    public void AddArmTask(string name, List<double[]> path, double[] startPose, AnimationCurve curve = null)
    {
        if (path == null || path.Count == 0) return;
        TrajectoryTask newTask = new TrajectoryTask { taskName = name, type = TaskType.Arm };
        if (startPose != null) {
            newTask.armStartPose = new double[startPose.Length];
            System.Array.Copy(startPose, newTask.armStartPose, startPose.Length);
        }

        if (path.Count == 1) 
        {
            double[] end = path[0]; int dof = end.Length;
            double[] start = (startPose != null && startPose.Length == dof) ? startPose : new double[dof];
            AnimationCurve cv = curve ?? AnimationCurve.EaseInOut(0, 0, 1, 1);
            
            for (int i = 1; i <= safeInterpolationSteps; i++) {
                float t = (float)i / safeInterpolationSteps; float c = cv.Evaluate(t);
                double[] pt = new double[dof];
                for (int j = 0; j < dof; j++) pt[j] = Mathf.Lerp((float)start[j], (float)end[j], c);
                newTask.armPoints.Add(pt);
            }
        } 
        else 
        {
            foreach (var q in path) {
                double[] pt = new double[q.Length]; System.Array.Copy(q, pt, q.Length);
                newTask.armPoints.Add(pt);
            }
        }
        taskPlaylist.Add(newTask);
    }

    public void AddLiftTask(string name, float targetHeight)
{
    TrajectoryTask newTask = new TrajectoryTask {
        taskName = name,
        type = TaskType.Lift,
        targetLiftHeight = targetHeight // 别忘了在 TrajectoryTask 类里加这个变量，见下方提示
    };
    taskPlaylist.Add(newTask);
}

    public void DispatchTask(int index)
    {
        if (index < 0 || index >= taskPlaylist.Count) return;
        var task = taskPlaylist[index];

        if (task.type == TaskType.Chassis) 
        {
            if (chassisTCP == null) { Debug.LogError("⚠️ 未绑定 ChassisTCP！"); return; }
            StartCoroutine(SendChassisSegmentedRoutine(task));
        } 
        else if (task.type == TaskType.Arm) 
        {
            if (dobotTCP == null) { Debug.LogError("⚠️ 未绑定 DobotTCP！"); return; }
            StartCoroutine(SendArmRoutine(task));
        }
        else if (task.type == TaskType.Lift) // 🌟 新增分支
        {
        if (liftTCP == null) { Debug.LogError("⚠️ 未绑定 LiftTCP！"); return; }
        StartCoroutine(SendLiftRoutine(task));
        }
    }

    // =================================================================================
    // 🌟 新增：一键自动化组调度系统 (底盘 -> 升降缸 -> 机械臂串联闭环)
    // =================================================================================

    /// <summary>
    /// 外部 UI 按钮调用的主入口
    /// </summary>
    public void DispatchNextTargetSequence()
    {
        if (isSequenceRunning)
        {
            Debug.LogWarning("⚠️ 上一个目标点的完整自动化流程正在执行中，请勿重复触发！");
            return;
        }
        StartCoroutine(ExecuteNextTargetSequenceRoutine());
    }

    private IEnumerator ExecuteNextTargetSequenceRoutine()
    {
        // 1. 扫描队列，寻找第一个还没下发的任务
        int firstUndispatchedIdx = -1;
        for (int i = 0; i < taskPlaylist.Count; i++)
        {
            if (!taskPlaylist[i].isDispatched)
            {
                firstUndispatchedIdx = i;
                break;
            }
        }

        if (firstUndispatchedIdx == -1)
        {
            Debug.Log("🎉 <color=green>【全套动作完结】所有目标点的真机任务均已成功下发完毕！</color>");
            yield break;
        }

        // 激活状态锁
        isSequenceRunning = true;

        // 2. 智能提取目标前缀（例如从 \"目标 1: 导航\" 中提取出 \"目标 1\"）
        string firstName = taskPlaylist[firstUndispatchedIdx].taskName;
        string targetPrefix = "";
        int colonIdx = firstName.IndexOf(':');
        if (colonIdx != -1)
        {
            targetPrefix = firstName.Substring(0, colonIdx).Trim(); // 拿到 \"目标 1\"
        }
        else
        {
            targetPrefix = firstName; // 兜底
        }

        Debug.Log($"🚀 <color=gold>【一键流水线】检测到未执行动作，开始串行执行【{targetPrefix}】的完整硬件同步...</color>");

        // 3. 收集该目标点下所有属于该组且未下发的任务
        List<TrajectoryTask> currentGroupTasks = new List<TrajectoryTask>();
        for (int i = firstUndispatchedIdx; i < taskPlaylist.Count; i++)
        {
            if (taskPlaylist[i].taskName.StartsWith(targetPrefix) && !taskPlaylist[i].isDispatched)
            {
                currentGroupTasks.Add(taskPlaylist[i]);
            }
        }

        // 4. 串行闭环执行：底盘 -> 升降缸 -> 机械臂
        foreach (var task in currentGroupTasks)
        {
            Debug.Log($"▶️ <color=lime>[自动流水线] 正在启动子任务:</color> {task.taskName}");

            IEnumerator subTaskRoutine = null;
            
            // 根据任务类型，将对应的底层通讯硬件协程抽离出来
            if (task.type == TaskType.Chassis) subTaskRoutine = SendChassisSegmentedRoutine(task);
            else if (task.type == TaskType.Lift) subTaskRoutine = SendLiftRoutine(task);
            else if (task.type == TaskType.Arm) subTaskRoutine = SendArmRoutine(task);

            if (subTaskRoutine != null)
            {
                // 🔥 核心魔法：使用 yield return 等待底层真机动作协程完全执行结束
                yield return StartCoroutine(subTaskRoutine);
            }

            // 安全防御验证：确保底层状态刷新
            while (!task.isDispatched)
            {
                yield return null;
            }

            Debug.Log($"⏸️ <color=white>[自动流水线] 子任务 {task.taskName} 已闭环确认完成。</color>");
            yield return new WaitForSeconds(0.8f); // 动作切换间的硬件物理缓冲时间
        }

        // 解除状态锁
        isSequenceRunning = false;
        Debug.Log($"✨ <color=green>【一键流水线】【{targetPrefix}】的底盘、升降缸、机械臂组合动作全部圆满安全完成！</color>");
    }





    /// <summary>
    /// 🌟 完美分段走停追踪 (包含起点对齐修正)
    /// </summary>
    private IEnumerator SendChassisSegmentedRoutine(TrajectoryTask task)
    {
        Debug.Log($"🚧 [真机调度] 开始分段执行底盘路径，共 {task.chassisPoints.Count} 个节点...");

        if (task.chassisPoints == null || task.chassisPoints.Count < 2) yield break;

        // 🌟 核心游标：记录车头【此刻】的朝向，初始值为传进来的 startForward
        Vector3 currentFacing = task.initialForward.normalized;

        for (int i = 0; i < task.chassisPoints.Count - 1; i++)
        {
            Vector3 currentPoint = task.chassisPoints[i];
            Vector3 nextPoint = task.chassisPoints[i + 1];

            // 1. 计算目标线段的朝向
            Vector3 dir = (nextPoint - currentPoint).normalized;

            // 2. 计算【当前车头】与【目标朝向】的夹角
            float currentAngleDeg = Mathf.Atan2(currentFacing.x, currentFacing.z) * Mathf.Rad2Deg;
            float targetAngleDeg = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float deltaAngleDeg = Mathf.DeltaAngle(currentAngleDeg, targetAngleDeg);


            // 3. 旋转修正 (误差 > 1度时执行)
            if (Mathf.Abs(deltaAngleDeg) > 1.0f)
            {
                // [修改 1]：根据 API 3056 要求，angle 必须是绝对值 (单位: 弧度)
                float absAngleRad = Mathf.Abs(deltaAngleDeg) * Mathf.Deg2Rad; 
                
                // [修改 2]：旋转方向由 vw 的正负号决定。正为逆时针，负为顺时针。
                // ⚠️ 调试提示：如果实车旋转方向与预期相反，把这里的逻辑改成 (deltaAngleDeg < 0) 即可
                float signedVw = deltaAngleDeg < 0 ? chassisTurnSpeedW : -chassisTurnSpeedW;

                // [修改 3]：构建 3056 专属的 JSON，使用 vw 字段
                // string rotJson = $"{{\"angle\":{absAngleRad:F3}, \"vw\":{signedVw:F2}}}";
                string rotJson = $"{{\"angle\":{absAngleRad.ToString("F3")}, \"vw\":{signedVw.ToString("F2")}}}";
                Debug.Log($"🔄 [第{i}段] 原地旋转: 差值 {deltaAngleDeg:F1}° -> <color=yellow>{rotJson}</color>");
                
                // [修改 4]：发送专属转动 API 3056
                chassisTCP.SendCommand(3056, rotJson);

                // 估算旋转所需时间并等待 (时间 = 绝对角度/角速度 + 缓冲)
                float rotTime = ClampEstimatedWait(absAngleRad / chassisTurnSpeedW + commandBufferTime + rotTimeBuffer, chassisMinWaitTime, chassisMaxWaitTime);
                yield return new WaitForSeconds(rotTime);
            }

            // 🌟 核心状态更新：旋转做完后，车头已经指向当前线段方向了，更新游标！
            currentFacing = dir;

            // 4. 直线平动
            float dist = Vector3.Distance(new Vector3(currentPoint.x, 0, currentPoint.z), 
                                          new Vector3(nextPoint.x, 0, nextPoint.z));

            if (dist > 0.01f)
            {
                string moveJson = $"{{\"dist\":{dist:F3}, \"vx\":{-chassisMoveSpeedX:F2}, \"vy\":0.00}}";
                Debug.Log($"⬆️ [第{i}段] 直线平动: <color=cyan>{moveJson}</color>");
                chassisTCP.SendCommand(3055, moveJson);

                float moveTime = ClampEstimatedWait(dist / chassisMoveSpeedX + commandBufferTime + moveTimeBuffer, chassisMinWaitTime, chassisMaxWaitTime);
                yield return new WaitForSeconds(moveTime);
            }
        }

        task.isDispatched = true;
        Debug.Log($"✅ 底盘路径 <color=orange>{task.taskName}</color> 严格沿线执行完毕！");
    }

    private IEnumerator SendArmRoutine(TrajectoryTask task)
    {
        Debug.Log($"📡 [真机调度] 开始执行机械臂动作: <color=cyan>{task.taskName}</color>，共 {task.armPoints.Count} 个节点");
        
        double[] previousJoints = ExtractArmJoints(task.armStartPose);

        foreach (var pt in task.armPoints) 
        {
            double[] armJoints = ExtractArmJoints(pt);

            // 发送给真机
            dobotTCP.MoveJoints(armJoints);

            float waitTime = armFirstPointWaitTime;

            if (previousJoints != null)
            {
                // 找出 6 个关节中，转动角度最大的那一个
                float maxDiffDeg = 0f;
                for (int i = 0; i < 6; i++) {
                    float diff = Mathf.Abs((float)(armJoints[i] - previousJoints[i]));
                    if (diff > maxDiffDeg) maxDiffDeg = diff;
                }

                // 时间 = 最大角度差 / 关节角速度 + 通讯缓冲时间
                waitTime = (maxDiffDeg / Mathf.Max(armJointSpeedDeg, 0.001f)) + armCommandBufferTime;
            }

            waitTime = ClampEstimatedWait(waitTime, armMinWaitTime, armMaxWaitTime);
            Debug.Log($"⏳ [机械臂等待] 预计本段耗时: {waitTime:F2} 秒...");
            yield return new WaitForSeconds(waitTime);

            // 更新历史记录
            previousJoints = armJoints;
        }
        
        task.isDispatched = true;
        Debug.Log($"✅ 机械臂动作 <color=cyan>{task.taskName}</color> 执行完毕！");
    }

    private IEnumerator SendLiftRoutine(TrajectoryTask task)
    {
        Debug.Log($"🛗 [真机调度] 开始执行升降动作: <color=cyan>{task.taskName}</color> -> 目标高度: {task.targetLiftHeight}m");
        
        // 1. 发送运动指令
        liftTCP.MoveToPosition(task.targetLiftHeight, liftVelocity);

        // 2. 闭环等待：不断读取当前位置，直到满足容差
        float targetRealPos = task.targetLiftHeight * liftTCP.simToRealScale;
        float tolerance = liftPositionTolerance;
        float elapsed = 0f;

        yield return new WaitForSeconds(liftStartupWaitTime);

        while (elapsed < liftTimeout)
        {
            float currentPos = liftTCP.ReadCurrentPosition();
            if (Mathf.Abs(currentPos - targetRealPos) <= tolerance)
            {
                Debug.Log("🎯 [升降缸] 已精准到达目标位置！");
                break; 
            }
            elapsed += liftPollInterval;
            yield return new WaitForSeconds(liftPollInterval);
        }

        if (elapsed >= liftTimeout)
        {
            Debug.LogError("🚨 [升降缸] 运动超时！请确认电缸是否断电或被卡死！");
        }

        yield return new WaitForSeconds(liftCompletionBufferTime);
        task.isDispatched = true;
    }

    private float ClampEstimatedWait(float waitTime, float minWait, float maxWait)
    {
        waitTime = Mathf.Max(waitTime, minWait);
        if (maxWait > 0f) waitTime = Mathf.Min(waitTime, maxWait);
        return waitTime;
    }

    private double[] ExtractArmJoints(double[] sourcePose)
    {
        if (sourcePose == null) return null;

        double[] armJoints = new double[6];
        for (int i = 0; i < 6; i++)
        {
            int ikIndex = armJointStartIndex + i;
            armJoints[i] = ikIndex < sourcePose.Length ? sourcePose[ikIndex] * Mathf.Rad2Deg : 0;
        }
        return armJoints;
    }

    private void SetPositiveFloat(string value, ref float target, string label)
    {
        if (!float.TryParse(value, out float parsed) || parsed <= 0f)
        {
            Debug.LogWarning($"{label} 输入无效，必须是大于 0 的数字: {value}");
            return;
        }

        target = parsed;
        Debug.Log($"{label} 已设置为: {target}");
    }

    private void SetNonNegativeFloat(string value, ref float target, string label)
    {
        if (!float.TryParse(value, out float parsed) || parsed < 0f)
        {
            Debug.LogWarning($"{label} 输入无效，必须是大于等于 0 的数字: {value}");
            return;
        }

        target = parsed;
        Debug.Log($"{label} 已设置为: {target}");
    }

}
