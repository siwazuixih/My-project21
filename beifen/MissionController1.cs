using UnityEngine;
using UnityEngine.AI;
using Mujoco;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(LineRenderer))]
public class MissionController : MonoBehaviour
{
    // ========================================================================
    // 1. Inspector 配置
    // ========================================================================
    [System.Serializable]
    public class CoreReferences
    {
        public Transform targetObject;          
        public MujocoStaticIKSolver ikSolver;   
        public BITStarPlanner bitPlanner;       
        public GameObject realRobotBase;        
        public Transform armEndEffector;        
    }

    [System.Serializable]
    public class ChassisSettings
    {
        [Header("执行器 & 关节 (直接复用)")]
        public MjActuator actuatorX;
        public MjActuator actuatorZ;
        public MjActuator actuatorRot;

        [Header("运动参数")]
        public float moveSpeed = 0.5f;
        public float turnSpeed = 2.0f;
        public float alignThreshold = 5.0f;
        public float stopDistance = 0.05f;
        public float armReachDistance = 0.9f; 
        public bool autoStartArm = true;
        public float inertiaDelay = 0.5f;

        [Header("方向校准")]
        public bool invertX = false;
        public bool invertZ = false;
        public bool swapXZ = false;
        [Range(-180, 180)] public float headingOffset = -90.0f;

        [Header("动态避障")]
        public bool enableDynamicAvoidance = true;
        public float avoidanceUpdateInterval = 0.5f;
    }

    [System.Serializable]
    public class ArmSettings
    {
        public List<float> initialAngles = new List<float>();
        public bool useBitStarPlanner = true;
        public float jointSpeed = 0.8f;
        public AnimationCurve motionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public float physicalTolerance = 0.05f;
        
        [Header("Eye-in-Hand")]
        public bool enableLookAt = true;
        public float observationDistance = 0.25f;
        public Vector3 faceAxis = new Vector3(0, 0, 1);
        public bool useManualObservation = true;
        public Vector3 manualObservationVec = new Vector3(-1, 0, 0);
    }

    [System.Serializable]
    public class MissionSettings
    {
        public List<Transform> targets = new List<Transform>();
        public bool loopMission = false;
        public bool resetArmBeforeMoving = true;
        public float intervalBetweenTasks = 1.0f;
    }

    [System.Serializable]
    public class DebugSettings
    {
        public bool showGUI = true;
        public bool showGizmos = true;
        public bool showRealtimePath = true;
        public Color globalPathColor = Color.white;
        public Color armPlanColor = Color.magenta;
        public Color realtimeColor = Color.green;
        public Color ghostColor = new Color(1, 1, 0, 0.5f);
    }

    // 🕵️ 诊断快照：记录预计算时的所有参数
    struct DiagnosisSnapshot
    {
        public int taskId;
        public Vector3 precalcChassisPos; // 预计算底盘位置 (World)
        public float precalcChassisAngle; // 预计算底盘角度
        public Vector3 precalcArmTarget;  // 预计算抓取目标
        public bool precalcSuccess;       // 预计算是否成功
    }

    // ========================================================================
    // 变量声明
    // ========================================================================
    public CoreReferences refs;
    public ChassisSettings chassis;
    public ArmSettings arm;
    public MissionSettings mission;
    public DebugSettings debug;

    private List<Vector3[]> _chassisPaths = new List<Vector3[]>();
    private List<List<double[]>> _armPlans = new List<List<double[]>>();
    private List<List<Vector3>> _armVis = new List<List<Vector3>>();
    private List<DiagnosisSnapshot> _snapshots = new List<DiagnosisSnapshot>(); 
    private bool _precalcDone = false;

    private enum State { Idle, Rotating, Moving, Stabilizing, Planning, MovingArm, ResettingArm, Finished }
    private State currentState = State.Idle;
    private int currentMissionIndex = 0;
    
    private Vector3[] currentPath;
    private int pathIndex = 0;
    private float currentFacingAngle = 0f;
    
    private LineRenderer lineVis;
    private string debug_Status = "Ready";
    private float debug_RawDx, debug_RawDz;
    private float debug_FinalInputX, debug_FinalInputZ;

    // ========================================================================
    // 🔄 生命周期
    // ========================================================================
    void Start()
    {
        lineVis = GetComponent<LineRenderer>();
        lineVis.startWidth = 0.02f;
        
        // 读取初始角度
        if (chassis.actuatorRot != null && chassis.actuatorRot.Joint is MjHingeJoint h) 
            currentFacingAngle = (float)h.Configuration * Mathf.Rad2Deg;
            
        ApplyInitialArmPose();
    }

    void Update()
    {
        if (currentState == State.Idle && Input.GetKeyDown(KeyCode.Space))
        {
            StartCoroutine(MissionRoutine());
        }

        // 仅在移动状态下调用 PID 跟踪
        if (currentState == State.Rotating || currentState == State.Moving)
        {
            TrackPath();
        }
        
        if (lineVis && debug.showRealtimePath)
        {
            lineVis.enabled = (currentState == State.MovingArm);
        }
    }

    // ========================================================================
    // 🚀 主任务协程
    // ========================================================================
    // ========================================================================
    // 🚀 主任务协程 (修复版：解决双重控制冲突)
    // ========================================================================
    IEnumerator MissionRoutine()
    {
        // 1. 预计算
        if (!_precalcDone)
        {
            debug_Status = "Pre-calculating...";
            bool success = PrecomputeAll();
            debug_Status = "Pre-calc Done";
            yield return new WaitForSeconds(1.0f);
        }

        // 🔥 关键修复：暂时关闭底盘的“自动动臂”功能
        // 防止 ProcessChassisMovement 自己启动机械臂协程，导致和这里的逻辑打架
        bool originalAutoStart = chassis.autoStartArm;
        chassis.autoStartArm = false; 

        currentMissionIndex = 0;
        while (true)
        {
            if (currentMissionIndex >= mission.targets.Count)
            {
                if (mission.loopMission) currentMissionIndex = 0;
                else break;
            }

            refs.targetObject = mission.targets[currentMissionIndex];
            Debug.Log($"🏁 [Task {currentMissionIndex}] 前往: {refs.targetObject.name}");

            // A. 底盘移动
            if (currentMissionIndex < _chassisPaths.Count) {
                currentPath = _chassisPaths[currentMissionIndex];
                pathIndex = 1; 
                currentState = State.Rotating;
            }
            
            // 等待底盘到达 (因为 autoStartArm=false，底盘到底后会切到 WaitingForInput)
            while (currentState == State.Rotating || currentState == State.Moving) yield return null;

            // B. 稳定 (手动接管稳定过程)
            if (chassis.inertiaDelay > 0) {
                currentState = State.Stabilizing;
                yield return new WaitForSeconds(chassis.inertiaDelay);
            }

            // C. 诊断
            if (currentMissionIndex < _snapshots.Count)
            {
                CompareAndReport(_snapshots[currentMissionIndex]);
            }

            // D. 机械臂规划与执行
            currentState = State.Planning;
            
            Vector3 realTimeArmTarget; Quaternion realTimeArmRot;
            CalculateObservationPose(refs.targetObject.position, GetRobotPosition(), out realTimeArmTarget, out realTimeArmRot);

            // 实时规划
            List<double[]> planToRun = null;
            if (arm.useBitStarPlanner && refs.bitPlanner != null)
            {
                planToRun = refs.bitPlanner.Plan(realTimeArmTarget);
            }
            else
            {
                double[] ik = arm.enableLookAt ? 
                    refs.ikSolver.SolveIK(realTimeArmTarget, realTimeArmRot) : 
                    refs.ikSolver.SolveIK(realTimeArmTarget);
                if (ik != null) planToRun = new List<double[]> { ik };
            }

            // 执行
            currentState = State.MovingArm;
            if (planToRun != null && planToRun.Count > 0)
            {
                // 可视化
                if (refs.bitPlanner != null && lineVis != null) {
                    var pts = refs.bitPlanner.GetPathInWorldSpace(planToRun);
                    if(pts != null) { lineVis.positionCount = pts.Count; lineVis.SetPositions(pts.ToArray()); }
                }
                yield return StartCoroutine(ExecuteArmPlan(planToRun));
            }
            else
            {
                Debug.LogError($"❌ [运行时] IK 失败！无法到达: {realTimeArmTarget}");
            }

            // E. 复位
            if (mission.resetArmBeforeMoving)
            {
                currentState = State.ResettingArm;
                yield return StartCoroutine(ResetArmRoutine());
            }
            
            yield return new WaitForSeconds(mission.intervalBetweenTasks);
            currentMissionIndex++;
        }

        // 任务结束，恢复配置
        chassis.autoStartArm = originalAutoStart;
        currentState = State.Finished;
        Debug.Log("🎉 任务完成");
    }    
    // ========================================================================
    // 📊 诊断核心代码
    // ========================================================================
    void CompareAndReport(DiagnosisSnapshot snap)
    {
        Vector3 actualPos = GetRobotPosition();
        float actualAngle = currentFacingAngle;
        
        Vector3 actualTarget; Quaternion _;
        CalculateObservationPose(refs.targetObject.position, actualPos, out actualTarget, out _);

        float posErr = Vector3.Distance(snap.precalcChassisPos, actualPos);
        float angleErr = Mathf.DeltaAngle(snap.precalcChassisAngle, actualAngle);
        float targetOffset = Vector3.Distance(snap.precalcArmTarget, actualTarget);

        // 打印详细对比日志
        Debug.LogWarning($"📊 [DIAGNOSIS Task {snap.taskId}] ---------------------------");
        
        // 1. 底盘位置
        string pColor = posErr > 0.1f ? "red" : "green";
        Debug.LogFormat("<color={0}>1. 底盘位置误差: {1:F3}m</color> (预计算:{2} | 实际:{3})", 
            pColor, posErr, snap.precalcChassisPos, actualPos);

        // 2. 底盘角度
        string aColor = Mathf.Abs(angleErr) > 2.0f ? "red" : "green";
        Debug.LogFormat("<color={0}>2. 底盘角度误差: {1:F1}°</color> (预计算:{2:F1}° | 实际:{3:F1}°)", 
            aColor, angleErr, snap.precalcChassisAngle, actualAngle);

        // 3. 目标点漂移
        // 如果底盘位置导致目标点变了，这就是为什么IK会抓空
        string tColor = targetOffset > 0.1f ? "red" : "green";
        Debug.LogFormat("<color={0}>3. 抓取目标偏移: {1:F3}m</color> (预计算:{2} | 实际:{3})", 
            tColor, targetOffset, snap.precalcArmTarget, actualTarget);
            
        Debug.LogWarning("-------------------------------------------------------");
    }

    // ========================================================================
    // 🧠 预计算 (只负责算，不负责动)
    // ========================================================================
    unsafe bool PrecomputeAll()
    {
        if (mission.targets.Count == 0) return false;
        
        _chassisPaths.Clear(); _armPlans.Clear(); _armVis.Clear(); _snapshots.Clear(); 

        int nq = MjScene.Instance.Model->nq;
        double[] backupQpos = new double[nq];
        for(int i=0; i<nq; i++) backupQpos[i] = MjScene.Instance.Data->qpos[i];

        Vector3 startPos = GetRobotPosition();

        for (int i = 0; i < mission.targets.Count; i++)
        {
            Transform dest = mission.targets[i];
            
            // 1. 算路
            NavMeshPath path = new NavMeshPath();
            NavMeshHit hit;
            if (NavMesh.SamplePosition(startPos, out hit, 2.0f, NavMesh.AllAreas)) startPos = hit.position;
            NavMesh.CalculatePath(startPos, dest.position, NavMesh.AllAreas, path);
            _chassisPaths.Add(path.corners);

            // 2. 算停车点
            Vector3 finalPoint = path.corners[path.corners.Length - 1];
            Vector3 stopPoint = finalPoint;
            Vector3 forwardDir = Vector3.forward;

            if (path.corners.Length >= 2)
            {
                Vector3 prev = path.corners[path.corners.Length - 2];
                forwardDir = (finalPoint - prev).normalized;
                float dist = Vector3.Distance(finalPoint, prev);
                float backDist = Mathf.Min(chassis.armReachDistance, dist * 0.99f);
                stopPoint = finalPoint - (forwardDir * backDist);
            }
            startPos = stopPoint;

            // 3. 瞬移 (使用 Actuator 绑定的 Joint)
            TeleportWithActuators(stopPoint, forwardDir);
            
            float angle = 0;
            if (forwardDir != Vector3.zero) angle = Quaternion.LookRotation(forwardDir).eulerAngles.y;

            // 4. IK
            Vector3 targetPos; Quaternion targetRot;
            CalculateObservationPose(dest.position, stopPoint, out targetPos, out targetRot);

            // 记录快照
            DiagnosisSnapshot snap = new DiagnosisSnapshot {
                taskId = i,
                precalcChassisPos = stopPoint,
                precalcChassisAngle = angle,
                precalcArmTarget = targetPos,
                precalcSuccess = false
            };

            List<double[]> armPlan = null;
            if (arm.useBitStarPlanner && refs.bitPlanner != null)
                armPlan = refs.bitPlanner.Plan(targetPos);
            else {
                double[] ik = arm.enableLookAt ? 
                    refs.ikSolver.SolveIK(targetPos, targetRot) : refs.ikSolver.SolveIK(targetPos);
                if (ik != null) armPlan = new List<double[]> { ik };
            }

            if (armPlan != null && armPlan.Count > 0)
            {
                snap.precalcSuccess = true;
                _armPlans.Add(armPlan);
                if (refs.bitPlanner) _armVis.Add(refs.bitPlanner.GetPathInWorldSpace(armPlan));
                else _armVis.Add(null);
            }
            else
            {
                Debug.LogWarning($"[Precalc] Task {i} IK Failed");
                _armPlans.Add(null);
                _armVis.Add(null);
            }
            _snapshots.Add(snap);
        }

        // 恢复
        for(int i=0; i<nq; i++) MjScene.Instance.Data->qpos[i] = backupQpos[i];
        MujocoLib.mj_forward(MjScene.Instance.Model, MjScene.Instance.Data);

        _precalcDone = true;
        return true;
    }

    // ========================================================================
    // 🛠️ 辅助功能 (瞬移使用 Actuator 绑定的 Joint)
    // ========================================================================
    unsafe void TeleportWithActuators(Vector3 pos, Vector3 fwd)
    {
        Vector3 localPos = pos;
        if (refs.realRobotBase.transform.parent != null)
            localPos = refs.realRobotBase.transform.parent.InverseTransformPoint(pos);

        float angleRad = 0;
        if (fwd != Vector3.zero) {
            float unityY = Quaternion.LookRotation(fwd).eulerAngles.y;
            angleRad = -(unityY + chassis.headingOffset) * Mathf.Deg2Rad;
        }

        // 直接使用 chassis settings 里的执行器所绑定的关节
        if (chassis.actuatorX != null && chassis.actuatorX.Joint != null) {
            float val = chassis.invertX ? -localPos.x : localPos.x;
            if (chassis.swapXZ) val = chassis.invertZ ? -localPos.z : localPos.z;
            MjScene.Instance.Data->qpos[chassis.actuatorX.Joint.QposAddress] = val;
        }
        if (chassis.actuatorZ != null && chassis.actuatorZ.Joint != null) {
            float val = chassis.invertZ ? -localPos.z : localPos.z;
            if (chassis.swapXZ) val = chassis.invertX ? -localPos.x : localPos.x;
            MjScene.Instance.Data->qpos[chassis.actuatorZ.Joint.QposAddress] = val;
        }
        if (chassis.actuatorRot != null && chassis.actuatorRot.Joint != null) {
            MjScene.Instance.Data->qpos[chassis.actuatorRot.Joint.QposAddress] = angleRad;
        }
        MujocoLib.mj_forward(MjScene.Instance.Model, MjScene.Instance.Data);
    }

    void TrackPath()
    {
        if (currentPath == null || pathIndex >= currentPath.Length) return;
        Vector3 target = currentPath[pathIndex];
        Vector3 robot = GetRobotPosition();
        Vector3 dir = target - robot; dir.y = 0;
        float dist = dir.magnitude;

        float thresh = (pathIndex == currentPath.Length - 1) ? chassis.armReachDistance : chassis.stopDistance;
        if (dist < thresh) {
            if (pathIndex < currentPath.Length - 1) { pathIndex++; currentState = State.Rotating; }
            else { currentState = State.Idle; }
            return;
        }

        float targetAng = Quaternion.LookRotation(dir).eulerAngles.y;
        float diff = Mathf.DeltaAngle(currentFacingAngle, targetAng);

        if (currentState == State.Rotating) {
            if (Mathf.Abs(diff) > chassis.alignThreshold) {
                float step = chassis.turnSpeed * Mathf.Rad2Deg * Time.deltaTime;
                float newAng = Mathf.MoveTowardsAngle(currentFacingAngle, targetAng, step);
                currentFacingAngle = newAng;
            } else currentState = State.Moving;
        } else if (currentState == State.Moving) {
            if (Mathf.Abs(diff) > 10f) { currentState = State.Rotating; return; }
            Vector3 step = dir.normalized * chassis.moveSpeed * Time.deltaTime;
            debug_RawDx = step.x; debug_RawDz = step.z;
            float dx = step.x; float dz = step.z;
            
            if (chassis.actuatorX) chassis.actuatorX.Control += (chassis.swapXZ ? dz : dx) * (chassis.invertX ? -1 : 1);
            if (chassis.actuatorZ) chassis.actuatorZ.Control += (chassis.swapXZ ? dx : dz) * (chassis.invertZ ? -1 : 1);
        }

        if (chassis.actuatorRot) {
            float rad = -(currentFacingAngle + chassis.headingOffset) * Mathf.Deg2Rad;
            chassis.actuatorRot.Control = rad;
        }
    }

    IEnumerator ExecuteArmPlan(List<double[]> plan)
    {
        var acts = refs.ikSolver.actuators;
        int nv = acts.Count;
        
        // 1. 建立映射表：记录每个执行器对应的 qpos 索引
        int[] qposIndices = new int[nv];
        for (int i = 0; i < nv; i++)
        {
            if (acts[i].Joint is MjHingeJoint h) qposIndices[i] = h.QposAddress;
            else if (acts[i].Joint is MjSlideJoint s) qposIndices[i] = s.QposAddress;
            else qposIndices[i] = -1;
        }

        // 获取当前电机值
        double[] cur = new double[nv];
        for(int i=0; i<nv; i++) cur[i] = (double)acts[i].Control;

        foreach (var fullQposState in plan) // 遍历路径点（每个点都是完整的 qpos）
        {
            // 2. 从完整的 qpos 中提取出属于这几个电机的目标值
            double[] targetControls = new double[nv];
            for (int i = 0; i < nv; i++)
            {
                int addr = qposIndices[i];
                if (addr != -1 && addr < fullQposState.Length)
                    targetControls[i] = fullQposState[addr]; // ✅ 正确映射
                else
                    targetControls[i] = acts[i].Control; // 找不到就保持不动
            }

            // 3. 执行运动 (朝着提取出来的 targetControls 移动)
            while (!HasReached(cur, targetControls, nv)) 
            {
                for(int i=0; i<nv; i++) 
                {
                    float c = (float)cur[i]; 
                    float t = (float)targetControls[i];
                    float step = arm.jointSpeed * Time.deltaTime;
                    
                    if (acts[i].Joint is MjHingeJoint) {
                        float cD = c * Mathf.Rad2Deg; float tD = t * Mathf.Rad2Deg;
                        cur[i] = Mathf.MoveTowardsAngle(cD, tD, step * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    } else {
                        cur[i] = Mathf.MoveTowards(c, t, step);
                    }
                    acts[i].Control = (float)cur[i];
                }
                yield return null;
            }
        }
    }    
    
    private bool HasReached(double[] cur, double[] tar, int nv)
    {
        for (int i = 0; i < nv; i++) 
            if (Mathf.Abs((float)(cur[i] - tar[i])) > 0.01f) return false;
        return true;
    }
    
    
    IEnumerator ResetArmRoutine()
    {
        var acts = refs.ikSolver.actuators;
        List<float> starts = new List<float>();
        foreach(var a in acts) starts.Add((float)a.Control);
        float t = 0;
        while (t < 1.0f) {
            t += Time.deltaTime / 1.5f;
            float cv = arm.motionCurve.Evaluate(t);
            for (int i = 0; i < acts.Count; i++) {
                if (i < arm.initialAngles.Count) {
                    float target = arm.initialAngles[i];
                    if (acts[i].Joint is MjHingeJoint) target *= Mathf.Deg2Rad;
                    acts[i].Control = Mathf.Lerp(starts[i], target, cv);
                }
            }
            yield return null;
        }
    }

    void ApplyInitialArmPose()
    {
        var acts = refs.ikSolver.actuators;
        for (int i=0; i<acts.Count; i++) {
            if(i < arm.initialAngles.Count) {
                float v = arm.initialAngles[i];
                if(acts[i].Joint is MjHingeJoint) v *= Mathf.Deg2Rad;
                acts[i].Control = v;
            }
        }
    }

    void CalculateObservationPose(Vector3 target, Vector3 robot, out Vector3 pos, out Quaternion rot)
    {
        Vector3 dir = arm.useManualObservation ? 
            (arm.manualObservationVec == Vector3.zero ? Vector3.up : arm.manualObservationVec.normalized) : 
            (robot - target).normalized;
        pos = target + (dir * arm.observationDistance);
        if(!arm.useManualObservation) pos.y += 0;
        Vector3 look = target - pos;
        if(look == Vector3.zero) look = Vector3.forward;
        rot = Quaternion.LookRotation(look) * Quaternion.FromToRotation(arm.faceAxis, Vector3.forward);
    }

    Vector3 GetRobotPosition() { return refs.realRobotBase ? refs.realRobotBase.transform.position : transform.position; }
    bool Reached(double[] c, double[] t, int n) { for(int i=0; i<n; i++) if(Mathf.Abs((float)(c[i]-t[i])) > 0.01f) return false; return true; }
    IEnumerator WaitAndMove(float delay) { yield return new WaitForSeconds(delay); }
    IEnumerator WaitAndStartArm() { yield return new WaitForSeconds(chassis.inertiaDelay); StartArmSequence(); }
    void StartArmSequence() { currentState = State.Planning; StartCoroutine(ExecuteArmPlan(null)); } // Placeholder

    void OnDrawGizmos()
    {
        if(!debug.showGizmos) return;
        
        Gizmos.color = debug.globalPathColor;
        foreach(var p in _chassisPaths) {
            for(int i=0; i<p.Length-1; i++) Gizmos.DrawLine(p[i], p[i+1]);
        }

        foreach(var snap in _snapshots)
        {
            if (snap.precalcSuccess)
            {
                Gizmos.color = debug.ghostColor;
                Gizmos.DrawWireSphere(snap.precalcChassisPos, 0.5f);
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(snap.precalcChassisPos, snap.precalcArmTarget);
                Gizmos.DrawWireSphere(snap.precalcArmTarget, 0.1f);
            }
        }
        
        // 画实时目标
        if (refs.targetObject != null)
        {
            Vector3 rtTarget; Quaternion _;
            CalculateObservationPose(refs.targetObject.position, GetRobotPosition(), out rtTarget, out _);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(GetRobotPosition(), rtTarget);
            Gizmos.DrawWireSphere(rtTarget, 0.15f);
        }
    }

    void OnGUI()
    {
        if (!debug.showGUI) return;
        GUI.Box(new Rect(10, 10, 300, 150), "🤖 Diagnosis Mode");
        GUILayout.BeginArea(new Rect(20, 40, 280, 200));
        GUILayout.Label($"State: {currentState}");
        GUILayout.Label($"Debug: {debug_Status}");
        if(_precalcDone) GUILayout.Label("✅ Precalc Data Ready");
        GUILayout.EndArea();
    }
}