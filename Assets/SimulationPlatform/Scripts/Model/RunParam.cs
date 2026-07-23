
using System;
using System.Xml.Serialization;

[Serializable]
public class RunParam
{
    [XmlAttribute]
    public string RobotIp { get; set; }

    [XmlAttribute]
    public string ChassisIp { get; set; }

    [XmlAttribute]
    public int Speed { get; set; }

    [XmlAttribute]
    public bool IsSingle { get; set; }

    [XmlAttribute]
    public bool IsSync { get; set; }
}
