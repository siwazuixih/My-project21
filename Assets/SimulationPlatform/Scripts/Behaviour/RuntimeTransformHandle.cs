using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class RuntimeTransformHandle : MonoBehaviour
{
    #region 单例与核心状态
    public static RuntimeTransformHandle Instance;
    private Transform selectedTransform;
    private bool isHandleActive;
    private int currentMode = 0; // 0=平移 1=旋转
    private int dragType = -1;   // -1=无 0=X轴 1=Y轴 2=Z轴 3=XY面 4=XZ面 5=YZ面
    #endregion

    #region 样式配置（匹配编辑器默认样式）
    [Header("编辑器样式配置")]
    public float axisLength = 1.5f;
    public float axisThickness = 0.018f;
    public float handleSize = 0.06f;
    public float planeHandleSize = 0.04f;
    public float rotateRingRadius = 1.2f;
    public Color xAxisColor = new Color(1, 0.2f, 0.2f);
    public Color yAxisColor = new Color(0.2f, 1, 0.2f);
    public Color zAxisColor = new Color(0.2f, 0.2f, 1);
    public Color xyPlaneColor = new Color(1, 1, 0.2f, 0.5f);
    public Color xzPlaneColor = new Color(1, 0.2f, 1, 0.5f);
    public Color yzPlaneColor = new Color(0.2f, 1, 1, 0.5f);
    #endregion

    #region 手柄对象管理
    private class TransformHandle
    {
        // 平移轴
        public LineRenderer xAxisLine;
        public LineRenderer yAxisLine;
        public LineRenderer zAxisLine;
        public GameObject xAxisHandle;
        public GameObject yAxisHandle;
        public GameObject zAxisHandle;

        // 平移面
        public GameObject xyPlaneHandle;
        public GameObject xzPlaneHandle;
        public GameObject yzPlaneHandle;

        // 旋转环
        public LineRenderer xRotateRing;
        public LineRenderer yRotateRing;
        public LineRenderer zRotateRing;
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
            DontDestroyOnLoad(gameObject);
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
        // X轴
        handle.xAxisLine = CreateAxisLine("XAxis", xAxisColor, axisThickness);
        handle.xAxisHandle = CreateCubeHandle("XAxisHandle", xAxisColor, handleSize);

        // Y轴
        handle.yAxisLine = CreateAxisLine("YAxis", yAxisColor, axisThickness);
        handle.yAxisHandle = CreateCubeHandle("YAxisHandle", yAxisColor, handleSize);

        // Z轴
        handle.zAxisLine = CreateAxisLine("ZAxis", zAxisColor, axisThickness);
        handle.zAxisHandle = CreateCubeHandle("ZAxisHandle", zAxisColor, handleSize);

        // ========== 创建平移面 ==========
        //handle.xyPlaneHandle = CreatePlaneHandle("XYPlaneHandle", xyPlaneColor, planeHandleSize);
        //handle.xzPlaneHandle = CreatePlaneHandle("XZPlaneHandle", xzPlaneColor, planeHandleSize);
        //handle.yzPlaneHandle = CreatePlaneHandle("YZPlaneHandle", yzPlaneColor, planeHandleSize);

        // ========== 创建旋转环 ==========
        handle.xRotateRing = CreateRotateRing("XRotateRing", xAxisColor, axisThickness, 40);
        handle.yRotateRing = CreateRotateRing("YRotateRing", yAxisColor, axisThickness, 40);
        handle.zRotateRing = CreateRotateRing("ZRotateRing", zAxisColor, axisThickness, 40);
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
        return lr;
    }

    /// <summary>创建立方体手柄（轴端点）</summary>
    private GameObject CreateCubeHandle(string name, Color color, float size)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(obj.GetComponent<Collider>());
        obj.name = name;
        obj.transform.parent = transform;
        obj.hideFlags = HideFlags.HideInHierarchy;
        obj.GetComponent<Renderer>().material = new Material(Shader.Find("Unlit/Color")) { color = color };
        obj.transform.localScale = Vector3.one * size;
        return obj;
    }

    /// <summary>创建平面手柄（面拖动）</summary>
    private GameObject CreatePlaneHandle(string name, Color color, float size)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(obj.GetComponent<Collider>());
        obj.name = name;
        obj.transform.parent = transform;
        obj.hideFlags = HideFlags.HideInHierarchy;
        obj.GetComponent<Renderer>().material = new Material(Shader.Find("Unlit/Color")) { color = color };
        obj.transform.localScale = Vector3.one * size;
        return obj;
    }

    /// <summary>创建旋转环（LineRenderer）</summary>
    private LineRenderer CreateRotateRing(string name, Color color, float width, int segments)
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
        lr.positionCount = segments;
        lr.loop = true;
        lr.useWorldSpace = true;
        return lr;
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

        // 平移组件显隐
        handle.xAxisLine.enabled = isMoveMode;
        handle.yAxisLine.enabled = isMoveMode;
        handle.zAxisLine.enabled = isMoveMode;
        handle.xAxisHandle.SetActive(isMoveMode);
        handle.yAxisHandle.SetActive(isMoveMode);
        handle.zAxisHandle.SetActive(isMoveMode);
        //handle.xyPlaneHandle.SetActive(isMoveMode);
        //handle.xzPlaneHandle.SetActive(isMoveMode);
        //handle.yzPlaneHandle.SetActive(isMoveMode);

        // 旋转组件显隐
        handle.xRotateRing.enabled = !isMoveMode;
        handle.yRotateRing.enabled = !isMoveMode;
        handle.zRotateRing.enabled = !isMoveMode;
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
            // ========== 平移模式：更新坐标轴和平面 ==========
            // X轴
            handle.xAxisLine.SetPosition(0, origin);
            handle.xAxisLine.SetPosition(1, origin + right * axisLength);
            handle.xAxisHandle.transform.position = origin + right * axisLength;
            handle.xAxisHandle.transform.rotation = Quaternion.LookRotation(right);

            // Y轴
            handle.yAxisLine.SetPosition(0, origin);
            handle.yAxisLine.SetPosition(1, origin + up * axisLength);
            handle.yAxisHandle.transform.position = origin + up * axisLength;
            handle.yAxisHandle.transform.rotation = Quaternion.LookRotation(up);

            // Z轴
            handle.zAxisLine.SetPosition(0, origin);
            handle.zAxisLine.SetPosition(1, origin + forward * axisLength);
            handle.zAxisHandle.transform.position = origin + forward * axisLength;
            handle.zAxisHandle.transform.rotation = Quaternion.LookRotation(forward);

            // XY平面（在X/Y轴中间）
            //handle.xyPlaneHandle.transform.position = origin + (right + up) * axisLength * 0.5f;
            //handle.xyPlaneHandle.transform.rotation = Quaternion.LookRotation(forward);

            // XZ平面（在X/Z轴中间）
            //handle.xzPlaneHandle.transform.position = origin + (right + forward) * axisLength * 0.5f;
            //handle.xzPlaneHandle.transform.rotation = Quaternion.LookRotation(up);

            // YZ平面（在Y/Z轴中间）
            //handle.yzPlaneHandle.transform.position = origin + (up + forward) * axisLength * 0.5f;
            //handle.yzPlaneHandle.transform.rotation = Quaternion.LookRotation(right);
        }
        else
        {
            // ========== 旋转模式：更新旋转环 ==========
            UpdateRotateRing(handle.xRotateRing, origin, right, up, forward, rotateRingRadius);
            UpdateRotateRing(handle.yRotateRing, origin, up, forward, right, rotateRingRadius);
            UpdateRotateRing(handle.zRotateRing, origin, forward, right, up, rotateRingRadius);
        }
    }

    /// <summary>更新旋转环的形状</summary>
    private void UpdateRotateRing(LineRenderer ring, Vector3 origin, Vector3 axis, Vector3 cross1, Vector3 cross2, float radius)
    {
        for (int i = 0; i < ring.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2 / (ring.positionCount - 1);
            Vector3 point = origin + cross1 * Mathf.Cos(angle) * radius + cross2 * Mathf.Sin(angle) * radius;
            ring.SetPosition(i, point);
        }
    }

    /// <summary>处理鼠标拖动（平移/旋转）</summary>
    private void HandleMouseDrag()
    {
        // 开始拖动：检测点击的手柄类型
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

    /// <summary>检测点击的手柄类型（轴/面/旋转环）</summary>
    private int DetectDragType()
    {
        float threshold = 15f; // 像素检测阈值

        if (currentMode == 0)
        {
            // 平移模式：检测轴/面
            if (IsMouseOverLine(handle.xAxisLine, threshold)) return 0;
            if (IsMouseOverLine(handle.yAxisLine, threshold)) return 1;
            if (IsMouseOverLine(handle.zAxisLine, threshold)) return 2;
            //if (IsMouseOverObject(handle.xyPlaneHandle, threshold)) return 3;
            //if (IsMouseOverObject(handle.xzPlaneHandle, threshold)) return 4;
            //if (IsMouseOverObject(handle.yzPlaneHandle, threshold)) return 5;
        }
        else
        {
            // 旋转模式：检测旋转环
            if (IsMouseOverLine(handle.xRotateRing, threshold)) return 0;
            if (IsMouseOverLine(handle.yRotateRing, threshold)) return 1;
            if (IsMouseOverLine(handle.zRotateRing, threshold)) return 2;
        }

        return -1;
    }

    /// <summary>初始化拖动平面（用于平移计算）</summary>
    private void InitDragPlane()
    {
        Vector3 origin = selectedTransform.position;
        switch (dragType)
        {
            case 0: dragPlane = new Plane(selectedTransform.up, origin); break; // X轴：垂直Y轴
            case 1: dragPlane = new Plane(selectedTransform.forward, origin); break; // Y轴：垂直Z轴
            case 2: dragPlane = new Plane(selectedTransform.right, origin); break; // Z轴：垂直X轴
            case 3: dragPlane = new Plane(selectedTransform.forward, origin); break; // XY面：垂直Z轴
            case 4: dragPlane = new Plane(selectedTransform.up, origin); break; // XZ面：垂直Y轴
            case 5: dragPlane = new Plane(selectedTransform.right, origin); break; // YZ面：垂直X轴
        }
    }

    /// <summary>执行平移操作</summary>
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

                // 根据拖动类型限制平移方向
                switch (dragType)
                {
                    case 0: delta = Vector3.Project(delta, selectedTransform.right); break; // X轴
                    case 1: delta = Vector3.Project(delta, selectedTransform.up); break; // Y轴
                    case 2: delta = Vector3.Project(delta, selectedTransform.forward); break; // Z轴
                    case 3: delta = new Vector3(delta.x, delta.y, 0); break; // XY面
                    case 4: delta = new Vector3(delta.x, 0, delta.z); break; // XZ面
                    case 5: delta = new Vector3(0, delta.y, delta.z); break; // YZ面
                }

                selectedTransform.position += delta;
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

    /// <summary>检测鼠标是否在GameObject（平面手柄）上</summary>
    private bool IsMouseOverObject(GameObject obj, float threshold)
    {
        Vector3 center = mainCamera.WorldToScreenPoint(obj.transform.position);
        center.z = 0;
        return Vector2.Distance(Input.mousePosition, center) < threshold;
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

    /// <summary>设置所有手柄的激活状态</summary>
    private void SetAllHandlesActive(bool active)
    {
        if (handle == null) return;

        handle.xAxisLine.enabled = active && currentMode == 0;
        handle.yAxisLine.enabled = active && currentMode == 0;
        handle.zAxisLine.enabled = active && currentMode == 0;
        handle.xAxisHandle.SetActive(active && currentMode == 0);
        handle.yAxisHandle.SetActive(active && currentMode == 0);
        handle.zAxisHandle.SetActive(active && currentMode == 0);
        //handle.xyPlaneHandle.SetActive(active && currentMode == 0);
        //handle.xzPlaneHandle.SetActive(active && currentMode == 0);
        //handle.yzPlaneHandle.SetActive(active && currentMode == 0);

        handle.xRotateRing.enabled = active && currentMode == 1;
        handle.yRotateRing.enabled = active && currentMode == 1;
        handle.zRotateRing.enabled = active && currentMode == 1;
    }
    #endregion
}