using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;


[Serializable]
public class ModelParam
{
    //导程
    [XmlAttribute]
    public string Lead { get; set; }
    //中径
    [XmlAttribute]
    public string PitchDiameter { get; set; }
}
