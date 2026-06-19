using System.Collections.Generic;
using UnityEngine;

public class PathPointManager : MonoBehaviour
{
    public static PathPointManager Instance;

    [Header("模型上方偏移（世界空间）")]
    public float modelTopOffset = 0f;

    [Header("显示过滤设置")]
    public float maxVisibleDistance = 50f;
    public bool enableDistanceFilter = true;

    public GameObject tipPrefab;
    public Transform worldCanvasTrans;

    private List<GameObject> _pointObjList = new List<GameObject>();
    private List<Vector3> _worldPosList = new List<Vector3>();
    private List<GameObject> _tipObjList = new List<GameObject>();
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

    public int GetIndex(GameObject targetObj)
    {
        return _pointObjList.IndexOf(targetObj);
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

        int pointIndex = _pointObjList.Count - 1;

        // 创建 tipPrefab
        if (tipPrefab != null)
        {
            GameObject tipObj = Instantiate(tipPrefab, worldPos, Quaternion.identity);
            tipObj.transform.SetParent(worldCanvasTrans ?? transform);
            tipObj.transform.localScale = Vector3.one;
            
            Canvas canvas = tipObj.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.sortingOrder = 100;
            }
            
            _tipObjList.Add(tipObj);

            // 设置 PathPointMarker 的 index
            PathPointMarker marker = tipObj.GetComponent<PathPointMarker>();
            if (marker != null)
            {
                marker.Init(targetObj.transform, pointIndex+1);
            }
        }

        // 设置 TransformJointDataComponent 的索引
        TransformJointDataComponent tjdc = targetObj.GetComponent<TransformJointDataComponent>();
        if (tjdc != null)
        {
            tjdc.index = pointIndex;
        }

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
        Debug.Log($"[PathPointManager] 移除路径点: {index}, {_pointObjList[index]?.name}");
        _pointObjList.RemoveAt(index);
        if (index < _worldPosList.Count)
        {
            _worldPosList.RemoveAt(index);
        }

        // 销毁对应的 tip 对象
        if (index < _tipObjList.Count)
        {
            Destroy(_tipObjList[index]);
            _tipObjList.RemoveAt(index);
        }

        // 更新剩余物体的 TransformJointDataComponent 索引
        for (int i = index; i < _pointObjList.Count; i++)
        {
            TransformJointDataComponent tjdc = _pointObjList[i].GetComponent<TransformJointDataComponent>();
            if (tjdc != null)
            {
                tjdc.index = i;
            }
        }

        // 更新剩余 tip 对象的 PathPointMarker index
        for (int i = index; i < _tipObjList.Count; i++)
        {
            PathPointMarker marker = _tipObjList[i].GetComponent<PathPointMarker>();
            if (marker != null)
            {
                marker.SetIndex(i+1);
            }
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
        
        // 销毁所有 tip 对象
        foreach (GameObject tipObj in _tipObjList)
        {
            Destroy(tipObj);
        }
        _tipObjList.Clear();
    }

}