using UnityEngine;
using TMPro;
using System.Collections.Generic;
using UnityEngine.UI;

public class RuntimeConsole : MonoBehaviour
{
    private const string ToggleButtonName = "RuntimeConsoleToggleButton";

    [Header("把刚刚建的 ConsoleLogText 拖到这里")]
    public TextMeshProUGUI logTextDisplay;

    [Header("最多显示多少行日志？(防止内存爆炸)")]
    public int maxLines = 50;

    [Header("启动时是否默认收起")]
    public bool startCollapsed = true;

    // 用一个列表来缓存当前屏幕上的日志文字
    private List<string> logLines = new List<string>();
    private Image backgroundImage;
    private Mask panelMask;
    private GameObject titleObject;
    private Button toggleButton;
    private TextMeshProUGUI toggleButtonText;
    private bool isCollapsed;

    void Start()
    {
        CachePanelParts();
        CreateToggleButton();
        SetCollapsed(startCollapsed);
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

        // 3. 塞进我们的笔记本里
        logLines.Add(newLine);

        // 4. 重点保护：如果日志太多，就把最老的一条删掉，防止程序卡死
        if (logLines.Count > maxLines)
        {
            logLines.RemoveAt(0);
        }

        // 5. 把笔记本里的所有文字，拼成一个大字符串，塞给 UI 文本框
        if (logTextDisplay != null)
        {
            logTextDisplay.text = string.Join("\n", logLines);
        }
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
        if (toggleButtonText != null)
            toggleButtonText.text = collapsed ? "展开日志" : "收起日志";
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
}
