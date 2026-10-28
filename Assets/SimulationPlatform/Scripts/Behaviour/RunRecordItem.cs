using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 项目运行记录列表项实体类（挂载在记录列表 prefab 上）。
/// 包含任务类型/时间/结果图标/原因/关键信息摘要/耗时 各单元格控件，
/// 绑定 RunRecord 后自动填充。
/// </summary>
public class RunRecordItem : MonoBehaviour
{
    private RunRecord _record;

    /// <summary>返回目标面板（打开明细前的记录列表面板，运行时由 TaskList 设置）</summary>
    private GameObject _returnPanel;

    void Awake()
    {
        if (DetailBtn != null)
        {
            DetailBtn.onClick.AddListener(OnDetailClicked);
        }
    }

    /// <summary>
    /// 设置返回面板（由 TaskList 实例化时传入记录列表面板）。
    /// </summary>
    public void SetDetailReturn(GameObject returnPanel)
    {
        _returnPanel = returnPanel;
    }

    /// <summary>
    /// 回放详情：隐藏当前面板，打开执行明细面板。
    /// </summary>
    private void OnDetailClicked()
    {
        if (detailPanel == null)
        {
            Debug.LogWarning("RunRecordItem 未绑定 detailPanel，无法打开执行明细");
            return;
        }

        detailPanel.Open(_record, _returnPanel);
        if (_returnPanel != null)
        {
            _returnPanel.SetActive(false);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>运行记录（赋值即触发界面填充）</summary>
    public RunRecord Record
    {
        get { return _record; }
        set
        {
            _record = value;
            SetValue();
        }
    }

    private void SetValue()
    {
        if (_record == null)
        {
            return;
        }

        // 任务类型：仿真显示图标 SimulationIcon，真实显示 RealIcon
        bool isSimulation = _record.Mode == RunType.SIMULATION;
        if (TaskTypeCell != null)
        {
            TaskTypeCell.text = _record.Simulated ? "离线模拟" : isSimulation ? "仿真" : "真实运行";
        }
        if (SimulationIcon != null)
        {
            SimulationIcon.gameObject.SetActive(isSimulation);
        }
        if (RealIcon != null)
        {
            RealIcon.gameObject.SetActive(!isSimulation);
        }

        // 执行时间
        if (TimeCell != null)
        {
            TimeCell.text = _record.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        // 结果状态：成功/碰撞/急停 图标切换
        RunRecordResult result = RunRecordResult.Running;
        Enum.TryParse(_record.Result, out result);
        bool isSuccess = result == RunRecordResult.Success;
        if (SuccessIcon != null)
        {
            SuccessIcon.gameObject.SetActive(isSuccess);
        }
        if (CollideIcon != null)
        {
            CollideIcon.gameObject.SetActive(result == RunRecordResult.Collision);
        }
        if (StopIcon != null)
        {
            StopIcon.gameObject.SetActive(result == RunRecordResult.EmergencyStop);
        }

        // 结果文字（ResultCell）：成功/急停/碰撞 各带颜色
        if (ResultCell != null)
        {
            switch (result)
            {
                case RunRecordResult.Success:
                    ResultCell.gameObject.SetActive(true);
                    ResultCell.text = "成功";
                    ResultCell.color = SuccessColor;
                    break;
                case RunRecordResult.EmergencyStop:
                    ResultCell.gameObject.SetActive(true);
                    ResultCell.text = "软件停止";
                    ResultCell.color = StopColor;
                    break;
                case RunRecordResult.Collision:
                    ResultCell.gameObject.SetActive(true);
                    ResultCell.text = "碰撞";
                    ResultCell.color = CollideColor;
                    break;
                default:
                    ResultCell.gameObject.SetActive(true);
                    ResultCell.color = StopColor;
                    ResultCell.text = result == RunRecordResult.Dispatched ? "已下发/未验收到位" :
                        result == RunRecordResult.Failed ? "失败" :
                        result == RunRecordResult.Interrupted ? "未完成" : "运行中/未确认结束";
                    break;
            }
        }

        // 原因：非成功状态显示
        if (WhyCell != null)
        {
            WhyCell.gameObject.SetActive(!isSuccess && !string.IsNullOrEmpty(_record.ResultReason));
            if (!isSuccess)
            {
                WhyCell.text = _record.ResultReason;
            }
        }

        // 关键信息摘要
        if (SummaryCell != null)
        {
            SummaryCell.text = BuildSummary();
        }

        // 耗时
        if (SpendCell != null)
        {
            SpendCell.text = $"{_record.DurationSeconds:F1} 秒";
        }
    }

    /// <summary>
    /// 组装关键信息摘要：途经点数量、替换接头数、仿真参数、真实运行参数。
    /// 单行展示，用 | 分割（前后带空格）。
    /// </summary>
    private string BuildSummary()
    {
        string summary = $"{_record.Source ?? ""} | 途经点 {_record.TargetCount} 个 | 替换接头 {_record.ReplaceCount} 项";

        SimulationParam p = _record.SimParams;
        if (p != null)
        {
            summary += $" | 底盘速度 {p.BaseSpeedValue} | 机械臂速度 {p.ArmSpeedValue} | 观察距离 {p.ObservationDistanceValue}";
        }

        if (_record.Mode == RunType.REALTIME && !string.IsNullOrEmpty(_record.RealParamsSummary))
        {
            summary += $" | {_record.RealParamsSummary}";
        }

        return summary;
    }

    // ============================ 控件绑定 ============================

    /// <summary>任务类型文字（仿真/真实运行）</summary>
    public Text TaskTypeCell;

    /// <summary>仿真模式图标</summary>
    public Image SimulationIcon;

    /// <summary>真实运行图标</summary>
    public Image RealIcon;

    /// <summary>执行时间</summary>
    public Text TimeCell;

    /// <summary>结果状态：成功图标</summary>
    public Image SuccessIcon;

    /// <summary>结果状态：碰撞图标</summary>
    public Image CollideIcon;

    /// <summary>结果状态：急停图标</summary>
    public Image StopIcon;

    /// <summary>非成功状态的原因</summary>
    public Text WhyCell;

    /// <summary>关键信息摘要</summary>
    public Text SummaryCell;

    /// <summary>耗时</summary>
    public Text SpendCell;

    /// <summary>结果文字（成功/急停/碰撞，带颜色）</summary>
    public Text ResultCell;

    /// <summary>回放详情按钮</summary>
    public Button DetailBtn;

    /// <summary>执行明细面板（由 TaskList 加载记录项时运行时绑定）</summary>
    public StepDetailPanel detailPanel;

    // ============================ 结果文字颜色 ============================

    /// <summary>成功颜色 #05DF72</summary>
    private static readonly Color SuccessColor = HexToColor("05DF72");

    /// <summary>急停颜色 #FF6467</summary>
    private static readonly Color StopColor = HexToColor("FF6467");

    /// <summary>碰撞颜色 #FF8904</summary>
    private static readonly Color CollideColor = HexToColor("FF8904");

    private static Color HexToColor(string hex)
    {
        Color color;
        return ColorUtility.TryParseHtmlString("#" + hex, out color) ? color : Color.white;
    }
}
