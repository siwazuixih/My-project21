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

    public ProjectRecord()
    {
        Id = Guid.NewGuid().ToString();
        CreateTime = DateTime.Now;
    }
}
