using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class EditorLikeTransformHandle : MonoBehaviour
{
    #region 单例与核心状态
    public static EditorLikeTransformHandle Instance;
    private Transform selectedTransform;
    private bool isHandleActive;
    private int currentMode = 0; // 0=平移 1=旋转
    private int dragType = -1;   // -1=无 0=X轴 1=Y轴 2=Z轴（移除面拖动的3/4/5）
    #endregion

    #region 样式配置（匹配编辑器默认样式）
    [Header("编辑器样式配置")]
    public float axisLength = 1.5f;
    public float axisThickness = 0.018f;
    public float coneSize = 0.08f; // 圆锥尺寸
    public float rotateRingRadius = 1.2f;
    public Color xAxisColor = new Color(1, 0.2f, 0.2f);
    public Color yAxisColor = new Color(0.2f, 1, 0.2f);
    public Color zAxisColor = new Color(0.2f, 0.2f, 1);
    
    [Header("拖动范围限制")]
    public float maxDragDistance = 50f; // 最大拖动距离（相对于相机）
    public float minDragDistance = 0.5f; // 最小拖动距离（防止穿到相机后面）
    public float maxMovePerFrame = 5f; // 每帧最大移动距离
    // 移除：面拖动相关颜色配置
    
    [Header("屏幕空间尺寸控制")]
    public float handleScreenHeightRatio = 0.33f; // 手柄占屏幕高度的比例（1/3）
    #endregion

    #region 手柄对象管理
    private class TransformHandle
    {
        // 平移轴（手动构建的方块）
        public LineRenderer xAxisLine;
        public LineRenderer yAxisLine;
        public LineRenderer zAxisLine;
        public GameObject xAxisCube; // 手动构建的方块
        public GameObject yAxisCube;
        public GameObject zAxisCube;

        // 移除：面拖动相关手柄

        // 旋转环
        public LineRenderer xRotateRing;
        public LineRenderer yRotateRing;
        public LineRenderer zRotateRing;
        
        // 旋转环上的球体手柄（每个环 2 个球）
        public GameObject[] xRotateSpheres = new GameObject[2];
        public GameObject[] yRotateSpheres = new GameObject[2];
        public GameObject[] zRotateSpheres = new GameObject[2];
    }
    private TransformHandle handle;
    public Camera mainCamera;
    #endregion

    #region 交互变量
    private Vector3 lastMousePos;
    private bool isDragging;
    private Plane dragPlane;
    #endregion

    private void Awake()
    {
        // 单例初始化
        if (Instance == null)
        {
            Instance = this;
            //DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // 相机初始化
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("未找到主相机！请手动指定mainCamera");
            enabled = false;
            return;
        }

        // 创建手柄
        CreateAllHandles();
        SetAllHandlesActive(false);
    }

    private void Update()
    {
        // 处理物体点击（显隐手柄）
        HandleObjectClick();

        // 处理模式切换（Tab键）
        HandleModeSwitch();

        // 激活状态下更新手柄和交互
        if (selectedTransform != null && isHandleActive)
        {
            UpdateHandleTransform();
            HandleMouseDrag();
        }
    }

    #region 手柄创建（复刻编辑器样式）
    private void CreateAllHandles()
    {
        handle = new TransformHandle();

        // ========== 创建平移轴 ==========
        // X 轴（线 + 手动构建的方块）
        handle.xAxisLine = CreateAxisLine("XAxis", xAxisColor, axisThickness);
        handle.xAxisCube = CreateCubeHandle("XAxisCube", xAxisColor, coneSize);

        // Y 轴（线 + 手动构建的方块）
        handle.yAxisLine = CreateAxisLine("YAxis", yAxisColor, axisThickness);
        handle.yAxisCube = CreateCubeHandle("YAxisCube", yAxisColor, coneSize);

        // Z 轴（线 + 手动构建的方块）
        handle.zAxisLine = CreateAxisLine("ZAxis", zAxisColor, axisThickness);
        handle.zAxisCube = CreateCubeHandle("ZAxisCube", zAxisColor, coneSize);

        // 移除：面拖动手柄创建

        // ========== 创建旋转环 ==========
        handle.xRotateRing = CreateRotateRing("XRotateRing", xAxisColor, axisThickness, 40);
        handle.yRotateRing = CreateRotateRing("YRotateRing", yAxisColor, axisThickness, 40);
        handle.zRotateRing = CreateRotateRing("ZRotateRing", zAxisColor, axisThickness, 40);
        
        // ========== 创建旋转环上的球体手柄 ==========
        float sphereRadius = axisThickness * 2f; // 球的直径为环的直径的 4 倍
        CreateRotateSphereHandles(handle.xRotateSpheres, xAxisColor, sphereRadius);
        CreateRotateSphereHandles(handle.yRotateSpheres, yAxisColor, sphereRadius);
        CreateRotateSphereHandles(handle.zRotateSpheres, zAxisColor, sphereRadius);
    }

    /// <summary>创建轴线（LineRenderer）</summary>
    private LineRenderer CreateAxisLine(string name, Color color, float width)
    {
        GameObject obj = new GameObject(name);
        obj.transform.parent = transform;
        obj.hideFlags = HideFlags.HideInHierarchy;

        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Unlit/Color")) { color = color };
        lr.startColor = color;
        lr.endColor = color;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        //lr.useUniformScaling = true; // 启用统一缩放
        return lr;
    }

    /// <summary>手动创建方块手柄（替代 Unity 内置 Cube Primitive）</summary>
    private GameObject CreateCubeHandle(string name, Color color, float size)
    {
        // 创建空物体作为方块容器
        GameObject cubeObj = new GameObject(name);
        cubeObj.transform.parent = transform;
        cubeObj.hideFlags = HideFlags.HideInHierarchy;

        // 添加 MeshFilter 和 MeshRenderer
        MeshFilter meshFilter = cubeObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = cubeObj.AddComponent<MeshRenderer>();
        meshRenderer.material = new Material(Shader.Find("Unlit/Color")) { color = color };

        // 手动构建方块 Mesh
        Mesh cubeMesh = new Mesh();
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        // 构建立方体顶点（边长为 size 的立方体）
        float half = size * 0.5f;
        vertices.Add(new Vector3(-half, -half, -half)); // 0
        vertices.Add(new Vector3(half, -half, -half));  // 1
        vertices.Add(new Vector3(half, half, -half));   // 2
        vertices.Add(new Vector3(-half, half, -half));  // 3
        vertices.Add(new Vector3(-half, -half, half));  // 4
        vertices.Add(new Vector3(half, -half, half));   // 5
        vertices.Add(new Vector3(half, half, half));    // 6
        vertices.Add(new Vector3(-half, half, half));   // 7

        // 构建三角形（6 个面，每个面 2 个三角形）
        // 前面
        triangles.Add(0); triangles.Add(2); triangles.Add(1);
        triangles.Add(0); triangles.Add(3); triangles.Add(2);
        // 后面
        triangles.Add(5); triangles.Add(7); triangles.Add(4);
        triangles.Add(5); triangles.Add(6); triangles.Add(7);
        // 上面
        triangles.Add(3); triangles.Add(6); triangles.Add(2);
        triangles.Add(3); triangles.Add(7); triangles.Add(6);
        // 下面
        triangles.Add(1); triangles.Add(4); triangles.Add(0);
        triangles.Add(1); triangles.Add(5); triangles.Add(4);
        // 右面
        triangles.Add(1); triangles.Add(6); triangles.Add(5);
        triangles.Add(1); triangles.Add(2); triangles.Add(6);
        // 左面
        triangles.Add(0); triangles.Add(7); triangles.Add(3);
        triangles.Add(0); triangles.Add(4); triangles.Add(7);

        // 赋值 Mesh 数据
        cubeMesh.vertices = vertices.ToArray();
        cubeMesh.triangles = triangles.ToArray();
        cubeMesh.RecalculateNormals();
        cubeMesh.RecalculateBounds();
        meshFilter.mesh = cubeMesh;

        return cubeObj;
    }

    /// <summary>创建旋转环（LineRenderer）</summary>
    private LineRenderer CreateRotateRing(string name, Color color, float width, int segments)
    {
        GameObject obj = new GameObject(name);
        obj.transform.parent = transform;
        obj.hideFlags = HideFlags.HideInHierarchy;

        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Unlit/Color")) { color = color };
        lr.startColor = new Color(color.r, color.g, color.b, 0.3f); // 降低透明度
        lr.endColor = new Color(color.r, color.g, color.b, 0.3f);
        lr.startWidth = width;
        lr.endWidth = width;
        lr.positionCount = segments;
        lr.loop = true;
        lr.useWorldSpace = true;
        //lr.useUniformScaling = true; // 启用统一缩放
        return lr;
    }
    
    /// <summary>创建旋转环上的球体手柄（2 个球）</summary>
    private void CreateRotateSphereHandles(GameObject[] spheres, Color color, float radius)
    {
        for (int i = 0; i < 2; i++)
        {
            spheres[i] = CreateSphereHandle($"RotateSphere_{i}", color, radius);
        }
    }
    
    /// <summary>手动创建球体手柄</summary>
    private GameObject CreateSphereHandle(string name, Color color, float radius)
    {
        GameObject sphereObj = new GameObject(name);
        sphereObj.transform.parent = transform;
        sphereObj.hideFlags = HideFlags.HideInHierarchy;

        MeshFilter meshFilter = sphereObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = sphereObj.AddComponent<MeshRenderer>();
        Shader shader = Shader.Find("Standard");
        Material mat = new Material(shader);
        mat.color = color;
        mat.SetFloat("_Mode", 0); // 不透明模式
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        mat.SetInt("_ZWrite", 1);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = -1; // 使用默认渲染队列（不透明）
        meshRenderer.material = mat;
        
        // 添加 MeshCollider 以便更准确地进行射线检测
        MeshCollider collider = sphereObj.AddComponent<MeshCollider>();

        Mesh sphereMesh = new Mesh();
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        // 构建球体（经纬度细分）
        int segments = 16; // 经线分段数
        int rings = 12;    // 纬线分段数

        // 生成球体顶点
        for (int ring = 0; ring <= rings; ring++)
        {
            float phi = Mathf.PI * ring / rings;
            for (int seg = 0; seg <= segments; seg++)
            {
                float theta = Mathf.PI * 2 * seg / segments;
                float x = Mathf.Sin(phi) * Mathf.Cos(theta);
                float y = Mathf.Cos(phi);
                float z = Mathf.Sin(phi) * Mathf.Sin(theta);
                vertices.Add(new Vector3(x, y, z) * radius);
            }
        }

        // 构建三角形
        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                int current = ring * (segments + 1) + seg;
                int next = current + segments + 1;

                triangles.Add(current);
                triangles.Add(next);
                triangles.Add(current + 1);

                triangles.Add(next);
                triangles.Add(next + 1);
                triangles.Add(current + 1);
            }
        }

        sphereMesh.vertices = vertices.ToArray();
        sphereMesh.triangles = triangles.ToArray();
        sphereMesh.RecalculateNormals();
        sphereMesh.RecalculateBounds();
        meshFilter.mesh = sphereMesh;

        return sphereObj;
    }
    #endregion

    #region 核心交互逻辑
    /// <summary>处理物体点击（显隐手柄）</summary>
    private void HandleObjectClick()
    {
        if (Input.GetMouseButtonUp(0))
        {
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // 点击到物体
                if (hit.transform == selectedTransform)
                {
                    // 再次点击同一物体 → 关闭手柄
                    selectedTransform = null;
                    isHandleActive = false;
                    SetAllHandlesActive(false);
                }
                else
                {
                    // 点击新物体 → 显示手柄
                    selectedTransform = hit.transform;
                    isHandleActive = true;
                    SetAllHandlesActive(true);
                }
            }
            // 点击空白处 → 不做任何操作（保留选中）
        }
    }

    /// <summary>处理Tab键切换平移/旋转模式</summary>
    private void HandleModeSwitch()
    {
        if (Input.GetKeyDown(KeyCode.Tab) && isHandleActive)
        {
            currentMode = 1 - currentMode;
            UpdateHandleVisibility();
        }
    }

    /// <summary>更新手柄显示/隐藏（根据模式）</summary>
    private void UpdateHandleVisibility()
    {
        bool isMoveMode = currentMode == 0;

        // 平移组件显隐（仅保留轴，移除面）
        handle.xAxisLine.enabled = isMoveMode;
        handle.yAxisLine.enabled = isMoveMode;
        handle.zAxisLine.enabled = isMoveMode;
        handle.xAxisCube.SetActive(isMoveMode);
        handle.yAxisCube.SetActive(isMoveMode);
        handle.zAxisCube.SetActive(isMoveMode);

        // 旋转组件显隐
        handle.xRotateRing.enabled = !isMoveMode;
        handle.yRotateRing.enabled = !isMoveMode;
        handle.zRotateRing.enabled = !isMoveMode;
        
        // 旋转球体手柄显隐
        for (int i = 0; i < 2; i++)
        {
            handle.xRotateSpheres[i].SetActive(!isMoveMode);
            handle.yRotateSpheres[i].SetActive(!isMoveMode);
            handle.zRotateSpheres[i].SetActive(!isMoveMode);
        }
    }

    /// <summary>更新手柄位置和形状（跟随选中物体）</summary>
    private void UpdateHandleTransform()
    {
        Vector3 origin = selectedTransform.position;
        Vector3 right = selectedTransform.right;
        Vector3 up = selectedTransform.up;
        Vector3 forward = selectedTransform.forward;

        if (currentMode == 0)
        {
            // ========== 平移模式：更新坐标轴和方块 ==========
            // 计算相机距离（使用选中物体到相机的距离）
            float cameraDistance = Vector3.Distance(origin, mainCamera.transform.position);
            float targetScreenSize = GetTargetScreenSizePixels();
            float worldSize = CalculateWorldSizeFromScreenSize(targetScreenSize, cameraDistance);
            float scale = worldSize / coneSize; // coneSize 是原始尺寸
            
            // 计算轴线宽度（保持与方块相同的屏幕尺寸比例）
            float axisWidthRatio = axisThickness / coneSize;
            float axisWorldWidth = worldSize * axisWidthRatio;
            
            // X 轴
            handle.xAxisLine.SetPosition(0, origin);
            handle.xAxisLine.SetPosition(1, origin + right * axisLength);
            handle.xAxisLine.widthMultiplier = axisWorldWidth;
            // 更新 X 轴方块
            handle.xAxisCube.transform.position = origin + right * axisLength;
            handle.xAxisCube.transform.rotation = Quaternion.LookRotation(right) * Quaternion.Euler(0, 90, 0);
            handle.xAxisCube.transform.localScale = Vector3.one * scale;

            // Y 轴
            handle.yAxisLine.SetPosition(0, origin);
            handle.yAxisLine.SetPosition(1, origin + up * axisLength);
            handle.yAxisLine.widthMultiplier = axisWorldWidth;
            // 更新 Y 轴方块
            handle.yAxisCube.transform.position = origin + up * axisLength;
            handle.yAxisCube.transform.rotation = Quaternion.LookRotation(up);
            handle.yAxisCube.transform.localScale = Vector3.one * scale;

            // Z 轴
            handle.zAxisLine.SetPosition(0, origin);
            handle.zAxisLine.SetPosition(1, origin + forward * axisLength);
            handle.zAxisLine.widthMultiplier = axisWorldWidth;
            // 更新 Z 轴方块
            handle.zAxisCube.transform.position = origin + forward * axisLength;
            handle.zAxisCube.transform.rotation = Quaternion.LookRotation(forward) * Quaternion.Euler(0, 90, 0);
            handle.zAxisCube.transform.localScale = Vector3.one * scale;

            // 移除：面拖动手柄更新逻辑
        }
        else
        {
            // ========== 旋转模式：更新旋转环 ==========
            // 计算相机距离和环宽度
            float cameraDistance = Vector3.Distance(origin, mainCamera.transform.position);
            float targetScreenSize = GetTargetScreenSizePixels();
            float worldSize = CalculateWorldSizeFromScreenSize(targetScreenSize, cameraDistance);
            float ringWidthRatio = axisThickness / coneSize;
            float ringWorldWidth = worldSize * ringWidthRatio;
            
            UpdateRotateRing(handle.xRotateRing, origin, right, up, forward, rotateRingRadius, ringWorldWidth);
            UpdateRotateRing(handle.yRotateRing, origin, up, forward, right, rotateRingRadius, ringWorldWidth);
            UpdateRotateRing(handle.zRotateRing, origin, forward, right, up, rotateRingRadius, ringWorldWidth);
            
            // 更新旋转环上的球体手柄
            UpdateRotateSphereHandles(handle.xRotateSpheres, origin, right, up, forward, rotateRingRadius);
            UpdateRotateSphereHandles(handle.yRotateSpheres, origin, up, forward, right, rotateRingRadius);
            UpdateRotateSphereHandles(handle.zRotateSpheres, origin, forward, right, up, rotateRingRadius);
        }
    }

    /// <summary>更新旋转环的形状</summary>
    private void UpdateRotateRing(LineRenderer ring, Vector3 origin, Vector3 axis, Vector3 cross1, Vector3 cross2, float radius, float width)
    {
        for (int i = 0; i < ring.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2 / (ring.positionCount - 1);
            Vector3 point = origin + cross1 * Mathf.Cos(angle) * radius + cross2 * Mathf.Sin(angle) * radius;
            ring.SetPosition(i, point);
        }
        ring.widthMultiplier = width;
    }
    
    /// <summary>更新旋转环上的球体手柄位置（2 个球对称分布在环上）</summary>
    private void UpdateRotateSphereHandles(GameObject[] spheres, Vector3 origin, Vector3 axis, Vector3 cross1, Vector3 cross2, float radius)
    {
        // 计算相机距离
        float cameraDistance = Vector3.Distance(origin, mainCamera.transform.position);
        float targetScreenSize = GetTargetScreenSizePixels();
        float worldSize = CalculateWorldSizeFromScreenSize(targetScreenSize, cameraDistance);
        float baseSphereRadius = axisThickness * 2f; // 创建时的原始半径
        float scale = worldSize / (baseSphereRadius * 2f); // 直径
        
        for (int i = 0; i < 2; i++)
        {
            float angle = i * Mathf.PI; // 2 个球对称分布（180 度）
            Vector3 position = origin + cross1 * Mathf.Cos(angle) * radius + cross2 * Mathf.Sin(angle) * radius;
            spheres[i].transform.position = position;
            spheres[i].transform.rotation = Quaternion.LookRotation(axis);
            spheres[i].transform.localScale = Vector3.one * scale;
        }
    }

    /// <summary>处理鼠标拖动（平移/旋转）</summary>
    private void HandleMouseDrag()
    {
        // 开始拖动：检测点击的手柄类型（仅检测轴，移除面）
        if (Input.GetMouseButtonDown(0) && !isDragging)
        {
            lastMousePos = Input.mousePosition;
            dragType = DetectDragType();
            isDragging = dragType != -1;

            // 初始化拖动平面（用于平移）
            if (isDragging && currentMode == 0)
            {
                InitDragPlane();
            }
        }

        // 正在拖动：执行平移/旋转
        if (Input.GetMouseButton(0) && isDragging && dragType != -1)
        {
            Vector3 mouseDelta = Input.mousePosition - lastMousePos;
            lastMousePos = Input.mousePosition;

            if (currentMode == 0)
            {
                // 平移模式
                PerformTranslate(mouseDelta);
            }
            else
            {
                // 旋转模式
                PerformRotate(mouseDelta);
            }
        }

        // 结束拖动
        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;
            dragType = -1;
        }
    }

    /// <summary>检测点击的手柄类型（仅轴/旋转环，移除面）</summary>
    private int DetectDragType()
    {
        float threshold = 15f; // 像素检测阈值

        if (currentMode == 0)
        {
            // 平移模式：仅检测方块（移除轴线检测）
            if (IsMouseOverObject(handle.xAxisCube, threshold)) return 0;
            if (IsMouseOverObject(handle.yAxisCube, threshold)) return 1;
            if (IsMouseOverObject(handle.zAxisCube, threshold)) return 2;
        }
        else
        {
            // 旋转模式：检测旋转环上的球体手柄
            for (int i = 0; i < 2; i++)
            {
                if (IsMouseOverObject(handle.xRotateSpheres[i], threshold)) return 0;
                if (IsMouseOverObject(handle.yRotateSpheres[i], threshold)) return 1;
                if (IsMouseOverObject(handle.zRotateSpheres[i], threshold)) return 2;
            }
        }

        return -1;
    }

    /// <summary>初始化拖动平面（仅轴相关，移除面）</summary>
    private void InitDragPlane()
    {
        Vector3 origin = selectedTransform.position;
        switch (dragType)
        {
            case 0: dragPlane = new Plane(selectedTransform.up, origin); break; // X轴：垂直Y轴
            case 1: dragPlane = new Plane(selectedTransform.forward, origin); break; // Y轴：垂直Z轴
            case 2: dragPlane = new Plane(selectedTransform.right, origin); break; // Z轴：垂直X轴
                                                                                   // 移除：面拖动的平面初始化
        }
    }

    /// <summary>执行平移操作（仅轴平移，移除面）</summary>
    private void PerformTranslate(Vector3 mouseDelta)
    {
        Ray ray = mainCamera.ScreenPointToRay(lastMousePos - mouseDelta);
        if (dragPlane.Raycast(ray, out float enter))
        {
            Vector3 oldPos = ray.GetPoint(enter);

            ray = mainCamera.ScreenPointToRay(lastMousePos);
            if (dragPlane.Raycast(ray, out enter))
            {
                Vector3 newPos = ray.GetPoint(enter);
                Vector3 delta = newPos - oldPos;

                // 根据拖动类型限制平移方向（仅轴）
                switch (dragType)
                {
                    case 0: delta = Vector3.Project(delta, selectedTransform.right); break; // X 轴
                    case 1: delta = Vector3.Project(delta, selectedTransform.up); break; // Y 轴
                    case 2: delta = Vector3.Project(delta, selectedTransform.forward); break; // Z 轴
                }

                // 限制每帧最大移动距离
                if (delta.magnitude > maxMovePerFrame)
                {
                    delta = delta.normalized * maxMovePerFrame;
                }

                // 计算目标位置
                Vector3 targetPos = selectedTransform.position + delta;

                // 限制拖动范围（相对于相机的距离）
                Vector3 toTarget = targetPos - mainCamera.transform.position;
                float distance = toTarget.magnitude;
                
                if (distance > maxDragDistance)
                {
                    targetPos = mainCamera.transform.position + toTarget.normalized * maxDragDistance;
                }
                else if (distance < minDragDistance)
                {
                    targetPos = mainCamera.transform.position + toTarget.normalized * minDragDistance;
                }

                selectedTransform.position = targetPos;
            }
        }
    }

    /// <summary>执行旋转操作</summary>
    private void PerformRotate(Vector3 mouseDelta)
    {
        float rotateSpeed = 0.5f;
        Vector3 rotateAxis = Vector3.zero;

        switch (dragType)
        {
            case 0: rotateAxis = selectedTransform.right; break; // X轴旋转
            case 1: rotateAxis = selectedTransform.up; break; // Y轴旋转
            case 2: rotateAxis = selectedTransform.forward; break; // Z轴旋转
        }

        // 计算旋转角度（复刻编辑器旋转逻辑）
        float angle = (mouseDelta.x - mouseDelta.y) * rotateSpeed;
        selectedTransform.Rotate(rotateAxis, angle, Space.World);
    }
    #endregion

    #region 辅助检测方法
    /// <summary>检测鼠标是否在LineRenderer上</summary>
    private bool IsMouseOverLine(LineRenderer lr, float threshold)
    {
        for (int i = 0; i < lr.positionCount - 1; i++)
        {
            Vector3 start = mainCamera.WorldToScreenPoint(lr.GetPosition(i));
            Vector3 end = mainCamera.WorldToScreenPoint(lr.GetPosition(i + 1));
            start.z = end.z = 0;

            if (DistanceToLine(Input.mousePosition, start, end) < threshold)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>检测鼠标是否在 GameObject（球体手柄）上</summary>
    private bool IsMouseOverObject(GameObject obj, float threshold)
    {
        if (obj == null) return false;
        
        // 获取球体中心在屏幕上的位置
        Vector3 center = mainCamera.WorldToScreenPoint(obj.transform.position);
        if (center.z < 0) return false; // 球体在相机后面
        
        // 计算球体在屏幕上的半径（考虑透视投影）
        float screenRadius = GetScreenSpaceRadius(obj, center);
        
        // 使用屏幕空间距离检测
        float distance = Vector2.Distance(Input.mousePosition, center);
        return distance < (screenRadius + threshold);
    }
    
    /// <summary>计算物体在屏幕空间中的半径</summary>
    private float GetScreenSpaceRadius(GameObject obj, Vector3 screenCenter)
    {
        MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.mesh == null) return 10f;
        
        float radius = meshFilter.mesh.bounds.extents.magnitude;
        Vector3 worldPos = obj.transform.position;
        
        // 计算物体到相机的距离
        float distance = Vector3.Distance(worldPos, mainCamera.transform.position);
        if (distance < 0.01f) return 100f;
        
        // 根据距离计算屏幕空间半径
        float fovRad = mainCamera.fieldOfView * Mathf.Deg2Rad;
        float screenH = 2f * distance * Mathf.Tan(fovRad * 0.5f);
        float screenRadiusPixels = (radius / screenH) * mainCamera.pixelHeight;
        
        return Mathf.Max(screenRadiusPixels, 5f); // 最小 5 像素
    }
    
    /// <summary>根据屏幕空间尺寸计算世界空间尺寸</summary>
    /// <param name="screenSizePixels">屏幕空间尺寸（像素）</param>
    /// <param name="distance">物体到相机的距离</param>
    /// <returns>世界空间尺寸</returns>
    private float CalculateWorldSizeFromScreenSize(float screenSizePixels, float distance)
    {
        if (distance < 0.01f) distance = 0.01f;
        
        float fovRad = mainCamera.fieldOfView * Mathf.Deg2Rad;
        float screenH = 2f * distance * Mathf.Tan(fovRad * 0.5f);
        float worldSize = (screenSizePixels / mainCamera.pixelHeight) * screenH;
        
        return worldSize;
    }
    
    /// <summary>获取目标屏幕尺寸（像素）</summary>
    private float GetTargetScreenSizePixels()
    {
        return mainCamera.pixelHeight * handleScreenHeightRatio;
    }

    /// <summary>计算点到线段的距离</summary>
    private float DistanceToLine(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
    {
        Vector3 lineDir = lineEnd - lineStart;
        float lineLength = lineDir.magnitude;
        if (lineLength < Mathf.Epsilon) return Vector2.Distance(point, lineStart);

        float t = Mathf.Clamp01(Vector3.Dot(point - lineStart, lineDir) / lineLength);
        Vector3 closest = lineStart + t * lineDir;
        return Vector2.Distance(point, closest);
    }

    /// <summary>设置所有手柄的激活状态（移除面）</summary>
    private void SetAllHandlesActive(bool active)
    {
        if (handle == null) return;

        // 平移组件（轴 + 方块）
        handle.xAxisLine.enabled = active && currentMode == 0;
        handle.yAxisLine.enabled = active && currentMode == 0;
        handle.zAxisLine.enabled = active && currentMode == 0;
        handle.xAxisCube.SetActive(active && currentMode == 0);
        handle.yAxisCube.SetActive(active && currentMode == 0);
        handle.zAxisCube.SetActive(active && currentMode == 0);

        // 旋转组件
        handle.xRotateRing.enabled = active && currentMode == 1;
        handle.yRotateRing.enabled = active && currentMode == 1;
        handle.zRotateRing.enabled = active && currentMode == 1;
        
        // 旋转球体手柄
        for (int i = 0; i < 2; i++)
        {
            handle.xRotateSpheres[i].SetActive(active && currentMode == 1);
            handle.yRotateSpheres[i].SetActive(active && currentMode == 1);
            handle.zRotateSpheres[i].SetActive(active && currentMode == 1);
        }
    }
    #endregion
}