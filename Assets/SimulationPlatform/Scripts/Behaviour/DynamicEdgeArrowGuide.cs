using UnityEngine;
using UnityEngine.UI;

public class DynamicEdgeArrowGuide : MonoBehaviour
{
    [Header("主相机")]
    public Camera mainCam;

    [Header("两个指引目标")]
    public Transform targetA;
    public Transform targetB;

    [Header("箭头样式")]
    public Sprite arrowSprite;
    public Color arrowColor = Color.cyan;
    public Vector2 arrowSize = new Vector2(32, 32);

    [Header("文字偏移（相对箭头右侧）")]
    public Vector2 textOffset = new Vector2(28, 0);

    [Header("避让UI 四边边距(像素)")]
    public int leftUIMargin = 60;
    public int rightUIMargin = 60;
    public int topUIMargin = 80;
    public int bottomUIMargin = 80;

    [Header("重叠判定 / 视野 / 透明")]
    public float overlapThreshold = 70f;
    public float inViewMargin = 0.15f;
    public float maxFadeDistance = 50f;

    private Canvas _canvas;
    private Image _arrowA, _arrowB;
    private Text _textA, _textB;

    private float safeMinX, safeMaxX;
    private float safeMinY, safeMaxY;

    void Start()
    {
        CreateDynamicUI();
    }

    void Update()
    {
        if (arrowSprite == null || mainCam == null || targetA == null || targetB == null)
            return;

        CalcSafeRect();

        TargetData dataA = GetTargetData(targetA);
        TargetData dataB = GetTargetData(targetB);

        float pixelDist = Vector2.Distance(dataA.screenPos, dataB.screenPos);
        bool isConflict = pixelDist < overlapThreshold;

        if (isConflict)
        {
            ShowOnlyCloserOne(dataA, dataB);
        }
        else
        {
            UpdateSingle(_arrowA, _textA, dataA);
            UpdateSingle(_arrowB, _textB, dataB);
        }
    }

    void CalcSafeRect()
    {
        safeMinX = leftUIMargin;
        safeMaxX = Screen.width - rightUIMargin;
        safeMinY = bottomUIMargin;
        safeMaxY = Screen.height - topUIMargin;
    }

    #region 动态生成Canvas、箭头、文字
    void CreateDynamicUI()
    {
        GameObject canvasObj = new GameObject("DynamicArrowCanvas");
        _canvas = canvasObj.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        DontDestroyOnLoad(canvasObj);

        _arrowA = CreateArrow("ArrowA");
        _textA = CreateText(_arrowA.transform, "TextA");

        _arrowB = CreateArrow("ArrowB");
        _textB = CreateText(_arrowB.transform, "TextB");
    }

    Image CreateArrow(string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(_canvas.transform, false);
        Image img = go.AddComponent<Image>();
        img.sprite = arrowSprite;
        img.color = arrowColor;
        RectTransform rt = img.GetComponent<RectTransform>();
        rt.sizeDelta = arrowSize;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        return img;
    }

    Text CreateText(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 16;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleLeft;

        RectTransform rt = t.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = textOffset;
        rt.sizeDelta = new Vector2(120, 30);
        return t;
    }
    #endregion

    TargetData GetTargetData(Transform t)
    {
        TargetData data = new TargetData();
        data.trans = t;
        data.objName = t.name;
        data.worldPos = t.position;

        data.screenPos = mainCam.WorldToScreenPoint(data.worldPos);
        data.distance = Vector3.Distance(mainCam.transform.position, data.worldPos);
        data.isBack = data.screenPos.z < 0;
        data.isInView = !data.isBack && CheckInView(data.screenPos);

        if (data.isBack)
        {
            Vector3 dir = (data.worldPos - mainCam.transform.position).normalized;
            data.screenPos = new Vector3(Screen.width * 0.5f + dir.x * Screen.width * 0.4f,
                                         Screen.height * 0.5f - dir.y * Screen.height * 0.4f, 0);
        }
        else
        {
            data.screenPos.z = 0;
        }

        return data;
    }

    bool CheckInView(Vector3 screenPos)
    {
        float nx = screenPos.x / Screen.width;
        float ny = screenPos.y / Screen.height;
        return nx > inViewMargin && nx < 1f - inViewMargin &&
               ny > inViewMargin && ny < 1f - inViewMargin;
    }

    void ShowOnlyCloserOne(TargetData a, TargetData b)
    {
        if (a.distance <= b.distance)
        {
            UpdateSingle(_arrowA, _textA, a);
            SetActiveAll(_arrowB, _textB, false);
        }
        else
        {
            UpdateSingle(_arrowB, _textB, b);
            SetActiveAll(_arrowA, _textA, false);
        }
    }

    void UpdateSingle(Image arrow, Text txt, TargetData data)
    {
        if (data.isInView)
        {
            SetActiveAll(arrow, txt, false);
            return;
        }

        SetActiveAll(arrow, txt, true);

        Vector3 screenCenter = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0);
        Vector3 direction = data.screenPos - screenCenter;
        direction.z = 0;

        if (direction.magnitude < 1f)
        {
            direction = Vector3.right;
        }

        direction.Normalize();

        float ratioX = (Screen.width * 0.5f - safeMinX) / Mathf.Max(Mathf.Abs(direction.x), 0.001f);
        float ratioY = (Screen.height * 0.5f - safeMinY) / Mathf.Max(Mathf.Abs(direction.y), 0.001f);
        float ratio = Mathf.Min(ratioX, ratioY);

        Vector3 edgePos = screenCenter + direction * ratio;
        edgePos.x = Mathf.Clamp(edgePos.x, safeMinX, safeMaxX);
        edgePos.y = Mathf.Clamp(edgePos.y, safeMinY, safeMaxY);
        edgePos.z = 0;

        arrow.rectTransform.anchoredPosition = edgePos;

        float angle = Mathf.Atan2(-direction.y, -direction.x) * Mathf.Rad2Deg;
        arrow.rectTransform.rotation = Quaternion.Euler(0, 0, angle);

        Vector3 textPos = edgePos + new Vector3(direction.x * textOffset.x, direction.y * textOffset.x, 0);
        txt.rectTransform.anchoredPosition = textPos;

        float alpha = Mathf.Clamp01(1f - data.distance / maxFadeDistance);
        SetAlpha(arrow, txt, alpha);

        txt.text = data.objName;
    }

    void SetActiveAll(Image arrow, Text txt, bool active)
    {
        arrow.gameObject.SetActive(active);
        txt.gameObject.SetActive(active);
    }

    void SetAlpha(Image img, Text txt, float alpha)
    {
        Color c = img.color; c.a = alpha; img.color = c;
        Color tc = txt.color; tc.a = alpha; txt.color = tc;
    }

    private struct TargetData
    {
        public Transform trans;
        public string objName;
        public Vector3 worldPos;
        public Vector3 screenPos;
        public float distance;
        public bool isBack;
        public bool isInView;
    }
}