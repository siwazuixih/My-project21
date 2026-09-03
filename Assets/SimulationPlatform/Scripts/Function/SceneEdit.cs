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

    protected override async Task OnModelLoaded(GameObject model)
    {
        await Task.Yield();

        // 用户在编辑页重新导入另一个GLB时，Scene仍暂时指向旧文件。此时不能把旧
        // sidecar碰撞网格挂到新模型上；等用户先保存新模型、再重新切割即可。
        if (!CurrentModelMatchesSavedSceneFile())
        {
            m_cachedColliderModel = null;
            m_colliderDataModified = false;
            Debug.Log("当前为尚未保存的新导入模型，跳过旧碰撞网格自动恢复");
            return;
        }

        // OnEnable会先启动GLB异步加载，再同步读取sidecar XML。这里在模型真正实例化后
        // 才重建保存的碰撞网格，避免用户每次打开场景都重新切割。
        if (m_cachedColliderModel == null)
        {
            LoadExistingColliders();
        }

        if (m_cachedColliderModel != null)
        {
            ColliderApplyReport report = await ColliderManager.ApplyColliderDataAndWaitAsync(
                model,
                m_cachedColliderModel,
                true);
            if (report.IsSuccessful)
            {
                Debug.Log($"场景编辑已自动恢复并验证 {report.CreatedMeshCount} 个保存的碰撞网格");
                MessageManage.ShowMessage(
                    $"已加载并验证保存的碰撞网格（{report.CreatedMeshCount}个）",
                    1);
            }
            else
            {
                Debug.LogWarning(
                    $"场景碰撞恢复未通过验证: 创建={report.CreatedMeshCount}/" +
                    $"{report.RequestedMeshCount}, MuJoCo绑定={report.BoundMujocoGeomCount}, " +
                    $"旧路径歧义={report.AmbiguousLegacyPathCount}");
                MessageManage.ShowMessage(
                    m_cachedColliderModel.FormatVersion < 2
                        ? "旧版碰撞文件无法可靠定位，请重新切割并保存一次"
                        : "碰撞网格恢复验证失败，请查看日志",
                    2);
            }
        }
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
        if (Scene == null || Scene.Glb == null ||
            string.IsNullOrWhiteSpace(Scene.Glb.FilePath))
        {
            Debug.LogWarning("场景模型尚未保存，无法确定碰撞网格保存位置");
            MessageManage.ShowMessage("请先保存场景模型，再进行网格切割", 2);
            return;
        }

        if (!CurrentModelMatchesSavedSceneFile())
        {
            Debug.LogWarning("当前加载的是新导入模型，尚未保存到场景目录");
            MessageManage.ShowMessage("模型已更换，请先保存场景，再进行网格切割", 2);
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

        AutoColliderGen_Final colliderGen = null;
        if (ColliderBtn != null)
        {
            ColliderBtn.interactable = false;
        }

        try
        {
            // 动态添加 AutoColliderGen_Final 组件
            colliderGen = currentModel.GetComponent<AutoColliderGen_Final>();
            if (colliderGen == null)
            {
                colliderGen = currentModel.AddComponent<AutoColliderGen_Final>();
            }
            colliderGen.targetObject = currentModel;
            colliderGen.visualizeColliders = true;

            // 生成碰撞体
            await colliderGen.Generate();

            // 从生成的模型中提取碰撞体数据
            m_cachedColliderModel = ColliderManager.ExtractColliderData(
                currentModel,
                Scene.Id,
                Scene.Id,
                null,
                Scene.Name + "_Colliders");
            m_colliderDataModified = true;

            // 切割完成后立即保存，不再要求用户额外点击一次“保存”。
            bool saved = SaveCollidersToXml();
            if (saved)
            {
                Debug.Log("碰撞体生成并保存完成！");
                MessageManage.ShowMessage("网格切割并保存完成，下次将自动加载", 1);
            }
            else
            {
                Debug.LogWarning("碰撞体已生成，但保存失败");
                MessageManage.ShowMessage("网格已生成，但保存失败，请检查日志", 2);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            MessageManage.ShowMessage("碰撞体生成失败: " + e.Message, 2);
        }
        finally
        {
            if (colliderGen != null)
            {
                DestroyImmediate(colliderGen);
            }
            if (ColliderBtn != null)
            {
                ColliderBtn.interactable = true;
            }
        }
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

                // 后续碰撞网格必须保存在已复制的场景模型旁边。同步当前模型路径，
                // 也能避免下一次保存时把同一个模型再次判定为“重新导入”。
                modelFilePath = targetPath;
                if (ModelPathInput != null)
                {
                    ModelPathInput.text = targetPath;
                }
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

        if (isNewScene)
        {
            OperationLogTool.RecordLog(OperationType.模型管理, $"添加场景模型 - 名称：{Scene.Name}");
        }
        else
        {
            OperationLogTool.RecordLog(OperationType.模型管理, $"编辑场景模型 - 名称：{Scene.Name}");
        }
    }

    /// <summary>
    /// 确认预览中的模型就是Scene.Glb所指向的已保存文件。
    /// 替换模型后若未先保存，不能把新模型的碰撞数据写到旧模型的sidecar XML。
    /// </summary>
    private bool CurrentModelMatchesSavedSceneFile()
    {
        if (Scene?.Glb == null ||
            string.IsNullOrWhiteSpace(Scene.Glb.FilePath) ||
            string.IsNullOrWhiteSpace(modelFilePath))
        {
            return false;
        }

        try
        {
            string savedModelPath = Path.GetFullPath(
                PathTool.ResolvePhysicalPath(Scene.Glb.FilePath));
            string loadedModelPath = Path.GetFullPath(
                PathTool.ResolvePhysicalPath(modelFilePath));
            return string.Equals(
                savedModelPath,
                loadedModelPath,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"无法核对当前模型与场景模型路径: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 将缓存的碰撞体数据保存到模型所在目录
    /// </summary>
    private bool SaveCollidersToXml()
    {
        // 如果碰撞体数据没有被修改过，不需要保存
        if (!m_colliderDataModified)
        {
            Debug.Log("碰撞体数据未修改，跳过保存");
            return m_cachedColliderModel != null;
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
                return true;
            }
            else
            {
                Debug.LogWarning("碰撞体数据保存失败");
                return false;
            }
        }
        else
        {
            Debug.Log("没有缓存的碰撞体数据或模型路径为空");
            return false;
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
            m_colliderDataModified = false;
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
