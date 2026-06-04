using System;
using System.Collections.Generic;
using UnityEngine;
using Mujoco;

/// <summary>
/// 基于梯度下降法 (Gradient Descent) 的 MuJoCo 逆运动学 (IK) 求解器。
/// 修复版：采用“底座全向探索 + 手臂姿态约束”的混合策略，解决多解扭曲问题。
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
        if (MjScene.Instance.Model == null || MjScene.Instance.Data == null) return null;

        RefreshCache();

        int nq = MjScene.Instance.Model->nq;
        int nv = MjScene.Instance.Model->nv;

        // 1. 备份当前姿态 (用于恢复和计算最短路径)
        double[] startQpos = new double[nq];
        for (int i = 0; i < nq; i++) startQpos[i] = MjScene.Instance.Data->qpos[i];

        if (restQpos == null || restQpos.Length != nq) restQpos = startQpos;

        double[] bestQpos = null;

        // 评分变量 (越小越好)
        float bestTotalError = float.MaxValue; 
        float bestPosError = float.MaxValue;
        float bestRotError = float.MaxValue;
        double bestDistFromRest = double.MaxValue; // 离舒适姿态的距离

        // 2. 随机重试循环
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            // 第0次尝试保持原样，后续尝试进行随机扰动
            if (attempt > 0) RandomizeConfiguration();

            // 核心计算
            var (converged, finalPosErr, finalRotErr) = RunGradientDescent(targetPos, targetRot, nv);

            float totalErr = finalPosErr + finalRotErr;

            // 计算当前解离 Rest Pose (舒适姿态) 有多远
            double distFromRest = 0;
            for (int i = 0; i < actuators.Count; i++)
            {
                int qAddr = GetQposAddr(actuators[i]);
                if (qAddr != -1) 
                {
                    double diff = Math.Abs(MjScene.Instance.Data->qpos[qAddr] - restQpos[qAddr]);
                    // 如果是升降缸 (MjSlideJoint)，给它加上巨大的惩罚系数！
                    if (actuators[i].Joint is MjSlideJoint) {
                        distFromRest += diff * elevatorPenalty; 
                    } else {
                        distFromRest += diff;
                    }
                }
            }
            // 碰撞检测
            bool isCollisionFree = (!checkCollision || !CheckCollision());

            if (isCollisionFree)
            {
                // 评分公式：
                float weightPose = 0.2f; 
                float currentScore = totalErr + (float)distFromRest * weightPose;
                float bestScore = bestTotalError + (float)bestDistFromRest * weightPose;

                // 更新最佳解
                if (bestQpos == null || currentScore < bestScore)
                {
                    bestTotalError = totalErr;
                    bestPosError = finalPosErr;
                    bestRotError = finalRotErr;
                    bestDistFromRest = distFromRest;

                    if (bestQpos == null) bestQpos = new double[nq];
                    for (int i = 0; i < nq; i++) bestQpos[i] = MjScene.Instance.Data->qpos[i];
                }
            }

            // 只要没关优选模式，就一定要跑完所有 Retries
            if (!selectBestCandidate)
            {
                if (converged && isCollisionFree) break;
            }
        }

        // 3. 恢复现场 (让物理引擎回到起点，避免视觉闪烁)
        for (int i = 0; i < nq; i++) MjScene.Instance.Data->qpos[i] = startQpos[i];
        MujocoLib.mj_kinematics(MjScene.Instance.Model, MjScene.Instance.Data);

        // 4. 返回结果处理
        if (bestQpos != null)
        {
            // 就近旋转优化 (处理底座多圈问题)
            if (rotationOptimization == RotationMode.ShortestPath)
            {
                for (int i = 0; i < actuators.Count; i++)
                {
                    var act = actuators[i];
                    if (act.Joint is MjHingeJoint h)
                    {
                        int qAddr = h.QposAddress;
                        double startAngle = startQpos[qAddr];
                        double targetAngle = bestQpos[qAddr];

                        double delta = targetAngle - startAngle;
                        double twoPi = 2.0 * Math.PI;
                        while (delta > Math.PI) delta -= twoPi;
                        while (delta < -Math.PI) delta += twoPi;

                        double optimizedTarget = startAngle + delta;
                        double min = h.RangeLower * Mathf.Deg2Rad;
                        double max = h.RangeUpper * Mathf.Deg2Rad;
                        bool hasRange = Math.Abs(h.RangeUpper - h.RangeLower) > 0.01;

                        if (!hasRange || (optimizedTarget >= min && optimizedTarget <= max))
                        {
                            bestQpos[qAddr] = optimizedTarget;
                        }
                    }
                }
            }

            // float realPosErr = Mathf.Sqrt(bestPosError);
            // float realRotErr = Mathf.Sqrt(bestRotError);
            return bestQpos;
        }
        else
        {
            if (debugMode) Debug.LogError("❌ IK 彻底失败");
            return null;
        }
    }

    // =================================================================================
    // 6. 核心算法 (梯度下降)
    // =================================================================================
    private unsafe (bool, float, float) RunGradientDescent(Vector3 targetPos, Quaternion? targetRot, int nv)
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

            double[] deltaQ = new double[nv];
            for (int i = 0; i < actuators.Count; i++)
            {
                var act = actuators[i];
                if (act == null || act.Joint == null) continue;

                int dofAddr = -1; int qposAddr = -1;
                if (act.Joint is MjHingeJoint h) { dofAddr = h.DofAddress; qposAddr = h.QposAddress; }
                else if (act.Joint is MjSlideJoint s) { dofAddr = s.DofAddress; qposAddr = s.QposAddress; }
                else continue;

                double dx = jacp[0 * nv + dofAddr];
                double dy = jacp[1 * nv + dofAddr];
                double dz = jacp[2 * nv + dofAddr];
                double gradPos = dx * ex + dy * ey + dz * ez;

                double gradRot = 0;
                if (targetRot.HasValue)
                {
                    double rx = jacr[0 * nv + dofAddr];
                    double ry = jacr[1 * nv + dofAddr];
                    double rz = jacr[2 * nv + dofAddr];
                    gradRot = rx * erx + ry * ery + rz * erz;
                }

                double currentQ = MjScene.Instance.Data->qpos[qposAddr];
                double restQ = restQpos[qposAddr];

                double currentWeight = (i == 0) ? 0.001 : restPoseWeight;
                if (act.Joint is MjSlideJoint) currentWeight = elevatorWeight; 

                double biasForce = (restQ - currentQ) * currentWeight;

                double totalGrad = gradPos + (gradRot * rotWeight) + biasForce;
                double weight = 1.0;

                deltaQ[dofAddr] = totalGrad * stepSize * weight;
                MjScene.Instance.Data->qpos[qposAddr] += deltaQ[dofAddr];
                ClampJoint(act.Joint, qposAddr);
            }
        }
        return (false, currentPosErr, currentRotErr);
    }

    // =================================================================================
    // 7. 辅助功能 (已改为 public 以便 MissionController 调用)
    // =================================================================================
    public int GetQposAddr(MjActuator act)
    {
        if (act.Joint is MjHingeJoint h) return h.QposAddress;
        if (act.Joint is MjSlideJoint s) return s.QposAddress;
        return -1;
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
        if (ignoredGeomIds.Count == 0 && ignoreGeomNames.Count > 0)
        {
            foreach (var name in ignoreGeomNames)
            {
                int id = MujocoLib.mj_name2id(MjScene.Instance.Model, (int)MujocoLib.mjtObj.mjOBJ_GEOM, name);
                if (id != -1) ignoredGeomIds.Add(id);
            }
        }
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
            if (contact.dist < -0.001f)
            {
                if (ignoredGeomIds.Contains(contact.geom1) || ignoredGeomIds.Contains(contact.geom2))
                    continue;
                return true;
            }
        }
        return false;
    }

    private unsafe void RandomizeConfiguration()
    {
        for (int i = 0; i < actuators.Count; i++)
        {
            var act = actuators[i];
            bool isBaseJoint = (i == 0); 

            if (act.Joint is MjSlideJoint s)
            {
                MjScene.Instance.Data->qpos[s.QposAddress] += UnityEngine.Random.Range(-0.1f, 0.1f);
            }
            else if (act.Joint is MjHingeJoint h)
            {
                if (isBaseJoint)
                {
                    MjScene.Instance.Data->qpos[h.QposAddress] = UnityEngine.Random.Range(-3.14f, 3.14f);
                }
                else
                {
                    double restVal = restQpos[h.QposAddress];
                    MjScene.Instance.Data->qpos[h.QposAddress] = restVal + UnityEngine.Random.Range(-0.5f, 0.5f);
                }
            }
        }
        MujocoLib.mj_forward(MjScene.Instance.Model, MjScene.Instance.Data);
    }
}