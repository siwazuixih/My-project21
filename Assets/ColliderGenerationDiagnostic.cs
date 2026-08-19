using System.Collections;
using System.Collections.Generic;
using System.Text;
using Mujoco;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ColliderGenerationDiagnostic : MonoBehaviour
{
    [Header("诊断对象（通常由生成器自动填写）")]
    [SerializeField] private GameObject modelRoot;
    [SerializeField] private Transform robotCollisionRoot;
    [SerializeField] private Transform robotPoseTarget;

    [Header("诊断选项")]
    [SerializeField] private bool showGeneratedHulls = true;
    [SerializeField] private bool checkPhysXOverlap = true;
    [SerializeField, Min(1)] private int maxDetailedOverlapLogs = 20;
    [SerializeField, Min(0f)] private float movementWarningDistance = 0.01f;
    [SerializeField, Range(0f, 180f)] private float movementWarningAngle = 1f;

    [Header("最近一次结果（只读观察）")]
    [SerializeField] private int sourceMeshCount;
    [SerializeField] private int theoreticalHullLimit;
    [SerializeField] private int generatedHullCount;
    [SerializeField] private int generatedVertexCount;
    [SerializeField] private int generatedTriangleCount;
    [SerializeField] private int repairedNormalMeshCount;
    [SerializeField] private int originalMeshColliderCount;
    [SerializeField] private int finalMeshColliderCount;
    [SerializeField] private int finalMujocoGeomCount;
    [SerializeField] private int boundsOverlapCount;
    [SerializeField] private int confirmedPenetrationCount;
    [SerializeField] private int mujocoSceneRecreationCount;

    private sealed class HullRecord
    {
        public GameObject GameObject;
        public Mesh Mesh;
        public string SourceName;
    }

    private sealed class SourceRecord
    {
        public string Name;
        public string Path;
        public int HullCount;
        public double ElapsedMilliseconds;
        public Vector3 LossyScale;
        public Vector3 LocalMeshSize;
        public Vector3 WorldBoundsSize;
    }

    private readonly List<HullRecord> _hulls = new List<HullRecord>();
    private readonly List<SourceRecord> _sources = new List<SourceRecord>();
    private readonly List<Material> _paletteMaterials = new List<Material>();
    private Material _overlapMaterial;
    private Vector3 _initialRobotPosition;
    private Quaternion _initialRobotRotation;
    private bool _hasInitialRobotPose;
    private bool _createdUnityPhysics;
    private bool _createdMujocoGeoms;
    private bool _forcedAllMeshesThroughVhacd;
    private float _generationStartedAt;
    private Coroutine _pendingAnalysis;
    private MjScene _observedMujocoScene;

    private static readonly Color[] Palette =
    {
        new Color(0.10f, 0.85f, 1.00f, 0.32f),
        new Color(0.20f, 1.00f, 0.35f, 0.32f),
        new Color(1.00f, 0.80f, 0.10f, 0.32f),
        new Color(0.90f, 0.25f, 1.00f, 0.32f),
        new Color(1.00f, 0.45f, 0.10f, 0.32f),
        new Color(0.20f, 0.45f, 1.00f, 0.32f),
        new Color(0.30f, 1.00f, 0.80f, 0.32f),
        new Color(1.00f, 0.30f, 0.55f, 0.32f)
    };

    public bool ShowGeneratedHulls => showGeneratedHulls;
    public int GeneratedHullCount => generatedHullCount;

    public void BeginGeneration(
        GameObject root,
        Transform collisionRoot,
        Transform poseTarget,
        int meshCount,
        int maxHullsPerMesh,
        bool createUnityPhysics,
        bool createMujocoGeoms,
        bool forceAllMeshesThroughVhacd)
    {
        modelRoot = root;
        robotCollisionRoot = collisionRoot;
        robotPoseTarget = poseTarget != null ? poseTarget : collisionRoot;
        sourceMeshCount = meshCount;
        theoreticalHullLimit = Mathf.Max(0, meshCount) * Mathf.Max(0, maxHullsPerMesh);
        _createdUnityPhysics = createUnityPhysics;
        _createdMujocoGeoms = createMujocoGeoms;
        _forcedAllMeshesThroughVhacd = forceAllMeshesThroughVhacd;
        _generationStartedAt = Time.realtimeSinceStartup;

        _hulls.Clear();
        _sources.Clear();
        generatedHullCount = 0;
        generatedVertexCount = 0;
        generatedTriangleCount = 0;
        repairedNormalMeshCount = 0;
        boundsOverlapCount = 0;
        confirmedPenetrationCount = 0;
        mujocoSceneRecreationCount = 0;
        originalMeshColliderCount = CountNonGeneratedMeshColliders(root);

        StopObservingMujocoScene();
        if (createMujocoGeoms && MjScene.InstanceExists)
        {
            _observedMujocoScene = MjScene.Instance;
            _observedMujocoScene.preDestroyEvent += OnMujocoScenePreDestroy;
        }

        _hasInitialRobotPose = robotPoseTarget != null;
        if (_hasInitialRobotPose)
        {
            _initialRobotPosition = robotPoseTarget.position;
            _initialRobotRotation = robotPoseTarget.rotation;
        }
    }

    public void RecordHull(GameObject hullObject, Mesh mesh, MeshFilter source, bool normalsWereRepaired)
    {
        if (hullObject == null || mesh == null) return;

        _hulls.Add(new HullRecord
        {
            GameObject = hullObject,
            Mesh = mesh,
            SourceName = source != null ? source.name : "未知零件"
        });

        generatedHullCount++;
        generatedVertexCount += mesh.vertexCount;
        generatedTriangleCount += mesh.triangles.Length / 3;
        if (normalsWereRepaired) repairedNormalMeshCount++;

        if (showGeneratedHulls)
        {
            EnsureHullRenderer(hullObject, mesh, generatedHullCount - 1);
        }
    }

    public void RecordSource(MeshFilter source, int hullsCreated, double elapsedMilliseconds)
    {
        Mesh sourceMesh = source != null ? source.sharedMesh : null;
        Renderer sourceRenderer = source != null ? source.GetComponent<Renderer>() : null;
        _sources.Add(new SourceRecord
        {
            Name = source != null ? source.name : "未知零件",
            Path = source != null ? GetPath(source.transform) : "未知路径",
            HullCount = hullsCreated,
            ElapsedMilliseconds = elapsedMilliseconds,
            LossyScale = source != null ? source.transform.lossyScale : Vector3.one,
            LocalMeshSize = sourceMesh != null ? sourceMesh.bounds.size : Vector3.zero,
            WorldBoundsSize = sourceRenderer != null
                ? sourceRenderer.bounds.size
                : CalculateWorldBoundsSize(source != null ? source.transform : null, sourceMesh)
        });
    }

    public void CompleteGeneration()
    {
        RefreshComponentCounts();
        LogGenerationSummary();

        if (_pendingAnalysis != null)
        {
            StopCoroutine(_pendingAnalysis);
            _pendingAnalysis = null;
        }

        if (Application.isPlaying)
        {
            _pendingAnalysis = StartCoroutine(AnalyzeAfterPhysicsStep());
        }
        else
        {
            AnalyzeCurrentState();
        }
    }

    [ContextMenu("重新检查当前碰撞体与机器人重叠")]
    public void RunDiagnosisNow()
    {
        if (modelRoot == null) modelRoot = gameObject;
        RebuildHullListFromHierarchy();
        RefreshComponentCounts();
        AnalyzeCurrentState();
    }

    private IEnumerator AnalyzeAfterPhysicsStep()
    {
        yield return null;
        yield return new WaitForFixedUpdate();
        yield return null;
        _pendingAnalysis = null;
        AnalyzeCurrentState();
    }

    private void AnalyzeCurrentState()
    {
        if (modelRoot == null)
        {
            Debug.LogWarning("[碰撞诊断] 模型根对象为空，无法检查。", this);
            return;
        }

        if (_hulls.Count == 0) RebuildHullListFromHierarchy();
        ResetHullColors();
        boundsOverlapCount = 0;
        confirmedPenetrationCount = 0;

        if (checkPhysXOverlap && _createdUnityPhysics)
        {
            CheckRobotOverlap();
        }

        float movedDistance = 0f;
        float movedAngle = 0f;
        if (_hasInitialRobotPose && robotPoseTarget != null)
        {
            movedDistance = Vector3.Distance(_initialRobotPosition, robotPoseTarget.position);
            movedAngle = Quaternion.Angle(_initialRobotRotation, robotPoseTarget.rotation);
        }

        string overlapResult = _createdUnityPhysics
            ? $"包围盒疑似重叠={boundsOverlapCount}，PhysX确认穿透={confirmedPenetrationCount}"
            : "未创建 PhysX 碰撞体，已跳过 PhysX 重叠检查";
        Debug.Log(
            $"[碰撞诊断] 检查结束：{overlapResult}；机器人位移={movedDistance:F4} m，转角={movedAngle:F2}°；" +
            $"生成期间检测到 MjScene 重建={mujocoSceneRecreationCount} 次。",
            this);

        if (_hasInitialRobotPose &&
            (movedDistance > movementWarningDistance || movedAngle > movementWarningAngle))
        {
            string likelyCause = confirmedPenetrationCount > 0
                ? "已同时确认 PhysX 穿透，优先检查标红凸包与机器人碰撞体。"
                : mujocoSceneRecreationCount > 0
                    ? "未确认 PhysX 穿透但发生了 MjScene 重建，优先检查 MuJoCo重建和状态恢复。"
                    : "当前没有足够证据区分瞬时碰撞与其他脚本改写位姿，请结合逐帧记录继续排查。";
            Debug.LogError(
                $"[碰撞诊断] 生成期间机器人发生明显位姿变化：{movedDistance:F4} m / {movedAngle:F2}°。{likelyCause}",
                this);
        }

        if (boundsOverlapCount > 0)
        {
            Debug.LogWarning(
                "[碰撞诊断] 红色凸包与机器人碰撞体的世界包围盒相交。" +
                "如果“PhysX确认穿透”为0，仍不能排除 MuJoCo 接触；非凸 MeshCollider 之间无法全部由 Physics.ComputePenetration 精确确认。",
                this);
        }

        StopObservingMujocoScene();
    }

    private void CheckRobotOverlap()
    {
        if (robotCollisionRoot == null)
        {
            Debug.LogWarning(
                "[碰撞诊断] 没有找到 MissionController，无法自动确定机器人根对象。" +
                "可在本组件的 Robot Collision Root 中手动拖入机器人根节点后重新检查。",
                this);
            return;
        }

        Physics.SyncTransforms();
        Collider[] robotColliders = robotCollisionRoot.GetComponentsInChildren<Collider>(true);
        int detailedLogs = 0;

        for (int i = 0; i < _hulls.Count; i++)
        {
            HullRecord hull = _hulls[i];
            if (hull.GameObject == null) continue;
            Collider hullCollider = hull.GameObject.GetComponent<Collider>();
            if (hullCollider == null || !hullCollider.enabled || !hullCollider.gameObject.activeInHierarchy) continue;

            bool hullHasBoundsOverlap = false;
            for (int j = 0; j < robotColliders.Length; j++)
            {
                Collider robotCollider = robotColliders[j];
                if (robotCollider == null || !robotCollider.enabled || !robotCollider.gameObject.activeInHierarchy) continue;
                if (!hullCollider.bounds.Intersects(robotCollider.bounds)) continue;

                if (!hullHasBoundsOverlap)
                {
                    boundsOverlapCount++;
                    hullHasBoundsOverlap = true;
                    MarkHullAsOverlapping(hull.GameObject);
                }

                Vector3 direction;
                float distance;
                bool penetrates = false;
                try
                {
                    penetrates = Physics.ComputePenetration(
                        hullCollider,
                        hullCollider.transform.position,
                        hullCollider.transform.rotation,
                        robotCollider,
                        robotCollider.transform.position,
                        robotCollider.transform.rotation,
                        out direction,
                        out distance);
                }
                catch (System.Exception exception)
                {
                    direction = Vector3.zero;
                    distance = 0f;
                    if (detailedLogs < maxDetailedOverlapLogs)
                    {
                        Debug.LogWarning($"[碰撞诊断] 穿透计算失败：{exception.Message}", this);
                    }
                }

                if (penetrates)
                {
                    confirmedPenetrationCount++;
                    if (detailedLogs < maxDetailedOverlapLogs)
                    {
                        Debug.LogError(
                            $"[碰撞诊断] 确认穿透：凸包={GetPath(hull.GameObject.transform)}，" +
                            $"来源零件={hull.SourceName}，机器人碰撞体={GetPath(robotCollider.transform)}，" +
                            $"分离深度={distance:F6} m，分离方向={direction}。",
                            hull.GameObject);
                        detailedLogs++;
                    }
                }
                else if (detailedLogs < maxDetailedOverlapLogs)
                {
                    Debug.LogWarning(
                        $"[碰撞诊断] 包围盒重叠但未被 PhysX 精确确认：凸包={GetPath(hull.GameObject.transform)}，" +
                        $"来源零件={hull.SourceName}，机器人碰撞体={GetPath(robotCollider.transform)}。",
                        hull.GameObject);
                    detailedLogs++;
                }
            }
        }

        if (detailedLogs >= maxDetailedOverlapLogs)
        {
            Debug.LogWarning($"[碰撞诊断] 详细重叠日志已限制为前 {maxDetailedOverlapLogs} 条，避免控制台刷屏。", this);
        }
    }

    private void LogGenerationSummary()
    {
        float elapsedSeconds = Time.realtimeSinceStartup - _generationStartedAt;
        StringBuilder summary = new StringBuilder(512);
        summary.Append("[碰撞诊断] 生成统计：模型=").Append(modelRoot != null ? modelRoot.name : "空")
            .Append("，源Mesh=").Append(sourceMeshCount)
            .Append("，理论凸包上限=").Append(theoreticalHullLimit)
            .Append("，实际凸包=").Append(generatedHullCount)
            .Append("，顶点=").Append(generatedVertexCount)
            .Append("，三角面=").Append(generatedTriangleCount)
            .Append("，耗时=").Append(elapsedSeconds.ToString("F2")).Append(" s")
            .Append("，原有MeshCollider=").Append(originalMeshColliderCount)
            .Append("，生成后MeshCollider=").Append(finalMeshColliderCount)
            .Append("，生成后MjGeom=").Append(finalMujocoGeomCount)
            .Append("，MjScene重建=").Append(mujocoSceneRecreationCount)
            .Append("，补算法线的凸包=").Append(repairedNormalMeshCount).Append("。");
        Debug.Log(summary.ToString(), this);

        _sources.Sort(delegate(SourceRecord a, SourceRecord b)
        {
            int hullCompare = b.HullCount.CompareTo(a.HullCount);
            return hullCompare != 0 ? hullCompare : b.ElapsedMilliseconds.CompareTo(a.ElapsedMilliseconds);
        });

        int topCount = Mathf.Min(10, _sources.Count);
        if (topCount > 0)
        {
            StringBuilder detail = new StringBuilder(384);
            detail.Append("[碰撞诊断] 凸包最多/耗时较长的零件（前").Append(topCount).Append("个）：");
            for (int i = 0; i < topCount; i++)
            {
                SourceRecord source = _sources[i];
                detail.Append("\n  ").Append(i + 1).Append(". ").Append(source.Name)
                    .Append("：").Append(source.HullCount).Append("个凸包，")
                    .Append(source.ElapsedMilliseconds.ToString("F1")).Append(" ms");
            }
            Debug.Log(detail.ToString(), this);
        }

        LogSourceTransformDiagnostics();

        if (_forcedAllMeshesThroughVhacd && sourceMeshCount > 1)
        {
            Debug.LogWarning(
                $"[碰撞诊断] 当前启用了“所有 Mesh 强制 V-HACD”：{sourceMeshCount} 个装配零件会分别分解，" +
                $"最多可能得到 {theoreticalHullLimit} 个凸包。装配体越细碎，生成和运行负担越大。",
                this);
        }

        if (_createdUnityPhysics && _createdMujocoGeoms)
        {
            Debug.LogWarning(
                "[碰撞诊断] 当前同一批凸包同时创建了 MeshCollider/Rigidbody 和 MjGeom。" +
                "排查机器人被推开时，请分别使用“仅 PhysX”和“仅 MuJoCo”模式复现，避免两套物理表示同时干扰判断。",
                this);
        }

        if (_createdMujocoGeoms && Application.isPlaying && mujocoSceneRecreationCount > 1)
        {
            Debug.LogWarning(
                $"[碰撞诊断] 本次仍检测到 {mujocoSceneRecreationCount} 次 MjScene 重建；批量生成正常应只有1次，请检查是否有其他脚本同时增删 MjComponent。",
                this);
        }
        else if (_createdMujocoGeoms && Application.isPlaying && mujocoSceneRecreationCount == 1)
        {
            Debug.Log("[碰撞诊断] MuJoCo碰撞体已批量加入，本次 MjScene仅重建1次。", this);
        }

        if (originalMeshColliderCount > 0 && _createdUnityPhysics)
        {
            Debug.LogWarning(
                $"[碰撞诊断] 模型生成前已经有 {originalMeshColliderCount} 个非生成 MeshCollider，" +
                "现在又添加了分解后的 MeshCollider；原始整网格和凸包可能形成重复碰撞表示。",
                this);
        }

        if (repairedNormalMeshCount > 0)
        {
            Debug.LogWarning(
                $"[碰撞诊断] 有 {repairedNormalMeshCount} 个 V-HACD 网格缺少完整法线，已在交给 MjMeshShape 前补算。" +
                "这可避免 Scene 视图持续出现“Gizmos.DrawMesh requires a mesh with positions and normals”并造成日志卡顿。",
                this);
        }
    }

    private void LogSourceTransformDiagnostics()
    {
        const float scaleTolerance = 0.0001f;
        const int maximumDetailedSources = 20;
        int nonUnitScaleCount = 0;
        StringBuilder detail = new StringBuilder(1024);

        for (int i = 0; i < _sources.Count; i++)
        {
            SourceRecord source = _sources[i];
            Vector3 scale = source.LossyScale;
            bool hasNonUnitScale = Mathf.Abs(scale.x - 1f) > scaleTolerance ||
                                   Mathf.Abs(scale.y - 1f) > scaleTolerance ||
                                   Mathf.Abs(scale.z - 1f) > scaleTolerance;
            if (!hasNonUnitScale) continue;

            nonUnitScaleCount++;
            if (nonUnitScaleCount > maximumDetailedSources) continue;
            detail.Append("\n  ").Append(nonUnitScaleCount).Append(". ").Append(source.Path)
                .Append("；lossyScale=").Append(FormatVector(source.LossyScale))
                .Append("，Mesh局部尺寸=").Append(FormatVector(source.LocalMeshSize))
                .Append("，Renderer世界包围盒=").Append(FormatVector(source.WorldBoundsSize));
        }

        if (nonUnitScaleCount == 0)
        {
            Debug.Log("[MuJoCo缩放诊断] 所有参与生成的源Mesh运行时lossyScale均接近(1,1,1)。", this);
            return;
        }

        StringBuilder message = new StringBuilder(1280);
        message.Append("[MuJoCo缩放诊断] ").Append(nonUnitScaleCount)
            .Append("个源Mesh存在非单位世界缩放。MjMeshShape导出mesh.vertices时不会自动写入Transform缩放；")
            .Append("以下数据用于判断运行时导入器是否已把比例烘焙进网格：")
            .Append(detail);
        if (nonUnitScaleCount > maximumDetailedSources)
            message.Append("\n  其余").Append(nonUnitScaleCount - maximumDetailedSources).Append("个已省略，避免日志刷屏。");
        Debug.LogWarning(message.ToString(), this);
    }

    private static Vector3 CalculateWorldBoundsSize(Transform meshTransform, Mesh mesh)
    {
        if (meshTransform == null || mesh == null) return Vector3.zero;

        Bounds localBounds = mesh.bounds;
        Vector3 center = localBounds.center;
        Vector3 extents = localBounds.extents;
        Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 localCorner = center + Vector3.Scale(extents, new Vector3(x, y, z));
            Vector3 worldCorner = meshTransform.TransformPoint(localCorner);
            minimum = Vector3.Min(minimum, worldCorner);
            maximum = Vector3.Max(maximum, worldCorner);
        }

        return maximum - minimum;
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F6}, {value.y:F6}, {value.z:F6})";
    }

    private void RefreshComponentCounts()
    {
        if (modelRoot == null) return;
        finalMeshColliderCount = modelRoot.GetComponentsInChildren<MeshCollider>(true).Length;
        finalMujocoGeomCount = modelRoot.GetComponentsInChildren<MjGeom>(true).Length;
    }

    private void RebuildHullListFromHierarchy()
    {
        _hulls.Clear();
        generatedHullCount = 0;
        generatedVertexCount = 0;
        generatedTriangleCount = 0;
        if (modelRoot == null) return;

        HashSet<GameObject> visited = new HashSet<GameObject>();
        MeshFilter[] meshFilters = modelRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter filter = meshFilters[i];
            if (filter == null || filter.sharedMesh == null || !IsUnderGeneratedRoot(filter.transform)) continue;
            AddRebuiltHull(filter.gameObject, filter.sharedMesh, visited);
        }

        MeshCollider[] colliders = modelRoot.GetComponentsInChildren<MeshCollider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            MeshCollider collider = colliders[i];
            if (collider == null || collider.sharedMesh == null || !IsUnderGeneratedRoot(collider.transform)) continue;
            AddRebuiltHull(collider.gameObject, collider.sharedMesh, visited);
        }
    }

    private void AddRebuiltHull(GameObject hullObject, Mesh mesh, HashSet<GameObject> visited)
    {
        if (!visited.Add(hullObject)) return;
        _hulls.Add(new HullRecord
        {
            GameObject = hullObject,
            Mesh = mesh,
            SourceName = hullObject.transform.parent != null && hullObject.transform.parent.parent != null
                ? hullObject.transform.parent.parent.name
                : "未知零件"
        });
        generatedHullCount++;
        generatedVertexCount += mesh.vertexCount;
        generatedTriangleCount += mesh.triangles.Length / 3;
    }

    private int CountNonGeneratedMeshColliders(GameObject root)
    {
        if (root == null) return 0;
        int count = 0;
        MeshCollider[] colliders = root.GetComponentsInChildren<MeshCollider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (!IsUnderGeneratedRoot(colliders[i].transform)) count++;
        }
        return count;
    }

    private bool IsUnderGeneratedRoot(Transform target)
    {
        Transform current = target;
        while (current != null)
        {
            if (current.name.Contains("_MjRoot")) return true;
            if (modelRoot != null && current.gameObject == modelRoot) break;
            current = current.parent;
        }
        return false;
    }

    private void EnsureHullRenderer(GameObject hullObject, Mesh mesh, int paletteIndex)
    {
        MeshFilter meshFilter = hullObject.GetComponent<MeshFilter>();
        if (meshFilter == null) meshFilter = hullObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer = hullObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = hullObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = GetPaletteMaterial(paletteIndex);
    }

    private void ResetHullColors()
    {
        if (!showGeneratedHulls) return;
        for (int i = 0; i < _hulls.Count; i++)
        {
            HullRecord hull = _hulls[i];
            if (hull.GameObject == null || hull.Mesh == null) continue;
            EnsureHullRenderer(hull.GameObject, hull.Mesh, i);
        }
    }

    private void MarkHullAsOverlapping(GameObject hullObject)
    {
        if (!showGeneratedHulls || hullObject == null) return;
        MeshRenderer renderer = hullObject.GetComponent<MeshRenderer>();
        if (renderer != null) renderer.sharedMaterial = GetOverlapMaterial();
    }

    private Material GetPaletteMaterial(int index)
    {
        while (_paletteMaterials.Count < Palette.Length)
        {
            int colorIndex = _paletteMaterials.Count;
            _paletteMaterials.Add(CreateTransparentMaterial(Palette[colorIndex], "ColliderDiagnosticPalette" + colorIndex));
        }
        return _paletteMaterials[Mathf.Abs(index) % _paletteMaterials.Count];
    }

    private Material GetOverlapMaterial()
    {
        if (_overlapMaterial == null)
        {
            _overlapMaterial = CreateTransparentMaterial(new Color(1f, 0.05f, 0.05f, 0.68f), "ColliderDiagnosticOverlap");
        }
        return _overlapMaterial;
    }

    private static Material CreateTransparentMaterial(Color color, string materialName)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Hidden/InternalErrorShader");

        Material material = new Material(shader)
        {
            name = materialName,
            color = color,
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = 3000
        };
        return material;
    }

    private static string GetPath(Transform target)
    {
        if (target == null) return "空";
        StringBuilder path = new StringBuilder(target.name);
        Transform current = target.parent;
        while (current != null)
        {
            path.Insert(0, current.name + "/");
            current = current.parent;
        }
        return path.ToString();
    }

    private void OnMujocoScenePreDestroy(object sender, MjStepArgs args)
    {
        mujocoSceneRecreationCount++;
    }

    private void StopObservingMujocoScene()
    {
        if (_observedMujocoScene != null)
        {
            _observedMujocoScene.preDestroyEvent -= OnMujocoScenePreDestroy;
            _observedMujocoScene = null;
        }
    }

    private void OnDestroy()
    {
        StopObservingMujocoScene();
        for (int i = 0; i < _paletteMaterials.Count; i++)
        {
            DestroyMaterial(_paletteMaterials[i]);
        }
        _paletteMaterials.Clear();
        DestroyMaterial(_overlapMaterial);
        _overlapMaterial = null;
    }

    private static void DestroyMaterial(Material material)
    {
        if (material == null) return;
        if (Application.isPlaying) Destroy(material);
        else DestroyImmediate(material);
    }
}
