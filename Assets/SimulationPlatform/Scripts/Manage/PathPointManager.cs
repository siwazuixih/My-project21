using System.Collections.Generic;
using UnityEngine;

public class PathPointManager : MonoBehaviour
{
    public static PathPointManager Instance;

    [Header("气泡样式设置")]
    public float bubbleWidth = 60;
    public float bubbleHeight = 30;
    public int fontSize = 20;
    public Color bubbleColor = Color.cyan;
    public Color textColor = Color.white;

    [Header("模型上方偏移（世界空间）")]
    public float modelTopOffset = 1.5f;

    [Header("显示过滤设置")]
    public float maxVisibleDistance = 50f;
    public bool enableDistanceFilter = true;

    private List<GameObject> _pointObjList = new List<GameObject>();
    private List<Vector3> _worldPosList = new List<Vector3>();
    private Camera _cachedCamera;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }
    
    void Start()
    {
        // 优先使用 Camera.main
        _cachedCamera = Camera.main;
        
        Debug.Log($"[PathPointManager] Camera.main: {Camera.main?.name}");
        
        // 查找所有启用的相机
        Camera[] cameras = Camera.allCameras;
        Debug.Log($"[PathPointManager] 所有相机数量: {cameras.Length}");
        foreach (Camera cam in cameras)
        {
            Debug.Log($"[PathPointManager] 找到相机: {cam.name}, depth: {cam.depth}, tag: {cam.tag}, enabled: {cam.enabled}, activeInHierarchy: {cam.gameObject.activeInHierarchy}");
            // 如果 Camera.main 为空，选择 depth 最高的相机
            if (_cachedCamera == null && cam.enabled && cam.gameObject.activeInHierarchy)
            {
                _cachedCamera = cam;
            }
        }
        
        Debug.Log($"[PathPointManager] 最终使用相机: {_cachedCamera?.name}, 位置: {_cachedCamera?.transform.position}, forward: {_cachedCamera?.transform.forward}");
    }

    public void AddPoint(GameObject targetObj)
    {
        Vector3 worldPos = targetObj.transform.position + Vector3.up * modelTopOffset;
        AddPoint(targetObj, worldPos);
    }

    public void AddPoint(GameObject targetObj, Vector3 worldPos)
    {
        if (targetObj == null || _pointObjList.Contains(targetObj)) return;

        CleanupNullObjects();

        _pointObjList.Add(targetObj);
        _worldPosList.Add(worldPos);

        Debug.Log($"[PathPointManager] 添加路径点: {targetObj.name}, 世界坐标: {worldPos}, 当前数量: {_pointObjList.Count}");
    }
    
    private void CleanupNullObjects()
    {
        for (int i = _pointObjList.Count - 1; i >= 0; i--)
        {
            if (_pointObjList[i] == null)
            {
                _pointObjList.RemoveAt(i);
                if (i < _worldPosList.Count)
                {
                    _worldPosList.RemoveAt(i);
                }
            }
        }
    }

    public void RemovePoint(int index)
    {
        if (index < 0 || index >= _pointObjList.Count) return;
        _pointObjList.RemoveAt(index);
        if (index < _worldPosList.Count)
        {
            _worldPosList.RemoveAt(index);
        }
    }

    public void RemovePoint(GameObject targetObj)
    {
        int index = _pointObjList.IndexOf(targetObj);
        if (index >= 0)
        {
            RemovePoint(index);
        }
    }

    public void ClearAll()
    {
        _pointObjList.Clear();
        _worldPosList.Clear();
    }

    public List<GameObject> GetAllPoints() => _pointObjList;

    void OnGUI()
    {
        if (_pointObjList.Count == 0) return;

        Camera currentCamera = CameraTool.GetActiveCamera();
        if (currentCamera == null) return;

        for (int i = 0; i < _pointObjList.Count; i++)
        {
            GameObject targetObj = _pointObjList[i];
            if (targetObj == null) continue;

            Vector3 worldPos = i < _worldPosList.Count ? _worldPosList[i] : targetObj.transform.position + Vector3.up * modelTopOffset;

            if (enableDistanceFilter)
            {
                float distance = Vector3.Distance(currentCamera.transform.position, worldPos);
                if (distance > maxVisibleDistance) continue;
            }

            Vector3 screenPos = currentCamera.WorldToScreenPoint(worldPos);

            // 背面剔除
            if (screenPos.z < 0) continue;

            // 屏幕范围检查
            if (screenPos.x < 0 || screenPos.x > Screen.width || screenPos.y < 0 || screenPos.y > Screen.height)
            {
                //Debug.Log($"[PathPoint {i+1}] 超出屏幕: world={worldPos}, screen={screenPos}, Screen={Screen.width}x{Screen.height}, 相机={currentCamera.name}");
                continue;
            }

            float guiX = screenPos.x - bubbleWidth / 2;
            float guiY = Screen.height - screenPos.y - bubbleHeight / 2;

            Debug.Log($"[PathPoint {i+1}] world={worldPos}, screen={screenPos}, GUI=({guiX:F1},{guiY:F1}), 相机={currentCamera.name}");

            // 设置样式
            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.alignment = TextAnchor.MiddleCenter;
            boxStyle.fontSize = fontSize;
            boxStyle.normal.textColor = textColor;

            // 创建背景纹理
            Texture2D bgTexture = new Texture2D(1, 1);
            bgTexture.SetPixel(0, 0, bubbleColor);
            bgTexture.Apply();
            boxStyle.normal.background = bgTexture;

            // 绘制
            GUI.Box(new Rect(guiX, guiY, bubbleWidth, bubbleHeight), (i + 1).ToString(), boxStyle);

            // 释放纹理（避免内存泄漏）
            UnityEngine.Object.Destroy(bgTexture);
        }
    }

    private Texture2D CreateColorTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
}