using Assets.SimulationPlatform.Scripts.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;


[Serializable]
public class Project
{
    [XmlAttribute("Id")]
    public string Id { get; set; }
    [XmlAttribute("Name")]
    public string Name { get; set; }

    //说明
    [XmlAttribute("Comment")]
    public string Comment { get; set; }

    [XmlElement]
    public Robot Robot { get; set; }
    [XmlElement]
    public Scene Scene { get; set; }
    [XmlArray("Replaces")]
    [XmlArrayItem("Replace")]
    public List<JointReplaceRecord> Replaces { get; set; } = new List<JointReplaceRecord>();

    [XmlElement]
    public SimulationParam SimulationParam { get; set; }

    [XmlElement]
    public RunParam RunParam { get; set; }

    [XmlArray("ProjectRecords")]
    [XmlArrayItem("ProjectRecord")]
    public List<ProjectRecord> ProjectRecords { get; set; } = new List<ProjectRecord>();
}
