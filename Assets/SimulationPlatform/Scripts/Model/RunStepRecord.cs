using System;
using System.Xml.Serialization;

/// <summary>
/// 一次运行中的单步（途经点/目标）执行记录。
/// 仿真记录：途经点接头、接头参数、力矩、角度、仿真结果；
/// 真机运行记录：每个下发/执行的任务步骤。
/// </summary>
[Serializable]
public class RunStepRecord
{
    public DateTime Time { get; set; } = DateTime.Now;
    public bool HasTorque { get; set; }
    public bool HasAngle { get; set; }
    public string Detail { get; set; } = "";
    /// <summary>步骤序号（从 0 开始）</summary>
    [XmlAttribute("Index")]
    public int Index { get; set; }

    /// <summary>途经点/目标名称</summary>
    [XmlAttribute("TargetName")]
    public string TargetName { get; set; }

    /// <summary>该目标关联的接头模型Id（界面阶段反查解析）</summary>
    [XmlAttribute("JointId")]
    public string JointId { get; set; }

    /// <summary>接头参数摘要（如导程/节圆直径等）</summary>
    [XmlAttribute("JointParams")]
    public string JointParams { get; set; }

    /// <summary>力矩（仿真预留，默认 0）</summary>
    [XmlAttribute("Torque")]
    public float Torque { get; set; }

    /// <summary>角度（仿真预留，默认 0）</summary>
    [XmlAttribute("Angle")]
    public float Angle { get; set; }

    /// <summary>本步骤结果（成功 / 失败原因等）</summary>
    [XmlAttribute("Result")]
    public string Result { get; set; }

    public RunStepRecord()
    {
    }

    public RunStepRecord(int index, string targetName)
    {
        Index = index;
        TargetName = targetName ?? "";
    }
}
