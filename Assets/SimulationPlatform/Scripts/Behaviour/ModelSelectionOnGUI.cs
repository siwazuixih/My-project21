using UnityEngine;

public class ModelSelectionOnGUI : MonoBehaviour
{
    [Header("气泡样式设置")]
    public float bubbleWidth = 60;       // 气泡宽度
    public float bubbleHeight = 30;     // 气泡高度
    public int fontSize = 20;           // 序号字体大小
    public Color bubbleColor = Color.cyan; // 气泡颜色
    public Color textColor = Color.white;  // 文字颜色

    [Header("模型上方偏移（世界空间）")]
    public float modelTopOffset = 1.5f;

    // 当前选中的模型
    private GameObject _selectedModel;
    private Camera _mainCamera;

    void Start()
    {
        _mainCamera = Camera.main;
    }

    void Update()
    {
        // 鼠标点击选择模型
        if (Input.GetMouseButtonDown(0))
        {
            TrySelectModel();
        }
    }

    // 射线检测选中模型
    void TrySelectModel()
    {
        Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            // 你可以给可选中模型加个标签，比如 "Selectable"
            if (hit.collider.CompareTag("Selectable"))
            {
                _selectedModel = hit.collider.gameObject;
            }
        }
        else
        {
            // 点空处取消
            _selectedModel = null;
        }
    }

    // ====================== 核心：OnGUI 绘制 ======================
    void OnGUI()
    {
        if (_selectedModel == null) return;

        // 1. 计算模型头顶世界坐标
        Vector3 worldPos = _selectedModel.transform.position + Vector3.up * modelTopOffset;

        // 2. 转屏幕坐标（Unity屏幕坐标：左下是原点）
        Vector3 screenPos = _mainCamera.WorldToScreenPoint(worldPos);

        // 背面剔除：模型在相机背面不显示
        if (screenPos.z < 0) return;

        // 3. 转 GUI 坐标（GUI 原点是左上角）
        float guiX = screenPos.x - bubbleWidth / 2;
        float guiY = Screen.height - screenPos.y - bubbleHeight / 2;

        // 4. 绘制气泡背景（圆角矩形）
        GUI.color = bubbleColor;
        GUI.skin.box.alignment = TextAnchor.MiddleCenter;
        GUI.skin.box.fontSize = fontSize;
        GUI.skin.box.normal.textColor = textColor;
        GUI.Box(
            new Rect(guiX, guiY, bubbleWidth, bubbleHeight),
            GetModelIndex(_selectedModel).ToString()
        );
    }

    // 你可以自定义获取模型序号的方式：名称、组件、自定义ID都行
    int GetModelIndex(GameObject model)
    {
        // 示例1：从模型名称提取数字  Model_3 → 3
        // return int.Parse(model.name.Split('_')[1]);

        // 示例2：用组件存序号（最推荐）
        var indexComp = model.GetComponent<ModelIndex>();
        if (indexComp != null) return indexComp.index;

        return 0;
    }
}

// 存模型序号的组件（挂到模型上）
public class ModelIndex : MonoBehaviour
{
    public int index;
}