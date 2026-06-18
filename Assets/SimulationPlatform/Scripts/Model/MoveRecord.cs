using Mujoco;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts
{
    internal class MoveRecord
    {
        public Transform Transform { get; internal set; }

        public List<float> Controls = new List<float>();

        private List<Vector3> targets = new List<Vector3>();
        private List<List<double[]>> paths = new List<List<double[]>>();

        internal void InitActuators(List<MjActuator> actuators)
        {
            Controls.Clear();
            targets.Clear();
            paths.Clear();
            foreach (var item in actuators)
            {
                Controls.Add(item.Control);
            }
        }

        internal void Record(Transform target, List<double[]> paths)
        {
            targets.Add(target.position);
            this.paths.Add(paths);
        }
    }
}
