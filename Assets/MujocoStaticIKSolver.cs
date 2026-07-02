using System;
using System.Collections.Generic;
using UnityEngine;
using Mujoco;

/// <summary>
/// 基于加权阻尼最小二乘的 MuJoCo 逆运动学求解器。
/// 升降缸以高运动代价参与求解，机械臂本体无法满足任务时才优先使用。
/// </summary>
public class MujocoStaticIKSolver : MonoBehaviour
{
    // =================================================================================
    // 1. 组件绑定
    // =================================================================================
    [Header("绑定 (运行时可动态切换)")]
    [Tooltip("机械臂末端的控制点 (Site)，IK 会尝试让这个点重合到目标点")]
    public MjSite endEffectorSite;

    [Tooltip("参与 IK 解算的关节执行器列表 (⚠️必须确保列表第0个是底座旋转关节)")]
    public List<MjActuator> actuators;

    // =================================================================================
    // 2. 算法参数
    // =================================================================================
    [Header("IK 核心参数")]
    public int maxIterations = 500;   // 最大迭代次数
    public float stopThreshold = 0.005f; // 位置停止阈值 (5mm)
    [Tooltip("旋转停止阈值 (弧度)。0.05 ≈ 2.8度")]
    public float stopRotThreshold = 0.05f;
    public float stepSize = 0.5f;     // 步长

    [Header("阻尼最小二乘")]
    [Min(0.0001f), Tooltip("DLS 阻尼。越大越稳定，但靠近目标时会更保守。")]
    public float dlsDamping = 0.05f;
    [Min(0.001f), Tooltip("单次迭代允许的最大转动关节变化（弧度）。")]
    public float maxAngularStep = 0.12f;
    [Min(0.0001f), Tooltip("单次迭代允许的最大升降缸变化（米）。")]
    public float maxElevatorStep = 0.01f;
    [Min(1f), Tooltip("升降缸运动代价。越大越优先只用机械臂，必要时仍可升降。")]
    public float elevatorMotionPenalty = 20f;

    [Header("姿态控制")]
    [Tooltip("旋转误差的权重。0.1~0.5 比较合适。设为0则忽略姿态。")]
    public float rotWeight = 0.3f;

    [Header("防扭曲策略")]
    [Tooltip("偏置权重：让机械臂倾向于保持舒适姿态 (Rest Pose)。建议设为 0.1~0.2")]
    public float restPoseWeight = 0.1f;

    [Tooltip("升降缸稳定权重：越大越不爱动升降缸，强迫用机械臂去够。建议设为 1.0~2.0")]
    public float elevatorWeight = 1.5f; 
    
    [Tooltip("升降缸评分惩罚：在优选多解时，严厉惩罚升降缸偏离原点的解。")]
    public float elevatorPenalty = 5.0f; 

    // 旋转策略枚举
    public enum RotationMode
    {
        [Tooltip("就近旋转：忽略多圈限位，始终选择最近的角度 (±180度以内)，防止狂甩。")]
        ShortestPath,

        [Tooltip("严格限位：完全遵守 XML 里的限位范围 (-6.28~6.28)，可能会转圈。")]
        RespectLimits
    }

    [Header("稳定性策略")]
    [Tooltip("推荐选择【Shortest Path】以解决机械臂原地转圈的问题")]
    public RotationMode rotationOptimization = RotationMode.ShortestPath;

    [Tooltip("🔥 必须开启 True。通过多次随机尝试，筛选出姿态最自然的解。")]
    public bool selectBestCandidate = true;

    [Header("重试配置")]
    [Tooltip("重试次数。建议设为 10~20 次，给底座足够的概率随机到正确的朝向。")]
    public int maxRetries = 15;
    public bool checkCollision = false;
    [Min(0f), Tooltip("IK 端点允许的最大穿透深度（米），应与规划器保持一致。")]
    public float collisionPenetrationTolerance = 0.02f;
    [Min(0.1f), Tooltip("非底座转动关节随机种子的半径（弧度）。")]
    public float randomSeedRadius = 1.5f;
    [Min(0.01f), Tooltip("两个 IK 候选在周期关节归一化后的最小间距。")]
    public float candidateSeparation = 0.35f;

    [Header("结果验收")]
    [Tooltip("允许返回未达到严格收敛阈值、但误差仍在安全上限内的近似解。")]
    public bool allowApproximateSolution = true;
    [Min(0f), Tooltip("近似解允许的最大位置误差（米）。")]
    public float maxAcceptedPositionError = 0.01f;
    [Min(0f), Tooltip("近似解允许的最大姿态误差（弧度）。")]
    public float maxAcceptedRotationError = 0.03f;

    // 忽略碰撞的物体名字 (白名单)
    public List<string> ignoreGeomNames = new List<string>() { "floor" };

    [Header("调试")]
    public bool debugMode = true;

    // =================================================================================
    // 3. 内部缓存变量
    // =================================================================================
    private double[] jacp;
    private double[] jacr;
    private int cachedSiteId = -1;
    private string cachedSiteName = "";
    private HashSet<int> ignoredGeomIds = new HashSet<int>();
    private static readonly string[] DefaultIgnoredGeomTokens = { "floor", "ground", "plane", "wheel" };
    private double[] restQpos;

    // =================================================================================
    // 4. 初始化
    // =================================================================================
    unsafe void Start()
    {
        if (MjScene.Instance.Model != null)
        {
            int nq = MjScene.Instance.Model->nq;
            restQpos = new double[nq];
            for (int i = 0; i < nq; i++) restQpos[i] = MjScene.Instance.Data->qpos[i];
        }
    }

    // =================================================================================
    // 5. 对外接口
    // =================================================================================

    public unsafe double[] SolveIK(Vector3 targetPos)
    {
        return SolveIK(targetPos, null);
    }

    public unsafe double[] SolveIK(Vector3 targetPos, Quaternion? targetRot = null)
    {
        List<double[]> candidates = SolveIKCandidates(targetPos, targetRot, 1);
        return candidates.Count > 0 ? candidates[0] : null;
    }

    public unsafe List<double[]> SolveIKCandidates(Vector3 targetPos, Quaternion? targetRot, int maxCandidates)
    {
        List<double[]> result = new List<double[]>();
        if (MjScene.Instance.Model == null || MjScene.Instance.Data == null || maxCandidates <= 0) return result;
        if (endEffectorSite == null || actuators == null || actuators.Count == 0)
        {
            Debug.LogError("IK 求解失败：endEffectorSite 或 actuators 未绑定");
            return result;
        }
        for (int i = 0; i < actuators.Count; i++)
        {
            if (actuators[i]?.Joint != null) continue;
            Debug.LogError($"IK 求解失败：actuators[{i}] 或其 Joint 未绑定");
            return result;
        }

        RefreshCache();
        if (cachedSiteId < 0)
        {
            Debug.LogError($"IK 求解失败：MuJoCo 中找不到末端 Site '{endEffectorSite.name}'");
            return result;
        }
        int nq = MjScene.Instance.Model->nq;
        int nv = MjScene.Instance.Model->nv;
        MujocoStateSnapshot startState = CaptureState();
        double[] startQpos = startState.qpos;
        if (restQpos == null || restQpos.Length != nq) restQpos = (double[])startQpos.Clone();

        List<IKCandidate> accepted = new List<IKCandidate>();
        IKCandidate bestObserved = null;
        bool rotationRequired = targetRot.HasValue && rotWeight >= 0.001f;

        try
        {
            int attempts = Math.Max(1, maxRetries);
            for (int phase = 0; phase < 2; phase++)
            {
                bool allowElevator = phase == 1;
                if (allowElevator && accepted.Count > 0) break;
                if (allowElevator && debugMode)
                {
                    Debug.LogWarning("[IK候选] 六轴阶段没有可接受解，开始释放升降缸参与求解");
                }

                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    RestoreState(startState);
                    SetElevatorFromState(startQpos);
                    if (attempt > 0) RandomizeConfiguration(attempt);

                    var (converged, finalPosErr, finalRotErr) =
                        RunDampedLeastSquares(targetPos, targetRot, nv, allowElevator);
                    bool collisionFree = !checkCollision || !CheckCollision();
                    if (!collisionFree) continue;

                    IKCandidate candidate = CaptureCandidate(
                        nq, attempt + phase * attempts, converged, finalPosErr, finalRotErr,
                        rotationRequired, startQpos);

                    if (bestObserved == null || CompareCandidates(candidate, bestObserved) < 0)
                    {
                        bestObserved = candidate;
                    }

                    if (candidate.accepted)
                    {
                        AddOrReplaceEquivalentCandidate(accepted, candidate);
                    }

                    if (!selectBestCandidate && candidate.accepted) break;
                }
            }
        }
        finally
        {
            RestoreState(startState);
        }

        accepted.Sort(CompareCandidates);
        int count = Math.Min(maxCandidates, accepted.Count);
        for (int i = 0; i < count; i++)
        {
            IKCandidate candidate = accepted[i];
            result.Add((double[])candidate.qpos.Clone());
            if (debugMode)
            {
                Debug.Log($"[IK候选] {i + 1}/{count}, attempt={candidate.attempt}, " +
                          $"converged={candidate.converged}, lift={GetLiftValue(candidate.qpos):F4}");
                LogIKResultDiagnostics(candidate.qpos, targetPos, targetRot, candidate.posErrorSq,
                    candidate.rotErrorSq, candidate.distFromRest, candidate.converged, candidate.attempt);
            }
        }

        if (result.Count == 0)
        {
            if (bestObserved != null)
            {
                float posError = Mathf.Sqrt(Mathf.Max(bestObserved.posErrorSq, 0f));
                float rotError = Mathf.Sqrt(Mathf.Max(bestObserved.rotErrorSq, 0f));
                Debug.LogError($"IK 未找到可接受解: bestPosErr={posError:F5}m, bestRotErr={rotError:F5}");
            }
            else if (debugMode)
            {
                Debug.LogError("❌ IK 彻底失败：所有候选均发生碰撞或数值无效");
            }
        }

        return result;
    }

    private unsafe IKCandidate CaptureCandidate(
        int nq,
        int attempt,
        bool converged,
        float posErrorSq,
        float rotErrorSq,
        bool rotationRequired,
        double[] startQpos)
    {
        double[] qpos = new double[nq];
        for (int i = 0; i < nq; i++) qpos[i] = MjScene.Instance.Data->qpos[i];
        OptimizePeriodicAngles(qpos, startQpos);

        float posThresholdSq = Mathf.Max(stopThreshold * stopThreshold, 1e-12f);
        float poseScore = posErrorSq / posThresholdSq;
        if (rotationRequired)
        {
            float rotThresholdSq = Mathf.Max(stopRotThreshold * stopRotThreshold, 1e-12f);
            poseScore += rotErrorSq / rotThresholdSq;
        }

        float posError = Mathf.Sqrt(Mathf.Max(posErrorSq, 0f));
        float rotError = Mathf.Sqrt(Mathf.Max(rotErrorSq, 0f));
        bool approximateAccepted =
            allowApproximateSolution &&
            posError <= Mathf.Max(maxAcceptedPositionError, stopThreshold) &&
            (!rotationRequired || rotError <= Mathf.Max(maxAcceptedRotationError, stopRotThreshold));

        return new IKCandidate
        {
            qpos = qpos,
            attempt = attempt,
            converged = converged,
            accepted = IsFinite(posErrorSq) && IsFinite(rotErrorSq) && (converged || approximateAccepted),
            posErrorSq = posErrorSq,
            rotErrorSq = rotErrorSq,
            poseScore = poseScore,
            distFromRest = ComputeRestDistance(qpos)
        };
    }

    private int CompareCandidates(IKCandidate left, IKCandidate right)
    {
        if (left.converged != right.converged) return left.converged ? -1 : 1;
        int poseComparison = left.poseScore.CompareTo(right.poseScore);
        if (poseComparison != 0) return poseComparison;
        return left.distFromRest.CompareTo(right.distFromRest);
    }

    private void AddOrReplaceEquivalentCandidate(List<IKCandidate> candidates, IKCandidate candidate)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (CandidateDistance(candidates[i].qpos, candidate.qpos) >= candidateSeparation) continue;
            if (CompareCandidates(candidate, candidates[i]) < 0) candidates[i] = candidate;
            return;
        }
        candidates.Add(candidate);
    }

    private double CandidateDistance(double[] left, double[] right)
    {
        double sum = 0;
        for (int i = 0; i < actuators.Count; i++)
        {
            MjActuator act = actuators[i];
            int address = GetQposAddr(act);
            if (address < 0 || address >= left.Length || address >= right.Length) continue;

            double difference = left[address] - right[address];
            if (act.Joint is MjHingeJoint) difference = NormalizeAngle(difference);
            else if (act.Joint is MjSlideJoint) difference *= 10.0;
            sum += difference * difference;
        }
        return Math.Sqrt(sum);
    }

    private double ComputeRestDistance(double[] qpos)
    {
        double distance = 0;
        for (int i = 0; i < actuators.Count; i++)
        {
            MjActuator act = actuators[i];
            int address = GetQposAddr(act);
            if (address < 0 || address >= qpos.Length || address >= restQpos.Length) continue;

            double difference = qpos[address] - restQpos[address];
            if (act.Joint is MjHingeJoint) difference = NormalizeAngle(difference);
            distance += Math.Abs(difference) * (act.Joint is MjSlideJoint ? elevatorPenalty : 1.0);
        }
        return distance;
    }

    private void OptimizePeriodicAngles(double[] qpos, double[] startQpos)
    {
        if (rotationOptimization != RotationMode.ShortestPath) return;

        foreach (MjActuator act in actuators)
        {
            if (!(act?.Joint is MjHingeJoint hinge)) continue;
            int address = hinge.QposAddress;
            double optimized = startQpos[address] + NormalizeAngle(qpos[address] - startQpos[address]);
            double min = hinge.RangeLower * Mathf.Deg2Rad;
            double max = hinge.RangeUpper * Mathf.Deg2Rad;
            bool hasRange = Math.Abs(hinge.RangeUpper - hinge.RangeLower) > 0.01;
            if (!hasRange || (optimized >= min && optimized <= max)) qpos[address] = optimized;
        }
    }

    private double GetLiftValue(double[] qpos)
    {
        foreach (MjActuator act in actuators)
        {
            if (!(act?.Joint is MjSlideJoint slide)) continue;
            int address = slide.QposAddress;
            if (address >= 0 && address < qpos.Length) return qpos[address];
        }
        return 0;
    }

    private unsafe void SetElevatorFromState(double[] referenceQpos)
    {
        foreach (MjActuator act in actuators)
        {
            if (!(act?.Joint is MjSlideJoint slide)) continue;
            int address = slide.QposAddress;
            if (address >= 0 && address < referenceQpos.Length)
            {
                MjScene.Instance.Data->qpos[address] = referenceQpos[address];
            }
        }
    }

    private double NormalizeAngle(double angle)
    {
        double twoPi = 2.0 * Math.PI;
        while (angle > Math.PI) angle -= twoPi;
        while (angle < -Math.PI) angle += twoPi;
        return angle;
    }

    private bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    // =================================================================================
    // 6. 核心算法（加权阻尼最小二乘）
    // =================================================================================
    private unsafe (bool, float, float) RunDampedLeastSquares(
        Vector3 targetPos,
        Quaternion? targetRot,
        int nv,
        bool allowElevator)
    {
        float currentPosErr = 0;
        float currentRotErr = 0;
        float posThreshSq = stopThreshold * stopThreshold;
        float rotThreshSq = stopRotThreshold * stopRotThreshold;

        for (int k = 0; k < maxIterations; k++)
        {
            MujocoLib.mj_kinematics(MjScene.Instance.Model, MjScene.Instance.Data);
            MujocoLib.mj_comPos(MjScene.Instance.Model, MjScene.Instance.Data);

            double cx = MjScene.Instance.Data->site_xpos[cachedSiteId * 3 + 0];
            double cy = MjScene.Instance.Data->site_xpos[cachedSiteId * 3 + 1];
            double cz = MjScene.Instance.Data->site_xpos[cachedSiteId * 3 + 2];

            // ====== 🔥 新增排错日志 1：查看原始坐标数据 ======
            if (k == 0 && debugMode) 
            {
                Debug.Log($"<color=yellow>[IK 坐标排查]</color> 传入的 Unity 目标点: {targetPos:F3}");
                Debug.Log($"<color=yellow>[IK 坐标排查]</color> MuJoCo 底层末端点 (cx, cy, cz): ({cx:F3}, {cy:F3}, {cz:F3})");
            }
            // ===============================================

            double ex = targetPos.x - cx;
            double ey = targetPos.z - cy;
            double ez = targetPos.y - cz;

            // ====== 🔥 新增排错日志 2：查看 IK 眼中的误差 ======
            if (k == 0 && debugMode) 
            {
                Debug.Log($"<color=red>[IK 坐标排查]</color> 算法眼中的距离误差 (ex, ey, ez): ({ex:F3}, {ey:F3}, {ez:F3})");
            }
            // ===============================================

            double posErrorSq = ex * ex + ey * ey + ez * ez;
            currentPosErr = (float)posErrorSq;

            double erx = 0, ery = 0, erz = 0;
            double rotErrorSq = 0;
            if (targetRot.HasValue)
            {
                Vector3 tFwd = targetRot.Value * Vector3.forward;
                Vector3 tUp = targetRot.Value * Vector3.up;
                double[] t_fwd_m = { tFwd.x, tFwd.z, tFwd.y };
                double[] t_up_m = { tUp.x, tUp.z, tUp.y };

                double[] c_z_m = {
                    MjScene.Instance.Data->site_xmat[cachedSiteId * 9 + 2],
                    MjScene.Instance.Data->site_xmat[cachedSiteId * 9 + 5],
                    MjScene.Instance.Data->site_xmat[cachedSiteId * 9 + 8]
                };
                double[] c_y_m = {
                    MjScene.Instance.Data->site_xmat[cachedSiteId * 9 + 1],
                    MjScene.Instance.Data->site_xmat[cachedSiteId * 9 + 4],
                    MjScene.Instance.Data->site_xmat[cachedSiteId * 9 + 7]
                };

                double ez_x = c_z_m[1] * t_fwd_m[2] - c_z_m[2] * t_fwd_m[1];
                double ez_y = c_z_m[2] * t_fwd_m[0] - c_z_m[0] * t_fwd_m[2];
                double ez_z = c_z_m[0] * t_fwd_m[1] - c_z_m[1] * t_fwd_m[0];

                double ey_x = c_y_m[1] * t_up_m[2] - c_y_m[2] * t_up_m[1];
                double ey_y = c_y_m[2] * t_up_m[0] - c_y_m[0] * t_up_m[2];
                double ey_z = c_y_m[0] * t_up_m[1] - c_y_m[1] * t_up_m[0];

                erx = ez_x + ey_x; ery = ez_y + ey_y; erz = ez_z + ey_z;
                rotErrorSq = erx * erx + ery * ery + erz * erz;
                currentRotErr = (float)rotErrorSq;
            }

            bool ignoreRot = !targetRot.HasValue || rotWeight < 0.001f;
            if (posErrorSq < posThreshSq && (ignoreRot || rotErrorSq < rotThreshSq))
                return (true, currentPosErr, currentRotErr);

            fixed (double* p_jacp = jacp, p_jacr = jacr)
            {
                MujocoLib.mj_jacSite(MjScene.Instance.Model, MjScene.Instance.Data, p_jacp, p_jacr, cachedSiteId);
            }

            double[] taskError = targetRot.HasValue && rotWeight >= 0.001f
                ? new[] { ex, ey, ez, erx, ery, erz }
                : new[] { ex, ey, ez };

            if (!TryComputeDlsStep(taskError, nv, allowElevator, out double[] actuatorSteps))
            {
                break;
            }

            for (int i = 0; i < actuators.Count; i++)
            {
                MjActuator act = actuators[i];
                if (act?.Joint == null) continue;
                if (!allowElevator && act.Joint is MjSlideJoint) continue;
                int qposAddr = GetQposAddr(act);
                if (qposAddr < 0) continue;

                double restWeight = i == 0 ? 0.001 : restPoseWeight;
                if (act.Joint is MjSlideJoint) restWeight = elevatorWeight;

                double bias = (restQpos[qposAddr] - MjScene.Instance.Data->qpos[qposAddr]) * restWeight * stepSize;
                double maxStep = act.Joint is MjSlideJoint ? maxElevatorStep : maxAngularStep;
                double delta = Math.Clamp(actuatorSteps[i] * stepSize + bias, -maxStep, maxStep);
                MjScene.Instance.Data->qpos[qposAddr] += delta;
                ClampJoint(act.Joint, qposAddr);
            }
        }
        return (false, currentPosErr, currentRotErr);
    }

    private bool TryComputeDlsStep(
        double[] taskError,
        int nv,
        bool allowElevator,
        out double[] actuatorSteps)
    {
        int taskSize = taskError.Length;
        int actuatorCount = actuators.Count;
        actuatorSteps = new double[actuatorCount];
        double[,] taskJacobian = new double[taskSize, actuatorCount];
        double[] inverseJointWeights = new double[actuatorCount];
        double rotationScale = Math.Sqrt(Math.Max(rotWeight, 0.0));

        for (int i = 0; i < actuatorCount; i++)
        {
            MjActuator act = actuators[i];
            int dofAddr = GetDofAddr(act);
            if (dofAddr < 0 || dofAddr >= nv) continue;

            taskJacobian[0, i] = jacp[0 * nv + dofAddr];
            taskJacobian[1, i] = jacp[1 * nv + dofAddr];
            taskJacobian[2, i] = jacp[2 * nv + dofAddr];
            if (taskSize == 6)
            {
                taskJacobian[3, i] = jacr[0 * nv + dofAddr] * rotationScale;
                taskJacobian[4, i] = jacr[1 * nv + dofAddr] * rotationScale;
                taskJacobian[5, i] = jacr[2 * nv + dofAddr] * rotationScale;
            }

            if (act?.Joint is MjSlideJoint)
            {
                inverseJointWeights[i] = allowElevator
                    ? 1.0 / Math.Max(elevatorMotionPenalty, 1.0)
                    : 0.0;
            }
            else
            {
                inverseJointWeights[i] = 1.0;
            }
        }

        double[] weightedError = (double[])taskError.Clone();
        if (taskSize == 6)
        {
            weightedError[3] *= rotationScale;
            weightedError[4] *= rotationScale;
            weightedError[5] *= rotationScale;
        }

        double[,] normal = new double[taskSize, taskSize];
        for (int row = 0; row < taskSize; row++)
        {
            for (int col = 0; col < taskSize; col++)
            {
                double value = 0;
                for (int joint = 0; joint < actuatorCount; joint++)
                {
                    value += taskJacobian[row, joint] * inverseJointWeights[joint] * taskJacobian[col, joint];
                }
                normal[row, col] = value;
            }
            normal[row, row] += dlsDamping * dlsDamping;
        }

        if (!TrySolveLinearSystem(normal, weightedError, out double[] taskStep)) return false;

        for (int joint = 0; joint < actuatorCount; joint++)
        {
            double value = 0;
            for (int row = 0; row < taskSize; row++)
            {
                value += taskJacobian[row, joint] * taskStep[row];
            }
            actuatorSteps[joint] = inverseJointWeights[joint] * value;
            if (!IsFinite(actuatorSteps[joint])) return false;
        }
        return true;
    }

    private bool TrySolveLinearSystem(double[,] matrix, double[] rhs, out double[] solution)
    {
        int n = rhs.Length;
        double[,] augmented = new double[n, n + 1];
        solution = new double[n];
        for (int row = 0; row < n; row++)
        {
            for (int col = 0; col < n; col++) augmented[row, col] = matrix[row, col];
            augmented[row, n] = rhs[row];
        }

        for (int pivot = 0; pivot < n; pivot++)
        {
            int bestRow = pivot;
            double bestValue = Math.Abs(augmented[pivot, pivot]);
            for (int row = pivot + 1; row < n; row++)
            {
                double value = Math.Abs(augmented[row, pivot]);
                if (value > bestValue)
                {
                    bestValue = value;
                    bestRow = row;
                }
            }

            if (!IsFinite(bestValue) || bestValue < 1e-12) return false;
            if (bestRow != pivot)
            {
                for (int col = pivot; col <= n; col++)
                {
                    double temporary = augmented[pivot, col];
                    augmented[pivot, col] = augmented[bestRow, col];
                    augmented[bestRow, col] = temporary;
                }
            }

            double divisor = augmented[pivot, pivot];
            for (int col = pivot; col <= n; col++) augmented[pivot, col] /= divisor;

            for (int row = 0; row < n; row++)
            {
                if (row == pivot) continue;
                double factor = augmented[row, pivot];
                if (Math.Abs(factor) < 1e-15) continue;
                for (int col = pivot; col <= n; col++)
                {
                    augmented[row, col] -= factor * augmented[pivot, col];
                }
            }
        }

        for (int row = 0; row < n; row++)
        {
            solution[row] = augmented[row, n];
            if (!IsFinite(solution[row])) return false;
        }
        return true;
    }

    private unsafe void LogIKResultDiagnostics(double[] qpos, Vector3 targetPos, Quaternion? targetRot, float posErrorSq, float rotErrorSq, double distFromRest, bool converged, int attempt)
    {
        if (qpos == null || MjScene.Instance.Model == null || MjScene.Instance.Data == null || cachedSiteId < 0) return;

        MujocoStateSnapshot backup = CaptureState();
        try
        {
            for (int i = 0; i < qpos.Length; i++) MjScene.Instance.Data->qpos[i] = qpos[i];
            MujocoLib.mj_forward(MjScene.Instance.Model, MjScene.Instance.Data);

            double mx = MjScene.Instance.Data->site_xpos[cachedSiteId * 3 + 0];
            double my = MjScene.Instance.Data->site_xpos[cachedSiteId * 3 + 1];
            double mz = MjScene.Instance.Data->site_xpos[cachedSiteId * 3 + 2];
            Vector3 siteUnity = new Vector3((float)mx, (float)mz, (float)my);
            float unityDistance = Vector3.Distance(siteUnity, targetPos);

            Debug.Log(
                $"[IK诊断] site='{endEffectorSite?.name}', id={cachedSiteId}, bestAttempt={attempt}, converged={converged}, " +
                $"posErr={Mathf.Sqrt(Mathf.Max(posErrorSq, 0f)):F5}m, rotErr={Mathf.Sqrt(Mathf.Max(rotErrorSq, 0f)):F5}, restDist={distFromRest:F5}, " +
                $"targetUnity={targetPos:F4}, siteUnity={siteUnity:F4}, unityDistance={unityDistance:F5}m, " +
                $"targetRot={(targetRot.HasValue ? targetRot.Value.eulerAngles.ToString("F3") : "None")}");

            Debug.Log($"[IK诊断] fullQpos({qpos.Length}) = {FormatArray(qpos)}");
            Debug.Log($"[IK诊断] actuator映射 = {FormatActuatorQposMap(qpos)}");
        }
        finally
        {
            RestoreState(backup);
        }
    }

    // =================================================================================
    // 7. 辅助功能 (已改为 public 以便 MissionController 调用)
    // =================================================================================
    public int GetQposAddr(MjActuator act)
    {
        if (act?.Joint is MjHingeJoint h) return h.QposAddress;
        if (act?.Joint is MjSlideJoint s) return s.QposAddress;
        return -1;
    }

    private int GetDofAddr(MjActuator act)
    {
        if (act?.Joint is MjHingeJoint h) return h.DofAddress;
        if (act?.Joint is MjSlideJoint s) return s.DofAddress;
        return -1;
    }

    private string FormatArray(double[] values)
    {
        if (values == null) return "null";
        List<string> parts = new List<string>();
        for (int i = 0; i < values.Length; i++) parts.Add($"{i}:{values[i]:F4}");
        return string.Join(", ", parts);
    }

    private string FormatActuatorQposMap(double[] fullQpos)
    {
        if (actuators == null || fullQpos == null) return "null";

        List<string> parts = new List<string>();
        for (int i = 0; i < actuators.Count; i++)
        {
            MjActuator act = actuators[i];
            int addr = act != null ? GetQposAddr(act) : -1;
            string jointName = act?.Joint != null ? act.Joint.gameObject.name : "nullJoint";
            string value = addr >= 0 && addr < fullQpos.Length ? fullQpos[addr].ToString("F4") : "addrInvalid";
            parts.Add($"a{i}:{jointName}@qpos[{addr}]={value}");
        }
        return string.Join("; ", parts);
    }

    private unsafe void RefreshCache()
    {
        int nv = MjScene.Instance.Model->nv;
        if (jacp == null || jacp.Length != 3 * nv) { jacp = new double[3 * nv]; jacr = new double[3 * nv]; }

        if (endEffectorSite != null && (cachedSiteId == -1 || endEffectorSite.name != cachedSiteName))
        {
            cachedSiteId = MujocoLib.mj_name2id(MjScene.Instance.Model, (int)MujocoLib.mjtObj.mjOBJ_SITE, endEffectorSite.name);
            cachedSiteName = endEffectorSite.name;

            // ====== 🔥 新增名字与 ID 查验日志 ======
            Debug.Log($"<color=cyan>[IK 名字查验]</color> 正在用 Unity 物体名 <b>'{endEffectorSite.name}'</b> 去底层搜 ID，得到的 cachedSiteId = <b>{cachedSiteId}</b>");
            // ======================================
        }
        if (ignoredGeomIds.Count == 0)
        {
            foreach (var name in ignoreGeomNames)
            {
                int id = MujocoLib.mj_name2id(MjScene.Instance.Model, (int)MujocoLib.mjtObj.mjOBJ_GEOM, name);
                if (id != -1) ignoredGeomIds.Add(id);
            }

            foreach (MjGeom geom in FindObjectsOfType<MjGeom>())
            {
                if (geom == null || geom.MujocoId < 0) continue;
                if (ContainsIgnoredGeomToken(geom.name) || ContainsIgnoredGeomToken(geom.MujocoName))
                {
                    ignoredGeomIds.Add(geom.MujocoId);
                }
            }
        }
    }

    private bool ContainsIgnoredGeomToken(string geomName)
    {
        if (string.IsNullOrEmpty(geomName)) return false;
        foreach (string token in DefaultIgnoredGeomTokens)
        {
            if (geomName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private unsafe void ClampJoint(MjBaseJoint joint, int qposAddr)
    {
        double min = 0, max = 0;
        bool hasRange = false;

        if (joint is MjHingeJoint h)
        {
            min = h.RangeLower * Mathf.Deg2Rad; max = h.RangeUpper * Mathf.Deg2Rad;
            hasRange = Math.Abs(h.RangeUpper - h.RangeLower) > 0.01;
        }
        else if (joint is MjSlideJoint s)
        {
            min = s.RangeLower; max = s.RangeUpper;
            hasRange = Math.Abs(s.RangeUpper - s.RangeLower) > 0.01;
        }

        if (hasRange)
        {
            MjScene.Instance.Data->qpos[qposAddr] = Math.Clamp(MjScene.Instance.Data->qpos[qposAddr], min, max);
        }
    }

    private unsafe bool CheckCollision()
    {
        MujocoLib.mj_kinematics(MjScene.Instance.Model, MjScene.Instance.Data);
        MujocoLib.mj_comPos(MjScene.Instance.Model, MjScene.Instance.Data);
        MujocoLib.mj_fwdPosition(MjScene.Instance.Model, MjScene.Instance.Data);

        int ncon = MjScene.Instance.Data->ncon;
        for (int i = 0; i < ncon; i++)
        {
            var contact = MjScene.Instance.Data->contact[i];
            if (contact.dist < -Math.Max(collisionPenetrationTolerance, 0f))
            {
                if (ignoredGeomIds.Contains(contact.geom1) || ignoredGeomIds.Contains(contact.geom2))
                    continue;
                return true;
            }
        }
        return false;
    }

    private unsafe void RandomizeConfiguration(int attempt)
    {
        for (int i = 0; i < actuators.Count; i++)
        {
            var act = actuators[i];

            if (act.Joint is MjSlideJoint)
            {
                // 升降缸保持本次求解开始时的高度，由第二阶段 DLS 决定是否需要移动。
                continue;
            }
            else if (act.Joint is MjHingeJoint h)
            {
                bool useFullRangeSeed = i == 0 || attempt % 3 == 0;
                if (useFullRangeSeed && Math.Abs(h.RangeUpper - h.RangeLower) > 0.01)
                {
                    float min = h.RangeLower * Mathf.Deg2Rad;
                    float max = h.RangeUpper * Mathf.Deg2Rad;
                    MjScene.Instance.Data->qpos[h.QposAddress] = UnityEngine.Random.Range(min, max);
                }
                else
                {
                    double restVal = restQpos[h.QposAddress];
                    MjScene.Instance.Data->qpos[h.QposAddress] = restVal +
                        UnityEngine.Random.Range(-randomSeedRadius, randomSeedRadius);
                }
                ClampJoint(h, h.QposAddress);
            }
        }
        MujocoLib.mj_forward(MjScene.Instance.Model, MjScene.Instance.Data);
    }

    private unsafe MujocoStateSnapshot CaptureState()
    {
        var model = MjScene.Instance.Model;
        var data = MjScene.Instance.Data;
        MujocoStateSnapshot snapshot = new MujocoStateSnapshot
        {
            qpos = new double[model->nq],
            qvel = new double[model->nv],
            act = new double[model->na],
            ctrl = new double[model->nu]
        };

        for (int i = 0; i < snapshot.qpos.Length; i++) snapshot.qpos[i] = data->qpos[i];
        for (int i = 0; i < snapshot.qvel.Length; i++) snapshot.qvel[i] = data->qvel[i];
        for (int i = 0; i < snapshot.act.Length; i++) snapshot.act[i] = data->act[i];
        for (int i = 0; i < snapshot.ctrl.Length; i++) snapshot.ctrl[i] = data->ctrl[i];
        return snapshot;
    }

    private unsafe void RestoreState(MujocoStateSnapshot snapshot)
    {
        if (snapshot == null) return;

        var data = MjScene.Instance.Data;
        for (int i = 0; i < snapshot.qpos.Length; i++) data->qpos[i] = snapshot.qpos[i];
        for (int i = 0; i < snapshot.qvel.Length; i++) data->qvel[i] = snapshot.qvel[i];
        for (int i = 0; i < snapshot.act.Length; i++) data->act[i] = snapshot.act[i];
        for (int i = 0; i < snapshot.ctrl.Length; i++) data->ctrl[i] = snapshot.ctrl[i];
        MujocoLib.mj_forward(MjScene.Instance.Model, data);
    }

    private class IKCandidate
    {
        public double[] qpos;
        public int attempt;
        public bool converged;
        public bool accepted;
        public float posErrorSq;
        public float rotErrorSq;
        public float poseScore;
        public double distFromRest;
    }

    private class MujocoStateSnapshot
    {
        public double[] qpos;
        public double[] qvel;
        public double[] act;
        public double[] ctrl;
    }
}
