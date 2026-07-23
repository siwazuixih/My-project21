using System;
using System.Xml.Serialization;
using UnityEngine;

[Serializable]
public class QuaternionD
{
    [XmlAttribute]
    public float X { get; set; }
    [XmlAttribute]
    public float Y { get; set; }
    [XmlAttribute]
    public float Z { get; set; }
    [XmlAttribute]
    public float W { get; set; }

    public QuaternionD() { }
    public QuaternionD(Quaternion q)
    {
        X = q.x;
        Y = q.y;
        Z = q.z;
        W = q.w;
    }

    public Quaternion GetQuaternion()
    {
        return new Quaternion(X, Y, Z, W);
    }
}
