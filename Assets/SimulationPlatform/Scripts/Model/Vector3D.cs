using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using UnityEngine;


[Serializable]
public class Vector3D
{
    [XmlAttribute]
    public float X { get; set; }
    [XmlAttribute]
    public float Y { get; set; }
    [XmlAttribute]
    public float Z { get; set; }

    public Vector3D() { }
    public Vector3D(Vector3 v)
    {
        X = v.x;
        Y = v.y;
        Z = v.z;
    }

    public Vector3 GetVector3()
    {
        Vector3 v = new Vector3();
        v.x = X;
        v.y = Y;
        v.z = Z;
        return v;
    }
}
