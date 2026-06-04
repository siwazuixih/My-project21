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
    [HideInInspector] public int currentMissionIndex = 0;
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
            
            if (success) Debug.Log($"🚀 [预计算] 成功计算 {mission.targets.Count} 个任务！IK全部通过。");
            else Debug.LogError("⚠️ [预计算] 存在严重路径或IK问题！");

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
            float distToTarget = Vector3.Distance(new Vector3(simPos.x, 0, simPos.z), new Vector3(dest.position.x, 0, dest.position.z));
            Vector3 finalStopPoint = simPos;
            Vector3 finalFacingDir = simFwd;
            NavMeshPath tempPath = new NavMeshPath();
            Vector3[] chassisPathForExecution = null;
            bool hasNavigationPath = false;
            
            if (distToTarget < chassis.armReachDistance) {
                chassisPathForExecution = new Vector3[]{ simPos };
            } else {
                NavMesh.CalculatePath(simPos, dest.position, NavMesh.AllAreas, tempPath);

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
                            finalFacingDir = segmentDir; foundStop = true; break;
                        }
                    }
                }

                if (tempPath.corners != null && tempPath.corners.Length > 0) {
                    chassisPathForExecution = new Vector3[tempPath.corners.Length];
                    System.Array.Copy(tempPath.corners, chassisPathForExecution, tempPath.corners.Length);
                    chassisPathForExecution[chassisPathForExecution.Length - 1] = finalStopPoint;
                    hasNavigationPath = chassisPathForExecution.Length > 1;
                }
            }

            if (chassisPathForExecution == null || chassisPathForExecution.Length == 0)
                chassisPathForExecution = new Vector3[]{ simPos };
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

            if (armSolution == null) allSuccess = false;
            simPos = finalStopPoint; simFwd = finalFacingDir;
        }

        for(int i = 0; i < nq; i++) MjScene.Instance.Data->qpos[i] = backupQpos[i];
        MujocoLib.mj_forward(MjScene.Instance.Model, MjScene.Instance.Data);

        hasPrecalculated = true;
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
}
