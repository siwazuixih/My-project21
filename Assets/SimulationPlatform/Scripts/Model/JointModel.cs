using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;


[Serializable]
public class JointModel
{
    [XmlAttribute("Id")]
    public string Id { get; set; }
    [XmlAttribute("Name")]
    public string Name { get; set; }

    //型号
    [XmlAttribute("Model")]
    public string Model { get; set; }

    //说明
    [XmlAttribute("Comment")]
    public string Comment { get; set; }

    [XmlElement("Glb")]
    public GlbModel Glb { get; set; }

    [XmlElement("Param")]
    public ModelParam Param { get; set; } = new ModelParam();

    [XmlElement("Craft")]
    public CraftParam Craft { get; set; } = new CraftParam();
}
