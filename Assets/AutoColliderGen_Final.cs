using UnityEngine;
using System.Collections.Generic;
using UnityMeshSimplifier; 
using Mujoco; 
using MeshProcess; 
using System.Threading.Tasks;
using System;

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

    [ContextMenu("🧹 清除本脚本生成的内容 (Safe Clear)")]
    public void ClearGenerated()
    {
        if (targetObject == null) return;
        var filters = targetObject.GetComponentsInChildren<MeshFilter>();
        int deletedCount = 0;
        foreach (var filter in filters) {
            if (filter == null) continue;
            deletedCount += CleanUp(filter.transform);
        }
        Debug.Log($"🧹 清理完毕！移除 {deletedCount} 个节点。");
        #if UNITY_EDITOR
        AssetDatabase.Refresh();
        #endif
    }

    [ContextMenu("🚀 开始生成 (带进度条)")]
    public async Task Generate()
    {
        if (targetObject == null) return;
        
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
        int successCount = 0;
        int vhacdCount = 0; 
        int simpleCount = 0; 

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

                EnsureReadable(sourceMesh);
                CleanUp(filter.transform); 

                string uniqueID = System.Guid.NewGuid().ToString().Substring(0, 6);
                
                GameObject root = new GameObject($"{filter.gameObject.name}_{uniqueID}_MjRoot");
                root.transform.SetParent(filter.transform, false);
                root.transform.localPosition = Vector3.zero; 
                root.transform.localRotation = Quaternion.identity;
                
                MjBody mjBody = root.AddComponent<MjBody>();
                ModelTool.AddMeshCollidersToModel(root, true);

                // 只要这个零件被你拖进了 hollowParts 列表里，就对它进行 V-HACD 切割
                bool doVHACD = hollowParts.Contains(filter.gameObject);
                doVHACD = true;
                if (doVHACD)
                {
                    // === 走 V-HACD 高级通道===
                    Debug.Log($"正在处理指定的空心零件: {filter.name} ...");
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

                // 让出线程，防止卡死
                if (i % 10 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                await Task.Delay(1);
            }
        }
        finally
        {
            // 确保进度条必须关闭
            #if UNITY_EDITOR
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
            #endif
            Debug.Log($"🏁 生成完成！总计: {successCount}个。\n精雕掏空(V-HACD): {vhacdCount}个，极速挂载: {simpleCount}个。");
        }
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
        GameObject geomObj = new GameObject(uniqueName);
        geomObj.transform.SetParent(parent.transform, false);
        geomObj.transform.localPosition = Vector3.zero;
        geomObj.transform.localRotation = Quaternion.identity;
        
        MjGeom mjGeom = geomObj.AddComponent<MjGeom>();

        #if UNITY_EDITOR
        SerializedObject so = new SerializedObject(mjGeom);
        var prop = so.FindProperty("ShapeType") ?? so.FindProperty("shapeType") ?? so.FindProperty("m_ShapeType");
        if (prop != null) { prop.intValue = 6; so.ApplyModifiedProperties(); }
        #endif

        MjMeshShape shape = new MjMeshShape();
        shape.Mesh = mesh;
        mjGeom.Mesh = shape;

        MeshCollider meshCollider = geomObj.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = mesh;

        Rigidbody rigidbody = geomObj.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
        rigidbody.constraints = RigidbodyConstraints.FreezeAll;

        ModelCollisionHighlighter highlighter = geomObj.AddComponent<ModelCollisionHighlighter>();


        if (visualizeColliders)
        {
            var mf = geomObj.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = geomObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _debugMaterial;
        }
    }

    bool ShouldSkip(MeshFilter f)
    {
        if (f == null) return true;
        if (f.gameObject.name.Contains("_MjRoot")) return true;
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

    int CleanUp(Transform t)
    {
        if (t == null) return 0;
        int count = 0;
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            var child = t.GetChild(i);
            if (child.name.Contains("_MjRoot")) 
            {
                DestroyImmediate(child.gameObject);
                count++;
            }
        }
        return count;
    }
}