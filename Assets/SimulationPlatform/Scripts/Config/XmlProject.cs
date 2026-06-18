using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

[XmlRoot("Projects")]
[Serializable]
public class XmlProject
{
    [XmlElement]
    public List<Project> Projects { get; set; } = new List<Project>();
}
