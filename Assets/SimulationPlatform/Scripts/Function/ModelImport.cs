using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;

public class ModelImport : MonoBehaviour
{
    public Button ImportBtn;
    public Button ExitBtn;
    public GameObject BasePanel;
    public Transform ModelParent;
    public RawImage ModelDisplayImage;
    public Camera modelCamera;
    public InputField ModelPathInput;
    public Boolean UseImportCamera = true;

    protected GameObject currentModel;
    protected string modelFilePath;

    public void Start()
    {
        if (ImportBtn != null)
        {
            ImportBtn.onClick.AddListener(OnImportJointBtnClick);
        }

        if (ExitBtn != null)
        {
            ExitBtn.onClick.AddListener(OnExitBtnClick);
        }
        if (ModelPathInput != null)
        {
            ModelPathInput.text = "";
        }
    }
    protected virtual void OnExitBtnClick()
    {
        Debug.Log("Exit button clicked");
        BasePanel.SetActive(false);
        Destroy(currentModel);
        currentModel = null;
    }

    internal void OnImportJointBtnClick()
    {
        Debug.Log("Import Joint button clicked");
        string filePath = null;
        if (ModelPathInput == null || string.IsNullOrWhiteSpace(ModelPathInput.text))
        {
            filePath = FileDialogUtility.OpenFileDialog(
                "选择模型文件",
                "glTF模型(*.gltf;*.glb)\0*.gltf;*.glb\0所有文件(*.*)\0*.*\0\0"
            );
            //string filePath = "F:\\data\\mujoco-3.3.4-windows-x86_64\\model\\universal_robots_ur10e\\CR10AF\\jicang.glb";
        } else
        {
            filePath = ModelPathInput.text.Trim();
        }

        if (!string.IsNullOrEmpty(filePath))
        {
            LoadModel(filePath);
        }
    }

    // 加载glTF/GLB模型（打包后运行时支持）
    private void LoadModel(string filePath)
    {
        string physicalPath;
        try
        {
            physicalPath = PathTool.ResolvePhysicalPath(filePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"模型路径无效: {filePath}\n{e.Message}");
            MessageManage.ShowMessage("模型路径无效: " + filePath, 1);
            return;
        }

        if (!File.Exists(physicalPath))
        {
            Debug.LogError($"模型文件不存在: {physicalPath}");
            MessageManage.ShowMessage("模型文件不存在: " + physicalPath, 1);
            return;
        }

        MessageManage.ShowMessage("模型加载中...");
        modelFilePath = physicalPath;
        StartCoroutine(InstantiateGltfModel(PathTool.ToFileUri(physicalPath)));
    }

    protected virtual String LoadModelFromFile(string filePath)
    {
        string fullModelPath;
        try
        {
            fullModelPath = PathTool.ResolvePhysicalPath(filePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"模型路径无效: {filePath}\n{e.Message}");
            MessageManage.ShowMessage("模型路径无效: " + filePath, 1);
            return null;
        }

        Debug.Log($"开始加载模型: {fullModelPath}");

        // 检查文件是否存在
        if (File.Exists(fullModelPath))
        {
            LoadModel(fullModelPath);
            return fullModelPath;
        }
        else
        {
            Debug.LogError($"模型文件不存在: {fullModelPath}");
            MessageManage.ShowMessage("模型文件不存在: " +  filePath, 1);
            return null;
        }
    }

    protected virtual async Task OnModelLoaded(GameObject model)
    {
    }

    // 实例化glTF模型（使用glTFast插件）
    private IEnumerator InstantiateGltfModel(string filePath)
    {
        if (currentModel != null)
        {
            Destroy(currentModel);
        }
        var gltf = new GLTFast.GltfImport();

        // 推荐使用glTFast插件加载（更稳定），此处为示例
        Task<bool> loadTask = gltf.Load(filePath);

        // 加载并实例化模型
        yield return new WaitUntil(() => loadTask.IsCompleted);

        if (loadTask.IsFaulted)
        {
            Debug.LogError($"glTF模型加载失败: {loadTask.Exception}");
            MessageManage.ShowMessage("glTF模型加载失败", 1);
            yield break;
        }

        if (loadTask.Result)
        {
            if (gltf.SceneCount == 0)
            {
                Debug.LogError($"模型无场景数据，无法实例化");
                yield break;
            }

            gltf.GetSourceRoot().scene = 0;

            Task instantiateTask = gltf.InstantiateMainSceneAsync(ModelParent);
            yield return new WaitUntil(() => instantiateTask.IsCompleted);

            if (instantiateTask.IsFaulted)
            {
                Debug.LogError($"模型实例化失败:  {instantiateTask.Exception}");
            }
            else
            {
                if (ModelParent.childCount == 0)
                {
                    Debug.LogError("模型实例化失败，modelParent没有子物体！可能模型为空或格式错误。");
                    yield break;
                }

                currentModel = ModelParent.GetChild(ModelParent.childCount - 1).gameObject;
                currentModel.name = Path.GetFileNameWithoutExtension(modelFilePath);

                OnModelLoaded(currentModel);

                // 检查模型是否自带相机
                Camera[] modelCameras = currentModel.GetComponentsInChildren<Camera>();
                if (UseImportCamera && modelCameras.Length > 0)
                {
                    // 使用模型自带的第一个相机作为参考
                    Camera modelCameraRef = modelCameras[modelCameras.Length - 1];
                    Debug.Log($"检测到模型自带相机：{modelCameraRef.name}");
                    Debug.Log($"模型相机位置：{modelCameraRef.transform.position}");
                    Debug.Log($"模型相机旋转：{modelCameraRef.transform.rotation}");
                    Debug.Log($"模型相机FOV：{modelCameraRef.fieldOfView}");
                    Debug.Log($"模型相机近裁剪面：{modelCameraRef.nearClipPlane}");
                    Debug.Log($"模型相机远裁剪面：{modelCameraRef.farClipPlane}");
                    Debug.Log($"模型相机投影模式：{modelCameraRef.orthographic}");
                    Debug.Log($"模型相机正交大小：{modelCameraRef.orthographicSize}");
                    Debug.Log($"模型相机渲染层：{modelCameraRef.cullingMask}");
                    
                    // 复制模型相机的全局位置和旋转（考虑父对象变换）
                    modelCamera.transform.position = modelCameraRef.transform.position;
                    modelCamera.transform.rotation = modelCameraRef.transform.rotation;
                    
                    // 复制相机的所有投影参数
                    modelCamera.fieldOfView = modelCameraRef.fieldOfView;
                    // modelCamera.nearClipPlane = modelCameraRef.nearClipPlane;
                    // modelCamera.farClipPlane = modelCameraRef.farClipPlane;
                    modelCamera.orthographic = modelCameraRef.orthographic;
                    modelCamera.orthographicSize = modelCameraRef.orthographicSize;
                    modelCamera.aspect = modelCameraRef.aspect;
                    
                    // 复制相机的渲染设置
                    // modelCamera.cullingMask = modelCameraRef.cullingMask;
                    // modelCamera.clearFlags = modelCameraRef.clearFlags;
                    // modelCamera.backgroundColor = modelCameraRef.backgroundColor;
                    // modelCamera.depth = modelCameraRef.depth;
                    // modelCamera.renderingPath = modelCameraRef.renderingPath;
                    
                    // 启用modelCamera并设置targetTexture
                    modelCamera.gameObject.SetActive(true);
                    // 禁用模型自带的相机，避免冲突
                    foreach (Camera cam in modelCameras)
                    {
                        cam.enabled = false;
                        cam.gameObject.SetActive(false);
                    }

                    Debug.Log("已将modelCamera的所有参数设置为模型相机的对应值");
                }
                else
                {
                    // 禁用模型中的所有相机（如果没有使用）
                    //ModelTool.DisableModelCameras(currentModel);
                    // 添加摄像机自动定位
                    PositionCameraToViewModel(currentModel);
                }
                EnsureModelMaterials(currentModel);

                // 为模型添加MeshCollider和Rigidbody
                ModelTool.AddMeshCollidersToModel(currentModel);
                //AddRigidbodyToModel(currentModel);

                // 添加碰撞检测脚本
                //currentModel.AddComponent<ModelCollisionHighlighter>();

                //currentModel.AddComponent<ModelMouseController>();

                // 获取交互脚本并赋值模型
                if (ModelDisplayImage != null)
                {
                    ModelMouseInteraction interaction = ModelDisplayImage.GetComponent<ModelMouseInteraction>();
                    if (interaction != null)
                    {
                        interaction.modelTransform = currentModel.transform;
                        // 重置模型位置
                        interaction.ResetModelTransform();
                    }
                }

                Debug.Log("模型加载成功：" + currentModel.name);
                MessageManage.ShowMessage("模型加载成功：" + currentModel.name, 1);
            }
        }
        else
        {
            Debug.LogError($"glTF模型解析失败！");
            MessageManage.ShowMessage("glTF模型解析失败！", 1);
        }
    }
    /// <summary>
    /// 计算模型的边界框
    /// </summary>
    /// <param name="model">模型GameObject</param>
    /// <returns>模型的边界框</returns>
    private Bounds CalculateModelBounds(GameObject model)
    {
        Bounds bounds = new Bounds();
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        
        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            
            // 打印边界框信息，用于调试
            Debug.Log($"模型边界框：中心=({bounds.center.x:F2}, {bounds.center.y:F2}, {bounds.center.z:F2}), 大小=({bounds.size.x:F2}, {bounds.size.y:F2}, {bounds.size.z:F2})");
        }
        else
        {
            // 如果没有渲染器，使用一个默认大小
            bounds = new Bounds(Vector3.zero, new Vector3(1, 1, 1));
            Debug.Log("模型没有渲染器，使用默认边界框");
        }
        
        return bounds;
    }

    /// <summary>
    /// 根据模型边界框计算合适的摄像机距离
    /// </summary>
    /// <param name="bounds">模型边界框</param>
    /// <returns>摄像机应该距离模型的距离</returns>
    private float CalculateCameraDistance(Bounds bounds)
    {
        if (modelCamera == null) return 5.0f; // 默认值
        
        // 获取边界框的大小
        Vector3 size = bounds.size;
        float maxDimension = Mathf.Max(size.x, size.y, size.z);
        
        // 打印边界框大小和最大维度，用于调试
        Debug.Log($"边界框大小：{size}, 最大维度：{maxDimension}");
        
        // 方法1：根据摄像机视野计算距离
        float fov = modelCamera.fieldOfView * Mathf.Deg2Rad;
        float distance = (maxDimension / 2) / Mathf.Tan(fov / 2);
        
        // 方法2：基于模型大小的简单线性计算（用于处理非常大的模型）
        float linearDistance = maxDimension * 2.0f;
        
        // 取两种方法中的较大值，确保即使是非常大的模型也能被完整看到
        float finalDistance = Mathf.Max(distance, linearDistance);
        
        // 添加一些缓冲区
        finalDistance *= 1.2f;
        
        // 打印计算出的各种距离值，用于调试
        Debug.Log($"方法1计算的距离：{distance}, 方法2计算的距离：{linearDistance}, 最终距离：{finalDistance}");
        
        // 确保距离至少为一个合理的最小值
        finalDistance = Mathf.Max(distance, 5.0f);
        
        return finalDistance;
    }

    /// <summary>
    /// 定位摄像机以查看整个模型（只调整z值）
    /// </summary>
    /// <param name="model">要查看的模型</param>
    private void PositionCameraToViewModel(GameObject model)
    {
        if (model == null) return;

        Bounds bounds = CalculateModelBounds(model);
        float distance = CalculateCameraDistance(bounds);

        if (modelCamera != null)
        {
            // 只调整z值，保持x和y不变
            Vector3 cameraPos = modelCamera.transform.position;
            
            // 计算在保持x和y不变的情况下，z值应该是多少
            // 这里我们假设摄像机是沿z轴负方向看向模型
            // 我们需要将摄像机移动到一个距离，使得模型完全可见
            float newZ = bounds.center.z - distance;
            
            // 打印计算出的z值，用于调试
            Debug.Log($"计算出的z值：{newZ}");
            
            // 检查模型大小，如果模型非常大，可能需要更大的距离
            Vector3 size = bounds.size;
            float maxSize = Mathf.Max(size.x, size.y, size.z);
            
            // 如果模型非常大（例如超过100单位），增加距离
            if (maxSize > 100)
            {
                // 对于非常大的模型，使用更简单的距离计算
                float largeModelDistance = maxSize * 5.0f;
                newZ = bounds.center.z - largeModelDistance;
                Debug.Log($"检测到大型模型，调整z值为：{newZ}");
            }
            
            // 设置摄像机位置（只修改z值）
            modelCamera.transform.position = new Vector3(cameraPos.x, cameraPos.y, newZ);
            
            // 重置摄像机旋转为0
            modelCamera.transform.rotation = Quaternion.identity;
            

            
            Debug.Log($"摄像机位置调整：x={cameraPos.x}, y={cameraPos.y}, z={newZ}");
            Debug.Log("摄像机旋转已重置为默认值");
        }
    }

    // 在PositionCameraToViewModel方法后添加
    /// <summary>
    /// 确保模型有正确的材质，防止打包后渲染为红色
    /// </summary>
    /// <param name="model">模型GameObject</param>
    private void EnsureModelMaterials(GameObject model)
    {
        if (model == null) return;

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            //Debug.Log($"Render({renderer}): material({(renderer.material == null ? null : renderer.material)}): shader({(renderer.material == null || renderer.material.shader == null ? null : renderer.material.shader)})");
            if (renderer.material == null || renderer.material.shader == null || !renderer.material.shader.isSupported)
            {
                // 为没有材质的渲染器添加默认材质
                Material defaultMaterial = new Material(Shader.Find("Standard"));
                // 设置基本属性，确保模型不会呈粉红色
                defaultMaterial.color = new Color(0.5f, 0.5f, 0.5f); // 灰色
                defaultMaterial.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                renderer.material = defaultMaterial;
                Debug.Log($"为 {renderer.gameObject.name} 添加了默认材质");
            }
            else if (renderer.material.shader.name.Contains("Standard"))
            {
                // 如果使用的是Standard shader，确保它有基本的颜色设置
                if (renderer.material.color.r > 0.9f && renderer.material.color.g < 0.1f && renderer.material.color.b < 0.1f)
                {
                    // 如果颜色接近纯红色，可能是材质丢失的表现，重置为灰色
                    renderer.material.color = new Color(0.5f, 0.5f, 0.5f);
                    Debug.Log($"重置 {renderer.gameObject.name} 的材质颜色");
                }
            }
        }
    }

    /// <summary>
    /// 实例化替换后的模型
    /// </summary>
    protected IEnumerator InstantiateReplacedModel(string filePath, Vector3 position, Quaternion rotation, Vector3 scale, Transform parent, GameObject oldObject, JointModel jointModel = null, JointParamInfo jointParam = null, MissionController missionController = null)
    {
        var gltf = new GLTFast.GltfImport();

        // 使用glTFast加载模型
        Task<bool> loadTask = gltf.Load(filePath);
        yield return new WaitUntil(() => loadTask.IsCompleted);

        if (loadTask.Result)
        {
            if (gltf.SceneCount == 0)
            {
                Debug.LogError($"模型无场景数据，无法实例化");
                yield break;
            }

            // 创建临时父物体用于实例化
            GameObject tempParent = new GameObject("TempParent");
            tempParent.transform.position = Vector3.zero;
            tempParent.transform.rotation = Quaternion.identity;
            tempParent.transform.localScale = Vector3.one;

            // 实例化模型
            gltf.GetSourceRoot().scene = 0;
            Task instantiateTask = gltf.InstantiateMainSceneAsync(tempParent.transform);
            yield return new WaitUntil(() => instantiateTask.IsCompleted);

            if (instantiateTask.IsFaulted)
            {
                Debug.LogError($"模型实例化失败: {instantiateTask.Exception}");
                Destroy(tempParent);
            }
            else
            {
                if (tempParent.transform.childCount > 0)
                {
                    // 获取实例化的模型
                    GameObject newModel = tempParent.transform.GetChild(0).gameObject;

                    // 移除临时父物体
                    newModel.transform.parent = null;
                    newModel.transform.localScale = scale;

                    // 设置父物体
                    if (parent != null)
                    {
                        newModel.transform.parent = parent;
                    }

                    // 设置模型名称为JointModel.Name
                    if (jointModel != null && !string.IsNullOrEmpty(jointModel.Name))
                    {
                        newModel.name = jointModel.Name;
                    }

                    // 禁用模型自带相机
                    ModelTool.DisableModelCameras(newModel);

                    // 添加碰撞器和Rigidbody，并为子物体添加ModelCollisionHighlighter
                    ModelTool.AddMeshCollidersToModel(newModel, false);

                    // 为父物体添加ModelCollisionHighlighter并标记为替换的接头
                    ModelCollisionHighlighter parentHighlighter = newModel.GetComponent<ModelCollisionHighlighter>();
                    if (parentHighlighter == null)
                    {
                        parentHighlighter = newModel.AddComponent<ModelCollisionHighlighter>();
                    }
                    parentHighlighter.isReplacedJoint = true;

                    // 设置父物体的Rigidbody为运动学模式，避免掉落
                    Rigidbody parentRigidbody = newModel.GetComponent<Rigidbody>();
                    if (parentRigidbody != null)
                    {
                        parentRigidbody.isKinematic = true;
                        parentRigidbody.useGravity = false;
                        parentRigidbody.constraints = RigidbodyConstraints.FreezeAll;
                    }

                    // 确保所有子物体的高亮组件也标记为替换的接头
                    ModelCollisionHighlighter[] allHighlighters = newModel.GetComponentsInChildren<ModelCollisionHighlighter>();
                    foreach (ModelCollisionHighlighter highlighter in allHighlighters)
                    {
                        highlighter.isReplacedJoint = true;
                    }

                    // 添加模型鼠标控制器
                    //newModel.AddComponent<ModelMouseController>();

                    // 销毁临时父物体
                    //Destroy(tempParent);
                    tempParent.SetActive(false);

                    // 模型加载成功，现在销毁原物体
                    if (oldObject != null)
                    {
                        //Destroy(oldObject);
                        PathPointManager.Instance?.RemovePoint(oldObject);
                        ModelCollisionHighlighter.selectedObject = null;
                        oldObject.SetActive(false);

                        // 设置模型的位置、旋转和缩放与原高亮物体相同
                        newModel.transform.localPosition = Vector3.zero;
                        newModel.transform.localRotation = Quaternion.identity;

                        // 绑定 TransformJointDataComponent 并赋值
                        if (jointModel != null)
                        {
                            TransformJointDataComponent tjdc = newModel.GetComponent<TransformJointDataComponent>();
                            if (tjdc == null)
                            {
                                tjdc = newModel.AddComponent<TransformJointDataComponent>();
                            }
                            tjdc.jointModel = jointModel;
                            tjdc.paramInfo = jointParam;
                            tjdc.transformRef = newModel.transform;
                            tjdc.position = new Vector3D(newModel.transform.position);
                            tjdc.missionController = missionController;
                            tjdc.RefreshData();
                        }
                    }
                    Debug.Log($"模型替换成功: {newModel.name}");
                }
                else
                {
                    Debug.LogError("模型实例化失败，tempParent没有子物体！");
                    Destroy(tempParent);
                }
            }
        }
        else
        {
            Debug.LogError($"glTF模型解析失败！");
        }
    }
}
