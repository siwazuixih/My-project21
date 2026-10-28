using UnityEngine;
using UnityEngine.UI;

// Scene-free adapter for wlj's TaskList/StepDetailPanel and original cell prefabs.
// UI creation is on demand, once per management scene. No Update/Find or hardware commands.
public static class RunHistoryPanel
{
    private static TaskList list;
    private static Font font;

    public static void Open(Project project, ProjectList source)
    {
        if (project == null || source == null) return;
        Canvas canvas = source.GetComponentInParent<Canvas>();
        if (canvas == null) { Debug.LogError("项目列表缺少Canvas，无法打开运行记录"); return; }
        Transform host = source.transform.parent != null ? source.transform.parent : canvas.transform;
        if (list == null || list.transform.parent != host) Build(canvas, source);
        if (list == null) return;
        list.gameObject.SetActive(true);
        list.transform.SetAsLastSibling();
        list.DetailPanel.gameObject.SetActive(false);
        list.SetProject(project, source.gameObject);
        source.gameObject.SetActive(false);
    }

    private static void Build(Canvas canvas, ProjectList source)
    {
        // Stay inside ProjectTabPanel so existing menu switching hides this UI with ProjectManage.
        Transform host = source.transform.parent != null ? source.transform.parent : canvas.transform;
        GameObject row = Resources.Load<GameObject>("Run/History/TaskCell");
        GameObject step = Resources.Load<GameObject>("Run/History/StepCell");
        if (row == null || step == null) { Debug.LogError("运行历史prefab资源缺失"); return; }
        font = null;
        foreach (var text in source.GetComponentsInChildren<Text>(true))
            if (text.font != null && text.font.HasCharacter('中')) { font = text.font; break; }
        if (font == null)
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Font>())
                if (candidate.HasCharacter('中')) { font = candidate; break; }
        if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Noto Sans CJK SC", "Microsoft YaHei", "WenQuanYi Zen Hei" }, 18);

        GameObject root = Panel("ProjectRunHistory", host);
        list = root.AddComponent<TaskList>();
        list.ItemPrefab = row;
        Label(root.transform, "项目运行记录", 24, new Vector2(.025f, .88f), new Vector2(.46f, .98f));
        list.BackBtn = Button(root.transform, "返回项目", .85f, .92f, .98f, .98f);
        list.RefreshBtn = Button(root.transform, "刷新", .70f, .92f, .83f, .98f);
        list.StatusText = Label(root.transform, "", 16, new Vector2(.025f, .78f), new Vector2(.98f, .85f));
        RunTypeChose filters = root.AddComponent<RunTypeChose>(); filters.taskList = list;
        filters.AllBtn = Button(root.transform, "全部", .025f, .85f, .15f, .91f);
        filters.SimBtn = Button(root.transform, "仿真/离线模拟", .17f, .85f, .36f, .91f);
        filters.RealBtn = Button(root.transform, "真实运行", .38f, .85f, .53f, .91f);
        list.PreviousPageBtn = Button(root.transform, "上一页", .69f, .85f, .82f, .91f);
        list.NextPageBtn = Button(root.transform, "下一页", .85f, .85f, .98f, .91f);
        list.TableContent = Table(root.transform, "类型 / 时间 / 结果 / 摘要 / 耗时 / 详情", 1640);

        GameObject detailRoot = Panel("ProjectRunStepDetails", host);
        StepDetailPanel detail = detailRoot.AddComponent<StepDetailPanel>();
        detail.StepItemPrefab = step;
        detail.TimeText = Label(detailRoot.transform, "", 20, new Vector2(.025f, .90f), new Vector2(.65f, .98f));
        detail.SpanText = Label(detailRoot.transform, "", 16, new Vector2(.025f, .78f), new Vector2(.98f, .90f));
        detail.BackBtn = Button(detailRoot.transform, "返回记录", .85f, .92f, .98f, .98f);
        detail.TableContent = Table(detailRoot.transform, "序号 / 目标或阶段 / 力矩(Nm) / 角度(°) / 结果及说明", 1280);
        list.DetailPanel = detail;
        detailRoot.SetActive(false);
        root.SetActive(false);
    }

    private static GameObject Panel(string name, Transform parent)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Image)); root.transform.SetParent(parent, false);
        Place(root.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        root.GetComponent<Image>().color = new Color32(14, 29, 48, 255);
        return root;
    }
    private static void Place(RectTransform rect, Vector2 min, Vector2 max)
    { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; }
    private static Text Label(Transform parent, string value, int size, Vector2 min, Vector2 max)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
        Place(go.GetComponent<RectTransform>(), min, max);
        Text text = go.GetComponent<Text>(); text.font = font; text.fontSize = size; text.text = value;
        text.color = new Color32(224, 242, 254, 255); text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap;
        return text;
    }
    private static Button Button(Transform parent, string value, float x, float y, float right, float top)
    {
        var go = new GameObject(value, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
        Place(go.GetComponent<RectTransform>(), new Vector2(x, y), new Vector2(right, top));
        go.GetComponent<Image>().color = new Color32(23, 92, 150, 255);
        Text label = Label(go.transform, value, 18, Vector2.zero, Vector2.one); label.alignment = TextAnchor.MiddleCenter;
        return go.GetComponent<Button>();
    }
    private static Transform Table(Transform root, string header, float minWidth)
    {
        Label(root, header, 17, new Vector2(.025f, .72f), new Vector2(.98f, .78f));
        var go = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect)); go.transform.SetParent(root, false);
        Place(go.GetComponent<RectTransform>(), new Vector2(.025f, .035f), new Vector2(.98f, .715f));
        go.GetComponent<Image>().color = new Color32(10, 22, 38, 255);
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)); viewport.transform.SetParent(go.transform, false);
        Place(viewport.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var rect = content.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = new Vector2(minWidth, 0);
        var layout = content.GetComponent<VerticalLayoutGroup>(); layout.spacing = 6; layout.childControlWidth = true;
        layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = go.GetComponent<ScrollRect>(); scroll.viewport = viewport.GetComponent<RectTransform>(); scroll.content = rect;
        scroll.horizontal = true; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
        // Horizontal scroll is available for narrow displays; Shift+wheel is not required (drag the table).
        var bar = new GameObject("VerticalScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar)); bar.transform.SetParent(go.transform, false);
        Place(bar.GetComponent<RectTransform>(), new Vector2(.99f, 0), Vector2.one);
        bar.GetComponent<Image>().color = new Color32(30, 49, 70, 255);
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)); handle.transform.SetParent(bar.transform, false);
        Place(handle.GetComponent<RectTransform>(), Vector2.zero, Vector2.one); handle.GetComponent<Image>().color = new Color32(75, 137, 186, 255);
        var scrollbar = bar.GetComponent<Scrollbar>(); scrollbar.handleRect = handle.GetComponent<RectTransform>();
        scrollbar.targetGraphic = handle.GetComponent<Image>(); scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = scrollbar;
        return rect;
    }

    private static void PrepareRow(GameObject item, float height)
    {
        var layout = item.GetComponent<HorizontalLayoutGroup>(); if (layout != null) layout.enabled = false;
        var size = item.AddComponent<LayoutElement>(); size.preferredHeight = height; size.minHeight = height;
        foreach (Text text in item.GetComponentsInChildren<Text>(true))
        { if (font != null) text.font = font; text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; }
    }
    private static void Cell(Text text, float left, float right)
    {
        if (text == null) return;
        Place((RectTransform)text.transform.parent, new Vector2(left, 0), new Vector2(right, 1));
        Place(text.rectTransform, new Vector2(.025f, .05f), new Vector2(.97f, .95f));
    }
    public static void LayoutRecordRow(GameObject item)
    {
        PrepareRow(item, 112);
        var row = item.GetComponent<RunRecordItem>(); if (row == null) return;
        Cell(row.TaskTypeCell, 0, .09f); Cell(row.TimeCell, .09f, .22f); Cell(row.ResultCell, .22f, .39f);
        Cell(row.SummaryCell, .39f, .83f); Cell(row.SpendCell, .83f, .90f);
        if (row.TaskTypeCell != null) row.TaskTypeCell.rectTransform.offsetMin = new Vector2(32, 0);
        if (row.ResultCell != null) Place(row.ResultCell.rectTransform, new Vector2(.02f, .60f), new Vector2(.98f, .98f));
        if (row.WhyCell != null) Place(row.WhyCell.rectTransform, new Vector2(.02f, .02f), new Vector2(.98f, .60f));
        foreach (Image icon in new[] { row.SuccessIcon, row.CollideIcon, row.StopIcon }) if (icon != null) icon.gameObject.SetActive(false);
        if (row.DetailBtn != null)
        {
            Place((RectTransform)row.DetailBtn.transform.parent, new Vector2(.90f, 0), Vector2.one);
            Text label = row.DetailBtn.GetComponentInChildren<Text>(true);
            if (label != null) label.text = "查看详情";
        }
    }
    public static void LayoutStepRow(GameObject item)
    {
        PrepareRow(item, 108);
        var row = item.GetComponent<StepCellItem>(); if (row == null) return;
        Cell(row.NumCell, 0, .05f); Cell(row.NameCell, .05f, .29f); Cell(row.TorqueCell, .29f, .39f);
        Cell(row.AngleCell, .39f, .49f); Cell(row.ResultCell, .49f, 1);
        if (row.ResultCell != null)
        {
            var size = item.GetComponent<LayoutElement>();
            size.preferredHeight = Mathf.Max(size.preferredHeight, row.ResultCell.preferredHeight * 1.5f + 24);
        }
    }
}
