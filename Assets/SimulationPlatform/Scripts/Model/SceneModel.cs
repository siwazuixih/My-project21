using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;


[Serializable]
public class SceneModel
{
    [XmlAttribute("Id")]
    public string Id { get; set; }
    [XmlAttribute("Name")]
    public string Name { get; set; }

    //说明
    [XmlAttribute("Comment")]
    public string Comment { get; set; }

    [XmlElement("Glb")]
    public GlbModel Glb { get; set; }
}
