using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 运行记录步骤明细项实体类（挂载在步骤明细列表 prefab 上）。
/// 包含步骤序号/途径点名称/力矩/角度/结果 各单元格控件，
/// 绑定 RunStepRecord 后自动填充，序号按绘制顺序递增。
/// </summary>
public class StepCellItem : MonoBehaviour
{
    private RunStepRecord _step;

    /// <summary>步骤记录（赋值即触发界面填充）</summary>
    public RunStepRecord Step
    {
        get { return _step; }
        set
        {
            _step = value;
            SetValue();
        }
    }

    /// <summary>
    /// 填充步骤数据，序号按列表绘制顺序递增。
    /// </summary>
    /// <param name="step">步骤记录</param>
    /// <param name="number">显示序号（当前列表第几行，从 1 开始）</param>
    public void SetData(RunStepRecord step, int number)
    {
        _step = step;
        SetValue();
        // 显式传入的绘制序号优先
        SetNumber(number);
    }

    /// <summary>
    /// 手动给序号赋值（生成 cell 时调用）。
    /// </summary>
    public void SetNumber(int number)
    {
        if (NumCell != null)
        {
            NumCell.text = number.ToString();
        }
    }

    private void SetValue()
    {
        if (_step == null)
        {
            return;
        }

        // 序号：默认取步骤 Index+1（保证通过 Step 属性直接赋值时也有序号）
        if (NumCell != null)
        {
            NumCell.text = (_step.Index + 1).ToString();
        }

        // 途径点名称：若是替换接头则显示接头名称
        if (NameCell != null)
        {
            NameCell.text = _step.TargetName ?? "";
        }

        if (TorqueCell != null)
        {
            TorqueCell.text = _step.HasTorque ? _step.Torque.ToString("F2") : "未采集";
        }

        if (AngleCell != null)
        {
            AngleCell.text = _step.HasAngle ? _step.Angle.ToString("F2") : "未采集";
        }

        if (ResultCell != null)
        {
            ResultCell.text = (_step.Result ?? "") + (string.IsNullOrEmpty(_step.Detail) ? "" : "\n" + _step.Detail);
        }
    }

    // ============================ 控件绑定 ============================

    /// <summary>步骤序号（绘制递增）</summary>
    public Text NumCell;

    /// <summary>途径点名称（若是替换接头则显示接头名称）</summary>
    public Text NameCell;

    /// <summary>力矩</summary>
    public Text TorqueCell;

    /// <summary>角度</summary>
    public Text AngleCell;

    /// <summary>结果</summary>
    public Text ResultCell;
}
