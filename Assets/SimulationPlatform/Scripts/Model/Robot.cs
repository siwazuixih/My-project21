using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Assets.SimulationPlatform.Scripts.Model
{
    [Serializable]
    public class Robot
    {
        [XmlElement]
        public Position Position {  get; set; }

        [XmlElement]
        public Rotation Rotation { get; set; }

        [XmlArray("Actuators")]
        [XmlArrayItem("Actuator")]
        public List<float> Actuators { get; set; } = new List<float>();
    }
}
