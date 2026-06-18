using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;

/// <summary>
/// 模型碰撞高亮脚本
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ModelCollisionHighlighter : MonoBehaviour
{
    public bool isReplacedJoint = false;
    
    private Renderer[] allRenderers;
    private MaterialPropertyBlock propBlock;
    private bool isHighlighted = false;
    private bool isSelected = false;
    private Dictionary<Renderer, Color> originalColors = new Dictionary<Renderer, Color>();
    private Dictionary<Renderer, bool> usedDirectColorAccess = new Dictionary<Renderer, bool>(); // 记录是否使用直接颜色访问方式
    
    // 中心点高亮相关字段
    private GameObject centerPoint;
    private Material centerMaterial;
    private bool isInitialized = false;
    private float centerSize = 0.8f; // 增大中心点尺寸，使其更加醒目
    
    // 模型透明度相关字段
    private Dictionary<Renderer, float> originalOpacities = new Dictionary<Renderer, float>();
    private float highlightOpacity = 0.6f; // 高亮时的透明度
    
    // 静态变量，用于跟踪当前高亮的物体
    public static ModelCollisionHighlighter currentHighlightedObject = null;
    public static ModelCollisionHighlighter selectedObject = null;
    public static List<Transform> SeletectedObjects = new List<Transform>();

    void Start()
    {
        // 获取模型所有的Renderer组件
        allRenderers = GetComponentsInChildren<Renderer>();
        propBlock = new MaterialPropertyBlock();
    }
    
    /// <summary>
    /// 初始化中心点高亮
    /// </summary>
    private void InitializeCenterPoint()
    {
        if (isInitialized) return;
        
        // 创建中心点材质
        centerMaterial = new Material(Shader.Find("Unlit/Color"));
        centerMaterial.color = Color.red; // 中心点用红色高亮
        centerMaterial.hideFlags = HideFlags.DontSave;
        
        // 创建中心点对象
        centerPoint = new GameObject($"CenterPoint_{gameObject.name}");
        centerPoint.transform.parent = transform;
        
        // 计算模型中心点
        Bounds bounds = CalculateModelBounds();
        centerPoint.transform.localPosition = bounds.center;
        
        centerPoint.transform.localRotation = Quaternion.identity;
        centerPoint.transform.localScale = Vector3.one * centerSize;
        centerPoint.hideFlags = HideFlags.DontSave;
        
        // 添加渲染器和碰撞体
        MeshRenderer centerRenderer = centerPoint.AddComponent<MeshRenderer>();
        centerRenderer.material = centerMaterial;
        centerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        centerRenderer.receiveShadows = false;
        // 调整渲染队列，确保中心点显示在模型前面
        centerRenderer.material.renderQueue = 3001; // Transparent+1，确保在透明物体前面
        
        // 稍微向前偏移，避免被模型遮挡
        centerPoint.transform.localPosition += centerPoint.transform.forward * 0.1f;
        
        SphereCollider centerCollider = centerPoint.AddComponent<SphereCollider>();
        centerCollider.radius = centerSize * 0.5f;
        
        MeshFilter centerMeshFilter = centerPoint.AddComponent<MeshFilter>();
        centerMeshFilter.mesh = CreateSphereMesh();
        
        // 默认隐藏中心点
        centerPoint.SetActive(false);
        isInitialized = true;
    }
    
    /// <summary>
    /// 创建球体网格
    /// </summary>
    private Mesh CreateSphereMesh()
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Mesh mesh = sphere.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(sphere);
        return mesh;
    }
    
    /// <summary>
    /// 计算模型的边界框
    /// </summary>
    private Bounds CalculateModelBounds()
    {
        Bounds totalBounds = new Bounds(transform.position, Vector3.zero);
        bool hasBounds = false;
        
        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();
        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter.mesh != null && meshFilter.mesh.vertices.Length > 0)
            {
                Bounds meshBounds = meshFilter.mesh.bounds;
                Vector3 worldCenter = meshFilter.transform.TransformPoint(meshBounds.center);
                Vector3 worldExtents = meshFilter.transform.TransformVector(meshBounds.extents);
                Bounds worldBounds = new Bounds(worldCenter, worldExtents * 2);
                
                if (!hasBounds)
                {
                    totalBounds = worldBounds;
                    hasBounds = true;
                }
                else
                {
                    totalBounds.Encapsulate(worldBounds);
                }
            }
        }
        
        // 如果没有找到有效的网格，使用变换位置
        if (!hasBounds)
        {
            totalBounds = new Bounds(transform.position, Vector3.one);
        }
        
        return totalBounds;
    }
    
    /// <summary>
    /// 设置中心点可见性
    /// </summary>
    private void SetCenterPointVisible(bool visible)
    {
        if (centerPoint != null)
        {
            centerPoint.SetActive(visible);
        }
    }
    
    /// <summary>
    /// 设置模型透明度
    /// </summary>
    private void SetModelOpacity(float opacity)
    {
        foreach (Renderer renderer in allRenderers)
        {
            // 获取当前材质属性
            renderer.GetPropertyBlock(propBlock);
            
            // 保存原始透明度
            if (!originalOpacities.ContainsKey(renderer))
            {
                float originalAlpha = 1.0f;
                if (renderer.material.HasProperty("_BaseColorFactor"))
                {
                    originalAlpha = propBlock.HasColor("_BaseColorFactor") ? propBlock.GetColor("_BaseColorFactor").a : renderer.material.GetColor("_BaseColorFactor").a;
                }
                else if (renderer.material.HasProperty("_BaseColor"))
                {
                    originalAlpha = propBlock.HasColor("_BaseColor") ? propBlock.GetColor("_BaseColor").a : renderer.material.GetColor("_BaseColor").a;
                }
                else if (renderer.material.HasProperty("_Color"))
                {
                    originalAlpha = propBlock.HasColor("_Color") ? propBlock.GetColor("_Color").a : renderer.material.GetColor("_Color").a;
                }
                originalOpacities[renderer] = originalAlpha;
            }
            
            // 确保材质支持透明度
            if (opacity < 1.0f)
            {
                // 设置渲染队列为透明
                renderer.material.renderQueue = 3000;
                
                // 对于某些材质，可能需要启用透明度混合
                if (renderer.material.HasProperty("_Mode"))
                {
                    renderer.material.SetInt("_Mode", 3); // 设置为透明模式
                }
                if (renderer.material.HasProperty("_SrcBlend"))
                {
                    renderer.material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                }
                if (renderer.material.HasProperty("_DstBlend"))
                {
                    renderer.material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                }
                if (renderer.material.HasProperty("_ZWrite"))
                {
                    renderer.material.SetInt("_ZWrite", 0);
                }
                renderer.material.DisableKeyword("_ALPHATEST_ON");
                renderer.material.EnableKeyword("_ALPHABLEND_ON");
                renderer.material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                renderer.material.renderQueue = 3000;
            }
            else
            {
                // 恢复默认渲染队列
                renderer.material.renderQueue = -1;
                
                // 恢复不透明模式
                if (renderer.material.HasProperty("_Mode"))
                {
                    renderer.material.SetInt("_Mode", 0); // 设置为不透明模式
                }
                if (renderer.material.HasProperty("_ZWrite"))
                {
                    renderer.material.SetInt("_ZWrite", 1);
                }
                renderer.material.DisableKeyword("_ALPHATEST_ON");
                renderer.material.DisableKeyword("_ALPHABLEND_ON");
                renderer.material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                renderer.material.renderQueue = -1;
            }
            
            // 设置新的透明度
            if (renderer.material.HasProperty("_BaseColorFactor"))
            {
                Color color = propBlock.HasColor("_BaseColorFactor") ? propBlock.GetColor("_BaseColorFactor") : renderer.material.GetColor("_BaseColorFactor");
                color.a = opacity;
                propBlock.SetColor("_BaseColorFactor", color);
            }
            if (renderer.material.HasProperty("_BaseColor"))
            {
                Color color = propBlock.HasColor("_BaseColor") ? propBlock.GetColor("_BaseColor") : renderer.material.GetColor("_BaseColor");
                color.a = opacity;
                propBlock.SetColor("_BaseColor", color);
            }
            if (renderer.material.HasProperty("_Color"))
            {
                Color color = propBlock.HasColor("_Color") ? propBlock.GetColor("_Color") : renderer.material.GetColor("_Color");
                color.a = opacity;
                propBlock.SetColor("_Color", color);
            }
            
            // 应用材质属性
            renderer.SetPropertyBlock(propBlock);
        }
    }
    
    void OnCollisionEnter(Collision collision)
    {
        if (UIUtil.IsPointerOverUI()) return;
        if (IsInputActive()) return;
        HighlightModel(true);
        Debug.Log($"模型 {gameObject.name} 与 {collision.gameObject.name} 发生碰撞");
    }
    
    void OnCollisionExit(Collision collision)
    {
        //if (UIUtil.IsPointerOverUI()) return;
        // 当碰撞结束时恢复原色
        HighlightModel(false);
        Debug.Log($"模型 {gameObject.name} 与 {collision.gameObject.name} 碰撞结束");
    }
    
    // 添加OnCollisionStay方法用于调试
    void OnCollisionStay(Collision collision)
    {
        if (UIUtil.IsPointerOverUI()) return;
        // 持续碰撞时的调试信息
        Debug.Log($"模型 {gameObject.name} 与 {collision.gameObject.name} 持续碰撞中...");
    }
    
    // 添加触发器碰撞检测支持
    void OnTriggerEnter(Collider other)
    {
        if (UIUtil.IsPointerOverUI()) return;
        if (IsInputActive()) return;
        HighlightModel(true);
        Debug.Log($"模型 {gameObject.name} 进入 {other.gameObject.name} 的触发器");
    }
    
    void OnTriggerExit(Collider other)
    {
        //if (UIUtil.IsPointerOverUI()) return;
        // 当离开触发器时恢复原色
        HighlightModel(false);
        Debug.Log($"模型 {gameObject.name} 离开 {other.gameObject.name} 的触发器");
    }
    
    private void HighlightModel(bool highlight, Color highlightColor = default(Color))
    {
        if (isSelected)
        {
            return;
        }
        // 如果没有指定颜色，默认使用黄色
        if (highlightColor == default(Color))
        {
            highlightColor = Color.yellow; // 模型用黄色高亮
        }
        
        if (isHighlighted == highlight) return;
        
        // 如果要高亮当前物体，先取消之前高亮物体的高亮状态
        if (highlight && currentHighlightedObject != null && currentHighlightedObject != this)
        {
            currentHighlightedObject.HighlightModel(false);
        }
        
        isHighlighted = highlight;
        
        // 更新当前高亮物体
        if (highlight)
        {
            currentHighlightedObject = this;
            // 初始化中心点
            InitializeCenterPoint();
            // 显示中心点
            SetCenterPointVisible(true);
            // 设置模型透明化
            SetModelOpacity(highlightOpacity);
        }
        else if (currentHighlightedObject == this)
        {
            currentHighlightedObject = null;
            // 隐藏中心点
            SetCenterPointVisible(false);
            // 恢复模型不透明度
            SetModelOpacity(1.0f);
        }
        
        // 简化材质修改，减少调试日志
        foreach (Renderer renderer in allRenderers)
        {
            // 获取当前材质属性
            renderer.GetPropertyBlock(propBlock);
            
            if (highlight)
            {
                // 保存原始颜色，支持多种材质属性
                Color originalColor = Color.white;
                bool foundColor = false;
                
                // 优先检测常见的颜色属性
                if (renderer.material.HasProperty("_BaseColorFactor")) // glTF PBR 材质
                {
                    originalColor = propBlock.HasColor("_BaseColorFactor") ? propBlock.GetColor("_BaseColorFactor") : renderer.material.GetColor("_BaseColorFactor");
                    foundColor = true;
                }
                else if (renderer.material.HasProperty("_BaseColor")) // URP 材质或glTF PBR材质
                {
                    originalColor = propBlock.HasColor("_BaseColor") ? propBlock.GetColor("_BaseColor") : renderer.material.GetColor("_BaseColor");
                    foundColor = true;
                }
                else if (renderer.material.HasProperty("_Color")) // 传统材质
                {
                    originalColor = propBlock.HasColor("_Color") ? propBlock.GetColor("_Color") : renderer.material.GetColor("_Color");
                    foundColor = true;
                }
                else if (renderer.material.HasProperty("_MainColor")) // 其他可能的颜色属性名称
                {
                    originalColor = propBlock.HasColor("_MainColor") ? propBlock.GetColor("_MainColor") : renderer.material.GetColor("_MainColor");
                    foundColor = true;
                }
                else
                {
                    // 尝试直接获取材质的颜色
                    try
                    {
                        originalColor = renderer.material.color;
                        foundColor = true;
                        usedDirectColorAccess[renderer] = true;
                    }
                    catch
                    {
                        usedDirectColorAccess[renderer] = false;
                    }
                }
                
                originalColors[renderer] = originalColor;
                if (!usedDirectColorAccess.ContainsKey(renderer))
                {
                    usedDirectColorAccess[renderer] = false;
                }
                
                // 设置高亮颜色，支持多种材质属性
                bool colorSet = false;
                
                if (renderer.material.HasProperty("_BaseColorFactor"))
                {
                    propBlock.SetColor("_BaseColorFactor", highlightColor);
                    colorSet = true;
                }
                if (renderer.material.HasProperty("_BaseColor"))
                {
                    propBlock.SetColor("_BaseColor", highlightColor);
                    colorSet = true;
                }
                if (renderer.material.HasProperty("_Color"))
                {
                    propBlock.SetColor("_Color", highlightColor);
                    colorSet = true;
                }
                if (renderer.material.HasProperty("_MainColor"))
                {
                    propBlock.SetColor("_MainColor", highlightColor);
                    colorSet = true;
                }
                
                // 如果使用了直接颜色访问或者其他方式都没设置成功，尝试直接设置material.color
                if (usedDirectColorAccess.ContainsKey(renderer) && usedDirectColorAccess[renderer] || !colorSet)
                {
                    try
                    {
                        renderer.material.color = highlightColor;
                    }
                    catch
                    {
                        // 忽略设置失败的情况
                    }
                }
            }
            else
            {
                // 恢复原始颜色，支持多种材质属性
                if (originalColors.TryGetValue(renderer, out Color savedColor))
                {
                    bool colorRestored = false;
                    
                    if (renderer.material.HasProperty("_BaseColorFactor"))
                    {
                        propBlock.SetColor("_BaseColorFactor", savedColor);
                        colorRestored = true;
                    }
                    if (renderer.material.HasProperty("_BaseColor"))
                    {
                        propBlock.SetColor("_BaseColor", savedColor);
                        colorRestored = true;
                    }
                    if (renderer.material.HasProperty("_Color"))
                    {
                        propBlock.SetColor("_Color", savedColor);
                        colorRestored = true;
                    }
                    if (renderer.material.HasProperty("_MainColor"))
                    {
                        propBlock.SetColor("_MainColor", savedColor);
                        colorRestored = true;
                    }
                    
                    // 如果使用了直接颜色访问或者其他方式都没恢复成功，尝试直接设置material.color
                    if (usedDirectColorAccess.ContainsKey(renderer) && usedDirectColorAccess[renderer] || !colorRestored)
                    {
                        try
                        {
                            renderer.material.color = savedColor;
                        }
                        catch
                        {
                            // 忽略恢复失败的情况
                        }
                    }
                }
            }
            
            // 应用材质属性
            renderer.SetPropertyBlock(propBlock);
        }
    }


    private void SelectModel(bool highlight, Color highlightColor = default(Color))
    {
        // 如果没有指定颜色，默认使用黄色
        if (highlightColor == default(Color))
        {
            highlightColor = Color.red; // 模型用黄色高亮
        }

        if (isSelected == highlight) return;

        // 如果要高亮当前物体，先取消之前高亮物体的高亮状态
        if (highlight && selectedObject != null && selectedObject != this)
        {
            selectedObject.HighlightModel(false);
        }

        isSelected = highlight;

        // 更新当前高亮物体
        if (highlight)
        {
            selectedObject = this;
            if (!ModelCollisionHighlighter.SeletectedObjects.Contains(this.transform))
            {
                ModelCollisionHighlighter.SeletectedObjects.Add(this.transform);
            }
            Debug.Log($"添加途经点：{this.name}");
            PathPointManager.Instance?.AddPoint(gameObject);
            // 初始化中心点
            InitializeCenterPoint();
            // 显示中心点
            SetCenterPointVisible(true);
            // 设置模型透明化
            SetModelOpacity(highlightOpacity);
        }
        else
        {
            if (selectedObject == this)
            {
                selectedObject = null;
            }
            ModelCollisionHighlighter.SeletectedObjects.RemoveAll(item => item == this.transform);
            PathPointManager.Instance?.RemovePoint(gameObject);
            // 隐藏中心点
            SetCenterPointVisible(false);
            // 恢复模型不透明度
            SetModelOpacity(1.0f);
        }

        // 简化材质修改，减少调试日志
        foreach (Renderer renderer in allRenderers)
        {
            // 获取当前材质属性
            renderer.GetPropertyBlock(propBlock);

            if (highlight)
            {
                // 保存原始颜色，支持多种材质属性
                Color originalColor = Color.white;
                bool foundColor = false;

                // 优先检测常见的颜色属性
                if (renderer.material.HasProperty("_BaseColorFactor")) // glTF PBR 材质
                {
                    originalColor = propBlock.HasColor("_BaseColorFactor") ? propBlock.GetColor("_BaseColorFactor") : renderer.material.GetColor("_BaseColorFactor");
                    foundColor = true;
                }
                else if (renderer.material.HasProperty("_BaseColor")) // URP 材质或glTF PBR材质
                {
                    originalColor = propBlock.HasColor("_BaseColor") ? propBlock.GetColor("_BaseColor") : renderer.material.GetColor("_BaseColor");
                    foundColor = true;
                }
                else if (renderer.material.HasProperty("_Color")) // 传统材质
                {
                    originalColor = propBlock.HasColor("_Color") ? propBlock.GetColor("_Color") : renderer.material.GetColor("_Color");
                    foundColor = true;
                }
                else if (renderer.material.HasProperty("_MainColor")) // 其他可能的颜色属性名称
                {
                    originalColor = propBlock.HasColor("_MainColor") ? propBlock.GetColor("_MainColor") : renderer.material.GetColor("_MainColor");
                    foundColor = true;
                }
                else
                {
                    // 尝试直接获取材质的颜色
                    try
                    {
                        originalColor = renderer.material.color;
                        foundColor = true;
                        usedDirectColorAccess[renderer] = true;
                    }
                    catch
                    {
                        usedDirectColorAccess[renderer] = false;
                    }
                }

                originalColors[renderer] = originalColor;
                if (!usedDirectColorAccess.ContainsKey(renderer))
                {
                    usedDirectColorAccess[renderer] = false;
                }

                // 设置高亮颜色，支持多种材质属性
                bool colorSet = false;

                if (renderer.material.HasProperty("_BaseColorFactor"))
                {
                    propBlock.SetColor("_BaseColorFactor", highlightColor);
                    colorSet = true;
                }
                if (renderer.material.HasProperty("_BaseColor"))
                {
                    propBlock.SetColor("_BaseColor", highlightColor);
                    colorSet = true;
                }
                if (renderer.material.HasProperty("_Color"))
                {
                    propBlock.SetColor("_Color", highlightColor);
                    colorSet = true;
                }
                if (renderer.material.HasProperty("_MainColor"))
                {
                    propBlock.SetColor("_MainColor", highlightColor);
                    colorSet = true;
                }

                // 如果使用了直接颜色访问或者其他方式都没设置成功，尝试直接设置material.color
                if (usedDirectColorAccess.ContainsKey(renderer) && usedDirectColorAccess[renderer] || !colorSet)
                {
                    try
                    {
                        renderer.material.color = highlightColor;
                    }
                    catch
                    {
                        // 忽略设置失败的情况
                    }
                }
            }
            else
            {
                // 恢复原始颜色，支持多种材质属性
                if (originalColors.TryGetValue(renderer, out Color savedColor))
                {
                    bool colorRestored = false;

                    if (renderer.material.HasProperty("_BaseColorFactor"))
                    {
                        propBlock.SetColor("_BaseColorFactor", savedColor);
                        colorRestored = true;
                    }
                    if (renderer.material.HasProperty("_BaseColor"))
                    {
                        propBlock.SetColor("_BaseColor", savedColor);
                        colorRestored = true;
                    }
                    if (renderer.material.HasProperty("_Color"))
                    {
                        propBlock.SetColor("_Color", savedColor);
                        colorRestored = true;
                    }
                    if (renderer.material.HasProperty("_MainColor"))
                    {
                        propBlock.SetColor("_MainColor", savedColor);
                        colorRestored = true;
                    }

                    // 如果使用了直接颜色访问或者其他方式都没恢复成功，尝试直接设置material.color
                    if (usedDirectColorAccess.ContainsKey(renderer) && usedDirectColorAccess[renderer] || !colorRestored)
                    {
                        try
                        {
                            renderer.material.color = savedColor;
                        }
                        catch
                        {
                            // 忽略恢复失败的情况
                        }
                    }
                }
            }

            // 应用材质属性
            renderer.SetPropertyBlock(propBlock);
        }
    }

    // 鼠标进入模型时触发
    private void OnMouseEnter()
    {
        if (UIUtil.IsPointerOverUI()) return;
        if (IsInputActive()) return;
        Color highlightColor = isReplacedJoint ? new Color(1f, 0.5f, 0f) : Color.yellow;
        HighlightModel(true, highlightColor);
        //Debug.Log($"鼠标进入模型 {gameObject.name}，设置{(isReplacedJoint ? "橙色" : "黄色")}高亮");
    }
    
    // 鼠标离开模型时触发
    private void OnMouseExit()
    {
        //if (UIUtil.IsPointerOverUI()) return;
        // 鼠标离开时恢复原始状态
        HighlightModel(false);
        //Debug.Log($"鼠标离开模型 {gameObject.name}，恢复原始状态");
    }
    
    /// <summary>
    /// 检查是否有活动的输入（键盘或鼠标右键、中键）
    /// </summary>
    private bool IsInputActive()
    {
        return AnyKeyboardPressed()
            || Input.GetMouseButton(1) 
            || Input.GetMouseButton(2);
    }
    private bool AnyKeyboardPressed()
    {
        if (!Input.anyKey) return false;
        foreach (KeyCode key in System.Enum.GetValues(typeof(KeyCode)))
        {
            // KeyCode.Mouse0及以上是鼠标，小于则为键盘按键
            if (Input.GetKey(key) && key < KeyCode.Mouse0)
                return true;
        }
        return false;
    }

    private void Update()
    {
        // 检测鼠标左键点击，但忽略UI元素和活动输入
        Camera activeCamera = CameraTool.GetActiveCamera();
        if (Input.GetMouseButtonDown(0) && !UIUtil.IsPointerOverUI() && !IsInputActive() && activeCamera != null)
        {
            // 创建从相机到鼠标位置的射线
            Ray ray = activeCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            
            // 执行射线检测
            if (Physics.Raycast(ray, out hit))
            {
                // 检查点击的是否是当前游戏对象或其子对象
                if (hit.transform == transform || hit.transform.IsChildOf(transform))
                {
                    // 打印调试信息
                    Vector3 mousePos = Input.mousePosition;
                    Vector3 worldPos = hit.point + Vector3.up * 1.5f;
                    Vector3 screenPos = activeCamera.WorldToScreenPoint(worldPos);
                    Debug.Log($"[射线检测] 点击坐标: ({mousePos.x:F1}, {mousePos.y:F1}), 物体世界坐标: {hit.point}, 标记世界坐标: {worldPos}, 屏幕坐标: ({screenPos.x:F1}, {screenPos.y:F1}), 物体: {gameObject.name}");
                    
                    // 添加路径点，使用碰撞点 + 偏移作为世界坐标
                    PathPointManager.Instance?.AddPoint(gameObject, worldPos);
                    
                    // 切换高亮状态
                    SelectModel(!isSelected, Color.red);
                    if (!isSelected)
                    {
                        HighlightModel(false);
                        Debug.Log($"移除途经点：{this.name}");
                    }
                    Debug.Log($"模型 {gameObject.name} 点击{(isHighlighted ? "高亮" : "取消高亮")}");
                }
            }
        }
    }

    /// <summary>
    /// 清理创建的中心点对象
    /// </summary>
    private void OnDestroy()
    {
        // 清理中心点对象
        if (centerPoint != null)
        {
            DestroyImmediate(centerPoint);
        }
        
        // 清理材质
        if (centerMaterial != null)
        {
            DestroyImmediate(centerMaterial);
        }
    }
}
