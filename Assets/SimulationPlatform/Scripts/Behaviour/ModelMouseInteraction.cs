using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// GLB模型鼠标交互控制器（旋转+缩放）
/// 挂载到包含RawImage的UI物体上
/// </summary>
[RequireComponent(typeof(RawImage))]
public class ModelMouseInteraction : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("模型对象")]
    public Transform modelTransform;  // 需要交互的模型根节点
    public Camera modelCamera;        // 模型查看摄像头

    [Header("交互设置")]
    [Tooltip("旋转速度")]
    public float rotateSpeed = 2.0f;
    [Tooltip("缩放速度")]
    public float scaleSpeed = 0.1f;
    [Tooltip("最小缩放比例")]
    public float minScale = 0.2f;
    [Tooltip("最大缩放比例")]
    public float maxScale = 5.0f;
    [Tooltip("是否限制垂直旋转角度")]
    public bool limitVerticalRotation = true;
    [Tooltip("垂直旋转最大角度（上下）")]
    public float maxVerticalAngle = 80f;

    [Header("平滑设置")]
    [Tooltip("旋转阻尼（0-1，值越大越平滑）")]
    [Range(0.01f, 0.99f)]
    public float rotateDamping = 0.8f;
    [Tooltip("缩放阻尼（0-1，值越大越平滑）")]
    [Range(0.01f, 0.99f)]
    public float scaleDamping = 0.7f;
    
    [Tooltip("摄像头移动速度")]
    public float cameraMoveSpeed = 0.5f;
    [Tooltip("摄像头最小距离")]
    public float minCameraDistance = 1.0f;
    [Tooltip("摄像头最大距离")]
    public float maxCameraDistance = 20.0f;

    private RawImage modelDisplayImage;  // 显示模型的RawImage
    private bool isDragging = false;     // 是否正在拖拽
    private Vector2 lastMousePos;        // 上一帧鼠标位置
    private Vector3 targetRotation;      // 目标旋转角度
    private float targetCameraDistance;  // 目标摄像头距离

    private void Awake()
    {
        Debug.Log($"ModelMouseInteraction 初始化");
        // 获取RawImage组件
        modelDisplayImage = GetComponent<RawImage>();

        // 初始化目标旋转
        if (modelTransform != null)
        {
            targetRotation = modelTransform.localEulerAngles;
        }
        
        // 初始化目标摄像头距离
        if (modelCamera != null)
        {
            if (modelTransform != null)
            {
                // 计算当前摄像头到模型的距离
                targetCameraDistance = Vector3.Distance(modelCamera.transform.position, modelTransform.position);
            }
            else
            {
                targetCameraDistance = 5.0f; // 默认距离
            }
        }
    }

    private void Update()
    {
        if (UIUtil.IsPointerOverUI()) return;
        if (modelTransform == null) return;

        // 处理鼠标旋转
        HandleMouseRotation();

        // 处理鼠标滚轮缩放
        HandleMouseScroll();

        // 应用平滑的旋转和缩放
        ApplySmoothTransform();
    }

    /// <summary>
    /// 处理鼠标旋转逻辑
    /// </summary>
    private void HandleMouseRotation()
    {
        if (isDragging && Input.GetMouseButton(0))
        {
            // 获取鼠标移动增量
            Vector2 currentMousePos = Input.mousePosition;
            Vector2 delta = currentMousePos - lastMousePos;
            lastMousePos = currentMousePos;

            // 计算旋转增量（水平绕Y轴，垂直绕X轴）
            targetRotation.y += delta.x * rotateSpeed;
            targetRotation.x -= delta.y * rotateSpeed;

            // 限制垂直旋转角度
            if (limitVerticalRotation)
            {
                targetRotation.x = Mathf.Clamp(targetRotation.x, -maxVerticalAngle, maxVerticalAngle);
            }
        }
    }

    /// <summary>
    /// 处理鼠标滚轮缩放（调整摄像头远近）
    /// </summary>
    private void HandleMouseScroll()
    {
        // 检测鼠标滚轮输入
        float scrollDelta = Input.GetAxis("Mouse ScrollWheel");
        if (scrollDelta != 0 && IsPointerOverModelImage() && modelCamera != null)
        {
            Debug.Log($"鼠标滚轮输入: {scrollDelta}");
            // 计算目标摄像头距离
            targetCameraDistance -= scrollDelta * cameraMoveSpeed;
            // 限制摄像头距离范围
            targetCameraDistance = Mathf.Clamp(targetCameraDistance, minCameraDistance, maxCameraDistance);
            Debug.Log($"targetCameraDistance: {targetCameraDistance}, minCameraDistance: {minCameraDistance}, maxCameraDistance: {maxCameraDistance}");

        }
    }

    /// <summary>
    /// 应用平滑的旋转和摄像头移动
    /// </summary>
    private void ApplySmoothTransform()
    {
        // 平滑旋转（使用阻尼）
        Vector3 currentRotation = modelTransform.localEulerAngles;
        // 处理欧拉角360度循环问题
        currentRotation = new Vector3(
            Mathf.DeltaAngle(currentRotation.x, targetRotation.x),
            Mathf.DeltaAngle(currentRotation.y, targetRotation.y),
            0
        );
        modelTransform.localEulerAngles += currentRotation * (1 - rotateDamping);

        // 平滑调整摄像头距离
        if (modelCamera != null && modelTransform != null)
        {
            // Debug.Log($"目标摄像头距离: {targetCameraDistance}");
            // 计算当前摄像头到模型的距离
            float currentDistance = Vector3.Distance(modelCamera.transform.position, modelTransform.position);
            // 计算距离差并应用阻尼
            float distanceDelta = targetCameraDistance - currentDistance;
            float newDistance = currentDistance + distanceDelta * (1 - scaleDamping);
            
            // 计算摄像头方向
            Vector3 direction = (modelCamera.transform.position - modelTransform.position).normalized;
            // 设置新的摄像头位置
            modelCamera.transform.position = modelTransform.position + direction * newDistance;
        }
    }

    /// <summary>
    /// 检测鼠标是否在模型显示区域上
    /// </summary>
    private bool IsPointerOverModelImage()
    {
        if (EventSystem.current == null || modelDisplayImage == null) return false;

        PointerEventData pointerEventData = new PointerEventData(EventSystem.current);
        pointerEventData.position = Input.mousePosition;

        // 检测鼠标是否在RawImage上
        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerEventData, results);

        foreach (var result in results)
        {
            if (result.gameObject == gameObject)
            {
                return true;
            }
        }
        return false;
    }

    #region 接口实现 - 鼠标按下/抬起
    public void OnPointerDown(PointerEventData eventData)
    {
        // 只响应左键
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            isDragging = true;
            lastMousePos = Input.mousePosition;
            // 捕获鼠标，防止移出UI区域后停止响应
            Cursor.lockState = CursorLockMode.None;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            isDragging = false;
        }
    }
    #endregion

    #region 辅助方法
    /// <summary>
    /// 重置模型位置和旋转，以及摄像头距离
    /// </summary>
    public void ResetModelTransform()
    {
        if (modelTransform != null)
        {
            targetRotation = Vector3.zero;
            modelTransform.localEulerAngles = Vector3.zero;
        }
        
        // 重置摄像头距离
        if (modelCamera != null)
        {
            // targetCameraDistance = 5.0f; // 默认距离
            targetCameraDistance = Vector3.Distance(modelCamera.transform.position, modelTransform.position);

        }
    }

    /// <summary>
    /// 设置摄像头距离（直接设置，无平滑）
    /// </summary>
    /// <param name="distance">摄像头距离</param>
    public void SetCameraDistance(float distance)
    {
        distance = Mathf.Clamp(distance, minCameraDistance, maxCameraDistance);
        targetCameraDistance = distance;
        
        if (modelCamera != null && modelTransform != null)
        {
            Vector3 direction = (modelCamera.transform.position - modelTransform.position).normalized;
            modelCamera.transform.position = modelTransform.position + direction * distance;
        }
    }
    #endregion
}