using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 运行记录执行明细面板：展示某条运行记录的步骤明细（StepCellItem 列表），
/// 以及运行时间（TimeText）与耗时（SpanText）；提供返回按钮回到之前的面板。
/// </summary>
public class StepDetailPanel : MonoBehaviour
{
    /// <summary>步骤明细列表内容容器</summary>
    public Transform TableContent;

    /// <summary>步骤项 prefab（挂载 StepCellItem 组件）</summary>
    public GameObject StepItemPrefab;

    /// <summary>显示记录运行时间</summary>
    public Text TimeText;

    /// <summary>显示耗时</summary>
    public Text SpanText;

    /// <summary>返回按钮：隐藏当前面板，显示之前的面板</summary>
    public Button BackBtn;

    /// <summary>返回目标面板（运行时传入，优先于 Inspector 绑定）</summary>
    private GameObject _returnPanel;

    void Start()
    {
        if (BackBtn != null)
        {
            BackBtn.onClick.AddListener(OnBackClick);
        }
    }

    /// <summary>
    /// 打开执行明细面板并加载运行记录步骤。
    /// </summary>
    /// <param name="record">运行记录</param>
    /// <param name="returnPanel">返回时显示的面板</param>
    public void Open(RunRecord record, GameObject returnPanel)
    {
        _returnPanel = returnPanel;
        ClearTable();

        if (record == null)
        {
            return;
        }

        if (TimeText != null)
        {
            TimeText.text = record.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
        if (SpanText != null)
        {
            SpanText.text = $"{record.DurationSeconds:F1} 秒 | {record.Source} | {record.Result}（完整说明见首行）";
        }

        AddStepItem(new RunStepRecord(0, "运行摘要") { Result = record.Result,
            Detail = $"{record.ResultReason}\n{record.RealParamsSummary}\n报告目录：{record.ReportDirectory}" }, 0);

        if (record.Steps != null)
        {
            for (int i = 0; i < record.Steps.Count; i++)
            {
                AddStepItem(record.Steps[i], i + 1);
            }
        }

        gameObject.SetActive(true);
    }

    /// <summary>
    /// 添加一条步骤到列表（序号递增）。
    /// </summary>
    private void AddStepItem(RunStepRecord step, int number)
    {
        if (StepItemPrefab == null || TableContent == null)
        {
            Debug.LogError("StepDetailPanel 的 StepItemPrefab 或 TableContent 未设置");
            return;
        }

        GameObject item = Instantiate(StepItemPrefab, TableContent);
        StepCellItem cellItem = item.GetComponent<StepCellItem>();
        if (cellItem != null)
        {
            cellItem.SetData(step, number);
            // 生成时手动赋值序号
            cellItem.SetNumber(number);
            if (number == 0 && cellItem.NumCell != null) cellItem.NumCell.text = "—";
            RunHistoryPanel.LayoutStepRow(item);
        }
        else
        {
            Debug.LogWarning("步骤项 prefab 上未挂载 StepCellItem 组件");
        }
    }

    private void ClearTable()
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
    /// 返回按钮事件：隐藏当前面板，显示之前的面板。
    /// </summary>
    private void OnBackClick()
    {
        gameObject.SetActive(false);
        if (_returnPanel != null)
        {
            _returnPanel.SetActive(true);
        }
        else
        {
            Debug.LogWarning("StepDetailPanel 未设置返回面板（returnPanel）");
        }
    }
}
