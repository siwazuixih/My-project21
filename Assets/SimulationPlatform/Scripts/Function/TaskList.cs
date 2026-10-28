using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 任务回放列表（项目管理 - 记录）：显示当前项目的运行历史（仿真/真实）。
/// 通过 RunRecordItem 填充各单元格；提供返回按钮切回项目管理面板。
/// </summary>
public class TaskList : MonoBehaviour
{
    /// <summary>记录列表面板内容容器</summary>
    public Transform TableContent;

    /// <summary>记录项 prefab（挂载 RunRecordItem 组件）</summary>
    public GameObject ItemPrefab;

    /// <summary>刷新按钮（可空）</summary>
    public Button RefreshBtn;

    /// <summary>返回按钮：隐藏当前面板，显示项目管理面板</summary>
    public Button BackBtn;

    /// <summary>项目管理面板（返回时显示）</summary>
    public GameObject ProjectPanel;

    /// <summary>执行明细面板（加载记录项时绑定给 RunRecordItem 的 detailPanel）</summary>
    public StepDetailPanel DetailPanel;
    public Text StatusText;
    public Button PreviousPageBtn, NextPageBtn;
    private int page;
    private const int PageSize = 50;

    private Project _project;

    /// <summary>返回目标面板（运行时由 ProjectList 传入，优先于 Inspector 的 ProjectPanel）</summary>
    private GameObject _returnPanel;

    /// <summary>当前筛选条件（null=全部，SIMULATION=仿真，REALTIME=真实运行）</summary>
    private RunType? _filterMode;

    // Start is called before the first frame update
    void Start()
    {
        if (PreviousPageBtn != null) PreviousPageBtn.onClick.AddListener(() => { page--; RefreshTaskList(); });
        if (NextPageBtn != null) NextPageBtn.onClick.AddListener(() => { page++; RefreshTaskList(); });
        if (RefreshBtn != null)
        {
            RefreshBtn.onClick.AddListener(RefreshTaskList);
        }
        if (BackBtn != null)
        {
            BackBtn.onClick.AddListener(OnBackClick);
        }
    }

    /// <summary>
    /// 设置当前项目并刷新记录列表。
    /// </summary>
    /// <param name="project">当前查看记录的项目</param>
    public void SetProject(Project project)
    {
        _project = project;
        page = 0;
        RefreshTaskList();
    }

    /// <summary>
    /// 设置当前项目并记录返回目标面板（返回时恢复显示该面板）。
    /// </summary>
    /// <param name="project">当前查看记录的项目</param>
    /// <param name="returnPanel">打开记录前的面板（返回时显示）</param>
    public void SetProject(Project project, GameObject returnPanel)
    {
        _returnPanel = returnPanel;
        SetProject(project);
    }

    /// <summary>
    /// 设置筛选条件并重新查询（全部/仿真/真实运行）。
    /// </summary>
    /// <param name="mode">null=全部，SIMULATION=仿真，REALTIME=真实运行</param>
    public void SetFilterMode(RunType? mode)
    {
        _filterMode = mode;
        page = 0;
        RefreshTaskList();
    }

    /// <summary>
    /// 刷新任务记录列表（按执行时间倒序，最新在前）
    /// </summary>
    public void RefreshTaskList()
    {
        ClearTaskList();

        if (_project == null)
        {
            Debug.LogWarning("TaskList 未设置项目，无法加载运行记录");
            return;
        }

        List<RunRecord> records = _filterMode.HasValue
            ? RunRecorder.GetRecords(_project, _filterMode.Value)
            : RunRecorder.GetRecords(_project);
        page = Mathf.Clamp(page, 0, Mathf.Max(0, (records.Count - 1) / PageSize));
        if (PreviousPageBtn != null) PreviousPageBtn.interactable = page > 0;
        if (NextPageBtn != null) NextPageBtn.interactable = (page + 1) * PageSize < records.Count;
        if (StatusText != null) StatusText.text = string.IsNullOrEmpty(RunRecorder.LastError)
            ? $"{_project.Name}：{records.Count} 条记录，第{page + 1}页（每页50条）。详情不会重放设备运动。"
            : "历史存储异常：" + RunRecorder.LastError;
        for (int i = page * PageSize; i < Mathf.Min(records.Count, (page + 1) * PageSize); i++)
        {
            AddTaskItem(records[i]);
        }
        ScrollRect scroll = TableContent != null ? TableContent.GetComponentInParent<ScrollRect>(true) : null;
        if (scroll != null) scroll.verticalNormalizedPosition = 1;
    }

    /// <summary>
    /// 清空记录列表
    /// </summary>
    private void ClearTaskList()
    {
        if (TableContent != null)
        {
            foreach (Transform child in TableContent)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }
    }

    /// <summary>
    /// 添加一条运行记录到列表
    /// </summary>
    /// <param name="record">运行记录</param>
    private void AddTaskItem(RunRecord record)
    {
        if (ItemPrefab == null || TableContent == null)
        {
            Debug.LogError("TaskList 的 ItemPrefab 或 TableContent 未设置");
            return;
        }

        GameObject item = Instantiate(ItemPrefab, TableContent);
        RunHistoryPanel.LayoutRecordRow(item);
        RunRecordItem recordItem = item.GetComponent<RunRecordItem>();
        if (recordItem != null)
        {
            recordItem.Record = record;
            // 记录列表面板作为打开执行明细后的返回目标
            recordItem.SetDetailReturn(gameObject);
            // 执行明细面板由 TaskList 运行时绑定
            if (DetailPanel != null)
            {
                recordItem.detailPanel = DetailPanel;
            }
        }
        else
        {
            Debug.LogWarning("记录项 prefab 上未挂载 RunRecordItem 组件");
        }
    }

    /// <summary>
    /// 返回按钮事件：隐藏当前面板，显示项目管理面板
    /// </summary>
    private void OnBackClick()
    {
        gameObject.SetActive(false);
        GameObject target = _returnPanel != null ? _returnPanel : ProjectPanel;
        if (target != null)
        {
            target.SetActive(true);
        }
        else
        {
            Debug.LogWarning("TaskList 未绑定返回面板（ProjectPanel），请检查");
        }
    }
}
