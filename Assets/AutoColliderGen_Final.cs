using UnityEngine;
using System.Collections.Generic;
using UnityMeshSimplifier; 
using Mujoco; 
using MeshProcess; 
using System.Threading.Tasks;
using System;
using System.Text;

#if UNITY_EDITOR
using UnityEditor; 
#endif

public class AutoColliderGen_Final : MonoBehaviour
{
    [Header("1. 完整场景模型 ")]
    public GameObject targetObject;

    [Header("2. 需切割场景零件 ")]
    [Tooltip("把你想要用 V-HACD 精心掏空的零件拖到这个列表里。\n在这个列表里的走精雕，不在列表里的自动走极速实心挂载！")]
    public List<GameObject> hollowParts = new List<GameObject>();

    [Header("3. 实心零件设置 ")]
    [Tooltip("普通零件的简面质量。越小越快，建议 0.1")]
    [Range(0.01f, 1.0f)] public float simpleQuality = 0.1f; 
    
    [Header("4. 空心零件设置 ")]
    public int hullCount = 32; 
    public int resolution = 100000; 
    [Range(0.0001f, 0.1f)] public double concavity = 0.001;
    public bool useGPU = true;
    [Range(1, 16)] public uint planeDownsampling = 4;
    [Range(1, 16)] public uint hullDownsampling = 4;

    [Header("5. 安全过滤 ")]
    public float minThickness = 0.002f;
    public float minVolume = 1e-6f; 

    [Header("6. 调试选项")]
    public bool visualizeColliders = false; 
    private Material _debugMaterial;

    [Header("7. 碰撞诊断与隔离测试")]
    [Tooltip("生成结束后统计凸包数量、耗时、机器人重叠及位姿变化。诊断只执行一次，不会在 Update 中持续扫描。")]
    public bool enableCollisionDiagnostics = true;
    [Tooltip("保持当前旧逻辑：忽略 hollowParts 列表，让装配体每个 Mesh 都执行 V-HACD。关闭后只有 hollowParts 中的零件会执行 V-HACD。")]
    public bool forceVHACDForAllMeshes = true;
    [Tooltip("创建 Unity MeshCollider 和 Rigidbody。生成 Hull 只作为原模型的碰撞/射线代理，不再独立添加高亮脚本。关闭它可进行仅 MuJoCo 测试。")]
    public bool createUnityPhysicsColliders = true;
    [Tooltip("创建 MuJoCo MjGeom。关闭它可进行仅 PhysX 测试。")]
    public bool createMujocoGeoms = true;
    [Tooltip("运行时先在未激活节点中创建全部 MjBody/MjGeom，最后统一启用，使 MuJoCo 场景只重建一次。")]
    public bool batchMujocoSceneRebuild = true;
    [Tooltip("MuJoCo 批量重建完成后恢复重建前的 qpos/qvel/act/ctrl 等运行状态，防止机器人跳回初始位置。")]
    public bool restoreMujocoStateAfterRebuild = true;
    [Tooltip("批量加入凸包前设置 MuJoCo arena 内存。装配体接触约束较多时，默认 -1 自动值可能过小并导致原生层直接退出。")]
    public string mujocoArenaMemory = "64M";
    [Tooltip("让同一场景模型生成的固定凸包彼此不碰撞，但仍与默认类别的机器人碰撞，减少无意义接触约束。")]
    public bool disableGeneratedGeomSelfCollision = true;

    private ColliderGenerationDiagnostic _diagnostic;
    private MeshFilter _currentSourceFilter;
    private readonly List<GameObject> _pendingGeneratedRoots = new List<GameObject>();

    private sealed class MujocoRuntimeSnapshot
    {
        public double Time;
        public double[] Qpos;
        public double[] Qvel;
        public double[] Act;
        public double[] Ctrl;
        public double[] QaccWarmstart;
        public double[] QfrcApplied;
        public double[] MocapPosition;
        public double[] MocapRotation;

        public static unsafe MujocoRuntimeSnapshot Capture(MjScene scene)
        {
            if (scene == null || scene.Model == null || scene.Data == null) return null;

            return new MujocoRuntimeSnapshot
            {
                Time = scene.Data->time,
                Qpos = Copy(scene.Data->qpos, scene.Model->nq),
                Qvel = Copy(scene.Data->qvel, scene.Model->nv),
                Act = Copy(scene.Data->act, scene.Model->na),
                Ctrl = Copy(scene.Data->ctrl, scene.Model->nu),
                QaccWarmstart = Copy(scene.Data->qacc_warmstart, scene.Model->nv),
                QfrcApplied = Copy(scene.Data->qfrc_applied, scene.Model->nv),
                MocapPosition = Copy(scene.Data->mocap_pos, scene.Model->nmocap * 3),
                MocapRotation = Copy(scene.Data->mocap_quat, scene.Model->nmocap * 4)
            };
        }

        public unsafe bool Restore(MjScene scene)
        {
            if (scene == null || scene.Model == null || scene.Data == null) return false;
            if (Qpos.Length != scene.Model->nq || Qvel.Length != scene.Model->nv ||
                Act.Length != scene.Model->na || Ctrl.Length != scene.Model->nu)
            {
                Debug.LogError(
                    $"[MuJoCo批量重建] 重建前后自由度不一致，已停止自动恢复：" +
                    $"nq {Qpos.Length}->{scene.Model->nq}，nv {Qvel.Length}->{scene.Model->nv}，" +
                    $"na {Act.Length}->{scene.Model->na}，nu {Ctrl.Length}->{scene.Model->nu}。");
                return false;
            }

            scene.Data->time = Time;
            CopyTo(Qpos, scene.Data->qpos);
            CopyTo(Qvel, scene.Data->qvel);
            CopyTo(Act, scene.Data->act);
            CopyTo(Ctrl, scene.Data->ctrl);
            CopyTo(QaccWarmstart, scene.Data->qacc_warmstart);
            CopyTo(QfrcApplied, scene.Data->qfrc_applied);
            if (MocapPosition.Length == scene.Model->nmocap * 3)
                CopyTo(MocapPosition, scene.Data->mocap_pos);
            if (MocapRotation.Length == scene.Model->nmocap * 4)
                CopyTo(MocapRotation, scene.Data->mocap_quat);

            MujocoLib.mj_forward(scene.Model, scene.Data);
            scene.SyncUnityToMjState();
            Debug.Log(
                $"[MuJoCo批量重建] 状态前向计算完成：arena={scene.Model->narena} bytes，" +
                $"ncon={scene.Data->ncon}，nefc={scene.Data->nefc}。");
            LogMujocoContactPairs(scene);
            return true;
        }

        private static unsafe double[] Copy(double* source, int count)
        {
            if (count <= 0 || source == null) return Array.Empty<double>();
            double[] result = new double[count];
            for (int i = 0; i < count; i++) result[i] = source[i];
            return result;
        }

        private static unsafe void CopyTo(double[] source, double* destination)
        {
            if (source == null || source.Length == 0 || destination == null) return;
            for (int i = 0; i < source.Length; i++) destination[i] = source[i];
        }
    }

    private sealed class MujocoContactPair
    {
        public string GeomA;
        public string GeomB;
        public int Count;
        public double MinimumDistance = double.PositiveInfinity;
    }

    private static unsafe void LogMujocoContactPairs(MjScene scene)
    {
        if (scene == null || scene.Model == null || scene.Data == null || scene.Data->ncon <= 0) return;

        // 不调用自动绑定的mj_id2name：原生函数返回MuJoCo内部只读char*，而该版本
        // C#绑定声明成string后会错误释放这块内存，Linux下会触发invalid pointer退出。
        Dictionary<int, string> geomNames = BuildSafeGeomNameMap();
        Dictionary<string, MujocoContactPair> pairs = new Dictionary<string, MujocoContactPair>();
        for (int i = 0; i < scene.Data->ncon; i++)
        {
            MujocoLib.mjContact_ contact = scene.Data->contact[i];
            string geomA = geomNames.TryGetValue(contact.geom1, out string mappedA)
                ? mappedA
                : $"geom#{contact.geom1}";
            string geomB = geomNames.TryGetValue(contact.geom2, out string mappedB)
                ? mappedB
                : $"geom#{contact.geom2}";

            if (string.CompareOrdinal(geomA, geomB) > 0)
            {
                string temporary = geomA;
                geomA = geomB;
                geomB = temporary;
            }

            string key = geomA + "\n" + geomB;
            if (!pairs.TryGetValue(key, out MujocoContactPair pair))
            {
                pair = new MujocoContactPair { GeomA = geomA, GeomB = geomB };
                pairs.Add(key, pair);
            }

            pair.Count++;
            if (contact.dist < pair.MinimumDistance) pair.MinimumDistance = contact.dist;
        }

        List<MujocoContactPair> orderedPairs = new List<MujocoContactPair>(pairs.Values);
        orderedPairs.Sort(delegate(MujocoContactPair left, MujocoContactPair right)
        {
            int distanceComparison = left.MinimumDistance.CompareTo(right.MinimumDistance);
            return distanceComparison != 0 ? distanceComparison : right.Count.CompareTo(left.Count);
        });

        const int maximumDetailedPairs = 20;
        int detailCount = Math.Min(maximumDetailedPairs, orderedPairs.Count);
        StringBuilder message = new StringBuilder(1024);
        message.Append("[MuJoCo接触诊断] mj_forward后共有")
            .Append(scene.Data->ncon).Append("个接触点、")
            .Append(orderedPairs.Count).Append("组几何体接触；按最深距离列出前")
            .Append(detailCount).Append("组：");
        for (int i = 0; i < detailCount; i++)
        {
            MujocoContactPair pair = orderedPairs[i];
            message.Append("\n  ").Append(i + 1).Append(". ")
                .Append(pair.GeomA).Append(" <-> ").Append(pair.GeomB)
                .Append("；接触点=").Append(pair.Count)
                .Append("，最小dist=").Append(pair.MinimumDistance.ToString("F6")).Append(" m");
        }
        if (orderedPairs.Count > detailCount)
            message.Append("\n  其余").Append(orderedPairs.Count - detailCount).Append("组已省略，避免日志刷屏。");

        Debug.LogWarning(message.ToString());
    }

    private static Dictionary<int, string> BuildSafeGeomNameMap()
    {
        Dictionary<int, string> result = new Dictionary<int, string>();
        MjGeom[] components = UnityEngine.Object.FindObjectsOfType<MjGeom>();
        for (int i = 0; i < components.Length; i++)
        {
            MjGeom component = components[i];
            if (component == null || !component.isActiveAndEnabled || component.MujocoId < 0) continue;
            string displayName = string.IsNullOrEmpty(component.MujocoName)
                ? component.gameObject.name
                : component.MujocoName;
            result[component.MujocoId] = displayName;
        }
        return result;
    }

    [ContextMenu("🧹 清除本脚本生成的内容 (Safe Clear)")]
    public void ClearGenerated()
    {
        if (targetObject == null) return;
        List<GameObject> generatedRoots = FindGeneratedRoots();
        for (int i = 0; i < generatedRoots.Count; i++)
        {
            if (generatedRoots[i] != null) DestroyImmediate(generatedRoots[i]);
        }
        Debug.Log($"🧹 清理完毕！移除 {generatedRoots.Count} 个节点。");
        #if UNITY_EDITOR
        AssetDatabase.Refresh();
        #endif
    }

    [ContextMenu("🚀 开始生成 (带进度条)")]
    public async Task Generate()
    {
        if (targetObject == null) return;

        if (createMujocoGeoms) ConfigureMujocoArenaMemory();

        bool deferGeneratedRootSwap = Application.isPlaying && batchMujocoSceneRebuild;
        List<GameObject> previousGeneratedRoots = FindGeneratedRoots();
        if (!deferGeneratedRootSwap)
        {
            DestroyGeneratedRoots(previousGeneratedRoots);
            previousGeneratedRoots.Clear();
        }
        _pendingGeneratedRoots.Clear();
        
        // 准备调试材质
    if (visualizeColliders)
        {
            if (_debugMaterial == null) 
            {
                // 1. 先尝试找 URP 材质
                Shader targetShader = Shader.Find("Universal Render Pipeline/Lit");
                
                // 2. 找不到 URP？退一步找标准管线
                if (targetShader == null) targetShader = Shader.Find("Standard");
                
                // 3. 连标准版都没有？拿 Unity 最底层的默认材质保底，绝对不报错！
                if (targetShader == null) targetShader = Shader.Find("Hidden/InternalErrorShader");

                _debugMaterial = new Material(targetShader);
            }
            _debugMaterial.color = new Color(0, 1, 0, 0.4f); 
            _debugMaterial.hideFlags = HideFlags.DontSave;
        }
        
        var filters = targetObject.GetComponentsInChildren<MeshFilter>();
        int total = filters.Length;
        int diagnosticSourceCount = 0;
        foreach (MeshFilter filter in filters)
        {
            if (!ShouldSkip(filter) && filter.sharedMesh != null) diagnosticSourceCount++;
        }
        int successCount = 0;
        int vhacdCount = 0; 
        int simpleCount = 0; 

        if (enableCollisionDiagnostics)
        {
            _diagnostic = targetObject.GetComponent<ColliderGenerationDiagnostic>();
            if (_diagnostic == null) _diagnostic = targetObject.AddComponent<ColliderGenerationDiagnostic>();

            MissionController missionController = FindObjectOfType<MissionController>();
            Transform robotCollisionRoot = missionController != null ? missionController.transform : null;
            Transform robotPoseTarget = robotCollisionRoot;
            if (missionController != null && missionController.refs != null && missionController.refs.realRobotBase != null)
            {
                robotPoseTarget = missionController.refs.realRobotBase.transform;
            }

            _diagnostic.BeginGeneration(
                targetObject,
                robotCollisionRoot,
                robotPoseTarget,
                diagnosticSourceCount,
                hullCount,
                createUnityPhysicsColliders,
                createMujocoGeoms,
                forceVHACDForAllMeshes);
        }

        Debug.Log($"🚀 开始生成... 共有 {hollowParts.Count} 个零件被指定为需要掏空。");

        try
        {
            for (int i = 0; i < total; i++)
            {
                var filter = filters[i];
                if (ShouldSkip(filter)) continue;

                #if UNITY_EDITOR
                float progress = (float)i / total;
                bool isCancelled = EditorUtility.DisplayCancelableProgressBar(
                    "碰撞体生成中...", 
                    $"进度 ({i}/{total}): {filter.name}", 
                    progress
                );
                if (isCancelled) { Debug.LogWarning("⚠️ 用户手动取消！"); break; }
                #endif

                Mesh sourceMesh = filter.sharedMesh;
                if (sourceMesh == null) continue;

                _currentSourceFilter = filter;
                int hullCountBeforeSource = _diagnostic != null ? GetGeneratedHullCount() : 0;
                var sourceStopwatch = System.Diagnostics.Stopwatch.StartNew();

                EnsureReadable(sourceMesh);

                string uniqueID = System.Guid.NewGuid().ToString().Substring(0, 6);
                
                GameObject root = new GameObject($"{filter.gameObject.name}_{uniqueID}_MjRoot");
                root.transform.SetParent(filter.transform, false);
                root.transform.localPosition = Vector3.zero; 
                root.transform.localRotation = Quaternion.identity;
                if (deferGeneratedRootSwap) root.SetActive(false);
                _pendingGeneratedRoots.Add(root);
                
                if (createMujocoGeoms)
                {
                    root.AddComponent<MjBody>();
                }

                // 只要这个零件被你拖进了 hollowParts 列表里，就对它进行 V-HACD 切割
                bool doVHACD = forceVHACDForAllMeshes || hollowParts.Contains(filter.gameObject);
                if (doVHACD)
                {
                    // === 走 V-HACD 高级通道===
                    Debug.Log($"正在进行 V-HACD 分解: {filter.name} ...");
                    Mesh simplifiedMesh = Simplify(sourceMesh, simpleQuality);

                    VHACD decomposer = gameObject.AddComponent<VHACD>();
                    var paramsCopy = decomposer.m_parameters;
                    paramsCopy.m_resolution = (uint)resolution;
                    paramsCopy.m_maxConvexHulls = (uint)hullCount;
                    paramsCopy.m_concavity = concavity;
                    paramsCopy.m_oclAcceleration = useGPU ? 1u : 0u; 
                    paramsCopy.m_planeDownsampling = planeDownsampling; 
                    paramsCopy.m_convexhullDownsampling = hullDownsampling; 
                    decomposer.m_parameters = paramsCopy;

                    List<Mesh> hulls = decomposer.GenerateConvexMeshes(simplifiedMesh);
                    DestroyImmediate(decomposer);

                    if (hulls != null && hulls.Count > 0)
                    {
                        int index = 0;
                        foreach (var hull in hulls) 
                        {
                            if (!IsSafeForQhull(hull)) continue; 
                            string uniqueName = $"{filter.name}_{uniqueID}_Hull_{index}";
                            hull.name = uniqueName; 
                            CreateGeom(root, hull, uniqueName);
                            index++;
                        }
                    }
                    vhacdCount++;
                }
                else
                {
                    // === 走极速通道 ===
                    Mesh destMesh = Simplify(sourceMesh, simpleQuality);
                    if (IsSafeForQhull(destMesh))
                    {
                        string uniqueName = $"{filter.name}_{uniqueID}_Simple";
                        destMesh.name = uniqueName;
                        CreateGeom(root, destMesh, $"{filter.name}_{uniqueID}_Geom");
                        simpleCount++;
                    }
                    else
                    {
                        DestroyImmediate(root);
                    }
                }
                successCount++;

                sourceStopwatch.Stop();
                if (_diagnostic != null)
                {
                    int generatedForSource = GetGeneratedHullCount() - hullCountBeforeSource;
                    _diagnostic.RecordSource(filter, generatedForSource, sourceStopwatch.Elapsed.TotalMilliseconds);
                }

                // 让出主线程，避免长时间完全无响应。MuJoCo组件仍处于未激活状态，
                // 因此这里不会再让每个装配零件分别触发一次 MjScene 重建。
                await Task.Delay(1);
            }
        }
        finally
        {
            if (deferGeneratedRootSwap)
            {
                await SwapGeneratedRootsAndRebuildMujocoOnce(previousGeneratedRoots);
            }

            // 确保进度条必须关闭
            #if UNITY_EDITOR
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
            #endif
            _currentSourceFilter = null;
            if (_diagnostic != null) _diagnostic.CompleteGeneration();
            Debug.Log($"🏁 生成完成！总计: {successCount}个。\n精雕掏空(V-HACD): {vhacdCount}个，极速挂载: {simpleCount}个。");
            _pendingGeneratedRoots.Clear();
        }
    }

    private async Task SwapGeneratedRootsAndRebuildMujocoOnce(List<GameObject> previousGeneratedRoots)
    {
        bool removesMujocoComponents = ContainsMujocoComponents(previousGeneratedRoots);
        bool addsMujocoComponents = createMujocoGeoms && _pendingGeneratedRoots.Count > 0;
        bool mutatesMujocoScene = removesMujocoComponents || addsMujocoComponents;

        MjScene scene = null;
        MujocoRuntimeSnapshot snapshot = null;
        TaskCompletionSource<bool> recreationCompleted = null;
        EventHandler<MjStepArgs> postInitHandler = null;

        if (mutatesMujocoScene && MjScene.InstanceExists)
        {
            scene = MjScene.Instance;
            if (IsMujocoSceneReady(scene))
            {
                if (restoreMujocoStateAfterRebuild) snapshot = MujocoRuntimeSnapshot.Capture(scene);
                recreationCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                postInitHandler = delegate { recreationCompleted.TrySetResult(true); };
                scene.postInitEvent += postInitHandler;
            }
        }

        try
        {
            // 旧组件的 OnDisable 和新组件的 OnEnable 均发生在同一帧；插件随后只会在
            // LateUpdate 合并处理一次 SceneRecreationAtLateUpdateRequested。
            DestroyGeneratedRoots(previousGeneratedRoots);
            for (int i = 0; i < _pendingGeneratedRoots.Count; i++)
            {
                GameObject root = _pendingGeneratedRoots[i];
                if (root != null && !root.activeSelf) root.SetActive(true);
            }

            if (recreationCompleted == null) return;

            Task finished = await Task.WhenAny(recreationCompleted.Task, Task.Delay(10000));
            if (finished != recreationCompleted.Task)
            {
                Debug.LogError("[MuJoCo批量重建] 等待 MjScene 重建超时。新碰撞体已生成，但没有自动恢复运行状态。");
                return;
            }

            // TaskCompletionSource 强制异步续接，因此这里会在 postInitEvent 所在的
            // RecreateScene 完整返回后继续，再用更完整的快照覆盖并执行 mj_forward。
            if (snapshot != null && snapshot.Restore(scene))
            {
                Debug.Log("[MuJoCo批量重建] 全部碰撞体已一次性加入 MjScene，并成功恢复重建前运行状态。");
            }
            else if (restoreMujocoStateAfterRebuild)
            {
                Debug.LogWarning("[MuJoCo批量重建] MjScene 已完成一次重建，但运行状态快照未能恢复，请检查上方日志。");
            }
        }
        finally
        {
            if (scene != null && postInitHandler != null) scene.postInitEvent -= postInitHandler;
        }
    }

    private List<GameObject> FindGeneratedRoots()
    {
        List<GameObject> result = new List<GameObject>();
        if (targetObject == null) return result;
        Transform[] transforms = targetObject.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform current = transforms[i];
            if (current != null && current != targetObject.transform && current.name.Contains("_MjRoot"))
                result.Add(current.gameObject);
        }
        return result;
    }

    private static void DestroyGeneratedRoots(List<GameObject> roots)
    {
        if (roots == null) return;
        for (int i = 0; i < roots.Count; i++)
        {
            if (roots[i] != null) DestroyImmediate(roots[i]);
        }
    }

    private static bool ContainsMujocoComponents(List<GameObject> roots)
    {
        if (roots == null) return false;
        for (int i = 0; i < roots.Count; i++)
        {
            if (roots[i] != null && roots[i].GetComponentInChildren<MjComponent>(true) != null) return true;
        }
        return false;
    }

    private static unsafe bool IsMujocoSceneReady(MjScene scene)
    {
        return scene != null && scene.Model != null && scene.Data != null;
    }

    private void ConfigureMujocoArenaMemory()
    {
        MjGlobalSettings settings = MjGlobalSettings.Instance;
        if (settings == null || string.IsNullOrWhiteSpace(mujocoArenaMemory)) return;
        string requestedMemory = mujocoArenaMemory.Trim();
        if (settings.GlobalSizes.Memory == requestedMemory) return;

        string previous = settings.GlobalSizes.Memory;
        settings.GlobalSizes.Memory = requestedMemory;
        Debug.Log(
            $"[MuJoCo批量重建] arena内存配置：{previous} -> {settings.GlobalSizes.Memory}。" +
            "该设置将在本次碰撞体加入后的 MjScene重建中生效。");
    }

    // --- 安全防崩逻辑 ---
    bool IsSafeForQhull(Mesh m)
    {
        if (m == null || m.vertexCount < 4) return false;
        m.RecalculateBounds();
        Vector3 size = m.bounds.size;
        if (size.x < minThickness || size.y < minThickness || size.z < minThickness) return false;
        if (CalculateVolume(m) < minVolume) return false;
        return true;
    }

    float CalculateVolume(Mesh mesh)
    {
        float volume = 0;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 p1 = vertices[triangles[i + 0]];
            Vector3 p2 = vertices[triangles[i + 1]];
            Vector3 p3 = vertices[triangles[i + 2]];
            volume += Vector3.Dot(Vector3.Cross(p1, p2), p3) / 6f;
        }
        return Mathf.Abs(volume);
    }

    void CreateGeom(GameObject parent, Mesh mesh, string uniqueName)
    {
        bool normalsWereRepaired = mesh.normals == null || mesh.normals.Length != mesh.vertexCount;
        if (normalsWereRepaired) mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject geomObj = new GameObject(uniqueName);
        geomObj.transform.SetParent(parent.transform, false);
        geomObj.transform.localPosition = Vector3.zero;
        geomObj.transform.localRotation = Quaternion.identity;
        
        if (createMujocoGeoms)
        {
            MjGeom mjGeom = geomObj.AddComponent<MjGeom>();

            if (disableGeneratedGeomSelfCollision)
            {
                MjGeomSettings settings = mjGeom.Settings;
                // 生成场景凸包之间：2&1为0，不产生自碰撞；默认机器人通常为1/1，
                // 反向1&1非0，因此机器人与场景之间仍会产生接触。
                settings.Filtering.Contype = 2;
                settings.Filtering.Conaffinity = 1;
                mjGeom.Settings = settings;
            }

            // ShapeType是公开字段，必须在Player中也设置为Mesh；旧代码只在
            // UNITY_EDITOR下改SerializedProperty，打包后会保留默认Sphere。
            mjGeom.ShapeType = MjShapeComponent.ShapeTypes.Mesh;

            MjMeshShape shape = new MjMeshShape();
            // MuJoCo的mesh asset只导出vertices，不会读取Unity Transform缩放。
            // 给MuJoCo单独使用已烘焙层级比例的副本；PhysX和诊断显示仍使用原mesh。
            shape.Mesh = MujocoMeshTransformUtility.CreateBakedMesh(mesh, geomObj.transform);
            mjGeom.Mesh = shape;
        }

        if (createUnityPhysicsColliders)
        {
            MeshCollider meshCollider = geomObj.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;

            Rigidbody rigidbody = geomObj.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.constraints = RigidbodyConstraints.FreezeAll;

            // Hull 只是原模型的碰撞/射线代理。原模型的高亮脚本能够通过子级命中处理它；
            // 不在代理上重复添加高亮脚本，避免鼠标消息争抢和未初始化引用。
        }


        if (visualizeColliders)
        {
            var mf = geomObj.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = geomObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _debugMaterial;
        }

        if (_diagnostic != null)
        {
            _diagnostic.RecordHull(geomObj, mesh, _currentSourceFilter, normalsWereRepaired);
        }
    }

    int GetGeneratedHullCount()
    {
        return _diagnostic != null ? _diagnostic.GeneratedHullCount : 0;
    }

    bool ShouldSkip(MeshFilter f)
    {
        if (f == null) return true;
        Transform current = f.transform;
        while (current != null)
        {
            if (current.name.Contains("_MjRoot")) return true;
            if (targetObject != null && current == targetObject.transform) break;
            current = current.parent;
        }
        if (f.GetComponent<MjBody>() != null) return true;
        if (f.GetComponent<MjGeom>() != null) return true; 
        return false;
    }

    Mesh Simplify(Mesh src, float q)
    {
        var simplifier = new MeshSimplifier();
        simplifier.Initialize(src);
        simplifier.SimplifyMesh(q);
        var m = simplifier.ToMesh();
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    void EnsureReadable(Mesh m)
    {
        #if UNITY_EDITOR
        if (!m.isReadable) {
            var path = AssetDatabase.GetAssetPath(m);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer) { importer.isReadable = true; importer.SaveAndReimport(); }
        }
        #endif
    }

}

public static class MujocoMeshTransformUtility
{
    private const float MatrixTolerance = 0.000001f;

    public static Mesh CreateBakedMesh(Mesh sourceMesh, Transform meshTransform)
    {
        if (sourceMesh == null || meshTransform == null) return sourceMesh;

        // MjEngineTool只把组件的世界位置和旋转写入MJCF。这里剥离相同的刚体变换，
        // 把剩余的层级缩放/镜像/剪切烘焙到顶点中，使MuJoCo世界形状与Unity一致。
        Matrix4x4 rigidWorldMatrix = Matrix4x4.TRS(
            meshTransform.position,
            meshTransform.rotation,
            Vector3.one);
        Matrix4x4 residualTransform = rigidWorldMatrix.inverse * meshTransform.localToWorldMatrix;
        if (IsIdentity(residualTransform)) return sourceMesh;

        Mesh bakedMesh = UnityEngine.Object.Instantiate(sourceMesh);
        bakedMesh.name = sourceMesh.name + "_MuJoCoBaked";
        bakedMesh.hideFlags = HideFlags.DontSave;

        Vector3[] vertices = bakedMesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = residualTransform.MultiplyPoint3x4(vertices[i]);
        bakedMesh.vertices = vertices;
        bakedMesh.RecalculateBounds();
        bakedMesh.RecalculateNormals();
        return bakedMesh;
    }

    private static bool IsIdentity(Matrix4x4 matrix)
    {
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 4; column++)
        {
            float expected = row == column ? 1f : 0f;
            if (Mathf.Abs(matrix[row, column] - expected) > MatrixTolerance) return false;
        }
        return true;
    }
}
