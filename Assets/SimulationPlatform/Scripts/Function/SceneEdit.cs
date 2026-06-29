using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class SceneEdit : ModelImport
{
    public Button SaveSceneBtn;
    public Button ColliderBtn;
    public InputField SceneNameInput;
    public InputField SceneModelInput;
    public InputField SceneCommentInput;
    public InputField ScenePathInput;

    // 缓存的碰撞体模型数据
    private ColliderModel m_cachedColliderModel;
    public ColliderModel CachedColliderModel => m_cachedColliderModel;
    
    // 标记碰撞体是否被修改过（需要保存）
    private bool m_colliderDataModified;
    public bool ColliderDataModified => m_colliderDataModified;

    public SceneModel Scene { get; set; }
    
    // Start is called before the first frame update
    void Start()
    {
        base.Start();

        if (SaveSceneBtn != null)
        {
            SaveSceneBtn.onClick.AddListener(OnSaveSceneBtnClick);
        }

        if (ColliderBtn != null)
        {
            ColliderBtn.onClick.AddListener(OnColliderBtnClick);
        }

        // 初始化输入框值
        InitializeInputFields();
    }
    
    // 当GameObject激活时调用
    private void OnEnable()
    {
        // 显示对应文本
        InitializeInputFields();

        // 加载模型
        LoadModelFromScene();

        // 加载已有的碰撞体数据
        LoadExistingColliders();
    }
    
    private void InitializeInputFields()
    {
        if (Scene != null)
        {
            if (SceneNameInput != null)
            {
                SceneNameInput.text = Scene.Name;
            }
            /*if (SceneModelInput != null)
            {
                SceneModelInput.text = Scene.Model;
            }*/
            if (SceneCommentInput != null)
            {
                SceneCommentInput.text = Scene.Comment;
            }
        }
    }

    private void OnSaveSceneBtnClick()
    {
        Debug.Log("Save Scene button clicked");
        // 保存输入框值到Scene对象
        SaveSceneValues();
    }

    private async void OnColliderBtnClick()
    {
        if (Scene == null)
        {
            Debug.LogWarning("Scene 为空，请先保存 Scene");
            MessageManage.ShowMessage("请先保存Scene", 2);
            return;
        }

        // 检查当前是否已加载模型
        if (currentModel == null)
        {
            Debug.LogWarning("模型未加载，请先加载模型");
            MessageManage.ShowMessage("请先加载模型", 2);
            return;
        }

        Debug.Log("Collider button clicked, 开始生成碰撞体...");

        try
        {
            // 动态添加 AutoColliderGen_Final 组件
            AutoColliderGen_Final colliderGen = currentModel.AddComponent<AutoColliderGen_Final>();
            colliderGen.targetObject = currentModel;

            // 生成碰撞体
            await colliderGen.Generate();

            // 从生成的模型中提取碰撞体数据
            ExtractColliderMeshes(currentModel);

            // 清理临时组件
            DestroyImmediate(colliderGen);

            Debug.Log("碰撞体生成完成！");
            MessageManage.ShowMessage("碰撞体生成完成", 1);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            MessageManage.ShowMessage("碰撞体生成失败: " + e.Message, 2);
        }
    }

    private void ExtractColliderMeshes(GameObject modelRoot)
    {
        m_cachedColliderModel = new ColliderModel
        {
            Id = Scene.Id,
            SceneId = Scene.Id,
            Name = Scene.Name + "_Colliders"
        };
        
        // 标记碰撞体数据已修改
        m_colliderDataModified = true;

        // 查找所有 _MjRoot 对象
        var allChildren = modelRoot.GetComponentsInChildren<Transform>();
        int mjRootIndex = 0;

        foreach (var child in allChildren)
        {
            if (child.name.Contains("_MjRoot"))
            {
                Debug.Log($"找到 MjRoot: {child.name}");
                
                var mjRootData = new ColliderMjRootData
                {
                    Name = child.name,
                    ParentPath = GetTransformPath(child.parent, modelRoot)
                };

                // 查找这个 MjRoot 下的所有碰撞体
                var meshFilters = child.GetComponentsInChildren<MeshFilter>();
                int meshIndex = 0;

                foreach (var mf in meshFilters)
                {
                    if (mf.sharedMesh == null) continue;
                    if (mf == null) continue;

                    // 检查是否为VHACD生成的碰撞体（名字包含 _Hull_）
                    bool isVHACD = mf.gameObject.name.Contains("_Hull_");

                    var meshData = ColliderManager.MeshToColliderMeshData(mf.sharedMesh, isVHACD);
                    meshData.Name = mf.gameObject.name;
                    mjRootData.Meshes.Add(meshData);

                    meshIndex++;
                }

                if (mjRootData.Meshes.Count > 0)
                {
                    m_cachedColliderModel.MjRoots.Add(mjRootData);
                    Debug.Log($"MjRoot {mjRootData.Name} 包含 {mjRootData.Meshes.Count} 个碰撞体");
                }

                mjRootIndex++;
            }
        }

        // 如果没有找到 _MjRoot，兼容旧版本数据结构
        if (m_cachedColliderModel.MjRoots.Count == 0)
        {
            Debug.LogWarning("未找到 _MjRoot，使用兼容模式提取");
            var meshFilters = modelRoot.GetComponentsInChildren<MeshFilter>();
            int meshIndex = 0;

            // 为了兼容，创建一个默认的 MjRoot
            var defaultMjRoot = new ColliderMjRootData
            {
                Name = "Default_MjRoot",
                ParentPath = ""
            };

            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null) continue;

                // 检查是否为VHACD生成的碰撞体（名字包含 _Hull_）
                bool isVHACD = mf.gameObject.name.Contains("_Hull_");

                var meshData = ColliderManager.MeshToColliderMeshData(mf.sharedMesh, isVHACD);
                meshData.Name = mf.gameObject.name;
                defaultMjRoot.Meshes.Add(meshData);

                meshIndex++;
            }

            if (defaultMjRoot.Meshes.Count > 0)
            {
                m_cachedColliderModel.MjRoots.Add(defaultMjRoot);
            }
        }

        int totalMeshes = 0;
        foreach (var mjRoot in m_cachedColliderModel.MjRoots)
        {
            totalMeshes += mjRoot.Meshes.Count;
        }
        Debug.Log($"提取了 {m_cachedColliderModel.MjRoots.Count} 个 MjRoot，共 {totalMeshes} 个碰撞体网格数据");
    }

    private string GetTransformPath(Transform target, GameObject root)
    {
        if (target == null || target.gameObject == root)
        {
            return "";
        }

        System.Text.StringBuilder path = new System.Text.StringBuilder();
        Transform current = target;

        while (current != null && current.gameObject != root)
        {
            if (path.Length > 0)
            {
                path.Insert(0, "/");
            }
            path.Insert(0, current.name);
            current = current.parent;
        }

        return path.ToString();
    }
    protected override void OnExitBtnClick()
    {
        base.OnExitBtnClick();
        
        // 将scene置为null
        Scene = null;
        
        // 清空输入框内容
        ClearInputFields();
        
        // 退出后刷新场景列表
        RefreshSceneList();
    }
    
    /// <summary>
    /// 清空输入框内容
    /// </summary>
    private void ClearInputFields()
    {
        if (SceneNameInput != null)
        {
            SceneNameInput.text = "";
        }
        if (SceneCommentInput != null)
        {
            SceneCommentInput.text = "";
        }
        if (ModelPathInput != null)
        {
            ModelPathInput.text = "";
        }

        // 清空缓存的碰撞体数据
        m_cachedColliderModel = null;
        
        // 重置碰撞体修改标记
        m_colliderDataModified = false;
    }
    
    /// <summary>
    /// 刷新场景列表
    /// </summary>
    private void RefreshSceneList()
    {
        // 查找SceneList组件
        SceneList sceneList = FindObjectOfType<SceneList>();
        if (sceneList != null)
        {
            sceneList.RefreshSceneList();
            Debug.Log("关节列表已刷新");
        }
        else
        {
            Debug.LogWarning("未找到SceneList组件，无法刷新关节列表");
        }
    }



    private void SaveSceneValues()
    {
        bool isNewScene = Scene == null;
        
        if (isNewScene)
        {
            Scene = new SceneModel();
            ModelManager.AddScene(Scene);
        }
        
        if (SceneNameInput != null)
        {
            Scene.Name = SceneNameInput.text;
        }
        
        if (SceneCommentInput != null)
        {
            Scene.Comment = SceneCommentInput.text;
        }

        // 生成uuid（如果还没有）
        if (string.IsNullOrEmpty(Scene.Id))
        {
            Scene.Id = Guid.NewGuid().ToString();
        }

        // 只有在以下情况下才复制模型文件：
        // 1. 是新建场景，且有新导入的模型文件
        // 2. 编辑场景时重新导入了不同的模型文件
        bool shouldCopyModelFile = false;
        string physicalModelPath = PathTool.ResolvePhysicalPath(modelFilePath);
        if (!string.IsNullOrEmpty(physicalModelPath) && System.IO.File.Exists(physicalModelPath))
        {
            if (isNewScene)
            {
                // 新建场景，需要复制模型文件
                shouldCopyModelFile = true;
            }
            else if (Scene.Glb == null || string.IsNullOrEmpty(Scene.Glb.FilePath))
            {
                // 编辑场景但之前没有模型文件，现在有新导入的模型
                shouldCopyModelFile = true;
            }
            else
            {
                // 检查是否重新导入了不同的模型文件
                string existingFullPath = PathTool.ResolvePhysicalPath(Scene.Glb.FilePath);
                if (!string.Equals(existingFullPath, physicalModelPath, StringComparison.OrdinalIgnoreCase))
                {
                    // 导入了不同的模型文件
                    shouldCopyModelFile = true;
                }
            }
        }

        if (shouldCopyModelFile)
        {
            // 复制文件到uuid命名的文件夹下
            string folderName = Scene.Id;
            string targetPath = FileManager.CopyFileToProjectFiles(folderName, physicalModelPath);

            if (!string.IsNullOrEmpty(targetPath))
            {
                // 确保Scene.GlbModel不为空
                if (Scene.Glb == null)
                {
                    Scene.Glb = new GlbModel();
                }

                // 计算相对于项目的路径
                string relativePath = PathTool.GetRelativePathFromExecutableDir(targetPath);

                // 设置GlbModel的FilePath为相对路径
                Scene.Glb.FilePath = relativePath;
                Debug.Log($"设置Scene.Glb.FilePath为相对路径: {relativePath}");
                Debug.Log($"原始完整路径: {targetPath}");
            }
        }
        else if (!string.IsNullOrEmpty(modelFilePath))
        {
            Debug.Log($"未复制模型文件：{modelFilePath}");
        }

        // 保存碰撞体数据到模型所在目录
        SaveCollidersToXml();

        // 直接保存到xml
        ModelManager.Save();
        MessageManage.ShowMessage("保存成功", 1);
        Debug.Log(isNewScene ? "新Scene已保存" : "Scene已更新并保存");
    }

    /// <summary>
    /// 将缓存的碰撞体数据保存到模型所在目录
    /// </summary>
    private void SaveCollidersToXml()
    {
        // 如果碰撞体数据没有被修改过，不需要保存
        if (!m_colliderDataModified)
        {
            Debug.Log("碰撞体数据未修改，跳过保存");
            return;
        }

        // 如果有缓存的碰撞体数据
        if (m_cachedColliderModel != null && Scene != null && Scene.Glb != null && !string.IsNullOrEmpty(Scene.Glb.FilePath))
        {
            // 确保 Scene 有 Id
            if (string.IsNullOrEmpty(Scene.Id))
            {
                Scene.Id = Guid.NewGuid().ToString();
            }

            // 更新碰撞体数据的 Id 和 SceneId
            m_cachedColliderModel.Id = Scene.Id;
            m_cachedColliderModel.SceneId = Scene.Id;
            m_cachedColliderModel.Name = Scene.Name + "_Colliders";

            // 保存到模型所在目录
            bool saved = ColliderManager.SaveColliderData(m_cachedColliderModel, Scene.Glb.FilePath);

            if (saved)
            {
                int totalMeshes = 0;
                foreach (var mjRoot in m_cachedColliderModel.MjRoots)
                {
                    totalMeshes += mjRoot.Meshes != null ? mjRoot.Meshes.Count : 0;
                }
                Debug.Log($"碰撞体数据已保存，包含 {m_cachedColliderModel.MjRoots.Count} 个 MjRoot，共 {totalMeshes} 个网格");
                
                // 保存成功后重置修改标记
                m_colliderDataModified = false;
            }
            else
            {
                Debug.LogWarning("碰撞体数据保存失败");
            }
        }
        else
        {
            Debug.Log("没有缓存的碰撞体数据或模型路径为空");
        }
    }
    
    /// <summary>
    /// 从Scene的glb.FilePath加载模型
    /// </summary>
    private void LoadModelFromScene()
    {
        if (Scene != null && Scene.Glb != null && !string.IsNullOrEmpty(Scene.Glb.FilePath))
        {
            string path = LoadModelFromFile(Scene.Glb.FilePath);
            if (ModelPathInput != null)
            {
                ModelPathInput.text = path;
            }
        }
        else
        {
            Debug.Log("Scene或模型路径为空，无法加载模型");
        }
    }

    /// <summary>
    /// 加载已有的碰撞体数据
    /// </summary>
    private void LoadExistingColliders()
    {
        if (Scene == null || Scene.Glb == null || string.IsNullOrEmpty(Scene.Glb.FilePath))
        {
            m_cachedColliderModel = null;
            Debug.Log("Scene为空或模型路径为空，无法加载碰撞体数据");
            return;
        }

        // 从模型所在目录加载碰撞体数据
        ColliderModel existingCollider = ColliderManager.LoadColliderData(Scene.Glb.FilePath);
        if (existingCollider != null)
        {
            m_cachedColliderModel = existingCollider;
            int totalMeshes = 0;
            if (existingCollider.MjRoots != null)
            {
                foreach (var mjRoot in existingCollider.MjRoots)
                {
                    totalMeshes += mjRoot.Meshes != null ? mjRoot.Meshes.Count : 0;
                }
            }
            Debug.Log($"已加载已有的碰撞体数据，包含 {existingCollider.MjRoots?.Count ?? 0} 个 MjRoot，共 {totalMeshes} 个网格");
        }
        else
        {
            m_cachedColliderModel = null;
            Debug.Log("未找到对应的碰撞体数据");
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
