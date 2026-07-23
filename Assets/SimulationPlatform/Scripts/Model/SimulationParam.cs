
using System;
using System.Xml.Serialization;

[Serializable]
public class SimulationParam
{
    [XmlElement]
    public Vector3D RobotXYZ {  get; set; }

    [XmlElement]
    public Vector3D RobotRotation { get; set; }

    [XmlElement]
    public Vector3D SceneXYZ { get; set; }

    [XmlElement]
    public Vector3D SceneRotation { get; set; }

    [XmlAttribute]
    public float BaseSpeedValue { get; set; }

    [XmlAttribute]
    public float ArmSpeedValue { get; set; }

    [XmlAttribute]
    public float ObservationDistanceValue { get; set; }

    [XmlElement]
    public Vector3D XYZPanelValue { get; set; }

    [XmlAttribute]
    public bool ShowTarget { get; set; }
}
