using System;
using System.Collections.Generic;
using System.Xml.Serialization;

[Serializable]
public class ProjectRecord
{
    [XmlAttribute("Id")]
    public string Id { get; set; }

    [XmlAttribute("CreateTime")]
    public DateTime CreateTime { get; set; }

    [XmlArray("Replaces")]
    [XmlArrayItem("Replace")]
    public List<JointReplaceRecord> Replaces { get; set; } = new List<JointReplaceRecord>();

    [XmlElement]
    public SimulationParam SimulationParam { get; set; }

    [XmlElement]
    public RunParam RunParam { get; set; }

    // 兼容伍老师版本已经写入 ProjectRecord 的视角数据。新记录也会同时写入
    // SimulationParam，便于所有仿真参数保持在同一数据对象中。
    [XmlAttribute]
    public float CameraCurrentX { get; set; }

    [XmlAttribute]
    public float CameraCurrentY { get; set; }

    [XmlAttribute]
    public float CameraCurrentDistance { get; set; }

    [XmlElement]
    public Vector3D CameraPanOffset { get; set; }

    public ProjectRecord()
    {
        Id = Guid.NewGuid().ToString();
        CreateTime = DateTime.Now;
    }
}
