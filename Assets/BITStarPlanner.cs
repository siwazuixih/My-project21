using System;
using System.Collections.Generic;
using UnityEngine;
using Mujoco;
using System.Linq;

public class BITStarPlanner : MonoBehaviour
{
    [Header("组件绑定")]
    public MujocoStaticIKSolver ikSolver;
    public List<MjActuator> actuators;

    [Header("BIT* 核心参数")]
    public int batchSize = 2000;    // 每一批撒多少点
    public int maxBatches = 50;     // 最大迭代轮数
    public float eta = 3.0f;        // 搜索半径系数 (建议 3.0 - 5.0)
    
    [Header("重启动策略")]
    public int restartAttempts = 5; // 跑几次取最优 (解决拓扑陷阱)
    public float timeLimitPerRun = 0.5f; // 单次尝试的时间限制 (秒)

    [Header("笛卡尔空间限制 (Workspace)")]
    public bool useWorkspaceLimits = true;
    public Vector3 minLimit = new Vector3(-0.8f, 0.05f, -0.8f); // Unity坐标系 (x, y, z)
    public Vector3 maxLimit = new Vector3(0.8f, 1.2f, 0.8f);

    // 内部变量
    private int nv;
    private double[] q_start;
    private double[] q_goal;
    private int siteId = -1;

    // -------------------------------------------------------------------------
    // 主入口：Plan
    // -------------------------------------------------------------------------
    public List<double[]> Plan(Vector3 targetPos, Quaternion? targetRot = null)
    {
        if (!InitSystem()) return null;

        bool startValid = IsValidConfig(q_start);
        Debug.Log($"🧐 [起点体检] IsValidConfig(q_start) = {startValid}");
        if (!startValid) Debug.LogError("💀 破案了：起点本身就是碰撞状态！请检查是不是机械臂初始姿态自相交了？");

        Debug.Log("🔍 BIT*: 正在调用 IK 寻找终点 (包含位姿)...");
        Debug.Log($"🎯 目标位置: {targetPos}, 目标旋转: {(targetRot.HasValue ? targetRot.Value.eulerAngles.ToString() : "None")}");
        
        // 2. 将 targetRot 传给 IK 求解器！
        double[] fullIkResult = ikSolver.SolveIK(targetPos, targetRot);      
        Debug.Log($"IK: {string.Join(", ", fullIkResult.Select(d => d.ToString("F4")))}");        
        if (fullIkResult == null) return null;

        q_goal = ExtractCompactState(fullIkResult);

        // =========================================================
        // 👇👇👇 新增：打印直线 Cost 和 连通性检查 👇👇👇
        // =========================================================
        double straightCost = Distance(q_start, q_goal);
        bool canGoStraight = IsValidConnection(q_start, q_goal);

        Debug.Log($"📏 [基准数据] 理论直线 Cost: {straightCost:F4}");
        Debug.Log($"🚧 [基准数据] 直线是否无碰撞: {canGoStraight}");
        
        if (canGoStraight)
        {
            Debug.Log("🚀 居然可以直接走直线！那你应该能看到 Cost 和直线一样...");
        }
        else
        {
            Debug.Log("🧱 直线被挡住了，必须绕路！最终 Cost 一定会大于直线 Cost。");
        }
        // =========================================================

        
        // --- 🏆 冠军挑战赛：并行重启动 ---
        Debug.Log($"🏁 BIT* 启动竞速模式: 尝试 {restartAttempts} 次...");
        
        List<double[]> bestPath = null;
        double bestCost = double.MaxValue;

        for (int i = 0; i < restartAttempts; i++)
        {
            // 运行一次完整的 BIT*
            var (path, cost) = RunBITStarInternal(timeLimitPerRun);

            if (path != null)
            {
                Debug.Log($"Attempt {i + 1}: Cost = {cost:F4}");
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestPath = path;
                }
            }
        }

        if (bestPath != null)
        {
            Debug.Log($"🏆 最终胜出方案 Cost: {bestCost:F4}, 节点数: {bestPath.Count}");
            
            // 🔥 后处理：路径拉直
            var smoothedPath = ShortcutPath(bestPath);
            Debug.Log($"✨ 平滑后节点数: {smoothedPath.Count}");
            
            return smoothedPath;
        }


        Debug.LogError("❌ BIT* 所有尝试均失败");
        return null;
    }

    // -------------------------------------------------------------------------
    // BIT* 核心逻辑 (单次运行)
    // -------------------------------------------------------------------------
    private (List<double[]>, double) RunBITStarInternal(float timeLimit)
    {
        // 1. 初始化数据结构
        List<BITNode> treeNodes = new List<BITNode>();
        List<BITNode> samples = new List<BITNode>();
        
        // 起点入树
        BITNode root = new BITNode(0, q_start, 0.0, null);
        treeNodes.Add(root);

        // 队列
        MinHeap<Vertex> vertexQueue = new MinHeap<Vertex>();
        MinHeap<Edge> edgeQueue = new MinHeap<Edge>();

        double finalCost = double.MaxValue;
        BITNode goalNodeInTree = null;
        
        float startTime = Time.realtimeSinceStartup;
        double radius = double.MaxValue;

        for (int batchIdx = 0; batchIdx < maxBatches; batchIdx++)
        {
            // 检查超时
            if (Time.realtimeSinceStartup - startTime > timeLimit) break;

            // --- 只有当队列为空时，才撒新点 ---
            if (vertexQueue.IsEmpty() && edgeQueue.IsEmpty())
            {
                // 剪枝
                Prune(ref samples, finalCost);
                
                // 采样新点 (包含笛卡尔限制)
                var newSamples = SampleInformed(batchSize, finalCost);
                samples.AddRange(newSamples);

                // 更新半径 (RGG公式)
                int q = treeNodes.Count + samples.Count;
                radius = CalculateRadius(q);

                // 重建 Vertex Queue
                vertexQueue.Clear();
                edgeQueue.Clear();
                
                // 将树上所有有潜力的节点加入队列
                foreach (var node in treeNodes)
                {
                    // g + h < current_best
                    double fScore = node.gScore + Distance(node.q, q_goal);
                    if (fScore < finalCost)
                    {
                        vertexQueue.Push(new Vertex(node, fScore));
                    }
                }
            }

            // --- 处理队列 ---
            while (!vertexQueue.IsEmpty() || !edgeQueue.IsEmpty())
            {
                // 超时检查 (粒度更细)
                if (Time.realtimeSinceStartup - startTime > timeLimit) break;

                // 比较两个队列的最小值
                double bestV = vertexQueue.IsEmpty() ? double.MaxValue : vertexQueue.Peek().fScore;
                double bestE = edgeQueue.IsEmpty() ? double.MaxValue : edgeQueue.Peek().fScore;

                // 剪枝：如果最好的潜力都比已知路径差，这轮就废了
                if (Math.Min(bestV, bestE) > finalCost)
                {
                    vertexQueue.Clear();
                    edgeQueue.Clear();
                    break;
                }

                if (bestV <= bestE)
                {
                    ExpandVertex(vertexQueue.Pop(), samples, edgeQueue, radius, finalCost);
                }
                else
                {
                    var bestEdge = edgeQueue.Pop();
                    // 尝试连接
                    if (bestEdge.fScore < finalCost) // 再次剪枝检查
                    {
                        // 碰撞检测 (延迟到最后一刻做)
                        if (IsValidConfig(bestEdge.parent.q) && IsValidConnection(bestEdge.parent.q, bestEdge.childQ))
                        {
                            // 连通！加入树
                            BITNode newNode = new BITNode(treeNodes.Count, bestEdge.childQ, bestEdge.gScore, bestEdge.parent);
                            treeNodes.Add(newNode);

                            // 从样本列表中移除 (可选，为了性能通常标记移除，这里简化不移除)
                            // 将新节点加入 Vertex 队列以扩展更多
                            double newF = newNode.gScore + Distance(newNode.q, q_goal);
                            vertexQueue.Push(new Vertex(newNode, newF));

                            // 检查是否到达终点 (这里假设终点就在 samples 里或者是特定检查)
                            double distToGoal = Distance(newNode.q, q_goal);
                            if (distToGoal < 0.01f || bestEdge.isGoal) 
                            {
                                if (newNode.gScore < finalCost)
                                {
                                    finalCost = newNode.gScore;
                                    goalNodeInTree = newNode;
                                }
                            }
                        }
                    }
                }
            }
        }

        if (goalNodeInTree != null)
        {
            return (ReconstructPath(goalNodeInTree), finalCost);
        }
        return (null, double.MaxValue);
    }


    // -------------------------------------------------------------------------
    // 可视化辅助：将关节路径转为笛卡尔空间坐标 (用于画线)
    // -------------------------------------------------------------------------
    public unsafe List<Vector3> GetPathInWorldSpace(List<double[]> jointPath)
    {
        // 安全检查
        if (MjScene.Instance.Model == null || jointPath == null || jointPath.Count == 0) return null;

        List<Vector3> worldPoints = new List<Vector3>();
        
        // 1. 备份当前机器人的姿态 (防止画线的时候机器人乱动)
        double[] backup = new double[MjScene.Instance.Model->nq];
        for(int i=0; i<backup.Length; i++) backup[i] = MjScene.Instance.Data->qpos[i];
        
        // 确保 siteId 已初始化
        if (siteId == -1) 
            siteId = MujocoLib.mj_name2id(MjScene.Instance.Model, (int)MujocoLib.mjtObj.mjOBJ_SITE, ikSolver.endEffectorSite.name);

        // 2. 遍历路径中的每一个点
        foreach (var config in jointPath)
        {
            // 应用关节角度
            ApplyConfig(config);
            
            // 计算正向运动学 (FK)
            MujocoLib.mj_kinematics(MjScene.Instance.Model, MjScene.Instance.Data);
            
            // 读取末端执行器位置 (MuJoCo 坐标系)
            double mx = MjScene.Instance.Data->site_xpos[siteId * 3 + 0];
            double my = MjScene.Instance.Data->site_xpos[siteId * 3 + 1];
            double mz = MjScene.Instance.Data->site_xpos[siteId * 3 + 2];

            // 3. 坐标转换 MuJoCo -> Unity
            // MuJoCo: Z轴向上
            // Unity: Y轴向上
            // 转换公式: Unity(x, y, z) = MuJoCo(x, z, y)
            worldPoints.Add(new Vector3((float)mx, (float)mz, (float)my));
        }

        // 3. 恢复机器人原来的姿态
        for(int i=0; i<backup.Length; i++) MjScene.Instance.Data->qpos[i] = backup[i];
        MujocoLib.mj_kinematics(MjScene.Instance.Model, MjScene.Instance.Data);
        
        return worldPoints;
    }




    // -------------------------------------------------------------------------
    // 核心算法细节：采样、扩展、剪枝
    // -------------------------------------------------------------------------
    
// -------------------------------------------------------------------------
    // 修复后的 ExpandVertex：加入 K-近邻限制，防止内存爆炸
    // -------------------------------------------------------------------------
    private void ExpandVertex(Vertex v, List<BITNode> samples, MinHeap<Edge> edgeQueue, double r, double cMax)
    {
        // 1. 优先检查终点 (保持 "舔狗模式"，只要 Cost 允许，无视半径直连终点)
        double distToGoal = Distance(v.node.q, q_goal);
        if (v.node.gScore + distToGoal < cMax) 
        {
            edgeQueue.Push(new Edge(v.node, q_goal, v.node.gScore + distToGoal, v.node.gScore + distToGoal, true));
        }

        // 2. 检查样本点 (加入 K-近邻限制 / KNN)
        // ---------------------------------------------------------
        int maxNeighbors = 20; // 🔥 熔断阀值：每个点最多只许连最近的 20 个邻居
        // ---------------------------------------------------------

        // 用一个临时列表存储所有在半径内的候选邻居
        var potentialNeighbors = new List<(BITNode node, double dist)>();

        // 遍历样本集 (这里依然是线性扫描，但后续只推入有限的边)
        foreach (var sample in samples)
        {
            // 排除自己
            if (sample == v.node) continue;

            double dist = Distance(v.node.q, sample.q);
            
            // 只有在半径内的才考虑
            if (dist <= r)
            {
                potentialNeighbors.Add((sample, dist));
            }
        }

        // 🔥 关键修复：如果邻居太多，会撑爆内存，所以我们只取“最近的”那几个
        if (potentialNeighbors.Count > maxNeighbors)
        {
            // 按距离排序 (从小到大)
            potentialNeighbors.Sort((a, b) => a.dist.CompareTo(b.dist));
            
            // 只保留前 maxNeighbors 个，其他的丢弃
            // 这样 edgeQueue 的大小就被严格控制住了，绝对不会闪退
            int count = Math.Min(potentialNeighbors.Count, maxNeighbors);
            for (int i = 0; i < count; i++)
            {
                PushEdgeIfBetter(v, potentialNeighbors[i].node, potentialNeighbors[i].dist, edgeQueue, cMax);
            }
        }
        else
        {
            // 如果邻居很少，那就全加进去
            foreach (var item in potentialNeighbors)
            {
                PushEdgeIfBetter(v, item.node, item.dist, edgeQueue, cMax);
            }
        }
    }

    // 辅助小函数，避免代码重复
    private void PushEdgeIfBetter(Vertex v, BITNode sample, double dist, MinHeap<Edge> edgeQueue, double cMax)
    {
        double estG = v.node.gScore + dist;
        double estF = estG + Distance(sample.q, q_goal);

        if (estF < cMax)
        {
            edgeQueue.Push(new Edge(v.node, sample.q, estF, estG, false));
        }
    }    // 采样：带笛卡尔限制 + 启发式拒绝
    private List<BITNode> SampleInformed(int n, double cMax)
    {
        // 👇👇👇 加上这一行，防止 Debug 点无限堆积 👇👇👇
        debugSamplePoints.Clear();

        List<BITNode> result = new List<BITNode>();
        int attempts = 0;

        // 🔥 记录开始时间
        float startTime = Time.realtimeSinceStartup;
        
        while (result.Count < n && attempts < n * 100)
        {

            // 🔥 关键修复：如果这一帧采样超过了 0.1秒 还没完，强制停手，防止卡死
            if (Time.realtimeSinceStartup - startTime > 0.1f) 
            {
                Debug.LogWarning($"⚠️ 采样超时！只生成了 {result.Count}/{n} 个点，但这比卡死强。");
                break;
            }

            attempts++;
            double[] q = new double[nv];

            // 1. 关节空间随机
            for(int i=0; i<nv; i++)
            {
                var act = actuators[i];
                float min = -3.14f, max = 3.14f;
                if (act.Joint is MjHingeJoint h && Math.Abs(h.RangeUpper - h.RangeLower) > 0.01) { min = h.RangeLower * Mathf.Deg2Rad; max = h.RangeUpper * Mathf.Deg2Rad; }
                else if (act.Joint is MjSlideJoint s && Math.Abs(s.RangeUpper - s.RangeLower) > 0.01) { min = (float)s.RangeLower; max = (float)s.RangeUpper; }
                
                q[i] = UnityEngine.Random.Range(min, max);
            }

            // 2. 启发式剪枝 (Heuristic Rejection): g_hat + h_hat < cMax
            // 如果连直线距离都比当前最差解长，那就别试了 (Informed RRT* 原理)
            if (Distance(q_start, q) + Distance(q, q_goal) >= cMax) continue;

            // 3. 笛卡尔空间限制 (Workspace Rejection)
            if (useWorkspaceLimits)
            {
                if (!CheckCartesianLimits(q)) continue;
            }


            RecordSampleForDebug(q);    
            // 通过筛选
            result.Add(new BITNode(-1, q, double.MaxValue, null));
        }
        return result;
    }

    // 剪枝：移除 samples 中不再可能优化当前解的点
    private void Prune(ref List<BITNode> samples, double cMax)
    {
        if (cMax == double.MaxValue) return;
        // 只保留 f_hat < cMax 的点
        samples = samples.Where(s => Distance(q_start, s.q) + Distance(s.q, q_goal) < cMax).ToList();
    }

    // -------------------------------------------------------------------------
    // 物理与几何辅助
    // -------------------------------------------------------------------------
    
    // 路径拉直 (Shortcutting)
    private List<double[]> ShortcutPath(List<double[]> path, int maxTrials = 50)
    {
        if (path.Count < 3) return path;
        List<double[]> newPath = new List<double[]>(path);

        for (int k = 0; k < maxTrials; k++)
        {
            if (newPath.Count < 3) break;
            int i = UnityEngine.Random.Range(0, newPath.Count - 2);
            int j = UnityEngine.Random.Range(i + 2, newPath.Count); // 至少隔一个点

            if (IsValidConnection(newPath[i], newPath[j]))
            {
                // 移除 i+1 到 j-1 之间的点
                newPath.RemoveRange(i + 1, j - i - 1);
            }
        }
        return newPath;
    }

    private unsafe bool CheckCartesianLimits(double[] q)
    {
        // 备份当前状态
        double[] backup = new double[MjScene.Instance.Model->nq];
        for(int i=0; i<backup.Length; i++) backup[i] = MjScene.Instance.Data->qpos[i];

        // 设置 q
        ApplyConfig(q);
        MujocoLib.mj_kinematics(MjScene.Instance.Model, MjScene.Instance.Data);

        // 获取 Site 位置
        double x = MjScene.Instance.Data->site_xpos[siteId * 3 + 0]; // MuJoCo X
        double y = MjScene.Instance.Data->site_xpos[siteId * 3 + 1]; // MuJoCo Y
        double z = MjScene.Instance.Data->site_xpos[siteId * 3 + 2]; // MuJoCo Z

        // 还原
        for(int i=0; i<backup.Length; i++) MjScene.Instance.Data->qpos[i] = backup[i];

        // Unity 坐标系转换 (MuJoCo X=Unity X, MuJoCo Z=Unity Y, MuJoCo Y=Unity Z)
        // 你的 Python 代码 limits 是基于 Unity Inspector 看到的坐标
        // 假设 siteId 对应的就是末端执行器
        // MuJoCo 原生坐标:
        // 如果 MuJoCo Z 是向上 (通常如此), 对应 Unity Y
        
        // 简单判定: 将 MuJoCo 坐标直接与 Limit 对比 (需要确认坐标轴一致性)
        // 假设我们直接用 MuJoCo 的原生值对比 (注意轴向)
        // x, y, z 是 MuJoCo 的世界坐标
        
        // 在 Unity 中，位置通常是 Vector3(x, y, z)。MuJoCo插件会自动转换
        // 为了保险，我们用简单的逻辑：
        // 如果 Unity Y 是高度，对应 MuJoCo Z
        
        bool ok = (x >= minLimit.x && x <= maxLimit.x) &&  // X轴
                  (z >= minLimit.y && z <= maxLimit.y) &&  // 高度 (Unity Y / MuJoCo Z)
                  (y >= minLimit.z && y <= maxLimit.z);    // 深度 (Unity Z / MuJoCo Y)

        // 👇👇👇 加这行 Log 抓现行 👇👇👇
        if (!ok) Debug.LogWarning($"🚫 采样点因超出范围被剔除: UnityPos({x:F2}, {z:F2}, {y:F2})");

        return ok;
    }

    private unsafe bool IsValidConfig(double[] q)
    {
        // 1. 备份当前姿态
        double[] backup = new double[MjScene.Instance.Model->nq];
        for(int i=0; i<backup.Length; i++) backup[i] = MjScene.Instance.Data->qpos[i];

        // 2. 设置新姿态并计算物理
        ApplyConfig(q);
        MujocoLib.mj_kinematics(MjScene.Instance.Model, MjScene.Instance.Data);
        MujocoLib.mj_comPos(MjScene.Instance.Model, MjScene.Instance.Data);
        MujocoLib.mj_fwdPosition(MjScene.Instance.Model, MjScene.Instance.Data); // 计算接触点

        int ncon = MjScene.Instance.Data->ncon;
        bool collision = false;

        for (int i = 0; i < ncon; i++)
        {
            // 获取碰撞深度 (负数表示穿透)
            double dist = MjScene.Instance.Data->contact[i].dist;
            
            // 🚨 【修改1】 容差从 -0.001 改为 -0.02 (允许 2cm 的穿模)
            // 运动规划不需要像物理模拟那么精确，稍微蹭一点没关系
            if (dist < -0.02f) 
            { 
                // 获取碰撞体的 ID
                int geom1 = MjScene.Instance.Data->contact[i].geom1;
                int geom2 = MjScene.Instance.Data->contact[i].geom2;

                // 获取名字
                string n1 = MujocoLib.mj_id2name(MjScene.Instance.Model, (int)MujocoLib.mjtObj.mjOBJ_GEOM, geom1) ?? "";
                string n2 = MujocoLib.mj_id2name(MjScene.Instance.Model, (int)MujocoLib.mjtObj.mjOBJ_GEOM, geom2) ?? "";
                
                // 转小写方便比较
                n1 = n1.ToLower();
                n2 = n2.ToLower();

                // 🚨 【修改2】 忽略地板/地面碰撞！(这是 AGV 能动的关键)
                // 只要碰撞的一方名字里包含 floor, ground, plane，我们就当没看见
                if (n1.Contains("floor") || n1.Contains("ground") || n1.Contains("plane") ||
                    n2.Contains("floor") || n2.Contains("ground") || n2.Contains("plane"))
                {
                    continue; // 跳过，不算碰撞
                }
                
                // 也可以忽略轮子本身 (假设轮子名字叫 wheel)
                if (n1.Contains("wheel") || n2.Contains("wheel"))
                {
                    continue; 
                }

                // 只有真的撞墙了，才标记为 true
                collision = true; 
                break; 
            }
        }

        // 3. 还原姿态
        for(int i=0; i<backup.Length; i++) MjScene.Instance.Data->qpos[i] = backup[i];
        MujocoLib.mj_kinematics(MjScene.Instance.Model, MjScene.Instance.Data);
        
        return !collision;
    }
    private bool IsValidConnection(double[] from, double[] to)
    {
        double dist = Distance(from, to);
        int steps = (int)(dist / 0.1f) + 2; // 步长检测
        double[] temp = new double[nv];

        for (int i = 1; i < steps; i++)
        {
            float t = (float)i / steps;
            for (int j = 0; j < nv; j++) temp[j] = from[j] + (to[j] - from[j]) * t;
            if (!IsValidConfig(temp)) return false;
        }
        return true;
    }

    // -------------------------------------------------------------------------
    // 基础工具
    // -------------------------------------------------------------------------
    private double CalculateRadius(int q)
    {
        // 原有逻辑
        if (q <= 1) return double.MaxValue;
        double term = Math.Log(q) / q;
        double r = eta * 2.5 * Math.Pow(term, 1.0 / nv); 
        
        // 🔥【核心修复】兜底逻辑：
        // 在 10维空间，r 算出来可能只有 2.0，但这根本够不着邻居。
        // 强制它至少要有 6.0 (或者更大，取决于你的地图尺度)
        // 这里的 6.0 是一个经验值，代表“你认为两个随机点之间最大允许的跳跃距离”
        return Math.Max(r, 1);    }

    private double Distance(double[] a, double[] b)
    {
        // 加权欧几里得距离 (惩罚大臂移动)
        double sum = 0;
        // 权重：前3个关节(大臂) = 4.0, 后面的 = 1.0
        for (int i = 0; i < nv; i++)
        {
            double w = (i < 3) ? 4.0 : 1.0; 
            double d = (a[i] - b[i]) * w;
            sum += d * d;
        }
        return Math.Sqrt(sum);
    }

    private unsafe bool InitSystem()
    {
        if (MjScene.Instance.Model == null) return false;
        nv = actuators.Count;
        siteId = MujocoLib.mj_name2id(MjScene.Instance.Model, (int)MujocoLib.mjtObj.mjOBJ_SITE, ikSolver.endEffectorSite.name);
        
        q_start = new double[nv];
        for (int i = 0; i < nv; i++)
        {
            var act = actuators[i];
            if (act.Joint is MjHingeJoint h) q_start[i] = MjScene.Instance.Data->qpos[h.QposAddress];
            else if (act.Joint is MjSlideJoint s) q_start[i] = MjScene.Instance.Data->qpos[s.QposAddress];
        }
        return true;
    }

    private unsafe void ApplyConfig(double[] q)
    {
        for (int i = 0; i < nv; i++)
        {
            var act = actuators[i];
            if (act.Joint is MjHingeJoint h) MjScene.Instance.Data->qpos[h.QposAddress] = q[i];
            else if (act.Joint is MjSlideJoint s) MjScene.Instance.Data->qpos[s.QposAddress] = q[i];
        }
    }
    private double[] ExtractCompactState(double[] fullQpos)
    {
        double[] compact = new double[nv];
        for (int i = 0; i < nv; i++)
        {
            var act = actuators[i];
            int addr = -1;
            if (act.Joint is MjHingeJoint h) addr = h.QposAddress;
            else if (act.Joint is MjSlideJoint s) addr = s.QposAddress;
            if (addr != -1) compact[i] = fullQpos[addr];
        }
        return compact;
    }

    private List<double[]> ReconstructPath(BITNode node)
    {
        List<double[]> path = new List<double[]>();
        while (node != null)
        {
            path.Add(node.q);
            node = node.parent;
        }
        path.Reverse();
        path.Add(q_goal); // 确保终点精确
        return path;
    }

    // -------------------------------------------------------------------------
    // 内部类：数据结构
    // -------------------------------------------------------------------------
    private class BITNode
    {
        public int id;
        public double[] q;
        public double gScore;
        public BITNode parent;
        public BITNode(int id, double[] q, double g, BITNode p) { this.id = id; this.q = q; this.gScore = g; this.parent = p; }
    }

    private struct Vertex : IComparable<Vertex>
    {
        public BITNode node;
        public double fScore;
        public Vertex(BITNode n, double f) { node = n; fScore = f; }
        public int CompareTo(Vertex other) => fScore.CompareTo(other.fScore);
    }

    private struct Edge : IComparable<Edge>
    {
        public BITNode parent;
        public double[] childQ;
        public double fScore;
        public double gScore;
        public bool isGoal;
        public Edge(BITNode p, double[] c, double f, double g, bool goal) { parent = p; childQ = c; fScore = f; gScore = g; isGoal = goal; }
        public int CompareTo(Edge other) => fScore.CompareTo(other.fScore);
    }

    // 简易最小堆
    private class MinHeap<T> where T : IComparable<T>
    {
        private List<T> elements = new List<T>();
        public int Count => elements.Count;
        public bool IsEmpty() => elements.Count == 0;
        public void Clear() => elements.Clear();
        public T Peek() => elements[0];

        public void Push(T item)
        {
            elements.Add(item);
            int i = elements.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (elements[i].CompareTo(elements[parent]) >= 0) break;
                (elements[i], elements[parent]) = (elements[parent], elements[i]);
                i = parent;
            }
        }

        public T Pop()
        {
            T best = elements[0];
            elements[0] = elements[elements.Count - 1];
            elements.RemoveAt(elements.Count - 1);
            int i = 0;
            while (true)
            {
                int left = 2 * i + 1;
                if (left >= elements.Count) break;
                int right = left + 1;
                int minChild = (right < elements.Count && elements[right].CompareTo(elements[left]) < 0) ? right : left;
                if (elements[i].CompareTo(elements[minChild]) <= 0) break;
                (elements[i], elements[minChild]) = (elements[minChild], elements[i]);
                i = minChild;
            }
            return best;
        }
    }

    // =========================================================
    // 🎨 DEBUG: 采样点可视化 (Gizmos)
    // =========================================================
    
    // 存储采样点的位置 (仅用于 Debug 显示)
    private List<Vector3> debugSamplePoints = new List<Vector3>();

    // 在 SampleInformed 函数里调用这个
    private void RecordSampleForDebug(double[] q)
    {
        // 这是一个低效但直观的方法：用 FK 算一下这个采样点的末端位置
        // 注意：这只为了在 Scene 窗口画个绿点看看
        
        // 1. 备份当前姿态
        double[] backup = new double[nv];
        unsafe { for(int i=0; i<nv; i++) backup[i] = MjScene.Instance.Data->qpos[i]; }

        // 2. 应用采样姿态并计算 FK
        ApplyConfig(q);
        unsafe {
            MujocoLib.mj_kinematics(MjScene.Instance.Model, MjScene.Instance.Data);
            double x = MjScene.Instance.Data->site_xpos[siteId * 3 + 0];
            double y = MjScene.Instance.Data->site_xpos[siteId * 3 + 1];
            double z = MjScene.Instance.Data->site_xpos[siteId * 3 + 2];
            
            // 转 Unity 坐标 (MuJoCo Z-up -> Unity Y-up)
            debugSamplePoints.Add(new Vector3((float)x, (float)z, (float)y));
        }

        // 3. 恢复姿态
        unsafe { for(int i=0; i<nv; i++) MjScene.Instance.Data->qpos[i] = backup[i]; }
    }

    // Unity 自动调用的画图函数
    void OnDrawGizmos()
    {
        if (debugSamplePoints != null)
        {
            Gizmos.color = new Color(0, 1, 0, 0.3f); // 半透明绿色
            foreach (var p in debugSamplePoints)
            {
                Gizmos.DrawSphere(p, 0.03f); // 画小绿球
            }
        }
        
        // 画出起点和终点
        if (ikSolver != null && ikSolver.endEffectorSite != null)
        {
             Gizmos.color = Color.blue;
             // ... 这里如果存了起点位置也可以画 ...
        }
    }





}