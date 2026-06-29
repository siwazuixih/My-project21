using Assets.SimulationPlatform.Scripts.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;


[XmlRoot("Models")]
[Serializable]
public class XmlModel
{
    [XmlArray("Joints")]
    [XmlArrayItem("Joint")]
    public List<JointModel> Joints { get; set; } = new List<JointModel>();

    [XmlArray("Scenes")]
    [XmlArrayItem("Scene")]
    public List<SceneModel> Scenes { get; set; } = new List<SceneModel>();
}
