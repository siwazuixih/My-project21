using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityScene = UnityEngine.SceneManagement.Scene;

public sealed class ServoTighteningController : MonoBehaviour
{
    private const string ToggleButtonName = "ServoTighteningToggleButton";
    private const string ForwardButtonName = "ServoTighteningForwardButton";
    private const string ReverseButtonName = "ServoTighteningReverseButton";
    private const string StopButtonName = "ServoTighteningStopButton";
    private const string LogToggleButtonName = "RuntimeConsoleToggleButton";
    private const string CurveImageObjectName = "ServoTorqueCurveImage";
    private const string CurveAreaLabel = "实时力矩曲线";
    private const float MotionConfirmationSeconds = 3f;

    [Header("Unity -> 拧紧程序")]
    [SerializeField] private string bridgeHost = "127.0.0.1";
    [SerializeField] private int bridgePort = 9100;
    [SerializeField, Range(0.5f, 10f)] private float commandTimeoutSeconds = 2f;
    [SerializeField, Range(0.2f, 5f)] private float statusPollIntervalSeconds = 0.5f;

    [Header("拧紧程序 -> 电批")]
    [SerializeField] private string toolHost = "192.168.192.21";
    [SerializeField] private int toolPort = 1200;

    [Header("Python 程序")]
    [SerializeField] private string pythonExecutable = "/usr/bin/python3";
    [SerializeField] private string programRelativePath =
        "ExternalCode/servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py";

    [Header("实时力矩曲线")]
    [SerializeField] private string curveHost = "127.0.0.1";
    [SerializeField] private int curvePort = 9101;
    [SerializeField] private string curveImageUrl =
        "http://127.0.0.1:9101/curve.jpg";
    [SerializeField, Range(0.1f, 2f)] private float curveRefreshIntervalSeconds = 0.2f;
    [SerializeField, Range(1f, 10f)] private float curveRequestTimeoutSeconds = 3f;

    [Header("真实设备安全开关")]
    [Tooltip("第一阶段默认关闭。只有明确解锁后，Unity 才允许下发正转或反转命令。")]
    [SerializeField] private bool enableRealToolMotion;

    public bool ProgramReady { get; private set; }
    public bool ToolConnected { get; private set; }
    public string ToolState { get; private set; } = "STOPPED";
    public string LastResponse { get; private set; } = "拧紧程序未启动";
    public string LastSavedCsv { get; private set; } = "";
    public string LastSavedPng { get; private set; } = "";

    private Process programProcess;
    private bool ownsProgramProcess;
    private bool lifecycleRunning;
    private bool statusRequestRunning;
    private bool shuttingDown;
    private bool stopRequestedForHiddenUi;
    private Coroutine bindCoroutine;
    private Coroutine statusCoroutine;
    private Coroutine curveCoroutine;
    private Button toggleButton;
    private Image toggleButtonBackground;
    private TextMeshProUGUI toggleButtonText;
    private Button forwardButton;
    private TextMeshProUGUI forwardButtonText;
    private Button reverseButton;
    private TextMeshProUGUI reverseButtonText;
    private Button stopButton;
    private TextMeshProUGUI stopButtonText;
    private GameObject statusRealObject;
    private RawImage curveImage;
    private GameObject curvePopupOverlay;
    private RawImage curvePopupImage;
    private Texture2D latestCurveTexture;
    private bool curveRequestRunning;
    private bool receivedFirstCurve;
    private float nextCurveErrorLogTime;
    private bool motionCommandRunning;
    private bool stopCommandRunning;
    private string activeMotionCommand = "";
    private string pendingMotionCommand = "";
    private float motionConfirmationExpiresAt;
    private readonly SemaphoreSlim commandGate = new SemaphoreSlim(1, 1);
    private readonly Color stoppedColor = new Color(0.05f, 0.35f, 0.65f, 0.92f);
    private readonly Color runningColor = new Color(0.02f, 0.55f, 0.75f, 0.96f);
    private readonly Color warningColor = new Color(0.75f, 0.34f, 0.06f, 0.96f);
    private readonly Color curveIdleColor = new Color32(14, 25, 41, 255);

    [Serializable]
    private sealed class BridgeStatus
    {
        public bool ok;
        public string message;
        public bool tool_connected;
        public string state;
        public string csv;
        public string png;
    }

    public sealed class CommandResult
    {
        public bool Success { get; }
        public string RawResponse { get; }
        public string Error { get; }

        public CommandResult(bool success, string rawResponse, string error)
        {
            Success = success;
            RawResponse = rawResponse ?? "";
            Error = error ?? "";
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateController()
    {
        if (FindObjectOfType<ServoTighteningController>() != null)
        {
            return;
        }

        GameObject controllerObject = new GameObject("Servo Tightening Controller");
        DontDestroyOnLoad(controllerObject);
        controllerObject.AddComponent<ServoTighteningController>();
    }

    private void Awake()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        BeginBinding(SceneManager.GetActiveScene());
    }

    private void Update()
    {
        UpdateButtonVisibility();
        if (
            !string.IsNullOrEmpty(pendingMotionCommand)
            && Time.unscaledTime >= motionConfirmationExpiresAt
        )
        {
            ClearMotionConfirmation();
            UpdateButtonState();
        }
        if (
            curvePopupOverlay != null
            && curvePopupOverlay.activeSelf
            && Input.GetKeyDown(KeyCode.Escape)
        )
        {
            CloseCurvePopup();
        }
    }

    private void HandleSceneLoaded(UnityScene scene, LoadSceneMode _mode)
    {
        BeginBinding(scene);
    }

    private void BeginBinding(UnityScene scene)
    {
        toggleButton = null;
        toggleButtonBackground = null;
        toggleButtonText = null;
        forwardButton = null;
        forwardButtonText = null;
        reverseButton = null;
        reverseButtonText = null;
        stopButton = null;
        stopButtonText = null;
        statusRealObject = null;
        ClearMotionConfirmation();
        CloseCurvePopup();
        StopCurvePolling();
        ReleaseCurveTexture();
        curveImage = null;
        stopRequestedForHiddenUi = false;

        if (bindCoroutine != null)
        {
            StopCoroutine(bindCoroutine);
        }

        bindCoroutine = StartCoroutine(BindWhenReady(scene));
    }

    private IEnumerator BindWhenReady(UnityScene scene)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (Time.realtimeSinceStartup < deadline)
        {
            statusRealObject = FindSceneObject(scene, "StatusReal");
            GameObject logButtonObject =
                FindSceneObject(scene, LogToggleButtonName);
            bool curveBound = TryBindCurveArea(scene);
            if (
                statusRealObject != null
                && logButtonObject != null
                && curveBound
            )
            {
                CreateToggleButton(logButtonObject);
                CreateControlButtons(logButtonObject);
                UpdateButtonState();
                UpdateButtonVisibility();
                if (ProgramReady)
                {
                    StartCurvePolling();
                }
                bindCoroutine = null;
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.25f);
        }

        bindCoroutine = null;
        UnityEngine.Debug.Log(
            $"[ServoTightening] Scene '{scene.name}' is missing the formal "
            + $"real-run toolbar or '{CurveAreaLabel}' area."
        );
    }

    private bool TryBindCurveArea(UnityScene scene)
    {
        if (curveImage != null)
        {
            return true;
        }

        Text[] labels = FindObjectsOfType<Text>(true);
        foreach (Text label in labels)
        {
            if (
                label.gameObject.scene != scene
                || label.text.Trim() != CurveAreaLabel
            )
            {
                continue;
            }

            Image overlay = FindCurveOverlay(label.transform);
            if (overlay == null)
            {
                continue;
            }

            Transform existing =
                label.transform.Find(CurveImageObjectName);
            curveImage = existing != null
                ? existing.GetComponent<RawImage>()
                : CreateCurveImage(label.transform, overlay);
            if (curveImage == null)
            {
                continue;
            }

            curveImage.texture = null;
            curveImage.color = curveIdleColor;
            curveImage.raycastTarget = true;
            TorqueCurvePointerHandler pointerHandler =
                curveImage.GetComponent<TorqueCurvePointerHandler>();
            if (pointerHandler == null)
            {
                pointerHandler =
                    curveImage.gameObject.AddComponent<TorqueCurvePointerHandler>();
            }
            pointerHandler.Initialize(this, true);
            curveImage.gameObject.SetActive(true);
            UnityEngine.Debug.Log(
                "[ServoTightening] Bound live torque curve to "
                + $"'{GetHierarchyPath(label.transform)}/"
                + $"{CurveImageObjectName}'."
            );
            return true;
        }

        return false;
    }

    private static Image FindCurveOverlay(Transform container)
    {
        Image[] images = container.GetComponentsInChildren<Image>(true);
        foreach (Image image in images)
        {
            if (image.transform.parent != container)
            {
                continue;
            }

            if (image.sprite != null && image.sprite.name == "Camera")
            {
                return image;
            }
        }

        return null;
    }

    private static RawImage CreateCurveImage(
        Transform container,
        Image overlay
    )
    {
        GameObject curveObject = new GameObject(
            CurveImageObjectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage)
        );
        RectTransform curveRect =
            curveObject.GetComponent<RectTransform>();
        RectTransform overlayRect = overlay.rectTransform;

        curveRect.SetParent(container, false);
        curveRect.anchorMin = overlayRect.anchorMin;
        curveRect.anchorMax = overlayRect.anchorMax;
        curveRect.anchoredPosition = overlayRect.anchoredPosition;
        curveRect.sizeDelta = new Vector2(
            Mathf.Max(1f, overlayRect.sizeDelta.x - 2f),
            Mathf.Max(1f, overlayRect.sizeDelta.y - 2f)
        );
        curveRect.pivot = overlayRect.pivot;
        curveRect.localRotation = overlayRect.localRotation;
        curveRect.localScale = overlayRect.localScale;
        curveRect.SetSiblingIndex(overlay.transform.GetSiblingIndex() + 1);

        RawImage image = curveObject.GetComponent<RawImage>();
        image.color = Color.black;
        image.raycastTarget = true;
        return image;
    }

    public void ToggleCurvePopup()
    {
        if (
            latestCurveTexture == null
            || curveImage == null
            || !ProgramReady
        )
        {
            return;
        }

        if (
            curvePopupOverlay != null
            && curvePopupOverlay.activeSelf
        )
        {
            CloseCurvePopup();
            return;
        }

        EnsureCurvePopupCreated();
        if (curvePopupOverlay == null || curvePopupImage == null)
        {
            return;
        }

        curvePopupImage.texture = latestCurveTexture;
        curvePopupImage.color = Color.white;
        curvePopupOverlay.SetActive(true);
        curvePopupOverlay.transform.SetAsLastSibling();
    }

    public void CloseCurvePopup()
    {
        if (curvePopupImage != null)
        {
            curvePopupImage.texture = null;
            curvePopupImage.color = Color.black;
        }

        if (curvePopupOverlay != null)
        {
            curvePopupOverlay.SetActive(false);
        }
    }

    private void EnsureCurvePopupCreated()
    {
        if (curvePopupOverlay != null || curveImage == null)
        {
            return;
        }

        Canvas canvas = curveImage.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            return;
        }
        canvas = canvas.rootCanvas;

        curvePopupOverlay = new GameObject(
            "TorqueCurvePopupOverlay",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        curvePopupOverlay.layer = curveImage.gameObject.layer;
        RectTransform overlayRect =
            curvePopupOverlay.GetComponent<RectTransform>();
        overlayRect.SetParent(canvas.transform, false);
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        Image backdrop = curvePopupOverlay.GetComponent<Image>();
        backdrop.color = new Color(0f, 0.025f, 0.07f, 0.82f);
        backdrop.raycastTarget = true;
        TorqueCurvePointerHandler backdropHandler =
            curvePopupOverlay.AddComponent<TorqueCurvePointerHandler>();
        backdropHandler.Initialize(this, false, true);

        GameObject panelObject = new GameObject(
            "TorqueCurvePopupPanel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        panelObject.layer = curvePopupOverlay.layer;
        RectTransform panelRect =
            panelObject.GetComponent<RectTransform>();
        panelRect.SetParent(curvePopupOverlay.transform, false);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;

        RectTransform canvasRect = canvas.transform as RectTransform;
        float availableWidth =
            canvasRect != null ? canvasRect.rect.width : 1920f;
        float availableHeight =
            canvasRect != null ? canvasRect.rect.height : 1080f;
        float panelWidth = Mathf.Min(1120f, availableWidth * 0.82f);
        float plotWidth = Mathf.Max(320f, panelWidth - 32f);
        float panelHeight = plotWidth * 9f / 16f + 72f;
        if (panelHeight > availableHeight * 0.86f)
        {
            panelHeight = availableHeight * 0.86f;
            plotWidth = Mathf.Max(320f, (panelHeight - 72f) * 16f / 9f);
            panelWidth = plotWidth + 32f;
        }
        panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

        Image panelBackground = panelObject.GetComponent<Image>();
        panelBackground.color = new Color(0.035f, 0.11f, 0.21f, 1f);
        panelBackground.raycastTarget = true;

        TMP_FontAsset yaHeiFont = FindMicrosoftYaHeiFont();
        CreatePopupTitle(panelObject.transform, yaHeiFont);
        CreatePopupCloseButton(panelObject.transform, yaHeiFont);

        GameObject imageObject = new GameObject(
            "LargeTorqueCurveImage",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage)
        );
        imageObject.layer = panelObject.layer;
        RectTransform imageRect =
            imageObject.GetComponent<RectTransform>();
        imageRect.SetParent(panelObject.transform, false);
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = new Vector2(16f, 16f);
        imageRect.offsetMax = new Vector2(-16f, -56f);

        curvePopupImage = imageObject.GetComponent<RawImage>();
        curvePopupImage.color = Color.black;
        curvePopupImage.raycastTarget = true;
        TorqueCurvePointerHandler imageHandler =
            imageObject.AddComponent<TorqueCurvePointerHandler>();
        imageHandler.Initialize(this, true);

        curvePopupOverlay.SetActive(false);
    }

    private TMP_FontAsset FindMicrosoftYaHeiFont()
    {
        TMP_FontAsset[] fonts =
            Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        foreach (TMP_FontAsset font in fonts)
        {
            if (font != null && font.name == "微软雅黑 SDF")
            {
                return font;
            }
        }

        return toggleButtonText != null ? toggleButtonText.font : null;
    }

    private static void CreatePopupTitle(
        Transform parent,
        TMP_FontAsset font
    )
    {
        GameObject titleObject = new GameObject(
            "Title",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        titleObject.layer = parent.gameObject.layer;
        RectTransform titleRect =
            titleObject.GetComponent<RectTransform>();
        titleRect.SetParent(parent, false);
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(18f, -52f);
        titleRect.offsetMax = new Vector2(-60f, -8f);

        TextMeshProUGUI title =
            titleObject.GetComponent<TextMeshProUGUI>();
        title.text = "实时力矩曲线（双击缩小）";
        title.fontSize = 24f;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.color = new Color(0.49f, 0.83f, 0.99f, 1f);
        title.raycastTarget = false;
        if (font != null)
        {
            title.font = font;
        }
    }

    private void CreatePopupCloseButton(
        Transform parent,
        TMP_FontAsset font
    )
    {
        GameObject closeObject = new GameObject(
            "CloseButton",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button)
        );
        closeObject.layer = parent.gameObject.layer;
        RectTransform closeRect =
            closeObject.GetComponent<RectTransform>();
        closeRect.SetParent(parent, false);
        closeRect.anchorMin = new Vector2(1f, 1f);
        closeRect.anchorMax = new Vector2(1f, 1f);
        closeRect.pivot = new Vector2(1f, 1f);
        closeRect.anchoredPosition = new Vector2(-10f, -9f);
        closeRect.sizeDelta = new Vector2(42f, 38f);

        Image closeBackground = closeObject.GetComponent<Image>();
        closeBackground.color = new Color(0.7f, 0.12f, 0.16f, 0.95f);

        Button closeButton = closeObject.GetComponent<Button>();
        closeButton.targetGraphic = closeBackground;
        closeButton.onClick.AddListener(CloseCurvePopup);

        GameObject textObject = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        textObject.layer = closeObject.layer;
        RectTransform textRect =
            textObject.GetComponent<RectTransform>();
        textRect.SetParent(closeObject.transform, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI text =
            textObject.GetComponent<TextMeshProUGUI>();
        text.text = "×";
        text.fontSize = 26f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        if (font != null)
        {
            text.font = font;
        }
    }

    private void CreateToggleButton(GameObject logButtonObject)
    {
        RectTransform logButtonRect =
            logButtonObject.GetComponent<RectTransform>();
        if (logButtonRect == null || logButtonRect.parent == null)
        {
            return;
        }

        Transform existing = logButtonRect.parent.Find(ToggleButtonName);
        GameObject buttonObject;
        if (existing != null)
        {
            buttonObject = existing.gameObject;
        }
        else
        {
            buttonObject = new GameObject(
                ToggleButtonName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button)
            );
            buttonObject.layer = logButtonObject.layer;

            RectTransform buttonRect =
                buttonObject.GetComponent<RectTransform>();
            buttonRect.SetParent(logButtonRect.parent, false);
            buttonRect.anchorMin = logButtonRect.anchorMin;
            buttonRect.anchorMax = logButtonRect.anchorMax;
            buttonRect.pivot = logButtonRect.pivot;
            buttonRect.anchoredPosition =
                logButtonRect.anchoredPosition + new Vector2(240f, 0f);
            buttonRect.sizeDelta = logButtonRect.sizeDelta;
            buttonRect.localRotation = logButtonRect.localRotation;
            buttonRect.localScale = logButtonRect.localScale;

            CreateButtonText(
                buttonObject.transform,
                logButtonObject.GetComponentInChildren<TextMeshProUGUI>(true)
            );
        }

        toggleButton = buttonObject.GetComponent<Button>();
        toggleButtonBackground = buttonObject.GetComponent<Image>();
        toggleButtonText =
            buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);

        Button logButton = logButtonObject.GetComponent<Button>();
        if (toggleButton != null)
        {
            if (logButton != null)
            {
                toggleButton.colors = logButton.colors;
                toggleButton.transition = logButton.transition;
            }

            toggleButton.targetGraphic = toggleButtonBackground;
            toggleButton.onClick.RemoveListener(ToggleProgram);
            toggleButton.onClick.AddListener(ToggleProgram);
            toggleButton.transform.SetAsLastSibling();
        }

        UnityEngine.Debug.Log(
            "[ServoTightening] Program button created next to video button."
        );
    }

    private void CreateControlButtons(GameObject logButtonObject)
    {
        forwardButton = CreateControlButton(
            logButtonObject,
            ForwardButtonName,
            355f,
            HandleForwardButton
        );
        reverseButton = CreateControlButton(
            logButtonObject,
            ReverseButtonName,
            445f,
            HandleReverseButton
        );
        stopButton = CreateControlButton(
            logButtonObject,
            StopButtonName,
            535f,
            HandleStopButton
        );

        forwardButtonText = GetButtonText(forwardButton);
        reverseButtonText = GetButtonText(reverseButton);
        stopButtonText = GetButtonText(stopButton);

        SetButtonBackground(forwardButton, runningColor);
        SetButtonBackground(reverseButton, stoppedColor);
        SetButtonBackground(stopButton, warningColor);
    }

    private static Button CreateControlButton(
        GameObject templateObject,
        string objectName,
        float horizontalOffset,
        UnityEngine.Events.UnityAction onClick
    )
    {
        RectTransform templateRect =
            templateObject.GetComponent<RectTransform>();
        if (templateRect == null || templateRect.parent == null)
        {
            return null;
        }

        Transform existing = templateRect.parent.Find(objectName);
        GameObject buttonObject;
        if (existing != null)
        {
            buttonObject = existing.gameObject;
        }
        else
        {
            buttonObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button)
            );
            buttonObject.layer = templateObject.layer;

            RectTransform buttonRect =
                buttonObject.GetComponent<RectTransform>();
            buttonRect.SetParent(templateRect.parent, false);
            buttonRect.anchorMin = templateRect.anchorMin;
            buttonRect.anchorMax = templateRect.anchorMax;
            buttonRect.pivot = templateRect.pivot;
            buttonRect.anchoredPosition =
                templateRect.anchoredPosition
                + new Vector2(horizontalOffset, 0f);
            buttonRect.sizeDelta =
                new Vector2(82f, templateRect.sizeDelta.y);
            buttonRect.localRotation = templateRect.localRotation;
            buttonRect.localScale = templateRect.localScale;

            CreateButtonText(
                buttonObject.transform,
                templateObject.GetComponentInChildren<TextMeshProUGUI>(true)
            );
        }

        Button button = buttonObject.GetComponent<Button>();
        Button templateButton = templateObject.GetComponent<Button>();
        if (button != null)
        {
            if (templateButton != null)
            {
                button.colors = templateButton.colors;
                button.transition = templateButton.transition;
            }

            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClick);
            button.transform.SetAsLastSibling();
        }

        TextMeshProUGUI text = GetButtonText(button);
        if (text != null)
        {
            text.fontSize = Mathf.Min(text.fontSize, 15f);
        }

        return button;
    }

    private static TextMeshProUGUI GetButtonText(Button button)
    {
        return button != null
            ? button.GetComponentInChildren<TextMeshProUGUI>(true)
            : null;
    }

    private static void SetButtonBackground(Button button, Color color)
    {
        if (button == null)
        {
            return;
        }

        Image background = button.GetComponent<Image>();
        if (background != null)
        {
            background.color = color;
        }
    }

    private static void CreateButtonText(
        Transform buttonTransform,
        TextMeshProUGUI template
    )
    {
        GameObject textObject = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        textObject.layer = buttonTransform.gameObject.layer;
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(buttonTransform, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.fontSize = template != null ? template.fontSize : 18f;
        text.raycastTarget = false;
        if (template != null && template.font != null)
        {
            text.font = template.font;
        }
    }

    private static GameObject FindSceneObject(UnityScene scene, string objectName)
    {
        Transform[] transforms = FindObjectsOfType<Transform>(true);
        foreach (Transform candidate in transforms)
        {
            if (
                candidate.gameObject.scene == scene
                && candidate.name == objectName
            )
            {
                return candidate.gameObject;
            }
        }

        return null;
    }

    private void UpdateButtonVisibility()
    {
        if (toggleButton == null || statusRealObject == null)
        {
            return;
        }

        bool realUiVisible = statusRealObject.activeInHierarchy;
        if (toggleButton.gameObject.activeSelf != realUiVisible)
        {
            toggleButton.gameObject.SetActive(realUiVisible);
        }
        SetControlButtonsVisible(realUiVisible);

        if (
            !realUiVisible
            && IsProgramActive()
            && !stopRequestedForHiddenUi
            && !lifecycleRunning
        )
        {
            stopRequestedForHiddenUi = true;
            _ = StopProgramAsync();
        }
        else if (realUiVisible)
        {
            stopRequestedForHiddenUi = false;
        }
    }

    private void SetControlButtonsVisible(bool visible)
    {
        SetButtonVisible(forwardButton, visible);
        SetButtonVisible(reverseButton, visible);
        SetButtonVisible(stopButton, visible);
    }

    private static void SetButtonVisible(Button button, bool visible)
    {
        if (
            button != null
            && button.gameObject.activeSelf != visible
        )
        {
            button.gameObject.SetActive(visible);
        }
    }

    public async void ToggleProgram()
    {
        if (lifecycleRunning)
        {
            return;
        }

        if (IsProgramActive() || ProgramReady)
        {
            await StopProgramAsync();
        }
        else
        {
            await StartProgramAsync();
        }
    }

    public async Task<CommandResult> StartProgramAsync()
    {
        if (lifecycleRunning)
        {
            return Failure("程序正在执行启动或停止操作");
        }

        lifecycleRunning = true;
        LastResponse = "正在启动拧紧程序";
        UpdateButtonState();

        try
        {
            CommandResult existing = await SendCommandInternalAsync("status");
            if (existing.Success)
            {
                ownsProgramProcess = false;
                ProgramReady = true;
                ApplyBridgeStatus(existing.RawResponse);
                StartStatusPolling();
                StartCurvePolling();
                LastResponse = "已连接现有拧紧程序";
                UnityEngine.Debug.Log(
                    "[ServoTightening] Attached to an existing V26 bridge."
                );
                return existing;
            }

            string projectRoot =
                Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                return ReportFailure("无法确定 Unity 项目目录");
            }

            string scriptPath = Path.Combine(projectRoot, programRelativePath);
            if (!File.Exists(scriptPath))
            {
                return ReportFailure("找不到拧紧程序: " + scriptPath);
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                Arguments = Quote(scriptPath),
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };
            startInfo.EnvironmentVariables["SERVO_TOOL_IP"] = toolHost;
            startInfo.EnvironmentVariables["SERVO_TOOL_PORT"] =
                toolPort.ToString();
            startInfo.EnvironmentVariables["SERVO_BRIDGE_HOST"] = bridgeHost;
            startInfo.EnvironmentVariables["SERVO_BRIDGE_PORT"] =
                bridgePort.ToString();
            startInfo.EnvironmentVariables["SERVO_UNITY_EMBEDDED_MODE"] = "1";
            startInfo.EnvironmentVariables["SERVO_CURVE_HOST"] = curveHost;
            startInfo.EnvironmentVariables["SERVO_CURVE_PORT"] =
                curvePort.ToString();
            startInfo.EnvironmentVariables["SERVO_CURVE_INTERVAL"] =
                curveRefreshIntervalSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                );
            startInfo.EnvironmentVariables["SERVO_CURVE_FONT"] =
                Path.Combine(projectRoot, "Assets", "微软雅黑.ttf");
            string matplotlibConfigDirectory = Path.Combine(
                Path.GetTempPath(),
                "servo-tightening-matplotlib"
            );
            Directory.CreateDirectory(matplotlibConfigDirectory);
            startInfo.EnvironmentVariables["MPLCONFIGDIR"] =
                matplotlibConfigDirectory;

            programProcess = Process.Start(startInfo);
            ownsProgramProcess = programProcess != null;
            if (programProcess == null)
            {
                return ReportFailure("Python 进程启动失败");
            }

            for (int attempt = 0; attempt < 50; attempt++)
            {
                if (HasOwnedProcessExited())
                {
                    return ReportFailure(
                        "拧紧程序启动后提前退出，退出码: "
                        + programProcess.ExitCode
                    );
                }

                await Task.Delay(200);
                CommandResult status = await SendCommandInternalAsync("status");
                if (!status.Success)
                {
                    continue;
                }

                ProgramReady = true;
                ApplyBridgeStatus(status.RawResponse);
                StartStatusPolling();
                StartCurvePolling();
                LastResponse = "拧紧程序已启动，等待控制指令";
                UnityEngine.Debug.Log(
                    "[ServoTightening] V26 bridge is ready at "
                    + $"{bridgeHost}:{bridgePort}; no motion command was sent."
                );
                return status;
            }

            return ReportFailure("拧紧程序启动超时，控制端口未就绪");
        }
        catch (Exception exception)
        {
            return ReportFailure("启动拧紧程序失败: " + exception.Message);
        }
        finally
        {
            lifecycleRunning = false;
            UpdateButtonState();
        }
    }

    public Task<CommandResult> ConnectToolAsync()
    {
        return SendPublicCommandAsync("connect", false);
    }

    public Task<CommandResult> QueryStatusAsync()
    {
        return SendPublicCommandAsync("status", false);
    }

    public Task<CommandResult> StopToolAsync()
    {
        return SendPublicCommandAsync("stop", false);
    }

    public Task<CommandResult> StartTighteningAsync()
    {
        return SendPublicCommandAsync("forward", true);
    }

    public Task<CommandResult> ReverseHomeAsync()
    {
        return SendPublicCommandAsync("reverse", true);
    }

    private async void HandleForwardButton()
    {
        await ConfirmAndSendMotionAsync("forward");
    }

    private async void HandleReverseButton()
    {
        await ConfirmAndSendMotionAsync("reverse");
    }

    private async void HandleStopButton()
    {
        if (!ProgramReady || stopCommandRunning)
        {
            return;
        }

        ClearMotionConfirmation();
        enableRealToolMotion = false;
        stopCommandRunning = true;
        UpdateButtonState();
        try
        {
            await StopToolAsync();
        }
        finally
        {
            stopCommandRunning = false;
            UpdateButtonState();
        }
    }

    private async Task ConfirmAndSendMotionAsync(string command)
    {
        if (!ProgramReady || motionCommandRunning)
        {
            return;
        }

        bool confirmationValid =
            pendingMotionCommand == command
            && Time.unscaledTime < motionConfirmationExpiresAt;
        if (!confirmationValid)
        {
            pendingMotionCommand = command;
            motionConfirmationExpiresAt =
                Time.unscaledTime + MotionConfirmationSeconds;
            LastResponse = command == "forward"
                ? "请在 3 秒内再次点击“开始拧紧”确认动作"
                : "请在 3 秒内再次点击“反转回位”确认动作";
            UnityEngine.Debug.LogWarning(
                "[ServoTightening] Motion confirmation required: "
                + command
            );
            UpdateButtonState();
            return;
        }

        ClearMotionConfirmation();
        motionCommandRunning = true;
        activeMotionCommand = command;
        enableRealToolMotion = true;
        UpdateButtonState();
        try
        {
            if (command == "forward")
            {
                await StartTighteningAsync();
            }
            else
            {
                await ReverseHomeAsync();
            }
        }
        finally
        {
            enableRealToolMotion = false;
            motionCommandRunning = false;
            activeMotionCommand = "";
            UpdateButtonState();
        }
    }

    private void ClearMotionConfirmation()
    {
        pendingMotionCommand = "";
        motionConfirmationExpiresAt = 0f;
    }

    public void SetRealToolMotionEnabled(bool enabled)
    {
        enableRealToolMotion = enabled;
        UnityEngine.Debug.LogWarning(
            "[ServoTightening] Real tool motion commands "
            + (enabled ? "ENABLED." : "DISABLED.")
        );
    }

    public async Task<CommandResult> StopProgramAsync()
    {
        if (lifecycleRunning)
        {
            return Failure("程序正在执行启动或停止操作");
        }

        lifecycleRunning = true;
        enableRealToolMotion = false;
        ClearMotionConfirmation();
        LastResponse = "正在安全停止拧紧程序";
        UpdateButtonState();

        try
        {
            bool safeToClose = !ProgramReady;
            CommandResult stopResult =
                await SendCommandInternalAsync("stop");
            if (stopResult.Success)
            {
                ApplyBridgeStatus(stopResult.RawResponse);
                safeToClose = IsSafeStoppedState(ToolState)
                    || !ToolConnected;

                for (int attempt = 0; !safeToClose && attempt < 30; attempt++)
                {
                    await Task.Delay(100);
                    CommandResult status =
                        await SendCommandInternalAsync("status");
                    if (!status.Success)
                    {
                        continue;
                    }

                    ApplyBridgeStatus(status.RawResponse);
                    safeToClose = IsSafeStoppedState(ToolState)
                        || !ToolConnected;
                }
            }

            if (!safeToClose && IsProgramActive())
            {
                LastResponse =
                    "未确认电批已停止，已保留拧紧程序，请重试停止或检查急停";
                UnityEngine.Debug.LogError(
                    "[ServoTightening] Refused to kill V26 because a safe "
                    + "tool stop was not confirmed."
                );
                return Failure(LastResponse);
            }

            StopStatusPolling();
            StopCurvePolling();
            CloseCurvePopup();
            ReleaseCurveTexture();
            if (curveImage != null)
            {
                curveImage.texture = null;
                curveImage.color = curveIdleColor;
                curveImage.gameObject.SetActive(true);
            }
            if (ownsProgramProcess)
            {
                KillOwnedProcess();
            }

            programProcess = null;
            ownsProgramProcess = false;
            ProgramReady = false;
            ToolConnected = false;
            ToolState = "STOPPED";
            LastResponse = "拧紧程序已关闭";
            UnityEngine.Debug.Log(
                "[ServoTightening] V26 bridge stopped after safe-stop check."
            );
            return new CommandResult(true, "", "");
        }
        finally
        {
            lifecycleRunning = false;
            UpdateButtonState();
        }
    }

    private async Task<CommandResult> SendPublicCommandAsync(
        string command,
        bool requiresMotionUnlock
    )
    {
        if (!ProgramReady)
        {
            return Failure("拧紧程序尚未启动");
        }

        if (requiresMotionUnlock && !enableRealToolMotion)
        {
            return Failure(
                "真实电批运动安全开关未解锁，已拒绝命令: " + command
            );
        }

        CommandResult result = await SendCommandInternalAsync(command);
        if (result.Success)
        {
            ApplyBridgeStatus(result.RawResponse);
            LastResponse = $"{command}: {ToolState}";
        }
        else
        {
            LastResponse = result.Error;
        }

        UpdateButtonState();
        return result;
    }

    private async Task<CommandResult> SendCommandInternalAsync(string command)
    {
        await commandGate.WaitAsync();
        try
        {
            int timeoutMs = Math.Max(
                250,
                (int)Math.Round(commandTimeoutSeconds * 1000f)
            );
            return await Task.Run(
                () => SendCommandBlocking(command, timeoutMs)
            );
        }
        finally
        {
            commandGate.Release();
        }
    }

    private CommandResult SendCommandBlocking(string command, int timeoutMs)
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                client.SendTimeout = timeoutMs;
                client.ReceiveTimeout = timeoutMs;
                IAsyncResult connectResult =
                    client.BeginConnect(bridgeHost, bridgePort, null, null);
                using (connectResult.AsyncWaitHandle)
                {
                    if (!connectResult.AsyncWaitHandle.WaitOne(timeoutMs))
                    {
                        return Failure("连接拧紧程序超时");
                    }

                    client.EndConnect(connectResult);
                }

                using (NetworkStream stream = client.GetStream())
                {
                    string payload =
                        "{\"cmd\":\"" + EscapeJson(command) + "\"}\n";
                    byte[] bytes = Encoding.UTF8.GetBytes(payload);
                    stream.Write(bytes, 0, bytes.Length);

                    using (
                        StreamReader reader =
                            new StreamReader(stream, Encoding.UTF8)
                    )
                    {
                        string response = reader.ReadLine();
                        if (string.IsNullOrEmpty(response))
                        {
                            return Failure("拧紧程序没有返回数据");
                        }

                        return new CommandResult(true, response, "");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            return Failure("拧紧程序通信失败: " + exception.Message);
        }
    }

    private void ApplyBridgeStatus(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        try
        {
            BridgeStatus status = JsonUtility.FromJson<BridgeStatus>(json);
            if (status == null)
            {
                return;
            }

            ToolConnected = status.tool_connected;
            if (!string.IsNullOrEmpty(status.state))
            {
                ToolState = status.state;
            }
            if (!string.IsNullOrEmpty(status.csv))
            {
                LastSavedCsv = status.csv;
            }
            if (!string.IsNullOrEmpty(status.png))
            {
                LastSavedPng = status.png;
            }
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning(
                "[ServoTightening] Failed to parse bridge status: "
                + exception.Message
            );
        }
    }

    private void StartStatusPolling()
    {
        if (statusCoroutine == null)
        {
            statusCoroutine = StartCoroutine(PollStatus());
        }
    }

    private void StopStatusPolling()
    {
        if (statusCoroutine != null)
        {
            StopCoroutine(statusCoroutine);
            statusCoroutine = null;
        }
        statusRequestRunning = false;
    }

    private IEnumerator PollStatus()
    {
        while (!shuttingDown && ProgramReady)
        {
            if (!statusRequestRunning)
            {
                _ = RefreshStatusAsync();
            }

            yield return new WaitForSecondsRealtime(
                Mathf.Max(0.2f, statusPollIntervalSeconds)
            );
        }

        statusCoroutine = null;
    }

    private async Task RefreshStatusAsync()
    {
        statusRequestRunning = true;
        try
        {
            CommandResult result = await SendCommandInternalAsync("status");
            if (result.Success)
            {
                ApplyBridgeStatus(result.RawResponse);
            }
            else if (HasOwnedProcessExited())
            {
                ProgramReady = false;
                ToolConnected = false;
                ToolState = "STOPPED";
                LastResponse = "拧紧程序已经退出";
                StopStatusPolling();
                StopCurvePolling();
                CloseCurvePopup();
                ReleaseCurveTexture();
                if (curveImage != null)
                {
                    curveImage.texture = null;
                    curveImage.color = curveIdleColor;
                    curveImage.gameObject.SetActive(true);
                }
            }

            UpdateButtonState();
        }
        finally
        {
            statusRequestRunning = false;
        }
    }

    private void UpdateButtonState()
    {
        if (toggleButton != null)
        {
            toggleButton.interactable = !lifecycleRunning;
        }

        if (toggleButtonText != null)
        {
            toggleButtonText.text = lifecycleRunning
                ? (IsProgramActive() ? "安全停止..." : "启动中...")
                : (ProgramReady || IsProgramActive()
                    ? "关闭程序"
                    : "启动程序");
        }

        if (toggleButtonBackground != null)
        {
            toggleButtonBackground.color = lifecycleRunning
                ? warningColor
                : (ProgramReady || IsProgramActive()
                    ? runningColor
                    : stoppedColor);
        }

        bool controlsReady = ProgramReady && !lifecycleRunning;
        bool motionReady = controlsReady && ToolConnected;
        if (forwardButton != null)
        {
            forwardButton.interactable =
                motionReady && !motionCommandRunning;
        }
        if (reverseButton != null)
        {
            reverseButton.interactable =
                motionReady && !motionCommandRunning;
        }
        if (stopButton != null)
        {
            stopButton.interactable =
                controlsReady && !stopCommandRunning;
        }

        if (forwardButtonText != null)
        {
            forwardButtonText.text =
                pendingMotionCommand == "forward"
                ? "再次确认"
                : (activeMotionCommand == "forward"
                    ? "执行中..."
                    : "开始拧紧");
        }
        if (reverseButtonText != null)
        {
            reverseButtonText.text =
                pendingMotionCommand == "reverse"
                ? "再次确认"
                : (activeMotionCommand == "reverse"
                    ? "执行中..."
                    : "反转回位");
        }
        if (stopButtonText != null)
        {
            stopButtonText.text =
                stopCommandRunning ? "停止中..." : "立即停止";
        }
    }

    private void StartCurvePolling()
    {
        if (curveImage == null || curveCoroutine != null)
        {
            return;
        }

        receivedFirstCurve = false;
        nextCurveErrorLogTime = 0f;
        curveImage.texture = null;
        curveImage.color = Color.black;
        curveImage.gameObject.SetActive(true);
        curveCoroutine = StartCoroutine(PollCurveImage());
    }

    private void StopCurvePolling()
    {
        if (curveCoroutine != null)
        {
            StopCoroutine(curveCoroutine);
            curveCoroutine = null;
        }
        curveRequestRunning = false;
        receivedFirstCurve = false;
    }

    private IEnumerator PollCurveImage()
    {
        while (!shuttingDown && ProgramReady)
        {
            if (curveImage != null && !curveRequestRunning)
            {
                yield return FetchCurveImage();
            }

            yield return new WaitForSecondsRealtime(
                Mathf.Max(0.1f, curveRefreshIntervalSeconds)
            );
        }

        curveCoroutine = null;
    }

    private IEnumerator FetchCurveImage()
    {
        curveRequestRunning = true;

        using (
            UnityWebRequest request =
                UnityWebRequestTexture.GetTexture(curveImageUrl, false)
        )
        {
            request.timeout = Mathf.Max(
                1,
                Mathf.RoundToInt(curveRequestTimeoutSeconds)
            );
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (!ProgramReady)
                {
                    curveRequestRunning = false;
                    yield break;
                }

                Texture2D previousTexture = latestCurveTexture;
                latestCurveTexture =
                    DownloadHandlerTexture.GetContent(request);
                if (curveImage != null)
                {
                    curveImage.texture = latestCurveTexture;
                    curveImage.color = Color.white;
                }
                if (
                    curvePopupImage != null
                    && curvePopupOverlay != null
                    && curvePopupOverlay.activeSelf
                )
                {
                    curvePopupImage.texture = latestCurveTexture;
                    curvePopupImage.color = Color.white;
                }

                if (previousTexture != null)
                {
                    Destroy(previousTexture);
                }

                if (!receivedFirstCurve)
                {
                    receivedFirstCurve = true;
                    UnityEngine.Debug.Log(
                        "[ServoTightening] First live torque curve received: "
                        + $"{latestCurveTexture.width}x"
                        + $"{latestCurveTexture.height}."
                    );
                }
            }
            else if (
                ProgramReady
                && Time.realtimeSinceStartup >= nextCurveErrorLogTime
            )
            {
                UnityEngine.Debug.LogWarning(
                    "[ServoTightening] Waiting for live torque curve: "
                    + request.error
                );
                nextCurveErrorLogTime =
                    Time.realtimeSinceStartup + 2f;
            }
        }

        curveRequestRunning = false;
    }

    private void ReleaseCurveTexture()
    {
        if (latestCurveTexture == null)
        {
            return;
        }

        Destroy(latestCurveTexture);
        latestCurveTexture = null;
        if (curvePopupImage != null)
        {
            curvePopupImage.texture = null;
            curvePopupImage.color = Color.black;
        }
    }

    private bool IsProgramActive()
    {
        if (ProgramReady && !ownsProgramProcess)
        {
            return true;
        }

        return programProcess != null && !HasOwnedProcessExited();
    }

    private bool HasOwnedProcessExited()
    {
        if (programProcess == null)
        {
            return true;
        }

        try
        {
            return programProcess.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsSafeStoppedState(string state)
    {
        switch ((state ?? "").Trim().ToUpperInvariant())
        {
            case "IDLE":
            case "STOP":
            case "STOPPED":
            case "MONITOR":
            case "HOME_READY":
            case "OK":
            case "NG_SLIP":
            case "JAM":
            case "JAM_WARN":
            case "NG_DEVICE":
                return true;
            default:
                return false;
        }
    }

    private static string GetHierarchyPath(Transform current)
    {
        string path = current.name;
        while (current.parent != null)
        {
            current = current.parent;
            path = current.name + "/" + path;
        }

        return path;
    }

    private void KillOwnedProcess()
    {
        if (programProcess == null)
        {
            return;
        }

        try
        {
            if (!programProcess.HasExited)
            {
                programProcess.Kill();
                programProcess.WaitForExit(2000);
            }
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning(
                "[ServoTightening] Failed to terminate owned V26 process: "
                + exception.Message
            );
        }
        finally
        {
            programProcess.Dispose();
        }
    }

    private static CommandResult Failure(string error)
    {
        return new CommandResult(false, "", error);
    }

    private CommandResult ReportFailure(string error)
    {
        LastResponse = error;
        UnityEngine.Debug.LogError("[ServoTightening] " + error);
        return Failure(error);
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private void OnApplicationQuit()
    {
        shuttingDown = true;
        StopStatusPolling();
        StopCurvePolling();
        CloseCurvePopup();
        ReleaseCurveTexture();

        if (!IsProgramActive())
        {
            return;
        }

        int timeoutMs = Math.Max(
            500,
            (int)Math.Round(commandTimeoutSeconds * 1000f)
        );
        CommandResult stop = SendCommandBlocking("stop", timeoutMs);
        bool safeToClose = !ProgramReady;
        if (stop.Success)
        {
            ApplyBridgeStatus(stop.RawResponse);
            safeToClose =
                IsSafeStoppedState(ToolState) || !ToolConnected;

            for (int attempt = 0; !safeToClose && attempt < 25; attempt++)
            {
                Thread.Sleep(100);
                CommandResult status =
                    SendCommandBlocking("status", timeoutMs);
                if (!status.Success)
                {
                    continue;
                }

                ApplyBridgeStatus(status.RawResponse);
                safeToClose =
                    IsSafeStoppedState(ToolState) || !ToolConnected;
            }
        }

        if (ownsProgramProcess && safeToClose)
        {
            KillOwnedProcess();
        }
        else if (ownsProgramProcess)
        {
            UnityEngine.Debug.LogError(
                "[ServoTightening] Unity is exiting, but the V26 process "
                + "was kept alive because a safe tool stop was not confirmed."
            );
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        commandGate.Dispose();
    }
}
