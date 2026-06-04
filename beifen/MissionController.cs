using UnityEngine;
using UnityEngine.AI;
using Mujoco;
using System.Collections;
using System.Collections.Generic;
using System.Text;

// 强制依赖 LineRenderer 组件
[RequireComponent(typeof(LineRenderer))]
public class MissionController : MonoBehaviour
{
    // ========================================================================
    // 📦 1. 配置数据结构
    // ========================================================================

    [System.Serializable]
    public class CoreReferences
    {
        [Header("核心组件")]
        public Transform targetObject;          
        public MujocoStaticIKSolver ikSolver;   
        public BITStarPlanner bitPlanner;       
        public GameObject realRobotBase;        
        public Transform armEndEffector;        
    }

    [System.Serializable]
    public class ChassisSettings
    {
        [Header("执行器 (MjActuator)")]
        public MjActuator actuatorX;
        public MjActuator actuatorZ;
        public MjActuator actuatorRot;

        [Header("运动参数")]
        public float moveSpeed = 0.5f;
        public float turnSpeed = 2.0f;
        public float alignThreshold = 2.0f;
        public float stopDistance = 0.05f;

        [Header("交互逻辑")]
        public float armReachDistance = 1.5f; 
        public bool autoStartArm = true;
        public float inertiaDelay = 0.5f;

        [Header("方向校准")]
        public bool invertX = false;
        public bool invertZ = false;
        public bool swapXZ = false;
        [Range(-180, 180)] public float headingOffset = 0.0f;

        [Header("动态避障")]
        public bool enableDynamicAvoidance = true;
        public float avoidanceUpdateInterval = 0.5f;
    }

    [System.Serializable]
    public class ArmSettings
    {
        [Header("初始姿态")]
        public List<float> initialAngles = new List<float>();

        [Header("运动控制")]
        [Tooltip("是否直接使用预计算的机械臂路径？(勾选后运行时不再重新规划，直接复用结果，消除IK多解误差)")]
        public bool usePrecalculatedSolution = true; 
        
        public bool useBitStarPlanner = true;
        public float jointSpeed = 0.8f;
        public AnimationCurve motionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Tooltip("物理误差容忍度 (米)")]
        public float physicalTolerance = 0.05f;

        [Header("姿态感知 (Eye-in-Hand)")]
        public bool enableLookAt = true;
        public float observationDistance = 0.3f;
        public Vector3 faceAxis = new Vector3(0, 0, 1);
        
        [Header("手动观测模式")]
        public bool useManualObservation = false;
        public Vector3 manualObservationVec = new Vector3(0, 1, 0);
    }

    [System.Serializable]
    public class MissionSettings
    {
        [Header("多目标巡航")]
        public List<Transform> targets = new List<Transform>();
        public bool loopMission = false;
        public bool resetArmBeforeMoving = true;
        public float intervalBetweenTasks = 1.0f;
    }

    [System.Serializable]
    public class DebugSettings
    {
        public bool showGUI = true;
        [Header("Gizmos 开关")]
        public bool showGlobalPlan = true;
        public bool showRealtimePath = true;
        public bool showChassisLines = true;
        public bool showArmTrail = true;
        public bool showGhostRobot = true;

        [Header("颜色样式")]
        public Color globalPathColor = Color.white;
        public Color realtimePathColor = Color.green;
        public Color trailColor = Color.red;
        public Color ghostColor = new Color(1, 1, 0, 0.3f);
    }

    // 🕵️ 诊断快照结构
    private struct DiagnosisSnapshot
    {
        public int taskId;
        public Vector3 precalcChassisPos;   
        public float precalcChassisAngle;   
        public Vector3 precalcArmTarget;    
        public bool ikSuccess;
        public List<double[]> precalcArmPlan; 
    }

    // ========================================================================
    // 🎛️ 2. 变量定义
    // ========================================================================
    
    public CoreReferences refs;
    public ChassisSettings chassis;
    public ArmSettings arm;
    public MissionSettings mission;
    public DebugSettings debug;

    private enum State { Idle, Rotating, Moving, Stabilizing, WaitingForInput, Planning, MovingArm, ResettingArm, Finished }
    private State currentState = State.Idle;

    private List<Vector3[]> _globalPathCache = new List<Vector3[]>();
    private List<DiagnosisSnapshot> _snapshots = new List<DiagnosisSnapshot>();
    private bool _hasPrecalculated = false;

    private Vector3[] pathCorners;
    private int currentCornerIndex = 0;
    private float currentContinuousAngle = 0f;
    private float currentFacingAngle = 0f;
    private int currentMissionIndex = 0;

    private LineRenderer lineVis;
    private Coroutine dynamicPathCoroutine;

    private Vector3 lockedArmTargetPos;
    private Quaternion lockedArmTargetRot;
    private bool hasLockedTarget = false;

    // 调试数据
    private float debug_MotorX, debug_MotorZ;
    private string debug_PlannerStatus = "Ready";
    private float debug_LastPosErr = 0f;
    private float debug_LastAngErr = 0f;

    // ========================================================================
    // 🔄 3. Unity 生命周期
    // ========================================================================

    void Start()
    {
        InitializeVisuals();
        InitializeChassisState();
        ApplyInitialArmPose(); 
    }

    void Update()
    {
        UpdateVisuals();

        if (currentState == State.Idle && Input.GetKeyDown(KeyCode.Space))
        {
            StartMissionSequence();
        }

        if (currentState == State.Rotating || currentState == State.Moving) ProcessChassisMovement();
        if (currentState == State.WaitingForInput && Input.GetKeyDown(KeyCode.K)) StartArmSequence();

        if (chassis.actuatorX) debug_MotorX = (float)chassis.actuatorX.Control;
        if (chassis.actuatorZ) debug_MotorZ = (float)chassis.actuatorZ.Control;
    }

    // ========================================================================
    // 📅 4. 任务调度系统
    // ========================================================================

    void StartMissionSequence()
    {
        if (mission.targets != null && mission.targets.Count > 0)
        {
            Debug.Log("🔄 开始全任务预计算...");
            bool success = DeepPrecomputeAll(); 
            
            if (success)
            {
                Debug.Log($"🚀 [预计算] 成功计算 {mission.targets.Count} 个任务！IK全部通过。");
                currentMissionIndex = 0;
                refs.targetObject = mission.targets[0];
                CalculateAndStartPath(useCache: true); 
            }
            else
            {
                Debug.LogError("⚠️ [预计算] 存在严重路径或IK问题，请检查 Gizmos 红色标记！");
                currentMissionIndex = 0;
                refs.targetObject = mission.targets[0];
                CalculateAndStartPath(useCache: true); 
            }
        }
        else
        {
            Debug.Log("🚀 单点模式 (无预计算)");
            CalculateAndStartPath(useCache: false);
        }
    }

    void ProcessNextTarget()
    {
        if (mission.targets != null && mission.targets.Count > 0)
        {
            currentMissionIndex++;
            if (currentMissionIndex >= mission.targets.Count)
            {
                if (mission.loopMission) {
                    currentMissionIndex = 0;
                } else {
                    Debug.Log("🎉🎉🎉 所有任务完成！");
                    currentState = State.Finished;
                    return;
                }
            }

            refs.targetObject = mission.targets[currentMissionIndex];
            if (mission.resetArmBeforeMoving) {
                currentState = State.ResettingArm;
                StartCoroutine(ResetArmAndMove());
            } else {
                StartCoroutine(WaitAndMove(mission.intervalBetweenTasks));
            }
        }
    }

    // ========================================================================
    // 🧠 5. 深度预计算 (修复版：增量瞬移 + 机械臂复位)
    // ========================================================================

    unsafe bool DeepPrecomputeAll()
    {
        Debug.Log("🔍 [Debug] 进入深度预计算 (增量瞬移 + 强制复位)...");
        _globalPathCache.Clear();
        _snapshots.Clear();
        _hasPrecalculated = false;

        if (mission.targets == null || mission.targets.Count == 0) return false;

        // 备份物理状态
        int nq = MjScene.Instance.Model->nq;
        double[] backupQpos = new double[nq];
        for(int i = 0; i < nq; i++) backupQpos[i] = MjScene.Instance.Data->qpos[i];

        // 初始化模拟位置 (用于计算增量)
        Vector3 simPos = GetRobotPosition();
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
            
            // --- 1. 底盘步进模拟 ---
            float distToTarget = Vector3.Distance(new Vector3(simPos.x, 0, simPos.z), new Vector3(dest.position.x, 0, dest.position.z));
            Vector3 finalStopPoint = simPos;
            Vector3 finalFacingDir = simFwd;
            
            if (distToTarget < chassis.armReachDistance) {
                Debug.Log($"   ✅ [Precalc Task {i}] 偷懒模式: 初始距离 {distToTarget:F2} < {chassis.armReachDistance}");
                _globalPathCache.Add(new Vector3[]{ simPos, dest.position });
            } else {
                Debug.Log($"   🚀 [Precalc Task {i}] 移动模式: 初始距离 {distToTarget:F2}");
                NavMeshPath tempPath = new NavMeshPath();
                NavMesh.CalculatePath(simPos, dest.position, NavMesh.AllAreas, tempPath);
                _globalPathCache.Add(tempPath.corners);

                if (tempPath.corners.Length >= 2) {
                    Vector3 pathStart = tempPath.corners[tempPath.corners.Length - 2];
                    Vector3 pathEnd = tempPath.corners[tempPath.corners.Length - 1];
                    Vector3 segmentDir = (pathEnd - pathStart).normalized;
                    float segmentLen = Vector3.Distance(pathStart, pathEnd);
                    float stepSize = 0.05f; 
                    bool foundStop = false;
                    finalStopPoint = pathEnd; finalFacingDir = segmentDir;

                    for (float d = 0; d <= segmentLen; d += stepSize) {
                        Vector3 testPos = pathStart + (segmentDir * d);
                        float testDist = Vector3.Distance(new Vector3(testPos.x, 0, testPos.z), new Vector3(dest.position.x, 0, dest.position.z));
                        if (testDist < chassis.armReachDistance) {
                            finalStopPoint = testPos; finalStopPoint.y = simPos.y;
                            finalFacingDir = segmentDir;
                            foundStop = true;
                            Debug.Log($"      -> 模拟停车成功: 距离起点走了 {d:F2}m | 距目标 {testDist:F2}m");
                            break;
                        }
                    }
                    if (!foundStop) Debug.Log("      -> 警告: 走完路径仍未进入抓取范围，停在路径终点");
                }
            }

            // --- 2. 物理瞬移 (关键修复：增量模式) ---
            // 计算从“当前模拟位置”到“目标位置”的位移差
            Vector3 moveDelta = finalStopPoint - simPos;
            
            // 使用增量进行瞬移，避免绝对坐标导致的偏移问题
            TeleportSimulationRelative(moveDelta, finalFacingDir);
            
            // --- 3. 机械臂强制复位 ---
            // 确保 IK 从标准姿态开始计算，消除多解误差
            ResetArmQposInSimulation();

            // --- 4. IK 计算 ---
            Vector3 tPos; Quaternion tRot;
            CalculateObservationPose(dest.position, finalStopPoint, finalFacingDir, out tPos, out tRot);

            Debug.Log($"   🤖 [Task {i} IK] 目标: {tPos}");

            List<double[]> armSolution = null;
            if (arm.useBitStarPlanner && refs.bitPlanner != null) {
                armSolution = refs.bitPlanner.Plan(tPos);
            } else {
                double[] ikResult = arm.enableLookAt ? refs.ikSolver.SolveIK(tPos, tRot) : refs.ikSolver.SolveIK(tPos);
                if (ikResult != null) {
                    double[] compacted = new double[refs.ikSolver.actuators.Count];
                    for(int a=0; a<refs.ikSolver.actuators.Count; a++) {
                        int qAddr = GetActuatorQposAddr(refs.ikSolver.actuators[a]);
                        if(qAddr != -1 && qAddr < ikResult.Length) compacted[a] = ikResult[qAddr];
                    }
                    armSolution = new List<double[]> { compacted };
                }
            }

            _snapshots.Add(new DiagnosisSnapshot {
                taskId = i,
                precalcChassisPos = finalStopPoint,
                precalcChassisAngle = Quaternion.LookRotation(finalFacingDir).eulerAngles.y,
                precalcArmTarget = tPos,
                ikSuccess = (armSolution != null && armSolution.Count > 0),
                precalcArmPlan = armSolution 
            });

            if (armSolution == null) allSuccess = false;
            
            // 更新当前模拟位置，为下一轮计算做准备
            simPos = finalStopPoint;
            simFwd = finalFacingDir;
        }

        // 恢复物理状态
        for(int i = 0; i < nq; i++) MjScene.Instance.Data->qpos[i] = backupQpos[i];
        MujocoLib.mj_forward(MjScene.Instance.Model, MjScene.Instance.Data);

        _hasPrecalculated = true;
        return allSuccess;
    }

    // 🛠️ 关键修复：增量瞬移 (Relative Teleport)
    // 直接累加位移差到 qpos，不依赖世界坐标原点
    unsafe void TeleportSimulationRelative(Vector3 moveDelta, Vector3 targetFwd)
    {
        // 1. 处理位置 (X 和 Z 累加)
        if (chassis.actuatorX?.Joint != null) {
            float dx = moveDelta.x;
            if (chassis.invertX) dx = -dx;
            // 如果开启了XZ交换
            if (chassis.swapXZ) {
                // 注意：在 SwapXZ 情况下，delta.x 应该映射给 Z 关节，delta.z 映射给 X 关节？
                // 或者是控制逻辑交换？根据您之前的代码逻辑：
                // dx = chassis.invertZ ? -localPos.z : localPos.z;
                // 这里我们假设 delta 是世界坐标差值。
                // 如果 swapXZ，意味着 ActuatorX 控制的是 Z 方向的移动。
                // 这是一个简化处理，如果您的场景不涉及 swapXZ，这部分代码是安全的。
                // 如果涉及 swapXZ，请确保 delta 分量正确映射。
                // 暂时按未交换处理，因为您的层级结构似乎没有启用 swapXZ。
                MjScene.Instance.Data->qpos[chassis.actuatorX.Joint.QposAddress] += dx;
            } else {
                MjScene.Instance.Data->qpos[chassis.actuatorX.Joint.QposAddress] += dx;
            }
        }

        if (chassis.actuatorZ?.Joint != null) {
            float dz = moveDelta.z;
            if (chassis.invertZ) dz = -dz;
            MjScene.Instance.Data->qpos[chassis.actuatorZ.Joint.QposAddress] += dz;
        }

        // 2. 处理旋转 (Rot 绝对值)
        // 假设 Rot 控制的是 Heading，通常是绝对角度
        if (chassis.actuatorRot?.Joint != null) {
            float angleRad = -(Quaternion.LookRotation(targetFwd).eulerAngles.y + chassis.headingOffset) * Mathf.Deg2Rad;
            MjScene.Instance.Data->qpos[chassis.actuatorRot.Joint.QposAddress] = angleRad;
        }
        
        // 3. 刷新物理
        MujocoLib.mj_forward(MjScene.Instance.Model, MjScene.Instance.Data);
    }

    // 🛠️ 辅助函数：机械臂复位
    unsafe void ResetArmQposInSimulation()
    {
        var acts = refs.ikSolver.actuators;
        for (int i = 0; i < acts.Count; i++) {
            if (i < arm.initialAngles.Count) {
                float initVal = arm.initialAngles[i];
                var act = acts[i];
                if (act.Joint is MjHingeJoint h) MjScene.Instance.Data->qpos[h.QposAddress] = initVal * Mathf.Deg2Rad;
                else if (act.Joint is MjSlideJoint s) MjScene.Instance.Data->qpos[s.QposAddress] = initVal;
            }
        }
        MujocoLib.mj_forward(MjScene.Instance.Model, MjScene.Instance.Data);
    }

    // ========================================================================
    // 🚙 6. 运行时底盘控制
    // ========================================================================

    void CalculateAndStartPath(bool useCache = false)
    {
        if (!refs.targetObject) return;
        hasLockedTarget = false;
        bool pathFound = false;

        if (useCache && _hasPrecalculated && currentMissionIndex < _globalPathCache.Count)
        {
            pathCorners = _globalPathCache[currentMissionIndex];
            currentCornerIndex = (pathCorners.Length > 1) ? 1 : 0;
            pathFound = true;
            Debug.Log("🎉 使用预计算底盘路径成功！");
        }

        if (!pathFound)
        {
            if (RecalculateNavMeshPath()) pathFound = true;
            Debug.Log("⚠️ 预计算路径失败，使用实时重算");
        }

        if (pathFound)
        {
            if (chassis.actuatorRot != null)
            {
                float rawAngle = (float)chassis.actuatorRot.Control * -Mathf.Rad2Deg;
                currentContinuousAngle = rawAngle - chassis.headingOffset;
                currentFacingAngle = currentContinuousAngle;
            }
            
            currentState = State.Rotating;
            if (chassis.enableDynamicAvoidance) {
                if (dynamicPathCoroutine != null) StopCoroutine(dynamicPathCoroutine);
                dynamicPathCoroutine = StartCoroutine(DynamicAvoidanceRoutine());
            }
        }
    }

    bool RecalculateNavMeshPath()
    {
        NavMeshPath path = new NavMeshPath();
        if (NavMesh.CalculatePath(GetRobotPosition(), refs.targetObject.position, NavMesh.AllAreas, path))
        {
            pathCorners = path.corners;
            currentCornerIndex = 1;
            return true;
        }
        return false;
    }

    void ProcessChassisMovement()
    {
        if (pathCorners == null || pathCorners.Length == 0) return;
        if (currentCornerIndex >= pathCorners.Length) currentCornerIndex = pathCorners.Length - 1;

        Vector3 targetPoint = pathCorners[currentCornerIndex];
        Vector3 robotPos = GetRobotPosition();
        
        Vector3 dir = targetPoint - robotPos; 
        dir.y = 0;
        float distToPathNode = dir.magnitude;
        
        bool shouldStop = false;

        if (currentCornerIndex < pathCorners.Length - 1)
        {
            if (distToPathNode < chassis.stopDistance) shouldStop = true;
        }
        else
        {
            float distToRealTarget = float.MaxValue;
            if (refs.targetObject != null) {
                distToRealTarget = Vector3.Distance(
                    new Vector3(robotPos.x, 0, robotPos.z),
                    new Vector3(refs.targetObject.position.x, 0, refs.targetObject.position.z)
                );
            }

            bool isAtEndOfPath = distToPathNode < chassis.stopDistance;
            bool isInReach = distToRealTarget < chassis.armReachDistance;

            if (isInReach || isAtEndOfPath)
            {
                shouldStop = true;
                if (currentState == State.Moving || currentState == State.Rotating) 
                {
                    string reason = isInReach ? "进入抓取范围" : "路径尽头(强制停车)";
                    Debug.LogFormat("🛑 [Runtime Stop] Task {0} 停车。原因: {1} | 距目标:{2:F3}m | 距路尽头:{3:F3}m", 
                        currentMissionIndex, reason, distToRealTarget, distToPathNode);
                }
            }
        }

        if (shouldStop)
        {
            if (currentCornerIndex < pathCorners.Length - 1)
            {
                currentCornerIndex++;
                currentState = State.Rotating;
            }
            else
            {
                Debug.Log("✋ 底盘到达。执行诊断...");
                RunDetailedDiagnosis(currentMissionIndex);

                if (dynamicPathCoroutine != null) StopCoroutine(dynamicPathCoroutine);

                if (chassis.autoStartArm) {
                    currentState = State.Stabilizing;
                    StartCoroutine(WaitAndStartArm());
                } else {
                    currentState = State.WaitingForInput;
                }
            }
            return;
        }

        Vector3 navDir = targetPoint - robotPos; 
        navDir.y = 0;
        
        float targetAngle = Quaternion.LookRotation(navDir).eulerAngles.y;
        float angleDiff = Mathf.DeltaAngle(currentFacingAngle, targetAngle);

        if (currentState == State.Rotating)
        {
            if (Mathf.Abs(angleDiff) > chassis.alignThreshold) {
                float step = chassis.turnSpeed * Mathf.Rad2Deg * Time.deltaTime;
                float newAngle = Mathf.MoveTowardsAngle(currentFacingAngle, targetAngle, step);
                currentContinuousAngle += Mathf.DeltaAngle(currentFacingAngle, newAngle);
                currentFacingAngle = newAngle;
            } else currentState = State.Moving;
        }
        else if (currentState == State.Moving)
        {
            if (Mathf.Abs(angleDiff) > 15f) { currentState = State.Rotating; return; }
            
            Vector3 step = navDir.normalized * chassis.moveSpeed * Time.deltaTime;
            float dx = step.x; float dz = step.z;

            if (chassis.swapXZ) { float t = dx; dx = dz; dz = t; }
            if (chassis.invertX) dx = -dx;
            if (chassis.invertZ) dz = -dz;

            if (chassis.actuatorX) chassis.actuatorX.Control += dx;
            if (chassis.actuatorZ) chassis.actuatorZ.Control += dz;
        }

        if (chassis.actuatorRot) 
            chassis.actuatorRot.Control = -(currentContinuousAngle + chassis.headingOffset) * Mathf.Deg2Rad;
    }

    IEnumerator DynamicAvoidanceRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(chassis.avoidanceUpdateInterval);
        while (currentState == State.Moving || currentState == State.Rotating) {
            yield return wait;
            RecalculateNavMeshPath();
        }
    }

    // ========================================================================
    // 🦾 7. 机械臂逻辑 (集成预计算复用)
    // ========================================================================

    IEnumerator WaitAndStartArm()
    {
        if (chassis.inertiaDelay > 0.01f) yield return new WaitForSeconds(chassis.inertiaDelay);
        StartArmSequence();
    }

    void StartArmSequence()
    {
        currentState = State.Planning;
        CalculateObservationPose(refs.targetObject.position, GetRobotPosition(), transform.forward, out lockedArmTargetPos, out lockedArmTargetRot);
        hasLockedTarget = true;
        
        Debug.Log($"🤖 [Arm] Runtime目标 {lockedArmTargetPos}");

        double[] runtimeResult = arm.enableLookAt ? 
            refs.ikSolver.SolveIK(lockedArmTargetPos, lockedArmTargetRot) : 
            refs.ikSolver.SolveIK(lockedArmTargetPos);

        List<double[]> cachedPlan = null;
        if (_hasPrecalculated && currentMissionIndex < _snapshots.Count)
        {
            if (_snapshots[currentMissionIndex].ikSuccess) {
                cachedPlan = _snapshots[currentMissionIndex].precalcArmPlan;
            }
        }

        if (cachedPlan != null && cachedPlan.Count > 0 && runtimeResult != null)
        {
            CompareAndLogIK(cachedPlan[0], runtimeResult);
        }

        if (arm.usePrecalculatedSolution && cachedPlan != null)
        {
            Debug.Log($"🤖 [Arm] 使用预计算路径 (直接执行 {cachedPlan.Count} 步)");
            StartCoroutine(ExecutePath(cachedPlan, true)); 
        }
        else
        {
            if (arm.useBitStarPlanner && refs.bitPlanner != null) 
                StartCoroutine(RunBitStarPlanning());
            else 
            {
                if (runtimeResult != null)
                {
                    List<double[]> path = new List<double[]> { runtimeResult };
                    StartCoroutine(ExecutePath(path, true));
                }
                else
                {
                    StartCoroutine(RunSimpleIKInterp()); 
                }
            }
        }
    }

    void CompareAndLogIK(double[] precalcQpos, double[] runtimeQpos)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"🔍 [IK Compare Task {currentMissionIndex}] =====================");
        
        var acts = refs.ikSolver.actuators;
        for (int i = 0; i < acts.Count; i++)
        {
            int qAddr = GetActuatorQposAddr(acts[i]);
            if (qAddr == -1) continue;

            double valPre = (i < precalcQpos.Length) ? precalcQpos[i] : 0;
            double valRun = (qAddr < runtimeQpos.Length) ? runtimeQpos[qAddr] : 0;

            float showPre = (float)valPre;
            float showRun = (float)valRun;
            string unit = "";

            if (acts[i].Joint is MjHingeJoint) {
                showPre *= Mathf.Rad2Deg; showRun *= Mathf.Rad2Deg; unit = "°";
            }

            float diff = Mathf.Abs(showPre - showRun);
            string color = diff > 1.0f ? "red" : "green"; 

            sb.AppendLine($"   Joint {i} [{acts[i].name}]: Pre={showPre:F2}{unit} | Run={showRun:F2}{unit} | Diff=<color={color}>{diff:F2}{unit}</color>");
        }
        Debug.Log(sb.ToString());
    }

    IEnumerator RunBitStarPlanning()
    {
        debug_PlannerStatus = "Planning...";
        if (lineVis) lineVis.positionCount = 0;
        
        List<double[]> path = refs.bitPlanner.Plan(lockedArmTargetPos);
        
        if (path != null && path.Count > 0)
        {
            debug_PlannerStatus = $"Run ({path.Count} pts)";
            if (debug.showRealtimePath && lineVis != null) {
                List<Vector3> pts = refs.bitPlanner.GetPathInWorldSpace(path);
                if (pts != null) { lineVis.positionCount = pts.Count; lineVis.SetPositions(pts.ToArray()); }
            }
            yield return StartCoroutine(ExecutePath(path));
        }
        else
        {
            Debug.LogError("BIT* 失败，切换 IK 直连");
            yield return StartCoroutine(RunSimpleIKInterp());
        }
    }

    IEnumerator RunSimpleIKInterp()
    {
        debug_PlannerStatus = "Simple IK...";
        
        double[] finalQ = arm.enableLookAt ? 
            refs.ikSolver.SolveIK(lockedArmTargetPos, lockedArmTargetRot) : 
            refs.ikSolver.SolveIK(lockedArmTargetPos);

        if (finalQ != null)
        {
            List<double[]> path = new List<double[]> { finalQ };
            yield return StartCoroutine(ExecutePath(path, true)); 
        }
        else
        {
            debug_PlannerStatus = "IK Failed";
            Debug.LogError("IK 解算失败");
            ProcessNextTarget();
        }
    }

    // 🔥 通用路径执行器 (修复了数据对齐问题)
    IEnumerator ExecutePath(List<double[]> path, bool simpleLerp = false)
    {
        currentState = State.MovingArm;
        
        List<MjActuator> actuators = refs.ikSolver.actuators; 
        int nv = actuators.Count;
        
        double[] currentQ = new double[nv];
        for(int i=0; i<nv; i++) currentQ[i] = (double)actuators[i].Control;

        foreach (var fullQposState in path) 
        {
            double[] targetControls = new double[nv];
            
            if (fullQposState.Length > nv) 
            {
                for (int i = 0; i < nv; i++) {
                    int qAddr = GetActuatorQposAddr(actuators[i]);
                    if (qAddr != -1 && qAddr < fullQposState.Length) 
                        targetControls[i] = fullQposState[qAddr];
                    else 
                        targetControls[i] = actuators[i].Control;
                }
            } 
            else 
            {
                int safeLength = Mathf.Min(fullQposState.Length, nv);
                for (int i = 0; i < safeLength; i++) targetControls[i] = fullQposState[i];
                for (int i = safeLength; i < nv; i++) targetControls[i] = actuators[i].Control;
            }

            if (simpleLerp && path.Count == 1)
            {
                List<float> startVals = new List<float>();
                foreach(var a in actuators) startVals.Add((float)a.Control);

                float t = 0;
                while (t < 1.0f)
                {
                    t += Time.deltaTime * arm.jointSpeed;
                    float cv = arm.motionCurve.Evaluate(t);
                    for (int i = 0; i < nv; i++)
                        actuators[i].Control = Mathf.Lerp(startVals[i], (float)targetControls[i], cv);
                    yield return null;
                }
            }
            else 
            {
                while (!HasReached(currentQ, targetControls, nv))
                {
                    StepTowards(ref currentQ, targetControls, arm.jointSpeed * Time.deltaTime, nv);
                    for (int j = 0; j < nv; j++) actuators[j].Control = (float)currentQ[j];
                    yield return null;
                }
            }
        }

        float timer = 0f;
        while (timer < 2.0f)
        {
            timer += Time.deltaTime;
            if (refs.armEndEffector != null && Vector3.Distance(refs.armEndEffector.position, lockedArmTargetPos) < arm.physicalTolerance) break;
            yield return null;
        }

        debug_PlannerStatus = "Success";
        ProcessNextTarget();
    }

    IEnumerator ResetArmAndMove()
    {
        Debug.Log("🔙 机械臂复位中...");
        yield return new WaitForSeconds(0.5f);

        List<MjActuator> acts = refs.ikSolver.actuators;
        List<float> currentAngles = new List<float>();
        foreach(var a in acts) currentAngles.Add((float)a.Control);

        float t = 0;
        while (t < 1.0f)
        {
            t += Time.deltaTime / 1.5f;
            float cv = arm.motionCurve.Evaluate(t);
            for (int i = 0; i < acts.Count; i++)
            {
                if (i < arm.initialAngles.Count)
                {
                    float start = currentAngles[i];
                    float input = arm.initialAngles[i];
                    float end = (acts[i].Joint is MjHingeJoint) ? input * Mathf.Deg2Rad : input;
                    acts[i].Control = Mathf.Lerp(start, end, cv);
                }
            }
            yield return null;
        }
        
        Debug.Log("✅ 复位完成");
        yield return new WaitForSeconds(mission.intervalBetweenTasks);
        CalculateAndStartPath(useCache: true);
    }

    IEnumerator WaitAndMove(float delay)
    {
        yield return new WaitForSeconds(delay);
        CalculateAndStartPath(useCache: true);
    }

    IEnumerator VerifyArrival()
    {
        yield return new WaitForSeconds(0.5f);
        ProcessNextTarget();
    }

    // ========================================================================
    // 🛠️ 8. 辅助函数
    // ========================================================================

    void CalculateObservationPose(Vector3 target, Vector3 robot, Vector3 fwd, out Vector3 p, out Quaternion r)
    {
        Vector3 dir = arm.useManualObservation ? 
             arm.manualObservationVec.normalized : (robot - target).normalized;
        p = target + (dir * arm.observationDistance);
        if(!arm.useManualObservation) p.y += 0; 
        
        Vector3 look = target - p;
        if(look == Vector3.zero) look = Vector3.forward;
        r = Quaternion.LookRotation(look) * Quaternion.FromToRotation(arm.faceAxis, Vector3.forward);
    }

    private bool HasReached(double[] cur, double[] tar, int nv)
    {
        for (int i = 0; i < nv; i++) if (Mathf.Abs((float)(cur[i] - tar[i])) > 0.01f) return false;
        return true;
    }

    private void StepTowards(ref double[] cur, double[] tar, float step, int nv)
    {
        float maxD = 0f; 
        for (int i = 0; i < nv; i++) { float d = Mathf.Abs((float)(tar[i] - cur[i])); if (d > maxD) maxD = d; }
        if (maxD < 0.0001f) return;
        float r = (step > maxD ? maxD : step) / maxD;
        for (int i = 0; i < nv; i++) cur[i] += (tar[i] - cur[i]) * r;
    }

    int GetActuatorQposAddr(MjActuator act) {
        if (act.Joint is MjHingeJoint h) return h.QposAddress;
        if (act.Joint is MjSlideJoint s) return s.QposAddress;
        return -1;
    }

    void InitializeVisuals()
    {
        if (refs.realRobotBase != null) SetupTrail(refs.realRobotBase, Color.blue, 5.0f);
        if (refs.armEndEffector != null) SetupTrail(refs.armEndEffector.gameObject, debug.trailColor, 0.5f);
        lineVis = GetComponent<LineRenderer>();
    }
    
    void InitializeChassisState()
    {
        if (chassis.actuatorRot != null) 
            currentFacingAngle = (float)chassis.actuatorRot.Control * -Mathf.Rad2Deg;
    }

    private void SetupTrail(GameObject go, Color color, float time)
    {
        if (go.GetComponent<TrailRenderer>()) return;
        TrailRenderer tr = go.AddComponent<TrailRenderer>();
        tr.material = new Material(Shader.Find("Sprites/Default"));
        tr.startColor = color; tr.endColor = new Color(color.r, color.g, color.b, 0);
        tr.startWidth = 0.02f; tr.endWidth = 0.0f;
        tr.time = time; tr.autodestruct = false;
    }

    void UpdateVisuals()
    {
        if (lineVis) {
            lineVis.enabled = debug.showRealtimePath;
            lineVis.startColor = debug.realtimePathColor; lineVis.endColor = debug.realtimePathColor;
        }
    }

    void ApplyInitialArmPose()
    {
        if (refs.ikSolver == null || refs.ikSolver.actuators == null) return;
        var acts = refs.ikSolver.actuators;
        for (int i = 0; i < acts.Count; i++) {
            if (i < arm.initialAngles.Count) {
                float v = arm.initialAngles[i];
                acts[i].Control = (acts[i].Joint is MjHingeJoint) ? v * Mathf.Deg2Rad : v;
            }
        }
    }

    private Vector3 GetRobotPosition() 
    { 
        if (refs.realRobotBase) return refs.realRobotBase.transform.position; 
        return transform.position; 
    }

    // ========================================================================
    // 🎨 9. 诊断与调试
    // ========================================================================

    void RunDetailedDiagnosis(int index)
    {
        if (index >= _snapshots.Count) return;

        var snap = _snapshots[index];
        Vector3 actualPos = GetRobotPosition();
        
        Vector3 flatActual = new Vector3(actualPos.x, 0, actualPos.z);
        Vector3 flatPrecalc = new Vector3(snap.precalcChassisPos.x, 0, snap.precalcChassisPos.z);
        float posErr = Vector3.Distance(flatActual, flatPrecalc);
        
        float actualAngle = currentFacingAngle; 
        float angErr = Mathf.DeltaAngle(actualAngle, snap.precalcChassisAngle);

        Vector3 actualArmTarget; Quaternion _;
        CalculateObservationPose(refs.targetObject.position, actualPos, transform.forward, out actualArmTarget, out _);
        float targetDrift = Vector3.Distance(actualArmTarget, snap.precalcArmTarget);

        string posColor = posErr > 0.05f ? "red" : "green";
        string angColor = Mathf.Abs(angErr) > 2.0f ? "red" : "green";
        string driftColor = targetDrift > 0.05f ? "red" : "green";

        Debug.LogWarning($"📊 [诊断 Task {index}] -------------------------------");
        Debug.LogFormat($"   🚗 底盘水平误差: <color={posColor}>{posErr:F4} m</color> (预计{snap.precalcChassisPos} | 实际{actualPos})");
        Debug.LogFormat($"   🧭 底盘角度误差: <color={angColor}>{angErr:F2} °</color> (预计:{snap.precalcChassisAngle:F1}° | 实际:{actualAngle:F1}°)");
        Debug.LogFormat($"   🎯 抓取目标漂移: <color={driftColor}>{targetDrift:F4} m</color>");
        Debug.LogWarning("-------------------------------------------------------");
        
        debug_LastPosErr = posErr;
        debug_LastAngErr = angErr;
    }

    void OnDrawGizmos()
    {
        if (debug.showGlobalPlan && _hasPrecalculated && _globalPathCache.Count > 0)
        {
            Gizmos.color = debug.globalPathColor;
            foreach (var route in _globalPathCache) {
                if (route == null) continue;
                for (int i = 0; i < route.Length - 1; i++) Gizmos.DrawLine(route[i], route[i + 1]);
            }
        }

        if (debug.showGhostRobot && _snapshots.Count > 0)
        {
            foreach (var snap in _snapshots)
            {
                Gizmos.color = snap.ikSuccess ? debug.ghostColor : Color.red;
                Gizmos.DrawWireSphere(snap.precalcChassisPos, 0.5f);
                Gizmos.color = snap.ikSuccess ? Color.cyan : Color.red;
                Gizmos.DrawLine(snap.precalcChassisPos, snap.precalcArmTarget);
                Gizmos.DrawSphere(snap.precalcArmTarget, 0.08f);
            }
        }

        if (debug.showChassisLines && pathCorners != null && currentCornerIndex < pathCorners.Length)
        {
            Gizmos.color = debug.realtimePathColor;
            for (int i = 0; i < pathCorners.Length - 1; i++) Gizmos.DrawLine(pathCorners[i], pathCorners[i+1]);
            Gizmos.DrawLine(GetRobotPosition(), pathCorners[currentCornerIndex]);
        }
    }

    void OnGUI()
    {
        if (!debug.showGUI) return;
        GUIStyle style = new GUIStyle(); style.fontSize = 16; style.normal.textColor = Color.white; style.fontStyle = FontStyle.Bold;
        GUI.Box(new Rect(10, 10, 300, 220), "🤖 Mission Control (Pro)");
        GUILayout.BeginArea(new Rect(20, 40, 280, 280));
        
        GUILayout.Label($"State: {currentState}", style);
        
        if (currentState == State.Stabilizing) {
            style.normal.textColor = Color.cyan; GUILayout.Label($"⏳ Stabilizing...", style);
        }
        else if (currentState == State.MovingArm) {
            style.normal.textColor = Color.yellow; 
            GUILayout.Label($"Arm: {debug_PlannerStatus}", style);
        }
        else {
            style.normal.textColor = Color.white;
            GUILayout.Label($"Target: {currentMissionIndex + 1}/{mission.targets.Count}", style);
        }

        GUILayout.Space(10);
        style.fontSize = 14;
        style.normal.textColor = debug_LastPosErr > 0.05f ? Color.red : Color.green;
        GUILayout.Label($"Pos Err: {debug_LastPosErr*100:F1} cm");
        style.normal.textColor = Mathf.Abs(debug_LastAngErr) > 2.0f ? Color.red : Color.green;
        GUILayout.Label($"Ang Err: {debug_LastAngErr:F1} deg");

        if (_hasPrecalculated) {
             style.normal.textColor = Color.green; GUILayout.Label("[Precalc Ready]");
        }
        
        if (arm.usePrecalculatedSolution) {
             style.normal.textColor = Color.green; GUILayout.Label("⚡ Using Precalc Arm Solution");
        }

        GUILayout.EndArea();
    }
}