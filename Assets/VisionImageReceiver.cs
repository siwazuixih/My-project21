using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityScene = UnityEngine.SceneManagement.Scene;

public sealed class VisionImageReceiver : MonoBehaviour
{
    private const string LiveImageObjectName = "RealSenseLiveImage";
    private const string VideoToggleButtonName = "VisionVideoToggleButton";
    private const string LogToggleButtonName = "RuntimeConsoleToggleButton";
    private const string VideoLabel = "现场视频";

    [SerializeField] private string imageUrl = "http://127.0.0.1:8080/latest.jpg";
    [SerializeField] private string pythonExecutable = "/usr/bin/python3";
    [SerializeField] private string serverRelativePath =
        "ExternalCode/realsense_image_server.py";
    [SerializeField, Range(0.05f, 2f)] private float refreshIntervalSeconds = 0.05f;
    [SerializeField, Range(1f, 10f)] private float requestTimeoutSeconds = 3f;

    private Process serverProcess;
    private Coroutine bindCoroutine;
    private Coroutine streamCoroutine;
    private RawImage targetImage;
    private GameObject videoPopupOverlay;
    private RawImage videoPopupImage;
    private Texture2D latestTexture;
    private Button videoToggleButton;
    private Image videoToggleButtonBackground;
    private TextMeshProUGUI videoToggleButtonText;
    private GameObject statusRealObject;
    private bool requestRunning;
    private bool receivedFirstFrame;
    private bool videoEnabled;
    private bool shuttingDown;
    private float nextErrorLogTime;
    private readonly Color videoOffButtonColor =
        new Color(0.05f, 0.35f, 0.65f, 0.92f);
    private readonly Color videoOnButtonColor =
        new Color(0.02f, 0.55f, 0.75f, 0.96f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateReceiver()
    {
        if (FindObjectOfType<VisionImageReceiver>() != null)
        {
            return;
        }

        GameObject receiverObject = new GameObject("Vision Image Receiver");
        DontDestroyOnLoad(receiverObject);
        receiverObject.AddComponent<VisionImageReceiver>();
    }

    private void Awake()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        BeginBinding(SceneManager.GetActiveScene());
    }

    private void HandleSceneLoaded(UnityScene scene, LoadSceneMode _mode)
    {
        BeginBinding(scene);
    }

    private void Update()
    {
        UpdateVideoButtonVisibility();
        if (
            videoPopupOverlay != null
            && videoPopupOverlay.activeSelf
            && Input.GetKeyDown(KeyCode.Escape)
        )
        {
            CloseVideoPopup();
        }
    }

    private void BeginBinding(UnityScene scene)
    {
        StopVideo();
        CloseVideoPopup();
        targetImage = null;
        videoToggleButton = null;
        videoToggleButtonBackground = null;
        videoToggleButtonText = null;
        statusRealObject = null;

        if (bindCoroutine != null)
        {
            StopCoroutine(bindCoroutine);
        }

        bindCoroutine = StartCoroutine(BindWhenReady(scene));
    }

    private IEnumerator BindWhenReady(UnityScene scene)
    {
        const float timeoutSeconds = 15f;
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (TryBindReservedVideoArea(scene))
            {
                CreateVideoToggleButton(scene);
                UpdateVideoButtonVisibility();
                bindCoroutine = null;
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.25f);
        }

        bindCoroutine = null;
        UnityEngine.Debug.Log(
            $"[VisionImage] Scene '{scene.name}' has no '{VideoLabel}' video area; "
            + "RealSense preview was not started."
        );
    }

    private bool TryBindReservedVideoArea(UnityScene scene)
    {
        Text[] labels = FindObjectsOfType<Text>(true);
        foreach (Text label in labels)
        {
            if (label.gameObject.scene != scene || label.text.Trim() != VideoLabel)
            {
                continue;
            }

            Transform container = label.transform;
            Image overlay = FindCameraOverlay(container);
            if (overlay == null)
            {
                continue;
            }

            Transform existing = container.Find(LiveImageObjectName);
            if (existing != null)
            {
                targetImage = existing.GetComponent<RawImage>();
            }
            else
            {
                targetImage = CreateLiveImage(container, overlay, label.font);
            }

            if (targetImage == null)
            {
                continue;
            }

            targetImage.raycastTarget = true;
            VisionVideoPointerHandler pointerHandler =
                targetImage.GetComponent<VisionVideoPointerHandler>();
            if (pointerHandler == null)
            {
                pointerHandler =
                    targetImage.gameObject.AddComponent<VisionVideoPointerHandler>();
            }
            pointerHandler.Initialize(this, true);
            targetImage.gameObject.SetActive(false);
            UnityEngine.Debug.Log(
                $"[VisionImage] Bound RealSense preview to "
                + $"'{GetHierarchyPath(container)}/{LiveImageObjectName}'."
            );
            return true;
        }

        return false;
    }

    private static Image FindCameraOverlay(Transform container)
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

    private static RawImage CreateLiveImage(
        Transform container,
        Image overlay,
        Font labelFont
    )
    {
        GameObject liveImageObject = new GameObject(
            LiveImageObjectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage)
        );
        RectTransform liveRect = liveImageObject.GetComponent<RectTransform>();
        RectTransform overlayRect = overlay.rectTransform;

        liveRect.SetParent(container, false);
        liveRect.anchorMin = overlayRect.anchorMin;
        liveRect.anchorMax = overlayRect.anchorMax;
        liveRect.anchoredPosition = overlayRect.anchoredPosition;
        liveRect.sizeDelta = new Vector2(
            Mathf.Max(1f, overlayRect.sizeDelta.x - 2f),
            Mathf.Max(1f, overlayRect.sizeDelta.y - 2f)
        );
        liveRect.pivot = overlayRect.pivot;
        liveRect.localRotation = overlayRect.localRotation;
        liveRect.localScale = overlayRect.localScale;
        liveRect.SetSiblingIndex(overlay.transform.GetSiblingIndex() + 1);

        RawImage rawImage = liveImageObject.GetComponent<RawImage>();
        rawImage.color = Color.black;
        rawImage.raycastTarget = true;
        CreateLiveBadge(liveImageObject.transform, labelFont);
        return rawImage;
    }

    private static void CreateLiveBadge(Transform parent, Font font)
    {
        GameObject badgeObject = new GameObject(
            "RealSenseLiveBadge",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        RectTransform badgeRect = badgeObject.GetComponent<RectTransform>();
        badgeRect.SetParent(parent, false);
        badgeRect.anchorMin = new Vector2(1f, 1f);
        badgeRect.anchorMax = new Vector2(1f, 1f);
        badgeRect.pivot = new Vector2(1f, 1f);
        badgeRect.anchoredPosition = new Vector2(-4f, -4f);
        badgeRect.sizeDelta = new Vector2(42f, 15f);

        Image badgeBackground = badgeObject.GetComponent<Image>();
        badgeBackground.color = new Color(0.02f, 0.04f, 0.08f, 0.9f);
        badgeBackground.raycastTarget = false;

        GameObject textObject = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Text)
        );
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(badgeObject.transform, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text badgeText = textObject.GetComponent<Text>();
        badgeText.text = "● LIVE";
        badgeText.font = font;
        badgeText.fontSize = 9;
        badgeText.alignment = TextAnchor.MiddleCenter;
        badgeText.color = new Color(1f, 0.08f, 0.14f, 1f);
        badgeText.raycastTarget = false;
    }

    public void ToggleVideoPopup()
    {
        if (
            latestTexture == null
            || targetImage == null
            || !videoEnabled
        )
        {
            return;
        }

        if (
            videoPopupOverlay != null
            && videoPopupOverlay.activeSelf
        )
        {
            CloseVideoPopup();
            return;
        }

        EnsureVideoPopupCreated();
        if (videoPopupOverlay == null || videoPopupImage == null)
        {
            return;
        }

        videoPopupImage.texture = latestTexture;
        videoPopupImage.color = Color.white;
        videoPopupOverlay.SetActive(true);
        videoPopupOverlay.transform.SetAsLastSibling();
    }

    public void CloseVideoPopup()
    {
        if (videoPopupImage != null)
        {
            videoPopupImage.texture = null;
            videoPopupImage.color = Color.black;
        }

        if (videoPopupOverlay != null)
        {
            videoPopupOverlay.SetActive(false);
        }
    }

    private void EnsureVideoPopupCreated()
    {
        if (videoPopupOverlay != null || targetImage == null)
        {
            return;
        }

        Canvas canvas = targetImage.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            return;
        }
        canvas = canvas.rootCanvas;

        videoPopupOverlay = new GameObject(
            "VisionVideoPopupOverlay",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        videoPopupOverlay.layer = targetImage.gameObject.layer;
        RectTransform overlayRect =
            videoPopupOverlay.GetComponent<RectTransform>();
        overlayRect.SetParent(canvas.transform, false);
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        Image backdrop = videoPopupOverlay.GetComponent<Image>();
        backdrop.color = new Color(0f, 0.025f, 0.07f, 0.82f);
        backdrop.raycastTarget = true;
        VisionVideoPointerHandler backdropHandler =
            videoPopupOverlay.AddComponent<VisionVideoPointerHandler>();
        backdropHandler.Initialize(this, false, true);

        GameObject panelObject = new GameObject(
            "VisionVideoPopupPanel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        panelObject.layer = videoPopupOverlay.layer;
        RectTransform panelRect =
            panelObject.GetComponent<RectTransform>();
        panelRect.SetParent(videoPopupOverlay.transform, false);
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
        float videoWidth = Mathf.Max(320f, panelWidth - 32f);
        float panelHeight = videoWidth * 9f / 16f + 72f;
        if (panelHeight > availableHeight * 0.86f)
        {
            panelHeight = availableHeight * 0.86f;
            videoWidth = Mathf.Max(
                320f,
                (panelHeight - 72f) * 16f / 9f
            );
            panelWidth = videoWidth + 32f;
        }
        panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

        Image panelBackground = panelObject.GetComponent<Image>();
        panelBackground.color = new Color(0.035f, 0.11f, 0.21f, 1f);
        panelBackground.raycastTarget = true;

        TMP_FontAsset yaHeiFont = FindMicrosoftYaHeiFont();
        CreateVideoPopupTitle(panelObject.transform, yaHeiFont);
        CreateVideoPopupCloseButton(panelObject.transform, yaHeiFont);

        GameObject imageObject = new GameObject(
            "LargeVisionVideoImage",
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

        videoPopupImage = imageObject.GetComponent<RawImage>();
        videoPopupImage.color = Color.black;
        videoPopupImage.raycastTarget = true;
        VisionVideoPointerHandler imageHandler =
            imageObject.AddComponent<VisionVideoPointerHandler>();
        imageHandler.Initialize(this, true);

        videoPopupOverlay.SetActive(false);
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

        return videoToggleButtonText != null
            ? videoToggleButtonText.font
            : null;
    }

    private static void CreateVideoPopupTitle(
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
        title.text = "现场视频（双击缩小）";
        title.fontSize = 24f;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.color = new Color(0.49f, 0.83f, 0.99f, 1f);
        title.raycastTarget = false;
        if (font != null)
        {
            title.font = font;
        }
    }

    private void CreateVideoPopupCloseButton(
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
        closeButton.onClick.AddListener(CloseVideoPopup);

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

    private void CreateVideoToggleButton(UnityScene scene)
    {
        statusRealObject = FindSceneObject(scene, "StatusReal");
        GameObject logButtonObject = FindSceneObject(scene, LogToggleButtonName);
        if (statusRealObject == null || logButtonObject == null)
        {
            UnityEngine.Debug.LogWarning(
                "[VisionImage] Cannot create video button: "
                + "StatusReal or log toggle button was not found."
            );
            return;
        }

        RectTransform logButtonRect =
            logButtonObject.GetComponent<RectTransform>();
        if (logButtonRect == null || logButtonRect.parent == null)
        {
            UnityEngine.Debug.LogWarning(
                "[VisionImage] Cannot create video button: "
                + "log toggle button has no RectTransform parent."
            );
            return;
        }

        Transform existing = logButtonRect.parent.Find(VideoToggleButtonName);
        GameObject buttonObject;
        if (existing != null)
        {
            buttonObject = existing.gameObject;
        }
        else
        {
            buttonObject = new GameObject(
                VideoToggleButtonName,
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
                logButtonRect.anchoredPosition + new Vector2(120f, 0f);
            buttonRect.sizeDelta = logButtonRect.sizeDelta;
            buttonRect.localRotation = logButtonRect.localRotation;
            buttonRect.localScale = logButtonRect.localScale;

            CreateVideoButtonText(
                buttonObject.transform,
                logButtonObject.GetComponentInChildren<TextMeshProUGUI>(true)
            );
        }

        videoToggleButton = buttonObject.GetComponent<Button>();
        videoToggleButtonBackground = buttonObject.GetComponent<Image>();
        videoToggleButtonText =
            buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);

        Button logButton = logButtonObject.GetComponent<Button>();
        if (videoToggleButton != null)
        {
            if (logButton != null)
            {
                videoToggleButton.colors = logButton.colors;
                videoToggleButton.transition = logButton.transition;
            }

            videoToggleButton.targetGraphic = videoToggleButtonBackground;
            videoToggleButton.onClick.RemoveListener(ToggleVideo);
            videoToggleButton.onClick.AddListener(ToggleVideo);
            videoToggleButton.transform.SetAsLastSibling();
        }

        SetVideoButtonState(false);
        UnityEngine.Debug.Log(
            "[VisionImage] Video toggle button created next to log button."
        );
    }

    private static void CreateVideoButtonText(
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

    private void UpdateVideoButtonVisibility()
    {
        if (videoToggleButton == null || statusRealObject == null)
        {
            return;
        }

        bool realUiVisible = statusRealObject.activeInHierarchy;
        if (videoToggleButton.gameObject.activeSelf != realUiVisible)
        {
            videoToggleButton.gameObject.SetActive(realUiVisible);
        }

        if (!realUiVisible && videoEnabled)
        {
            StopVideo();
        }
    }

    public void ToggleVideo()
    {
        if (videoEnabled)
        {
            StopVideo();
        }
        else
        {
            StartVideo();
        }
    }

    public void StartVideo()
    {
        if (
            videoEnabled
            || targetImage == null
            || statusRealObject == null
            || !statusRealObject.activeInHierarchy
        )
        {
            return;
        }

        videoEnabled = true;
        receivedFirstFrame = false;
        nextErrorLogTime = 0f;
        targetImage.texture = null;
        targetImage.color = Color.black;
        targetImage.gameObject.SetActive(true);
        SetVideoButtonState(true);
        EnsureServerStarted();
        EnsureStreamingStarted();
        UnityEngine.Debug.Log(
            "[VisionImage] Video enabled; starting RealSense camera."
        );
    }

    public void StopVideo()
    {
        bool wasRunning =
            videoEnabled
            || streamCoroutine != null
            || (serverProcess != null && !serverProcess.HasExited);
        videoEnabled = false;

        if (streamCoroutine != null)
        {
            StopCoroutine(streamCoroutine);
            streamCoroutine = null;
        }

        requestRunning = false;
        receivedFirstFrame = false;
        CloseVideoPopup();
        ReleaseLatestTexture();

        if (targetImage != null)
        {
            targetImage.texture = null;
            targetImage.color = Color.black;
            targetImage.gameObject.SetActive(false);
        }

        StopServerProcess();
        SetVideoButtonState(false);

        if (wasRunning)
        {
            UnityEngine.Debug.Log(
                "[VisionImage] Video disabled; RealSense camera released."
            );
        }
    }

    private void SetVideoButtonState(bool enabled)
    {
        if (videoToggleButtonText != null)
        {
            videoToggleButtonText.text = enabled ? "关闭视频" : "开启视频";
        }

        if (videoToggleButtonBackground != null)
        {
            videoToggleButtonBackground.color =
                enabled ? videoOnButtonColor : videoOffButtonColor;
        }
    }

    private void EnsureServerStarted()
    {
        if (serverProcess != null && !serverProcess.HasExited)
        {
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
        {
            UnityEngine.Debug.LogError("[VisionImage] Cannot resolve project root.");
            return;
        }

        string scriptPath = Path.Combine(projectRoot, serverRelativePath);
        if (!File.Exists(scriptPath))
        {
            UnityEngine.Debug.LogError(
                $"[VisionImage] RealSense server script not found: {scriptPath}"
            );
            return;
        }

        try
        {
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
            serverProcess = Process.Start(startInfo);
            UnityEngine.Debug.Log("[VisionImage] RealSense HTTP server started.");
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogError(
                "[VisionImage] Failed to start RealSense server: "
                + exception.Message
            );
        }
    }

    private void EnsureStreamingStarted()
    {
        if (streamCoroutine == null)
        {
            streamCoroutine = StartCoroutine(StreamFrames());
        }
    }

    private IEnumerator StreamFrames()
    {
        yield return new WaitForSecondsRealtime(1f);

        while (!shuttingDown && videoEnabled)
        {
            if (targetImage != null && !requestRunning)
            {
                yield return FetchFrame();
            }

            yield return new WaitForSecondsRealtime(
                Mathf.Max(0.05f, refreshIntervalSeconds)
            );
        }

        streamCoroutine = null;
    }

    private IEnumerator FetchFrame()
    {
        requestRunning = true;

        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageUrl, false))
        {
            request.timeout = Mathf.Max(1, Mathf.RoundToInt(requestTimeoutSeconds));
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (!videoEnabled)
                {
                    requestRunning = false;
                    yield break;
                }

                Texture2D previousTexture = latestTexture;
                latestTexture = DownloadHandlerTexture.GetContent(request);

                if (targetImage != null)
                {
                    targetImage.texture = latestTexture;
                    targetImage.color = Color.white;
                }
                if (
                    videoPopupImage != null
                    && videoPopupOverlay != null
                    && videoPopupOverlay.activeSelf
                )
                {
                    videoPopupImage.texture = latestTexture;
                    videoPopupImage.color = Color.white;
                }

                if (previousTexture != null)
                {
                    Destroy(previousTexture);
                }

                if (!receivedFirstFrame)
                {
                    receivedFirstFrame = true;
                    string source = request.GetResponseHeader("X-Image-Source");
                    UnityEngine.Debug.Log(
                        $"[VisionImage] First frame received: "
                        + $"{latestTexture.width}x{latestTexture.height}, "
                        + $"source={source ?? "unknown"}."
                    );
                    SetVideoButtonState(true);
                }
            }
            else if (
                videoEnabled
                && Time.realtimeSinceStartup >= nextErrorLogTime
            )
            {
                UnityEngine.Debug.LogWarning(
                    "[VisionImage] Waiting for RealSense frame: " + request.error
                );
                nextErrorLogTime = Time.realtimeSinceStartup + 2f;
            }
        }

        requestRunning = false;
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

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private void ReleaseLatestTexture()
    {
        if (latestTexture == null)
        {
            return;
        }

        Destroy(latestTexture);
        latestTexture = null;
        if (videoPopupImage != null)
        {
            videoPopupImage.texture = null;
            videoPopupImage.color = Color.black;
        }
    }

    private void StopServerProcess()
    {
        if (serverProcess == null)
        {
            return;
        }

        try
        {
            if (!serverProcess.HasExited)
            {
                serverProcess.Kill();
                serverProcess.WaitForExit(2000);
            }
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning(
                "[VisionImage] Failed to stop RealSense server: "
                + exception.Message
            );
        }
        finally
        {
            serverProcess.Dispose();
            serverProcess = null;
        }
    }

    private void Shutdown()
    {
        if (shuttingDown)
        {
            return;
        }

        shuttingDown = true;
        StopVideo();
    }

    private void OnApplicationQuit()
    {
        Shutdown();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Shutdown();
    }
}
