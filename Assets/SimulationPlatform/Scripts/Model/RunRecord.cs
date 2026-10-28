using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using LiteDB;

/// <summary>
/// 运行结果状态：成功 / 急停 / 碰撞。非成功状态需填写原因（ResultReason）。
/// </summary>
public enum RunRecordResult
{
    /// <summary>进行中（尚未结束）</summary>
    Running,

    /// <summary>成功</summary>
    Success,

    /// <summary>急停（原因如：软急停-界面按下停止、力超限）</summary>
    EmergencyStop,

    /// <summary>碰撞（原因如：碰撞检测触发）</summary>
    Collision,
    Failed,
    Dispatched,
    Interrupted
}

/// <summary>
/// 一次运行（单次仿真运行或单次真机运行）的执行历史记录，绑定到所属项目。
/// 包含执行时间、结果状态、关键信息摘要与分步骤明细，供"项目记录"界面查看与回放。
/// </summary>
[Serializable]
public class RunRecord
{
    // Source distinguishes offline verification, field diagnostics and the legacy dispatcher.
    public string Source { get; set; } = "";
    public bool Simulated { get; set; }
    public bool Completed { get; set; }
    public string ReportDirectory { get; set; } = "";
    [BsonId]
    [XmlAttribute("Id")]
    public string Id { get; set; }

    /// <summary>所属项目 Id（关联 Project.Id，数据库索引）</summary>
    [XmlAttribute("ProjectId")]
    public string ProjectId { get; set; }

    [XmlAttribute("Mode")]
    public RunType Mode { get; set; }

    [XmlAttribute("StartTime")]
    public DateTime StartTime { get; set; }

    [XmlAttribute("EndTime")]
    public DateTime EndTime { get; set; }

    /// <summary>耗时（秒）</summary>
    [XmlAttribute("DurationSeconds")]
    public double DurationSeconds { get; set; }

    /// <summary>结果状态（RunRecordResult 名称）</summary>
    [XmlAttribute("Result")]
    public string Result { get; set; }

    /// <summary>非成功状态的原因（如：软急停（界面按下停止）、力超限、碰撞检测触发）</summary>
    [XmlAttribute("ResultReason")]
    public string ResultReason { get; set; }

    /// <summary>替换接头数量</summary>
    [XmlAttribute("ReplaceCount")]
    public int ReplaceCount { get; set; }

    /// <summary>途经点/目标数量</summary>
    [XmlAttribute("TargetCount")]
    public int TargetCount { get; set; }

    /// <summary>关键信息摘要：本次运行使用的仿真参数快照</summary>
    [XmlElement]
    public SimulationParam SimParams { get; set; }

    /// <summary>真实运行关键摘要：机器人IP/底盘IP/速度等（界面阶段可扩展）</summary>
    [XmlElement]
    public string RealParamsSummary { get; set; }

    /// <summary>分步骤明细：每个途经点的接头、参数、力矩、角度、结果</summary>
    [XmlArray("Steps")]
    [XmlArrayItem("Step")]
    public List<RunStepRecord> Steps { get; set; } = new List<RunStepRecord>();

    public RunRecord()
    {
        Id = Guid.NewGuid().ToString();
        Result = RunRecordResult.Running.ToString();
        ResultReason = "";
    }
}
