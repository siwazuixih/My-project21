using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

//工艺参数
[Serializable]
public class CraftParam
{
    //拧紧力矩
    [XmlAttribute]
    public string Torque { get; set; }
    //超限报警值
    [XmlAttribute]
    public string Threshold { get; set; }
    //角度
    [XmlAttribute]
    public string Angle { get; set; }
    //行程
    [XmlAttribute]
    public string Travel { get; set; }
    //起始点
    [XmlElement]
    public Position Start { get; set; }
}
