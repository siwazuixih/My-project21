using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

[Serializable]
public class JointReplace
{
    //被替换模型
    [XmlAttribute]
    public string Replaced { get; set; }

    //替换模型Id
    [XmlAttribute]
    public string ReplaceId { get; set; }

    [XmlElement]
    public Position Position { get; set; }

    [XmlElement]
    public Rotation Rotation { get; set; }
}

