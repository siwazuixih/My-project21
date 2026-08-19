using UnityEngine;
using UnityEngine.AI;
using Mujoco;
using System.Collections;
using System.Collections.Generic;
using RobotLogic;

[RequireComponent(typeof(ChassisController))]
[RequireComponent(typeof(ArmController))]
[RequireComponent(typeof(RobotDiagnosticUI))]
public class MissionController : MonoBehaviour
{
    public CoreReferences refs;
    public ChassisSettings chassis;
    public ArmSettings arm;
    public MissionSettings mission;
    public DebugSettings debug;
    public Connect connect;

    [HideInInspector] public MissionState currentState = MissionState.Initializing;
    [HideInInspector] private int _currentMissionIndex = 0;

    /// <summary>
    /// currentMissionIndex 值发生变化时触发的事件
    /// 参数：旧值, 新值
    /// </summary>
    public event System.Action<int, int> OnMissionIndexChanged;

    public int currentMissionIndex
    {
        get => _currentMissionIndex;
        set
        {
            int oldValue = _currentMissionIndex;
            _currentMissionIndex = value;
            OnMissionIndexChanged?.Invoke(oldValue, value);
        }
    }
    [HideInInspector] public List<Vector3[]> globalPathCache = new List<Vector3[]>();
    [HideInInspector] public List<DiagnosisSnapshot> snapshots = new List<DiagnosisSnapshot>();
    [HideInInspector] public bool hasPrecalculated = false;

    public ChassisController chassisCtrl { get; private set; }
    public ArmController armCtrl { get; private set; }
    public RobotDiagnosticUI diagUI { get; private set; }

    [Header("真机硬件联动索引配置")]
    [Tooltip("升降缸执行器在 MujocoStaticIKSolver 的 Actuators 列表中的索引编号（通常看你 Inspector 面板里怎么排的）")]
    public int liftActuatorIndex = 6;
    private double[] initialQpos;

    void Awake()
    {
        chassisCtrl = GetComponent<ChassisController>();
        armCtrl = GetComponent<ArmController>();
        diagUI = GetComponent<RobotDiagnosticUI>();

        chassisCtrl.Init(this);
        armCtrl.Init(this);
        diagUI.Init(this);
    }

    void Start()
    {
        currentState = MissionState.Initializing;
        StartCoroutine(armCtrl.InitArmRoutine());
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)) ResetMission(); 
        if (Input.GetKeyDown(KeyCode.Space))
        {
            switch (currentState)
            {
                case MissionState.Idle:
                    StartMissionSequence();
                    break;
                case MissionState.WaitingToStartPath:
                    currentState = MissionState.ChassisMoving;
                    chassisCtrl.CalculateAndStartPath(true);
                    break;
                case MissionState.WaitingForNextTarget:
                    ExecuteNextTargetLogic();
                    break;
                case MissionState.WaitingForInput:
                    StartArmWork();
                    break;
            }
        }
        if (currentState == MissionState.WaitingForInput && Input.GetKeyDown(KeyCode.K)) StartArmWork();
        if (currentState == MissionState.ChassisMoving) chassisCtrl.Tick();
    }

    void StartMissionSequence()
    {
        if (mission.targets != null && mission.targets.Count > 0)
        {
            Debug.Log("🔄 开始全任务预计算...");
            bool success = DeepPrecomputeAll(); 
            
            if (success)
            {
                Debug.Log($"🚀 [预计算] 成功计算 {mission.targets.Count} 个任务！底盘路径和IK全部通过。");
            }
            else
            {
                Debug.LogError("⚠️ [预计算] 存在严重路径或IK问题，任务已停止，不会执行不完整的缓存路径。");
                currentState = MissionState.Idle;
                return;
            }

            currentMissionIndex = 0;
            refs.targetObject = mission.targets[0];

            if (mission.stepByStepMode) currentState = MissionState.WaitingToStartPath;
            else { currentState = MissionState.ChassisMoving; chassisCtrl.CalculateAndStartPath(true); }
        }
        else
        { 
            if (mission.stepByStepMode) currentState = MissionState.WaitingToStartPath;
            else { currentState = MissionState.ChassisMoving; chassisCtrl.CalculateAndStartPath(false); }
        }
    }

    public void OnChassisReachedTarget()
    {
        diagUI.RunDetailedDiagnosis(currentMissionIndex);
        if (chassis.autoStartArm) {
            currentState = MissionState.Stabilizing;
            StartCoroutine(WaitAndStartArm());
        } else currentState = MissionState.WaitingForInput;
    }

    IEnumerator WaitAndStartArm()
    {
        if (chassis.inertiaDelay > 0.01f) yield return new WaitForSeconds(chassis.inertiaDelay);
        StartArmWork();
    }

    void StartArmWork()
    {
        currentState = MissionState.ArmPlanning;
        armCtrl.StartArmSequence();
    }

    public void OnArmTaskFinished()
    {
        if (mission.targets != null && mission.targets.Count > 0) {
            currentMissionIndex++;
            if (currentMissionIndex >= mission.targets.Count) {
                if (mission.loopMission) currentMissionIndex = 0;
                else {
                    Debug.Log("🎉🎉🎉 所有任务完成！");
                    currentState = MissionState.Finished;
                    return;
                }
            }
            if (mission.stepByStepMode) currentState = MissionState.WaitingForNextTarget;
            else ExecuteNextTargetLogic();
        }
    }

    void ExecuteNextTargetLogic()
    {
        refs.targetObject = mission.targets[currentMissionIndex];
        if (mission.resetArmBeforeMoving) {
            currentState = MissionState.ResettingArm;
            StartCoroutine(armCtrl.ResetArmRoutine(() => {
                currentState = MissionState.ChassisMoving;
                chassisCtrl.CalculateAndStartPath(true);
            }));
        } else {
            StartCoroutine(WaitAndMove(mission.intervalBetweenTasks));
        }
    }

    IEnumerator WaitAndMove(float delay)
    {
        yield return new WaitForSeconds(delay);
        currentState = MissionState.ChassisMoving;
        chassisCtrl.CalculateAndStartPath(true);
    }

    private bool TryBuildChassisPath(
        int taskIndex,
        Vector3 simPos,
        Vector3 simFwd,
        Transform destination,
        out Vector3[] executionPath,
        out Vector3 finalStopPoint,
        out Vector3 finalFacingDir,
        out bool hasNavigationPath,
        out string failureReason)
    {
        executionPath = null;
        finalStopPoint = simPos;
        finalFacingDir = HorizontalDirectionOrFallback(simFwd, transform.forward);
        hasNavigationPath = false;
        failureReason = null;

        if (destination == null)
        {
            failureReason = "目标对象为空";
            return false;
        }

        float horizontalDistance = HorizontalDistance(simPos, destination.position);
        Debug.Log(
            $"[底盘预计算] 目标 {taskIndex + 1}/{mission.targets.Count}: " +
            $"起点={simPos:F3}, 目标={destination.position:F3}, 水平距离={horizontalDistance:F3}m, " +
            $"机械臂工作半径={chassis.armReachDistance:F3}m");

        if (horizontalDistance <= chassis.armReachDistance)
        {
            executionPath = new[] { simPos };
            Debug.Log($"[底盘预计算] 目标 {taskIndex + 1} 已在机械臂工作半径内，底盘保持原位。");
            return true;
        }

        Vector3 sourceProbe = new Vector3(simPos.x, simPos.y, simPos.z);
        float sourceSampleRadius = Mathf.Max(0.5f, chassis.stopDistance * 4f);
        if (!NavMesh.SamplePosition(sourceProbe, out NavMeshHit sourceHit, sourceSampleRadius, NavMesh.AllAreas))
        {
            failureReason = $"底盘起点附近 {sourceSampleRadius:F2}m 内没有可用 NavMesh";
            return false;
        }

        // SamplePosition 得到的是 NavMesh 上的计算起点，并不一定等于底盘的真实位置。
        // 小偏移时只用它做 CalculatePath，避免底盘先去追这个采样点而产生短折线；
        // 偏移过大则说明底盘已经明显离开导航面，继续执行会有穿越障碍的风险。
        const float sourceSampleExecutionTolerance = 0.25f;
        float sourceSampleOffset = HorizontalDistance(simPos, sourceHit.position);
        if (sourceSampleOffset > sourceSampleExecutionTolerance)
        {
            failureReason =
                $"底盘真实起点距最近 NavMesh 点 {sourceSampleOffset:F3}m，" +
                $"超过允许偏差 {sourceSampleExecutionTolerance:F3}m";
            return false;
        }

        // 目标对象的 Transform 往往位于模型内部或离地。先投影到与底盘同高的地面，
        // 再在机械臂工作半径内寻找可导航点，避免直接把模型中心交给 CalculatePath。
        Vector3 destinationProbe = new Vector3(destination.position.x, simPos.y, destination.position.z);
        float targetSampleRadius = Mathf.Max(0.5f, chassis.armReachDistance);
        if (!NavMesh.SamplePosition(destinationProbe, out NavMeshHit destinationHit, targetSampleRadius, NavMesh.AllAreas))
        {
            failureReason = $"目标附近 {targetSampleRadius:F2}m 内没有可用 NavMesh 停靠点";
            return false;
        }

        float sampledDistance = HorizontalDistance(destinationHit.position, destination.position);
        if (sampledDistance > chassis.armReachDistance + 0.01f)
        {
            failureReason =
                $"最近 NavMesh 点距目标 {sampledDistance:F3}m，超过机械臂工作半径 {chassis.armReachDistance:F3}m";
            return false;
        }

        NavMeshPath navPath = new NavMeshPath();
        bool pathCalculated = NavMesh.CalculatePath(
            sourceHit.position,
            destinationHit.position,
            NavMesh.AllAreas,
            navPath);

        int cornerCount = navPath.corners != null ? navPath.corners.Length : 0;
        Debug.Log(
            $"[底盘预计算] 目标 {taskIndex + 1}: 起点采样={sourceHit.position:F3}, " +
            $"起点偏移={sourceSampleOffset:F3}m（采样点仅用于路径计算）, " +
            $"终点采样={destinationHit.position:F3}, CalculatePath={pathCalculated}, " +
            $"状态={navPath.status}, 角点数={cornerCount}");

        if (!pathCalculated || navPath.status != NavMeshPathStatus.PathComplete || cornerCount == 0)
        {
            failureReason = $"NavMesh 路径无效或不完整（返回={pathCalculated}, 状态={navPath.status}, 角点={cornerCount}）";
            return false;
        }

        List<Vector3> route = new List<Vector3>();
        AddPathPointIfDistinct(route, simPos);
        // navPath.corners[0] 是 sourceHit；执行路径从真实 simPos 直接接入主路径，
        // 不把小偏移的 NavMesh 采样点当作必须到达的底盘航点。
        for (int i = 1; i < cornerCount; i++)
        {
            AddPathPointIfDistinct(route, navPath.corners[i]);
        }
        AddPathPointIfDistinct(route, destinationHit.position);

        List<Vector3> trimmedPath = new List<Vector3> { simPos };
        bool foundStopPoint = false;
        const float sampleStep = 0.05f;

        for (int segmentIndex = 0; segmentIndex < route.Count - 1 && !foundStopPoint; segmentIndex++)
        {
            Vector3 segmentStart = route[segmentIndex];
            Vector3 segmentEnd = route[segmentIndex + 1];
            Vector3 horizontalSegment = segmentEnd - segmentStart;
            horizontalSegment.y = 0f;
            float segmentLength = horizontalSegment.magnitude;

            if (segmentLength <= 0.0001f)
            {
                continue;
            }

            Vector3 segmentDirection = horizontalSegment / segmentLength;
            int sampleCount = Mathf.Max(1, Mathf.CeilToInt(segmentLength / sampleStep));
            for (int sampleIndex = 0; sampleIndex <= sampleCount; sampleIndex++)
            {
                float distanceOnSegment = Mathf.Min(sampleIndex * sampleStep, segmentLength);
                Vector3 testPosition = segmentStart + segmentDirection * distanceOnSegment;
                testPosition.y = simPos.y;

                if (HorizontalDistance(testPosition, destination.position) <= chassis.armReachDistance)
                {
                    finalStopPoint = testPosition;
                    finalFacingDir = segmentDirection;
                    AddPathPointIfDistinct(trimmedPath, finalStopPoint);
                    foundStopPoint = true;
                    break;
                }
            }

            if (!foundStopPoint)
            {
                Vector3 routePoint = segmentEnd;
                routePoint.y = simPos.y;
                AddPathPointIfDistinct(trimmedPath, routePoint);
            }
        }

        if (!foundStopPoint)
        {
            failureReason =
                $"完整 NavMesh 路径上没有找到距目标 {chassis.armReachDistance:F3}m 内的底盘停靠点";
            return false;
        }

        executionPath = trimmedPath.ToArray();
        hasNavigationPath = executionPath.Length > 1 && HorizontalDistance(simPos, finalStopPoint) > chassis.stopDistance;

        Vector3 moveDelta = finalStopPoint - simPos;
        Debug.Log(
            $"[底盘预计算] 目标 {taskIndex + 1}: 停靠点={finalStopPoint:F3}, " +
            $"移动量={moveDelta:F3}, 停靠后距目标={HorizontalDistance(finalStopPoint, destination.position):F3}m, " +
            $"朝向={finalFacingDir:F3}, 执行路径点数={executionPath.Length}");
        return true;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static Vector3 HorizontalDirectionOrFallback(Vector3 direction, Vector3 fallback)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f) return direction.normalized;

        fallback.y = 0f;
        return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
    }

    private static void AddPathPointIfDistinct(List<Vector3> points, Vector3 point)
    {
        if (points.Count == 0 || HorizontalDistance(points[points.Count - 1], point) > 0.001f)
        {
            points.Add(point);
        }
    }

    unsafe bool DeepPrecomputeAll()
    {
        Debug.Log("🔍 进入深度预计算...");
        globalPathCache.Clear();
        snapshots.Clear();
        hasPrecalculated = false;

        // 💡 1. 每次预计算开始，清空任务清单
        if (connect.Commander != null) connect.Commander.ClearAllTasks();

        if (mission.targets == null || mission.targets.Count == 0) return false;

        int nq = MjScene.Instance.Model->nq;
        if (initialQpos == null || initialQpos.Length != nq) {
            initialQpos = new double[nq];
            for(int i = 0; i < nq; i++) initialQpos[i] = MjScene.Instance.Data->qpos[i];
        }
        double[] backupQpos = new double[nq];
        for(int i = 0; i < nq; i++) backupQpos[i] = MjScene.Instance.Data->qpos[i];

        // 💡 2. 提取机械臂初始角度，用作插值的起点
        int actCount = refs.ikSolver.actuators.Count;
        double[] virtualArmQ = new double[actCount];
        for (int i = 0; i < actCount; i++) {
            float initVal = (i < arm.initialAngles.Count) ? arm.initialAngles[i] : 0;
            virtualArmQ[i] = (refs.ikSolver.actuators[i].Joint is MjHingeJoint) ? initVal * Mathf.Deg2Rad : initVal;
        }

        Vector3 simPos = chassisCtrl.GetRobotPosition();
        if (refs.realRobotBase) simPos.y = refs.realRobotBase.transform.position.y;
        
        Vector3 simFwd = transform.forward; 
        if(chassis.actuatorRot != null) {
             float ang = (float)chassis.actuatorRot.Control * -Mathf.Rad2Deg;
             simFwd = Quaternion.Euler(0, ang - chassis.headingOffset, 0) * Vector3.forward;
        }

        bool allSuccess = true;

        for (int i = 0; i < mission.targets.Count; i++)
        {
            Transform dest = mission.targets[i];
            if (!TryBuildChassisPath(
                    i,
                    simPos,
                    simFwd,
                    dest,
                    out Vector3[] chassisPathForExecution,
                    out Vector3 finalStopPoint,
                    out Vector3 finalFacingDir,
                    out bool hasNavigationPath,
                    out string pathFailureReason))
            {
                Debug.LogError($"[底盘预计算] 目标 {i + 1} 路径规划失败：{pathFailureReason}。已终止整组任务。");
                allSuccess = false;
                break;
            }

            globalPathCache.Add(chassisPathForExecution);

            // 💡 3. 将底盘导航路径加入清单！
            // if (tempPath.corners != null && tempPath.corners.Length > 0 && connect.Commander != null) {
            //     // 传入当前的仿真车头朝向 simFwd，保证真机第一步旋转对齐不出错
            //     connect.Commander.AddChassisTask($"目标 {i+1}: 导航", tempPath.corners, simFwd);
            // }
            if (hasNavigationPath && connect.Commander != null) {
                connect.Commander.AddChassisTask($"目标 {i+1}: 导航", chassisPathForExecution, simFwd);
            }
            
            Vector3 moveDelta = finalStopPoint - simPos;
            chassisCtrl.TeleportSimulationRelative(moveDelta, finalFacingDir);
            armCtrl.ResetArmQposInSimulation();

            Vector3 tPos; Quaternion tRot;
            armCtrl.CalculateObservationPose(dest.position, finalStopPoint, finalFacingDir, out tPos, out tRot);

            List<double[]> armSolution = null;
            if (arm.useBitStarPlanner && refs.bitPlanner != null) {
                armSolution = arm.enableLookAt ? refs.bitPlanner.Plan(tPos, tRot) : refs.bitPlanner.Plan(tPos);
            } else {
                double[] ikResult = arm.enableLookAt ? refs.ikSolver.SolveIK(tPos, tRot) : refs.ikSolver.SolveIK(tPos);
                if (ikResult != null) {
                    double[] compacted = new double[refs.ikSolver.actuators.Count];
                    for(int a=0; a<refs.ikSolver.actuators.Count; a++) {
                        int qAddr = armCtrl.GetActuatorQposAddr(refs.ikSolver.actuators[a]);
                        if(qAddr != -1 && qAddr < ikResult.Length) compacted[a] = ikResult[qAddr];
                    }
                    armSolution = new List<double[]> { compacted };
                }
            }

            // 💡 4. 将机械臂抓取和复位路径加入清单！
            if (armSolution != null && armSolution.Count > 0 && connect.Commander != null) 
            {   

                // 获取本次 IK 结算出的最终位姿状态
                double[] finalPose = armSolution[armSolution.Count - 1];
                
                if (liftActuatorIndex < finalPose.Length)
                {
                    // 🌟 核心抽离：从 MuJoCo 仿真状态里直接提取出升降缸的目标高度 (单位：米)
                    float simulatedLiftHeight = (float)finalPose[liftActuatorIndex];
                    
                    // 自动向真机任务清单中插入一个独立的“升降缸动作”
                    connect.Commander.AddLiftTask($"目标 {i+1}: 升降缸调节", simulatedLiftHeight);
                }
                else
                {
                    Debug.LogError($"liftActuatorIndex 设置错误！索引 {liftActuatorIndex} 超过了执行器总数。");
                }

                connect.Commander.AddArmTask($"目标 {i+1}: 抓取", armSolution, virtualArmQ, arm.motionCurve);
                virtualArmQ = armSolution[armSolution.Count - 1]; // 更新末端状态

                if (mission.resetArmBeforeMoving && i < mission.targets.Count - 1) {
                    double[] resetPose = new double[actCount];
                    for (int j = 0; j < actCount; j++) {
                        float target = (j < arm.initialAngles.Count) ? arm.initialAngles[j] : 0;
                        if (refs.ikSolver.actuators[j].Joint is MjHingeJoint) target *= Mathf.Deg2Rad;
                        resetPose[j] = target;
                    }
                    connect.Commander.AddArmTask($"目标 {i+1}: 复位", new List<double[]> { resetPose }, virtualArmQ, arm.motionCurve);
                    virtualArmQ = resetPose;
                }
            }

            snapshots.Add(new DiagnosisSnapshot {
                taskId = i, precalcChassisPos = finalStopPoint,
                precalcChassisAngle = Quaternion.LookRotation(finalFacingDir).eulerAngles.y,
                precalcArmTarget = tPos, ikSuccess = (armSolution != null && armSolution.Count > 0), precalcArmPlan = armSolution 
            });

            if (armSolution == null || armSolution.Count == 0)
            {
                Debug.LogError($"[机械臂预计算] 目标 {i + 1} 未得到可执行解，已终止整组任务。");
                allSuccess = false;
                break;
            }

            simPos = finalStopPoint; simFwd = finalFacingDir;
        }

        for(int i = 0; i < nq; i++) MjScene.Instance.Data->qpos[i] = backupQpos[i];
        MujocoLib.mj_forward(MjScene.Instance.Model, MjScene.Instance.Data);

        hasPrecalculated = allSuccess;
        if (!allSuccess)
        {
            globalPathCache.Clear();
            snapshots.Clear();
            if (connect.Commander != null) connect.Commander.ClearAllTasks();
        }
        return allSuccess;
    }

    public unsafe void ResetMission()
    {
        Debug.Log("♻️ 正在执行完整系统重置...");
        StopAllCoroutines();
        chassisCtrl.StopMovement(); 
        armCtrl.StopAndResetControls(); 
        if (connect.Commander != null) connect.Commander.ClearAllTasks(); // 重置清空列表

        if (initialQpos != null) {
            for(int i = 0; i < initialQpos.Length; i++) MjScene.Instance.Data->qpos[i] = initialQpos[i];
            for(int i = 0; i < MjScene.Instance.Model->nv; i++) MjScene.Instance.Data->qvel[i] = 0;
            MujocoLib.mj_forward(MjScene.Instance.Model, MjScene.Instance.Data);
        }

        currentMissionIndex = 0; currentState = MissionState.Idle;
    }

    public unsafe void ControlMission(List<Transform> seletectedObjects, float? chassisSpeed, float? armSpeed, float? distance, float? x, float? y, float? z)
    {
        if (seletectedObjects == null || seletectedObjects.Count == 0)
        {
            Debug.Log("无路径点，退出控制");
            return;
        }
        mission.targets.Clear();
        HashSet<int> uniqueTargetIds = new HashSet<int>();
        foreach (Transform selectedTransform in seletectedObjects)
        {
            Transform logicalTarget = ModelCollisionHighlighter.ResolveLogicalSelectionTarget(selectedTransform);
            if (logicalTarget == null || !uniqueTargetIds.Add(logicalTarget.GetInstanceID())) continue;
            mission.targets.Add(logicalTarget);
        }

        if (mission.targets.Count == 0)
        {
            Debug.LogWarning("选中的路径点全部无效，退出控制。");
            return;
        }

        if (mission.targets.Count != seletectedObjects.Count)
        {
            Debug.Log($"[路径点去重] 原始 {seletectedObjects.Count} 个，合并原模型/Hull后保留 {mission.targets.Count} 个逻辑目标。");
        }
        if (chassisSpeed != null)
        {
            chassis.moveSpeed = chassisSpeed.Value;
        }
        if (armSpeed != null)
        {
            arm.jointSpeed = armSpeed.Value;
        }
        if (distance != null)
        {
            arm.observationDistance = distance.Value;
        }
        if (x != null && y != null && z != null)
        {
            arm.manualObservationVec = new Vector3(x.Value, y.Value, z.Value);
        }

        Debug.Log("当前状态：" + currentState);
        switch (currentState)
        {
            case MissionState.Idle:
                Debug.Log("开始任务");
                StartMissionSequence();
                break;
            case MissionState.WaitingToStartPath:
                Debug.Log("计算路径");
                currentState = MissionState.ChassisMoving;
                chassisCtrl.CalculateAndStartPath(true);
                break;
            case MissionState.WaitingForNextTarget:
                Debug.Log("执行目标");
                ExecuteNextTargetLogic();
                break;
            case MissionState.WaitingForInput:
                Debug.Log("机械臂工作");
                StartArmWork();
                break;
        }
    }
}
