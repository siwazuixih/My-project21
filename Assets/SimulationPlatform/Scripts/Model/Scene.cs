using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

[Serializable]
public class Scene
{
    [XmlAttribute]
    public string Id { get; set; }

    [XmlElement]
    public Position Position { get; set; }

    [XmlElement]
    public Rotation Rotation { get; set; }
}
