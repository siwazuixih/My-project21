using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Mujoco;

/// <summary>
/// 单个模拟视觉精调点。前六项是机械臂关节角偏移（度），第七项是升降轴偏移（米）。
/// 偏移量都以粗定位结束时的七执行器状态为基准，而不是逐点累加。
/// </summary>
[System.Serializable]
public sealed class MockVisionFineTunePoint
{
    public float[] actuatorOffsets = new float[7];
    public float durationSeconds = 0.6f;

    public MockVisionFineTunePoint()
    {
    }

    public MockVisionFineTunePoint(float[] offsets, float duration)
    {
        actuatorOffsets = offsets;
        durationSeconds = duration;
    }
}

/// <summary>
/// 视觉模块输出经过适配后的统一轨迹格式。
/// 内部单位与 MuJoCo 控制量一致：转动关节为弧度，移动关节为米。
/// </summary>
public sealed class FineTuneTrajectoryPlan
{
    public readonly List<double[]> Points = new List<double[]>();
    public readonly List<float> Durations = new List<float>();
    public string Source = "Unknown";
}

/// <summary>
/// 视觉精调轨迹适配接口。未来视觉模块无论返回笛卡尔轨迹还是七执行器数组，
/// 都先转换成 FineTuneTrajectoryPlan，编排层无需随视觉协议一起修改。
/// </summary>
public interface IVisionFineTuneTrajectoryProvider
{
    bool TryBuildTrajectory(
        IReadOnlyList<double> currentActuatorControls,
        int targetIndex,
        out FineTuneTrajectoryPlan plan,
        out string error);
}

/// <summary>
/// 当前离线验证使用的模拟视觉提供器。
/// </summary>
public sealed class MockVisionFineTuneTrajectoryProvider :
    IVisionFineTuneTrajectoryProvider
{
    private readonly IReadOnlyList<MockVisionFineTunePoint> configuredPoints;

    public MockVisionFineTuneTrajectoryProvider(
        IReadOnlyList<MockVisionFineTunePoint> points)
    {
        configuredPoints = points;
    }

    public bool TryBuildTrajectory(
        IReadOnlyList<double> currentActuatorControls,
        int targetIndex,
        out FineTuneTrajectoryPlan plan,
        out string error)
    {
        plan = null;
        error = "";

        if (currentActuatorControls == null || currentActuatorControls.Count != 7)
        {
            error = "模拟视觉精调要求当前状态恰好包含 7 个执行器控制量";
            return false;
        }

        if (configuredPoints == null || configuredPoints.Count == 0)
        {
            error = "没有配置模拟视觉精调轨迹点";
            return false;
        }

        FineTuneTrajectoryPlan result = new FineTuneTrajectoryPlan
        {
            Source = $"MockVision/Target-{targetIndex + 1}"
        };

        for (int pointIndex = 0; pointIndex < configuredPoints.Count; pointIndex++)
        {
            MockVisionFineTunePoint configured = configuredPoints[pointIndex];
            if (configured == null || configured.actuatorOffsets == null ||
                configured.actuatorOffsets.Length != 7)
            {
                error = $"模拟视觉精调点 {pointIndex + 1} 必须包含 7 个执行器偏移量";
                return false;
            }

            double[] absolutePoint = new double[7];
            for (int actuatorIndex = 0; actuatorIndex < 7; actuatorIndex++)
            {
                double offset = configured.actuatorOffsets[actuatorIndex];
                if (double.IsNaN(offset) || double.IsInfinity(offset))
                {
                    error = $"模拟视觉精调点 {pointIndex + 1} 含有无效数值";
                    return false;
                }

                if (actuatorIndex < 6)
                {
                    offset *= System.Math.PI / 180.0;
                }

                absolutePoint[actuatorIndex] =
                    currentActuatorControls[actuatorIndex] + offset;
            }

            result.Points.Add(absolutePoint);
            result.Durations.Add(
                System.Math.Max(configured.durationSeconds, 0.05f));
        }

        plan = result;
        return true;
    }
}

public enum TaskType { Chassis, Arm, Lift }
public enum TrajectoryStage { Unspecified, Navigation, Lift, CoarseArm, ReturnArm }

[System.Serializable]
public class TrajectoryTask 
{
    public string taskName;
    public TaskType type;
    public int targetIndex = -1;
    public TrajectoryStage stage = TrajectoryStage.Unspecified;
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

    [Header("全流程离线验证")]
    [Tooltip("离线回放底盘路径的速度（米/秒），不会下发真机。")]
    public float offlineChassisSpeed = 1.0f;
    [Tooltip("离线回放升降轴的最小动画时间（秒）。")]
    public float offlineLiftDuration = 0.8f;
    [Tooltip("离线回放粗定位/复位轨迹时，每个路径点的时间（秒）。")]
    public float offlineArmPointDuration = 0.25f;
    [Tooltip("离线模拟拧紧过程的耗时（秒）。")]
    public float offlineTighteningDuration = 2.0f;
    [Tooltip("模拟视觉精调轨迹：前六项是关节角偏移（度），第七项是升降轴偏移（米）。")]
    public List<MockVisionFineTunePoint> mockVisionTrajectory =
        new List<MockVisionFineTunePoint>();

    [HideInInspector] public bool isSequenceRunning = false; // 流水线状态锁

    private Coroutine fullProcessCoroutine;
    private bool fullProcessCancelRequested;
    private IVisionFineTuneTrajectoryProvider fineTuneTrajectoryProvider;
    private ServoTighteningController validationServoController;
    private bool validationStartedServoProgram;

    public void ClearAllTasks() => taskPlaylist.Clear();

    private void OnDestroy()
    {
        fullProcessCancelRequested = true;
        if (validationStartedServoProgram &&
            validationServoController != null &&
            validationServoController.ProgramReady)
        {
            // ServoTighteningController 跨场景存活；离开当前页面时也要关闭
            // 由本次离线验证启动的本机进程。
            _ = validationServoController.StopProgramAsync();
        }
    }

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
    public void AddChassisTask(
        string name,
        Vector3[] corners,
        Vector3 startForward,
        int targetIndex = -1,
        TrajectoryStage stage = TrajectoryStage.Navigation)
    {
        TrajectoryTask newTask = new TrajectoryTask {
            taskName = name, 
            type = TaskType.Chassis,
            targetIndex = targetIndex,
            stage = stage,
            chassisPoints = new List<Vector3>(corners),
            initialForward = startForward // 存入初始朝向
        };
        taskPlaylist.Add(newTask);
    }

    public void AddArmTask(
        string name,
        List<double[]> path,
        double[] startPose,
        AnimationCurve curve = null,
        int targetIndex = -1,
        TrajectoryStage stage = TrajectoryStage.CoarseArm)
    {
        if (path == null || path.Count == 0) return;
        TrajectoryTask newTask = new TrajectoryTask {
            taskName = name,
            type = TaskType.Arm,
            targetIndex = targetIndex,
            stage = stage
        };
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

    public void AddLiftTask(
        string name,
        float targetHeight,
        int targetIndex = -1,
        TrajectoryStage stage = TrajectoryStage.Lift)
{
    TrajectoryTask newTask = new TrajectoryTask {
        taskName = name,
        type = TaskType.Lift,
        targetIndex = targetIndex,
        stage = stage,
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
        TrajectoryTask firstTask = taskPlaylist[firstUndispatchedIdx];
        string firstName = firstTask.taskName;
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
            bool sameTarget = firstTask.targetIndex >= 0
                ? taskPlaylist[i].targetIndex == firstTask.targetIndex
                : ExtractTargetPrefix(taskPlaylist[i].taskName) == targetPrefix;
            if (sameTarget && !taskPlaylist[i].isDispatched)
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

    // =================================================================================
    // 全流程离线验证：粗定位 -> 视觉精调 -> 模拟拧紧 -> 复位 -> 下一目标
    // =================================================================================

    public void SetFineTuneTrajectoryProvider(
        IVisionFineTuneTrajectoryProvider provider)
    {
        fineTuneTrajectoryProvider = provider;
    }

    public void DispatchFullProcessValidation(MissionController missionController)
    {
        if (isSequenceRunning)
        {
            Debug.LogWarning("[全流程验证] 已有流程正在执行，请勿重复启动。");
            return;
        }

        if (missionController == null || missionController.refs == null ||
            missionController.refs.ikSolver == null ||
            missionController.refs.ikSolver.actuators == null)
        {
            Debug.LogError("[全流程验证] 缺少 MissionController 或 IK 执行器绑定。");
            return;
        }

        if (missionController.refs.ikSolver.actuators.Count != 7)
        {
            Debug.LogError(
                $"[全流程验证] 当前执行器数量为 " +
                $"{missionController.refs.ikSolver.actuators.Count}，模拟视觉轨迹要求恰好 7 个。");
            return;
        }

        if (taskPlaylist == null || taskPlaylist.Count == 0)
        {
            Debug.LogError("[全流程验证] 任务清单为空，请先点击“开始仿真”完成路径预计算。");
            return;
        }

        EnsureMockVisionTrajectory();
        if (fineTuneTrajectoryProvider == null)
        {
            fineTuneTrajectoryProvider =
                new MockVisionFineTuneTrajectoryProvider(mockVisionTrajectory);
        }

        foreach (TrajectoryTask task in taskPlaylist)
        {
            task.isDispatched = false;
        }

        fullProcessCancelRequested = false;
        isSequenceRunning = true;
        fullProcessCoroutine = StartCoroutine(
            ExecuteFullProcessValidationRoutine(missionController));
    }

    public void CancelFullProcessValidation()
    {
        if (fullProcessCoroutine == null)
        {
            return;
        }

        fullProcessCancelRequested = true;
        Debug.LogWarning("[全流程验证] 已请求取消，将在当前安全检查点退出。");
    }

    private IEnumerator ExecuteFullProcessValidationRoutine(
        MissionController missionController)
    {
        Debug.Log(
            "<color=cyan>[全流程验证] 离线模式启动。" +
            "不会向机械臂、底盘、升降缸或电批下发真实运动指令。</color>");

        string failureReason = "";
        List<int> targetOrder = GetTargetOrder();
        if (targetOrder.Count == 0)
        {
            failureReason = "任务清单没有目标编号，请重新进行路径预计算";
        }

        for (int orderIndex = 0;
             orderIndex < targetOrder.Count &&
             string.IsNullOrEmpty(failureReason) &&
             !fullProcessCancelRequested;
             orderIndex++)
        {
            int targetIndex = targetOrder[orderIndex];
            string targetLabel = $"目标 {targetIndex + 1}";
            List<TrajectoryTask> group = taskPlaylist.FindAll(
                task => task.targetIndex == targetIndex);

            Debug.Log($"<color=gold>[全流程验证][{targetLabel}] 开始。</color>");

            foreach (TrajectoryTask task in group)
            {
                if (task.stage == TrajectoryStage.ReturnArm)
                {
                    continue;
                }

                yield return StartCoroutine(
                    ExecuteOfflineTaskRoutine(
                        missionController,
                        task,
                        targetLabel));

                if (fullProcessCancelRequested)
                {
                    break;
                }
            }

            if (fullProcessCancelRequested)
            {
                break;
            }

            List<double> currentControls = ReadCurrentActuatorControls(
                missionController);
            if (!fineTuneTrajectoryProvider.TryBuildTrajectory(
                    currentControls,
                    targetIndex,
                    out FineTuneTrajectoryPlan fineTunePlan,
                    out string providerError))
            {
                failureReason = providerError;
                break;
            }

            if (!ValidateFineTunePlan(
                    fineTunePlan,
                    currentControls.Count,
                    out string validationError))
            {
                failureReason = validationError;
                break;
            }

            Debug.Log(
                $"[全流程验证][{targetLabel}][视觉精调] " +
                $"收到 {fineTunePlan.Points.Count} 个七执行器轨迹点，" +
                $"Source={fineTunePlan.Source}。");

            yield return StartCoroutine(
                ExecuteActuatorPlanRoutine(
                    missionController,
                    fineTunePlan.Points,
                    fineTunePlan.Durations,
                    targetLabel,
                    "视觉精调"));

            if (fullProcessCancelRequested)
            {
                break;
            }

            if (validationServoController == null)
            {
                validationServoController =
                    FindObjectOfType<ServoTighteningController>();
            }

            if (validationServoController == null)
            {
                failureReason = "未找到 ServoTighteningController，无法启动本机拧紧程序";
                break;
            }

            if (!validationServoController.ProgramReady)
            {
                Debug.Log(
                    $"[全流程验证][{targetLabel}][拧紧] " +
                    "正在启动本机 V26 拧紧程序…");
                System.Threading.Tasks.Task<ServoTighteningController.CommandResult>
                    startTask = validationServoController.StartProgramAsync();
                while (!startTask.IsCompleted && !fullProcessCancelRequested)
                {
                    yield return null;
                }

                if (fullProcessCancelRequested)
                {
                    break;
                }

                if (startTask.IsFaulted || startTask.IsCanceled ||
                    !startTask.Result.Success)
                {
                    failureReason = startTask.IsFaulted
                        ? startTask.Exception?.GetBaseException().Message
                        : startTask.IsCanceled
                            ? "启动拧紧程序的任务已取消"
                            : startTask.Result.Error;
                    break;
                }

                validationStartedServoProgram = true;
            }

            using (var cancel = new System.Threading.CancellationTokenSource())
            {
                var cycle = validationServoController.RunContactCycleAsync(true, cancel.Token);
                while (!cycle.IsCompleted)
                {
                    if (fullProcessCancelRequested) cancel.Cancel();
                    yield return null;
                }
                if (fullProcessCancelRequested) break;
                if (cycle.IsFaulted || cycle.IsCanceled || !cycle.Result.Success)
                {
                    failureReason = cycle.IsFaulted ? cycle.Exception.GetBaseException().Message
                        : cycle.IsCanceled ? "接触装配已取消" : cycle.Result.Error;
                    break;
                }
                Debug.Log($"[全流程验证][{targetLabel}] 卡入—拧紧—退出 Result=SIMULATED_OK");
            }

            TrajectoryTask returnTask = group.Find(
                task => task.stage == TrajectoryStage.ReturnArm);
            if (returnTask == null)
            {
                returnTask = BuildOfflineResetTask(
                    missionController,
                    targetIndex);
            }

            yield return StartCoroutine(
                ExecuteOfflineTaskRoutine(
                    missionController,
                    returnTask,
                    targetLabel));

            if (!fullProcessCancelRequested)
            {
                Debug.Log(
                    $"<color=green>[全流程验证][{targetLabel}] " +
                    "精调、拧紧和复位完成，继续下一目标。</color>");
            }
        }

        if (validationStartedServoProgram && validationServoController != null)
        {
            Debug.Log("[全流程验证] 正在关闭本次验证启动的 V26 程序…");
            System.Threading.Tasks.Task<ServoTighteningController.CommandResult>
                stopTask = validationServoController.StopProgramAsync();
            while (!stopTask.IsCompleted)
            {
                yield return null;
            }
            validationStartedServoProgram = false;
        }

        if (fullProcessCancelRequested)
        {
            Debug.LogWarning("[全流程验证] 流程已取消。");
        }
        else if (!string.IsNullOrEmpty(failureReason))
        {
            Debug.LogError($"[全流程验证] 流程中止：{failureReason}");
        }
        else
        {
            Debug.Log(
                "<color=green>[全流程验证] " +
                "所有目标的离线全流程验证已完成。</color>");
        }

        // 离线回放不代表真机任务已下发，保留真机队列的未执行状态。
        foreach (TrajectoryTask task in taskPlaylist)
        {
            task.isDispatched = false;
        }

        fullProcessCancelRequested = false;
        fullProcessCoroutine = null;
        isSequenceRunning = false;
    }

    private IEnumerator ExecuteOfflineTaskRoutine(
        MissionController missionController,
        TrajectoryTask task,
        string targetLabel)
    {
        Debug.Log(
            $"[全流程验证][{targetLabel}][{task.stage}] " +
            $"离线回放 {task.taskName}。");

        if (task.type == TaskType.Chassis)
        {
            yield return StartCoroutine(
                ExecuteOfflineChassisRoutine(missionController, task));
        }
        else if (task.type == TaskType.Lift)
        {
            yield return StartCoroutine(
                ExecuteOfflineLiftRoutine(missionController, task));
        }
        else if (task.type == TaskType.Arm)
        {
            List<float> durations = new List<float>();
            for (int i = 0; i < task.armPoints.Count; i++)
            {
                durations.Add(Mathf.Max(offlineArmPointDuration, 0.05f));
            }
            yield return StartCoroutine(
                ExecuteActuatorPlanRoutine(
                    missionController,
                    task.armPoints,
                    durations,
                    targetLabel,
                    task.stage.ToString()));
        }

        if (!fullProcessCancelRequested)
        {
            task.isDispatched = true;
        }
    }

    private IEnumerator ExecuteOfflineChassisRoutine(
        MissionController missionController,
        TrajectoryTask task)
    {
        if (task.chassisPoints == null || task.chassisPoints.Count < 2)
        {
            yield break;
        }

        for (int pointIndex = 0;
             pointIndex < task.chassisPoints.Count - 1 &&
             !fullProcessCancelRequested;
             pointIndex++)
        {
            Vector3 from = task.chassisPoints[pointIndex];
            Vector3 to = task.chassisPoints[pointIndex + 1];
            Vector3 delta = to - from;
            delta.y = 0f;
            if (delta.sqrMagnitude < 0.000001f)
            {
                continue;
            }

            float mappedX = delta.x;
            float mappedZ = delta.z;
            if (missionController.chassis.swapXZ)
            {
                float swap = mappedX;
                mappedX = mappedZ;
                mappedZ = swap;
            }
            if (missionController.chassis.invertX) mappedX = -mappedX;
            if (missionController.chassis.invertZ) mappedZ = -mappedZ;

            double startX = missionController.chassis.actuatorX != null
                ? missionController.chassis.actuatorX.Control
                : 0.0;
            double startZ = missionController.chassis.actuatorZ != null
                ? missionController.chassis.actuatorZ.Control
                : 0.0;
            double startRot = missionController.chassis.actuatorRot != null
                ? missionController.chassis.actuatorRot.Control
                : 0.0;
            float targetAngle = Quaternion.LookRotation(delta).eulerAngles.y;
            double targetRot = -(
                targetAngle + missionController.chassis.headingOffset) *
                Mathf.Deg2Rad;
            float duration = Mathf.Max(
                delta.magnitude / Mathf.Max(offlineChassisSpeed, 0.01f),
                0.1f);

            float elapsed = 0f;
            while (elapsed < duration && !fullProcessCancelRequested)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                if (missionController.chassis.actuatorX != null)
                    missionController.chassis.actuatorX.Control =
                        (float)(startX + mappedX * t);
                if (missionController.chassis.actuatorZ != null)
                    missionController.chassis.actuatorZ.Control =
                        (float)(startZ + mappedZ * t);
                if (missionController.chassis.actuatorRot != null)
                    missionController.chassis.actuatorRot.Control =
                        (float)(startRot + (targetRot - startRot) * t);
                yield return null;
            }
        }
    }

    private IEnumerator ExecuteOfflineLiftRoutine(
        MissionController missionController,
        TrajectoryTask task)
    {
        int liftIndex = missionController.liftActuatorIndex;
        List<MjActuator> actuators =
            missionController.refs.ikSolver.actuators;
        if (liftIndex < 0 || liftIndex >= actuators.Count)
        {
            Debug.LogError(
                $"[全流程验证][升降] 执行器索引 {liftIndex} 无效。");
            fullProcessCancelRequested = true;
            yield break;
        }

        double start = actuators[liftIndex].Control;
        float duration = Mathf.Max(offlineLiftDuration, 0.05f);
        float elapsed = 0f;
        while (elapsed < duration && !fullProcessCancelRequested)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            actuators[liftIndex].Control =
                (float)(start + (task.targetLiftHeight - start) * t);
            yield return null;
        }
    }

    private IEnumerator ExecuteActuatorPlanRoutine(
        MissionController missionController,
        IReadOnlyList<double[]> points,
        IReadOnlyList<float> durations,
        string targetLabel,
        string stageLabel)
    {
        List<MjActuator> actuators =
            missionController.refs.ikSolver.actuators;

        for (int pointIndex = 0;
             pointIndex < points.Count && !fullProcessCancelRequested;
             pointIndex++)
        {
            double[] target = points[pointIndex];
            if (target == null || target.Length != actuators.Count ||
                !AllFinite(target))
            {
                Debug.LogError(
                    $"[全流程验证][{targetLabel}][{stageLabel}] " +
                    $"轨迹点 {pointIndex + 1} 维度或数值无效。");
                fullProcessCancelRequested = true;
                yield break;
            }

            double[] starts = new double[actuators.Count];
            for (int i = 0; i < actuators.Count; i++)
            {
                starts[i] = actuators[i].Control;
            }

            float duration = Mathf.Max(durations[pointIndex], 0.05f);
            float elapsed = 0f;
            while (elapsed < duration && !fullProcessCancelRequested)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(elapsed / duration));
                for (int i = 0; i < actuators.Count; i++)
                {
                    actuators[i].Control =
                        (float)(starts[i] + (target[i] - starts[i]) * t);
                }
                yield return null;
            }
        }
    }

    private IEnumerator WaitForOfflineSeconds(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds && !fullProcessCancelRequested)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private TrajectoryTask BuildOfflineResetTask(
        MissionController missionController,
        int targetIndex)
    {
        List<MjActuator> actuators =
            missionController.refs.ikSolver.actuators;
        double[] resetPose = new double[actuators.Count];
        for (int i = 0; i < actuators.Count; i++)
        {
            float target = i < missionController.arm.initialAngles.Count
                ? missionController.arm.initialAngles[i]
                : (float)actuators[i].Control;
            if (actuators[i].Joint is MjHingeJoint)
            {
                target *= Mathf.Deg2Rad;
            }
            resetPose[i] = target;
        }

        return new TrajectoryTask
        {
            taskName = $"目标 {targetIndex + 1}: 离线复位",
            type = TaskType.Arm,
            targetIndex = targetIndex,
            stage = TrajectoryStage.ReturnArm,
            armPoints = new List<double[]> { resetPose }
        };
    }

    private List<int> GetTargetOrder()
    {
        List<int> result = new List<int>();
        foreach (TrajectoryTask task in taskPlaylist)
        {
            if (task.targetIndex >= 0 && !result.Contains(task.targetIndex))
            {
                result.Add(task.targetIndex);
            }
        }
        return result;
    }

    private List<double> ReadCurrentActuatorControls(
        MissionController missionController)
    {
        List<double> values = new List<double>();
        foreach (MjActuator actuator in
                 missionController.refs.ikSolver.actuators)
        {
            values.Add(actuator.Control);
        }
        return values;
    }

    private bool ValidateFineTunePlan(
        FineTuneTrajectoryPlan plan,
        int actuatorCount,
        out string error)
    {
        error = "";
        if (plan == null || plan.Points == null || plan.Points.Count == 0)
        {
            error = "视觉精调轨迹为空";
            return false;
        }

        if (plan.Durations == null ||
            plan.Durations.Count != plan.Points.Count)
        {
            error = "视觉精调轨迹点与时间数量不一致";
            return false;
        }

        for (int i = 0; i < plan.Points.Count; i++)
        {
            if (plan.Points[i] == null ||
                plan.Points[i].Length != actuatorCount ||
                !AllFinite(plan.Points[i]) ||
                float.IsNaN(plan.Durations[i]) ||
                float.IsInfinity(plan.Durations[i]) ||
                plan.Durations[i] <= 0f)
            {
                error = $"视觉精调轨迹点 {i + 1} 的维度、数值或时间无效";
                return false;
            }
        }

        return true;
    }

    private static bool AllFinite(double[] values)
    {
        foreach (double value in values)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return false;
            }
        }
        return true;
    }

    private void EnsureMockVisionTrajectory()
    {
        if (mockVisionTrajectory != null && mockVisionTrajectory.Count > 0)
        {
            return;
        }

        mockVisionTrajectory = new List<MockVisionFineTunePoint>
        {
            new MockVisionFineTunePoint(
                new[] { 0.4f, -0.3f, 0.2f, 0f, 0f, 0f, 0f },
                0.5f),
            new MockVisionFineTunePoint(
                new[] { 0.8f, -0.5f, 0.5f, 0.2f, 0f, 0f, 0.002f },
                0.7f),
            new MockVisionFineTunePoint(
                new[] { 0.5f, -0.2f, 0.3f, 0.1f, 0f, 0f, 0.001f },
                0.5f)
        };
    }

    private static string ExtractTargetPrefix(string taskName)
    {
        if (string.IsNullOrEmpty(taskName))
        {
            return "";
        }

        int colonIndex = taskName.IndexOf(':');
        return colonIndex >= 0
            ? taskName.Substring(0, colonIndex).Trim()
            : taskName.Trim();
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
