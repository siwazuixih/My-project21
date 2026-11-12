using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class RuntimeConsole : MonoBehaviour
{
    private const string ToggleButtonName = "RuntimeConsoleToggleButton";
    private const string ColliderToggleButtonName = "ColliderVisualizationToggleButton";
    private const string RobotFeedbackFilterName = "RuntimeConsoleRobotFeedbackFilter";

    [Header("把刚刚建的 ConsoleLogText 拖到这里")]
    public TextMeshProUGUI logTextDisplay;

    [Header("最多显示多少行日志？(防止内存爆炸)")]
    public int maxLines = 50;

    [Header("启动时是否默认收起")]
    public bool startCollapsed = true;

    [Header("只过滤运行时日志显示，不影响通信、文件日志或反馈数据")]
    [SerializeField] private bool hideRobotFeedbackLogs;

    private readonly RuntimeConsoleLogBuffer logBuffer = new RuntimeConsoleLogBuffer();
    private Image backgroundImage;
    private Mask panelMask;
    private GameObject titleObject;
    private Button toggleButton;
    private TextMeshProUGUI toggleButtonText;
    private Button colliderToggleButton;
    private Image colliderToggleButtonImage;
    private TextMeshProUGUI colliderToggleButtonText;
    private Toggle robotFeedbackFilterToggle;
    private RectTransform robotFeedbackFilterRect;
    private bool isCollapsed;
    private bool colliderVisualizationVisible = true;
    private float nextHiddenColliderRefreshTime;

    void Start()
    {
        CachePanelParts();
        CreateToggleButton();
        CreateColliderToggleButton();
        CreateRobotFeedbackFilterToggle();
        SetCollapsed(startCollapsed);
        UpdateColliderToggleButtonState();
        RefreshLogDisplay();
    }

    private void Update()
    {
        // 用户隐藏后若又重新切割，新生成的Renderer默认会启用。低频补扫一次，
        // 保证按钮状态持续生效；这里只关Renderer，不会关闭MeshCollider或MjGeom。
        if (!colliderVisualizationVisible &&
            Time.unscaledTime >= nextHiddenColliderRefreshTime)
        {
            nextHiddenColliderRefreshTime = Time.unscaledTime + 0.5f;
            ApplyColliderVisualization(false, false);
        }
    }

    // 当这个脚本被激活时，向 Unity 申请“监听所有日志”
    void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    // 当脚本被关闭或销毁时，取消监听（极其重要的好习惯！）
    void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void OnDestroy()
    {
        if (robotFeedbackFilterToggle != null)
        {
            robotFeedbackFilterToggle.onValueChanged.RemoveListener(SetRobotFeedbackLogFilter);
            Destroy(robotFeedbackFilterToggle.gameObject);
        }
    }

    // 这就是我们的“窃听器”核心处理厂
    void HandleLog(string logString, string stackTrace, LogType type)
    {
        // 1. 根据日志类型上色
        string colorHex = "#FFFFFF"; // 默认白色
        if (type == LogType.Error || type == LogType.Exception) 
            colorHex = "#FF4444"; // 错误标红
        else if (type == LogType.Warning) 
            colorHex = "#FFFF00"; // 警告标黄
        
        // 2. 拼装这行文字（加上时间戳和颜色标签）
        string timeStr = System.DateTime.Now.ToString("HH:mm:ss");
        string newLine = $"<color={colorHex}>[{timeStr}] {logString}</color>";

        bool feedback = RuntimeConsoleLogBuffer.IsRoutineRobotFeedback(logString, type == LogType.Log);
        logBuffer.Append(newLine, feedback, maxLines);
        // Hidden periodic feedback must not keep rebuilding the UI text or evict other logs.
        if (!hideRobotFeedbackLogs || !feedback) RefreshLogDisplay();
    }

    public void SetRobotFeedbackLogFilter(bool hidden)
    {
        hideRobotFeedbackLogs = hidden;
        if (robotFeedbackFilterToggle != null)
            robotFeedbackFilterToggle.SetIsOnWithoutNotify(hidden);
        RefreshLogDisplay();
    }

    private void RefreshLogDisplay()
    {
        if (logTextDisplay != null)
            logTextDisplay.text = logBuffer.Render(hideRobotFeedbackLogs, maxLines);
    }

    /// <summary>
    /// 展开或收起运行时日志窗口。日志监听始终保持工作。
    /// </summary>
    public void ToggleCollapsed()
    {
        SetCollapsed(!isCollapsed);
    }

    private void SetCollapsed(bool collapsed)
    {
        isCollapsed = collapsed;

        if (backgroundImage != null)
            backgroundImage.enabled = !collapsed;
        if (panelMask != null)
            panelMask.enabled = !collapsed;
        if (logTextDisplay != null)
            logTextDisplay.gameObject.SetActive(!collapsed);
        if (titleObject != null)
            titleObject.SetActive(!collapsed);
        if (robotFeedbackFilterToggle != null)
            robotFeedbackFilterToggle.gameObject.SetActive(!collapsed);
        if (toggleButtonText != null)
            toggleButtonText.text = collapsed ? "展开日志" : "收起日志";
    }

    private void CreateRobotFeedbackFilterToggle()
    {
        if (transform.parent == null || !(transform is RectTransform)) return;
        Transform existing = transform.parent.Find(RobotFeedbackFilterName);
        GameObject filterObject;
        if (existing != null) filterObject = existing.gameObject;
        else
        {
            filterObject = new GameObject(RobotFeedbackFilterName,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Toggle));
            filterObject.layer = gameObject.layer;
            filterObject.transform.SetParent(transform.parent, false);
            var background = filterObject.GetComponent<Image>();
            background.color = new Color(0.05f, 0.12f, 0.20f, 0.96f);

            var box = new GameObject("Checkbox", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            box.layer = gameObject.layer;
            box.transform.SetParent(filterObject.transform, false);
            var boxRect = box.GetComponent<RectTransform>();
            boxRect.anchorMin = boxRect.anchorMax = new Vector2(0f, 0.5f);
            boxRect.pivot = new Vector2(0f, 0.5f);
            boxRect.anchoredPosition = new Vector2(8f, 0f);
            boxRect.sizeDelta = new Vector2(22f, 22f);
            var boxImage = box.GetComponent<Image>();
            boxImage.color = new Color(0.75f, 0.82f, 0.88f, 1f);
            boxImage.raycastTarget = false;

            var check = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            check.layer = gameObject.layer;
            check.transform.SetParent(box.transform, false);
            var checkRect = check.GetComponent<RectTransform>();
            checkRect.anchorMin = checkRect.anchorMax = new Vector2(0.5f, 0.5f);
            checkRect.sizeDelta = new Vector2(14f, 14f);
            var checkImage = check.GetComponent<Image>();
            checkImage.color = new Color(0.02f, 0.65f, 0.35f, 1f);
            checkImage.raycastTarget = false;

            var label = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            label.layer = gameObject.layer;
            label.transform.SetParent(filterObject.transform, false);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(38f, 0f);
            labelRect.offsetMax = new Vector2(-6f, 0f);
            var text = label.GetComponent<TextMeshProUGUI>();
            text.text = "过滤机械臂反馈（仅日志）";
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontSize = 17f;
            text.color = Color.white;
            text.raycastTarget = false;
            if (logTextDisplay != null && logTextDisplay.font != null) text.font = logTextDisplay.font;

            var toggle = filterObject.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = checkImage;
            toggle.toggleTransition = Toggle.ToggleTransition.None;
        }

        robotFeedbackFilterRect = filterObject.GetComponent<RectTransform>();
        robotFeedbackFilterRect.pivot = Vector2.zero;
        robotFeedbackFilterRect.sizeDelta = new Vector2(260f, 30f);
        robotFeedbackFilterToggle = filterObject.GetComponent<Toggle>();
        if (robotFeedbackFilterToggle != null)
        {
            robotFeedbackFilterToggle.SetIsOnWithoutNotify(hideRobotFeedbackLogs);
            robotFeedbackFilterToggle.onValueChanged.RemoveListener(SetRobotFeedbackLogFilter);
            robotFeedbackFilterToggle.onValueChanged.AddListener(SetRobotFeedbackLogFilter);
        }
        PositionRobotFeedbackFilter();
        filterObject.transform.SetAsLastSibling();
    }

    private void OnRectTransformDimensionsChange()
    {
        PositionRobotFeedbackFilter();
    }

    private void PositionRobotFeedbackFilter()
    {
        var panel = transform as RectTransform;
        if (panel == null || robotFeedbackFilterRect == null) return;
        // Sibling above the panel: avoid the log Mask clipping the checkbox.
        robotFeedbackFilterRect.position = panel.TransformPoint(
            new Vector3(panel.rect.xMin + 8f, panel.rect.yMax + 4f, 0f));
        robotFeedbackFilterRect.localRotation = panel.localRotation;
        robotFeedbackFilterRect.localScale = panel.localScale;
    }

    private void CachePanelParts()
    {
        backgroundImage = GetComponent<Image>();
        panelMask = GetComponent<Mask>();

        Transform parent = transform.parent;
        if (parent != null)
        {
            Transform title = parent.Find("Console Log");
            if (title != null)
                titleObject = title.gameObject;
        }
    }

    private void CreateToggleButton()
    {
        Transform parent = transform.parent;
        if (parent == null) return;

        GameObject sceneButtonObject = GameObject.Find(ToggleButtonName);
        Transform existing = sceneButtonObject != null
            ? sceneButtonObject.transform
            : parent.Find(ToggleButtonName);
        if (existing != null)
        {
            toggleButton = existing.GetComponent<Button>();
            toggleButtonText = existing.GetComponentInChildren<TextMeshProUGUI>(true);
        }
        else
        {
            GameObject buttonObject = new GameObject(
                ToggleButtonName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.layer = gameObject.layer;
            buttonObject.transform.SetParent(parent, false);

            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            RectTransform panelRect = transform as RectTransform;
            buttonRect.anchorMin = panelRect.anchorMin;
            buttonRect.anchorMax = panelRect.anchorMax;
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = new Vector2(110f, 34f);
            buttonRect.anchoredPosition = panelRect.anchoredPosition + new Vector2(
                panelRect.rect.width * 0.5f - 55f,
                panelRect.rect.height * 0.5f + 22f);

            Image buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = new Color(0.05f, 0.35f, 0.65f, 0.92f);

            toggleButton = buttonObject.GetComponent<Button>();
            toggleButton.targetGraphic = buttonImage;

            GameObject textObject = new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.layer = gameObject.layer;
            textObject.transform.SetParent(buttonObject.transform, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            toggleButtonText = textObject.GetComponent<TextMeshProUGUI>();
            toggleButtonText.alignment = TextAlignmentOptions.Center;
            toggleButtonText.color = Color.white;
            toggleButtonText.fontSize = 18f;
            toggleButtonText.raycastTarget = false;
            if (logTextDisplay != null && logTextDisplay.font != null)
                toggleButtonText.font = logTextDisplay.font;
        }

        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveListener(ToggleCollapsed);
            toggleButton.onClick.AddListener(ToggleCollapsed);
            toggleButton.transform.SetAsLastSibling();
        }
    }

    private void CreateColliderToggleButton()
    {
        if (toggleButton == null)
        {
            return;
        }

        RectTransform logButtonRect = toggleButton.GetComponent<RectTransform>();
        if (logButtonRect == null || logButtonRect.parent == null)
        {
            return;
        }

        Transform existing = logButtonRect.parent.Find(ColliderToggleButtonName);
        GameObject buttonObject;
        if (existing != null)
        {
            buttonObject = existing.gameObject;
        }
        else
        {
            buttonObject = new GameObject(
                ColliderToggleButtonName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.layer = toggleButton.gameObject.layer;

            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.SetParent(logButtonRect.parent, false);
            buttonRect.anchorMin = logButtonRect.anchorMin;
            buttonRect.anchorMax = logButtonRect.anchorMax;
            buttonRect.pivot = logButtonRect.pivot;
            // 日志右侧已有视频、程序和设备控制按钮，碰撞显示按钮放在日志左侧。
            buttonRect.anchoredPosition =
                logButtonRect.anchoredPosition + new Vector2(-120f, 0f);
            buttonRect.sizeDelta = logButtonRect.sizeDelta;
            buttonRect.localRotation = logButtonRect.localRotation;
            buttonRect.localScale = logButtonRect.localScale;

            colliderToggleButtonImage = buttonObject.GetComponent<Image>();
            colliderToggleButtonImage.color =
                new Color(0.05f, 0.35f, 0.65f, 0.92f);

            GameObject textObject = new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.layer = buttonObject.layer;
            textObject.transform.SetParent(buttonObject.transform, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.fontSize = toggleButtonText != null ? toggleButtonText.fontSize : 18f;
            text.raycastTarget = false;
            if (toggleButtonText != null && toggleButtonText.font != null)
            {
                text.font = toggleButtonText.font;
            }
        }

        colliderToggleButton = buttonObject.GetComponent<Button>();
        colliderToggleButtonImage = buttonObject.GetComponent<Image>();
        colliderToggleButtonText =
            buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
        if (colliderToggleButton != null)
        {
            colliderToggleButton.colors = toggleButton.colors;
            colliderToggleButton.transition = toggleButton.transition;
            colliderToggleButton.targetGraphic = colliderToggleButtonImage;
            colliderToggleButton.onClick.RemoveListener(ToggleColliderVisualization);
            colliderToggleButton.onClick.AddListener(ToggleColliderVisualization);
            colliderToggleButton.transform.SetAsLastSibling();
        }
    }

    public void ToggleColliderVisualization()
    {
        colliderVisualizationVisible = !colliderVisualizationVisible;
        ApplyColliderVisualization(colliderVisualizationVisible, true);
        UpdateColliderToggleButtonState();
    }

    private void ApplyColliderVisualization(bool visible, bool writeLog)
    {
        int rendererCount = 0;
        MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>(true);
        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer == null || !IsGeneratedColliderRenderer(renderer.transform))
            {
                continue;
            }

            renderer.enabled = visible;
            rendererCount++;
        }

        if (writeLog)
        {
            Debug.Log(
                $"[碰撞网格显示] Visible={visible}, Renderer={rendererCount}；" +
                "只改变调试渲染，不改变PhysX或MuJoCo碰撞");
        }
    }

    private static bool IsGeneratedColliderRenderer(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            if (current.name.Contains("_MjRoot"))
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    private void UpdateColliderToggleButtonState()
    {
        if (colliderToggleButtonText != null)
        {
            colliderToggleButtonText.text =
                colliderVisualizationVisible ? "隐藏碰撞" : "显示碰撞";
        }
        if (colliderToggleButtonImage != null)
        {
            colliderToggleButtonImage.color = colliderVisualizationVisible
                ? new Color(0.02f, 0.55f, 0.30f, 0.96f)
                : new Color(0.22f, 0.25f, 0.30f, 0.92f);
        }
    }
}
