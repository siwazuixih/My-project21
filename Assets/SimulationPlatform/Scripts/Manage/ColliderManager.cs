using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Mujoco;
using UnityEngine;

public static class ColliderManager
{
    private const string ColliderDataFileExtension = ".collider.xml";
    private static Material s_debugMaterial;

    /// <summary>
    /// 根据模型路径获取碰撞体数据文件路径
    /// </summary>
    /// <param name="modelRelativePath">模型相对路径</param>
    /// <returns>碰撞体数据文件完整路径</returns>
    private static string GetColliderDataFilePath(
        string modelRelativePath,
        string scopeId = null)
    {
        if (string.IsNullOrEmpty(modelRelativePath))
        {
            return null;
        }

        // 获取完整的模型文件路径
        string fullModelPath = PathTool.ResolvePhysicalPath(modelRelativePath);

        if (!File.Exists(fullModelPath))
        {
            Debug.LogError($"模型文件不存在: {fullModelPath}");
            return null;
        }

        // 场景基础碰撞使用<模型>.collider.xml；替换接头后的最终装配按Project.Id
        // 单独保存，避免不同项目覆盖同一个场景模型的碰撞数据。
        string modelBaseName = Path.GetFileNameWithoutExtension(fullModelPath);
        string colliderFileName = string.IsNullOrWhiteSpace(scopeId)
            ? modelBaseName + ColliderDataFileExtension
            : modelBaseName + ".project-" + MakeFileNameSafe(scopeId) + ColliderDataFileExtension;
        string colliderFilePath = Path.Combine(Path.GetDirectoryName(fullModelPath), colliderFileName);

        return colliderFilePath;
    }

    /// <summary>
    /// 保存碰撞体数据到模型所在目录
    /// </summary>
    /// <param name="colliderModel">碰撞体模型</param>
    /// <param name="modelRelativePath">模型相对路径</param>
    /// <returns>是否保存成功</returns>
    public static bool SaveColliderData(
        ColliderModel colliderModel,
        string modelRelativePath,
        string scopeId = null)
    {
        try
        {
            if (colliderModel == null)
            {
                Debug.LogError("碰撞体数据为空，无法保存");
                return false;
            }

            string colliderFilePath = GetColliderDataFilePath(modelRelativePath, scopeId);
            if (string.IsNullOrEmpty(colliderFilePath))
            {
                return false;
            }

            Debug.Log(
                $"[碰撞持久化/保存] Format={colliderModel.FormatVersion}, " +
                $"Scope={scopeId ?? "<scene>"}, Root={colliderModel.MjRoots?.Count ?? 0}, " +
                $"Mesh={CountMeshes(colliderModel)}, Path={colliderFilePath}");

            // 创建 XmlSerializer
            XmlSerializer serializer = new XmlSerializer(typeof(ColliderModel));

            // 先写同目录临时文件，再原子替换正式文件。切割数据通常较大，若程序在
            // 序列化中途退出，原有可用的碰撞文件不会先被截断成半份XML。
            string temporaryFilePath = colliderFilePath + ".tmp";
            string backupFilePath = colliderFilePath + ".bak";

            try
            {
                using (StreamWriter writer = new StreamWriter(
                    temporaryFilePath,
                    false,
                    System.Text.Encoding.UTF8))
                {
                    serializer.Serialize(writer, colliderModel);
                }

                if (File.Exists(colliderFilePath))
                {
                    File.Replace(temporaryFilePath, colliderFilePath, backupFilePath);
                    try
                    {
                        if (File.Exists(backupFilePath)) File.Delete(backupFilePath);
                    }
                    catch (Exception cleanupException)
                    {
                        Debug.LogWarning($"碰撞体备份文件清理失败: {cleanupException.Message}");
                    }
                }
                else
                {
                    File.Move(temporaryFilePath, colliderFilePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryFilePath)) File.Delete(temporaryFilePath);
            }

            Debug.Log($"[碰撞持久化/保存成功] {colliderFilePath}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return false;
        }
    }

    /// <summary>
    /// 从模型所在目录加载碰撞体数据
    /// </summary>
    /// <param name="modelRelativePath">模型相对路径</param>
    /// <returns>碰撞体模型，加载失败返回null</returns>
    public static ColliderModel LoadColliderData(
        string modelRelativePath,
        string scopeId = null)
    {
        try
        {
            string colliderFilePath = GetColliderDataFilePath(modelRelativePath, scopeId);
            if (string.IsNullOrEmpty(colliderFilePath) || !File.Exists(colliderFilePath))
            {
                Debug.Log($"碰撞体数据文件不存在: {colliderFilePath}");
                return null;
            }

            Debug.Log($"正在加载碰撞体数据: {colliderFilePath}");

            // 创建 XmlSerializer
            XmlSerializer serializer = new XmlSerializer(typeof(ColliderModel));

            // 读取文件
            using (StreamReader reader = new StreamReader(colliderFilePath, System.Text.Encoding.UTF8))
            {
                ColliderModel colliderModel = serializer.Deserialize(reader) as ColliderModel;
                if (colliderModel == null)
                {
                    Debug.LogWarning($"碰撞体XML反序列化结果为空: {colliderFilePath}");
                    return null;
                }
                int totalMeshes = 0;
                if (colliderModel.MjRoots != null)
                {
                    foreach (var mjRoot in colliderModel.MjRoots)
                    {
                        totalMeshes += mjRoot.Meshes != null ? mjRoot.Meshes.Count : 0;
                    }
                }
                Debug.Log(
                    $"[碰撞持久化/加载成功] Format={colliderModel.FormatVersion}, " +
                    $"Scope={scopeId ?? "<scene>"}, Project={colliderModel.ProjectId ?? "<scene>"}, " +
                    $"Root={colliderModel.MjRoots?.Count ?? 0}, Mesh={totalMeshes}");
                return colliderModel;
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return null;
        }
    }

    /// <summary>
    /// 从AutoColliderGen_Final生成的_MjRoot层级提取可持久化数据。
    /// </summary>
    public static ColliderModel ExtractColliderData(
        GameObject modelRoot,
        string id,
        string sceneId,
        string projectId,
        string displayName)
    {
        if (modelRoot == null)
        {
            throw new ArgumentNullException(nameof(modelRoot));
        }

        var result = new ColliderModel
        {
            FormatVersion = 2,
            Id = id,
            SceneId = sceneId,
            ProjectId = projectId,
            Name = displayName
        };

        Transform[] allChildren = modelRoot.GetComponentsInChildren<Transform>();
        foreach (Transform child in allChildren)
        {
            if (child == null ||
                child.name.IndexOf("_MjRoot", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            Transform parent = child.parent;
            var rootData = new ColliderMjRootData
            {
                Name = child.name,
                ParentPath = GetTransformNamePath(parent, modelRoot.transform),
                ParentIndexPath = GetTransformIndexPath(parent, modelRoot.transform)
            };

            MeshCollider[] meshColliders = child.GetComponentsInChildren<MeshCollider>();
            foreach (MeshCollider meshCollider in meshColliders)
            {
                if (meshCollider == null || meshCollider.sharedMesh == null)
                {
                    continue;
                }

                bool isVhacd = meshCollider.gameObject.name.IndexOf(
                    "_Hull_",
                    StringComparison.OrdinalIgnoreCase) >= 0;
                ColliderMeshData meshData = MeshToColliderMeshData(
                    meshCollider.sharedMesh,
                    isVhacd);
                meshData.Name = meshCollider.gameObject.name;
                rootData.Meshes.Add(meshData);
            }

            if (rootData.Meshes.Count > 0)
            {
                result.MjRoots.Add(rootData);
                Debug.Log(
                    $"[碰撞持久化/提取] Root={rootData.Name}, Mesh={rootData.Meshes.Count}, " +
                    $"IndexPath={rootData.ParentIndexPath}, NamePath={rootData.ParentPath}");
            }
        }

        int totalMeshCount = CountMeshes(result);
        if (totalMeshCount == 0)
        {
            throw new InvalidOperationException("没有从_MjRoot中提取到任何碰撞网格");
        }

        Debug.Log(
            $"[碰撞持久化/提取汇总] Format={result.FormatVersion}, " +
            $"Project={result.ProjectId ?? "<scene>"}, Root={result.MjRoots.Count}, " +
            $"Mesh={totalMeshCount}");
        return result;
    }

    private static string GetTransformNamePath(Transform target, Transform root)
    {
        if (target == null || target == root)
        {
            return string.Empty;
        }

        var segments = new List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            segments.Add(current.name);
            current = current.parent;
        }

        if (current != root)
        {
            return null;
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    private static string GetTransformIndexPath(Transform target, Transform root)
    {
        if (target == null || target == root)
        {
            return string.Empty;
        }

        var indices = new List<int>();
        Transform current = target;
        while (current != null && current != root)
        {
            indices.Add(current.GetSiblingIndex());
            current = current.parent;
        }

        if (current != root)
        {
            return null;
        }

        indices.Reverse();
        return string.Join("/", indices.ConvertAll(
            index => index.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// 将Mesh转换为ColliderMeshData
    /// </summary>
    public static ColliderMeshData MeshToColliderMeshData(Mesh mesh, bool isVHACD = false)
    {
        var data = new ColliderMeshData
        {
            Name = mesh.name,
            IsVHACD = isVHACD
        };

        // 序列化顶点
        var vertices = mesh.vertices;
        var vertexList = new List<float>();
        foreach (var v in vertices)
        {
            vertexList.Add(v.x);
            vertexList.Add(v.y);
            vertexList.Add(v.z);
        }
        data.Vertices = string.Join(",", vertexList.ConvertAll(
            v => v.ToString("R", CultureInfo.InvariantCulture)).ToArray());

        // 序列化三角形
        var triangles = mesh.triangles;
        data.Triangles = string.Join(",", Array.ConvertAll(
            triangles, t => t.ToString(CultureInfo.InvariantCulture)));

        // 序列化法线
        var normals = mesh.normals;
        var normalList = new List<float>();
        foreach (var n in normals)
        {
            normalList.Add(n.x);
            normalList.Add(n.y);
            normalList.Add(n.z);
        }
        data.Normals = string.Join(",", normalList.ConvertAll(
            n => n.ToString("R", CultureInfo.InvariantCulture)).ToArray());

        // 序列化UV
        var uvs = mesh.uv;
        var uvList = new List<float>();
        foreach (var u in uvs)
        {
            uvList.Add(u.x);
            uvList.Add(u.y);
        }
        data.UVs = string.Join(",", uvList.ConvertAll(
            u => u.ToString("R", CultureInfo.InvariantCulture)).ToArray());

        return data;
    }

    /// <summary>
    /// 将ColliderMeshData转换为Mesh
    /// </summary>
    public static Mesh ColliderMeshDataToMesh(ColliderMeshData data)
    {
        if (data == null || string.IsNullOrWhiteSpace(data.Vertices) ||
            string.IsNullOrWhiteSpace(data.Triangles))
        {
            Debug.LogWarning("碰撞体网格数据缺少顶点或三角形");
            return null;
        }

        var mesh = new Mesh
        {
            name = data.Name
        };

        // 反序列化顶点
        var vertexStrings = data.Vertices.Split(',');
        if (vertexStrings.Length % 3 != 0)
        {
            Debug.LogWarning($"碰撞体顶点数据格式错误: {data.Name}");
            return null;
        }
        if (vertexStrings.Length / 3 > 65535)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }
        var vertices = new Vector3[vertexStrings.Length / 3];
        for (int i = 0; i < vertexStrings.Length; i += 3)
        {
            vertices[i / 3] = new Vector3(
                float.Parse(vertexStrings[i], CultureInfo.InvariantCulture),
                float.Parse(vertexStrings[i + 1], CultureInfo.InvariantCulture),
                float.Parse(vertexStrings[i + 2], CultureInfo.InvariantCulture)
            );
        }
        mesh.vertices = vertices;

        // 反序列化三角形
        var triangleStrings = data.Triangles.Split(',');
        var triangles = Array.ConvertAll(
            triangleStrings, t => int.Parse(t, CultureInfo.InvariantCulture));
        mesh.triangles = triangles;

        // 反序列化法线
        if (!string.IsNullOrEmpty(data.Normals))
        {
            var normalStrings = data.Normals.Split(',');
            var normals = new Vector3[normalStrings.Length / 3];
            for (int i = 0; i < normalStrings.Length; i += 3)
            {
                normals[i / 3] = new Vector3(
                    float.Parse(normalStrings[i], CultureInfo.InvariantCulture),
                    float.Parse(normalStrings[i + 1], CultureInfo.InvariantCulture),
                    float.Parse(normalStrings[i + 2], CultureInfo.InvariantCulture)
                );
            }
            mesh.normals = normals;
        }

        // 反序列化UV
        if (!string.IsNullOrEmpty(data.UVs))
        {
            var uvStrings = data.UVs.Split(',');
            var uvs = new Vector2[uvStrings.Length / 2];
            for (int i = 0; i < uvStrings.Length; i += 2)
            {
                uvs[i / 2] = new Vector2(
                    float.Parse(uvStrings[i], CultureInfo.InvariantCulture),
                    float.Parse(uvStrings[i + 1], CultureInfo.InvariantCulture)
                );
            }
            mesh.uv = uvs;
        }

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        return mesh;
    }

    /// <summary>
    /// 重建保存的碰撞网格，等待MuJoCo真正完成场景重建，并返回可核验的绑定统计。
    /// </summary>
    public static async Task<ColliderApplyReport> ApplyColliderDataAndWaitAsync(
        GameObject model,
        ColliderModel colliderModel,
        bool visualizeColliders = false,
        int rebuildTimeoutMilliseconds = 10000)
    {
        var report = new ColliderApplyReport
        {
            RequestedRootCount = colliderModel?.MjRoots?.Count ?? 0,
            RequestedMeshCount = CountMeshes(colliderModel)
        };

        if (model == null || colliderModel?.MjRoots == null || colliderModel.MjRoots.Count == 0)
        {
            Debug.LogWarning("[碰撞持久化/恢复] 没有可应用的碰撞体数据");
            return report;
        }

        MjScene mujocoScene = MjScene.InstanceExists ? MjScene.Instance : null;
        TaskCompletionSource<bool> rebuildCompleted = null;
        EventHandler<MjStepArgs> postInitHandler = null;
        if (HasInitializedMujocoModel(mujocoScene))
        {
            rebuildCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            postInitHandler = delegate { rebuildCompleted.TrySetResult(true); };
            mujocoScene.postInitEvent += postInitHandler;
        }

        var createdGeoms = new List<MjGeom>();
        var pendingRoots = new List<GameObject>();

        try
        {
            RemoveExistingColliderRoots(model);

            foreach (ColliderMjRootData rootData in colliderModel.MjRoots)
            {
                if (rootData?.Meshes == null || rootData.Meshes.Count == 0)
                {
                    continue;
                }

                Transform parent = ResolveParentTransform(model.transform, rootData, report);
                if (parent == null)
                {
                    report.MissingParentCount++;
                    continue;
                }

                string rootName = string.IsNullOrWhiteSpace(rootData.Name)
                    ? "Saved_MjRoot"
                    : rootData.Name;
                GameObject rootObject = new GameObject(rootName);
                rootObject.SetActive(false);
                rootObject.transform.SetParent(parent, false);
                rootObject.transform.localPosition = Vector3.zero;
                rootObject.transform.localRotation = Quaternion.identity;
                rootObject.transform.localScale = Vector3.one;
                rootObject.AddComponent<MjBody>();

                int meshCountBefore = report.CreatedMeshCount;
                foreach (ColliderMeshData meshData in rootData.Meshes)
                {
                    try
                    {
                        Mesh mesh = ColliderMeshDataToMesh(meshData);
                        if (mesh == null)
                        {
                            continue;
                        }

                        MjGeom geom = CreateColliderObject(
                            rootObject.transform,
                            mesh,
                            meshData,
                            visualizeColliders);
                        createdGeoms.Add(geom);
                        report.CreatedMeshCount++;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            $"[碰撞持久化/恢复] 重建网格失败: {meshData?.Name}\n{exception}");
                    }
                }

                if (report.CreatedMeshCount > meshCountBefore)
                {
                    pendingRoots.Add(rootObject);
                    report.CreatedRootCount++;
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(rootObject);
                }
            }

            foreach (GameObject rootObject in pendingRoots)
            {
                if (rootObject != null)
                {
                    rootObject.SetActive(true);
                }
            }

            Debug.Log(
                $"[碰撞持久化/Unity重建] 请求Root={report.RequestedRootCount}, " +
                $"Mesh={report.RequestedMeshCount}; 创建Root={report.CreatedRootCount}, " +
                $"Mesh={report.CreatedMeshCount}; 缺失父节点={report.MissingParentCount}, " +
                $"旧路径歧义={report.AmbiguousLegacyPathCount}");

            if (report.CreatedMeshCount > 0 && rebuildCompleted != null)
            {
                Task finished = await Task.WhenAny(
                    rebuildCompleted.Task,
                    Task.Delay(rebuildTimeoutMilliseconds));
                report.MujocoRebuildObserved = finished == rebuildCompleted.Task;
                report.MujocoRebuildTimedOut = !report.MujocoRebuildObserved;
                if (report.MujocoRebuildTimedOut)
                {
                    Debug.LogError(
                        $"[碰撞持久化/MuJoCo] 等待场景重建超时 " +
                        $"({rebuildTimeoutMilliseconds}ms)");
                }
            }
            else if (report.CreatedMeshCount > 0)
            {
                // MjScene尚未建立模型时，后续Start会统一编译；当前运行界面正常情况下
                // 应该不会走到这里，因此保留醒目日志。
                Debug.LogWarning(
                    "[碰撞持久化/MuJoCo] 当前没有可等待的已初始化MjScene，无法立即验证绑定");
            }

            await Task.Yield();
            report.BoundMujocoGeomCount = createdGeoms.FindAll(
                geom => geom != null &&
                    !string.IsNullOrEmpty(geom.MujocoName) &&
                    geom.MujocoId >= 0).Count;

            Debug.Log(
                $"[碰撞持久化/MuJoCo验证] RebuildObserved={report.MujocoRebuildObserved}, " +
                $"BoundGeom={report.BoundMujocoGeomCount}/{report.CreatedMeshCount}, " +
                $"Result={(report.IsSuccessful ? "PASS" : "FAIL")}");
            return report;
        }
        finally
        {
            if (mujocoScene != null && postInitHandler != null)
            {
                mujocoScene.postInitEvent -= postInitHandler;
            }
        }
    }

    private static MjGeom CreateColliderObject(
        Transform parent,
        Mesh mesh,
        ColliderMeshData meshData,
        bool visualizeColliders)
    {
        string objectName = string.IsNullOrWhiteSpace(meshData.Name)
            ? mesh.name
            : meshData.Name;
        GameObject colliderObject = new GameObject(objectName);
        colliderObject.transform.SetParent(parent, false);
        colliderObject.transform.localPosition = Vector3.zero;
        colliderObject.transform.localRotation = Quaternion.identity;
        colliderObject.transform.localScale = Vector3.one;

        MjGeom mjGeom = colliderObject.AddComponent<MjGeom>();
        MjGeomSettings settings = mjGeom.Settings;
        settings.Filtering.Contype = 2;
        settings.Filtering.Conaffinity = 1;
        mjGeom.Settings = settings;

        // ShapeType是公开字段，可以直接赋值；不能只在UNITY_EDITOR中通过反射设置，
        // 否则打包后的Player会把恢复网格当成默认Sphere。
        mjGeom.ShapeType = MjShapeComponent.ShapeTypes.Mesh;

        var shape = new MjMeshShape
        {
            Mesh = MujocoMeshTransformUtility.CreateBakedMesh(mesh, colliderObject.transform)
        };
        mjGeom.Mesh = shape;

        MeshCollider meshCollider = colliderObject.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = mesh;
        meshCollider.convex = meshData.IsVHACD;

        Rigidbody rigidbody = colliderObject.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
        rigidbody.constraints = RigidbodyConstraints.FreezeAll;

        if (visualizeColliders)
        {
            MeshFilter meshFilter = colliderObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;
            MeshRenderer meshRenderer = colliderObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = GetDebugMaterial();
        }

        // 不设置未定义的VHACD Tag。持久化状态由IsVHACD字段和_Hull_命名识别。
        return mjGeom;
    }

    private static Transform ResolveParentTransform(
        Transform root,
        ColliderMjRootData rootData,
        ColliderApplyReport report)
    {
        if (root == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(rootData.ParentIndexPath))
        {
            Transform indexedParent = FindParentByIndexPath(root, rootData.ParentIndexPath);
            string actualNamePath = GetTransformNamePath(indexedParent, root);
            if (indexedParent == null ||
                (!string.IsNullOrWhiteSpace(rootData.ParentPath) &&
                 !string.Equals(
                     actualNamePath,
                     rootData.ParentPath,
                     StringComparison.Ordinal)))
            {
                Debug.LogWarning(
                    $"[碰撞持久化/定位失败] Root={rootData.Name}, " +
                    $"IndexPath={rootData.ParentIndexPath}, " +
                    $"ExpectedNamePath={rootData.ParentPath}, ActualNamePath={actualNamePath}");
                return null;
            }

            Debug.Log(
                $"[碰撞持久化/定位成功] Root={rootData.Name}, " +
                $"Mode=Index, IndexPath={rootData.ParentIndexPath}");
            return indexedParent;
        }

        // FormatVersion 1兼容：只有名称路径时必须证明唯一。出现同名候选就拒绝恢复，
        // 防止把多组Hull全部挂到第一个同名节点并产生“数量正确、位置错误”的假成功。
        List<Transform> legacyMatches = FindParentsByNamePath(root, rootData.ParentPath);
        if (legacyMatches.Count == 1)
        {
            Debug.LogWarning(
                $"[碰撞持久化/旧格式] Root={rootData.Name}使用唯一名称路径恢复；" +
                "建议重新切割生成FormatVersion=2文件");
            return legacyMatches[0];
        }

        if (legacyMatches.Count > 1)
        {
            report.AmbiguousLegacyPathCount++;
            Debug.LogError(
                $"[碰撞持久化/旧格式歧义] Root={rootData.Name}, " +
                $"NamePath={rootData.ParentPath}, Candidates={legacyMatches.Count}；" +
                "已拒绝错误挂载，请重新切割一次");
        }
        else
        {
            Debug.LogWarning(
                $"[碰撞持久化/定位失败] Root={rootData.Name}, " +
                $"NamePath={rootData.ParentPath}, Candidates=0");
        }
        return null;
    }

    private static Transform FindParentByIndexPath(Transform root, string indexPath)
    {
        if (root == null) return null;
        if (string.IsNullOrWhiteSpace(indexPath)) return root;

        Transform current = root;
        string[] segments = indexPath.Split('/');
        foreach (string segment in segments)
        {
            if (!int.TryParse(
                    segment,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int siblingIndex) ||
                siblingIndex < 0 ||
                siblingIndex >= current.childCount)
            {
                return null;
            }
            current = current.GetChild(siblingIndex);
        }
        return current;
    }

    private static List<Transform> FindParentsByNamePath(Transform root, string namePath)
    {
        var currentMatches = new List<Transform> { root };
        if (root == null) return new List<Transform>();
        if (string.IsNullOrWhiteSpace(namePath)) return currentMatches;

        foreach (string segment in namePath.Split('/'))
        {
            if (string.IsNullOrWhiteSpace(segment)) continue;
            var nextMatches = new List<Transform>();
            foreach (Transform candidate in currentMatches)
            {
                for (int childIndex = 0; childIndex < candidate.childCount; childIndex++)
                {
                    Transform child = candidate.GetChild(childIndex);
                    if (string.Equals(child.name, segment, StringComparison.Ordinal))
                    {
                        nextMatches.Add(child);
                    }
                }
            }
            currentMatches = nextMatches;
            if (currentMatches.Count == 0) break;
        }
        return currentMatches;
    }

    private static int CountMeshes(ColliderModel colliderModel)
    {
        if (colliderModel?.MjRoots == null)
        {
            return 0;
        }

        int count = 0;
        foreach (ColliderMjRootData root in colliderModel.MjRoots)
        {
            count += root?.Meshes?.Count ?? 0;
        }
        return count;
    }

    private static unsafe bool HasInitializedMujocoModel(MjScene scene)
    {
        return scene != null && scene.Model != null;
    }

    private static string MakeFileNameSafe(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        var result = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            result.Append(Array.IndexOf(invalidCharacters, character) >= 0 ? '_' : character);
        }
        return result.ToString();
    }

    private static Material GetDebugMaterial()
    {
        if (s_debugMaterial != null)
        {
            return s_debugMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Standard") ??
                        Shader.Find("Hidden/InternalErrorShader");
        s_debugMaterial = new Material(shader)
        {
            name = "SavedCollider_DebugMaterial",
            // 与AutoColliderGen_Final刚切割完成时使用完全相同的调试颜色。
            color = new Color(0f, 1f, 0f, 0.4f),
            hideFlags = HideFlags.DontSave
        };
        return s_debugMaterial;
    }

    private static void RemoveExistingColliderRoots(GameObject model)
    {
        Transform[] transforms = model.GetComponentsInChildren<Transform>(true);
        var roots = new List<GameObject>();
        foreach (Transform current in transforms)
        {
            if (current != null && current != model.transform &&
                current.name.IndexOf("_MjRoot", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                roots.Add(current.gameObject);
            }
        }

        foreach (GameObject root in roots)
        {
            if (root != null)
            {
                root.SetActive(false);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
