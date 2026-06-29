using GLTFast.Schema;
using Mujoco;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;

namespace Assets.Scripts
{
    static class PathPlanManager
    {
        public static List<MjActuator> Actuators;
        public static MoveRecord MoveRecord;
        public static bool Move = false;
        public static bool Moving = false;

        private static GameObject model;
        private static MujocoStaticIKSolver solver;
        private static BITStarPlanner rrtPlanner;

        //private static TestRRT testRrt;

        private static MjSite endSite;

        private static bool inited = false;

        public unsafe static void InitAct(GameObject model)
        {
            if (inited) return;
            PathPlanManager.Actuators = new List<MjActuator>();
            MjActuator[] actuators = model.GetComponentsInChildren<MjActuator>();
            foreach (MjActuator actuator in actuators)
            {
                PathPlanManager.Actuators.Add(actuator);
            }
            inited = true;
        }

        public unsafe static void Init(GameObject model)
        {
            if (inited) return;
            PathPlanManager.model = model;
            solver = model.AddComponent<MujocoStaticIKSolver>();
            PathPlanManager.Actuators = new List<MjActuator>();
            solver.actuators = PathPlanManager.Actuators;

            //testRrt = model.AddComponent<TestRRT>();
            //testRrt.actuators = PathPlanManager.Actuators;

            MjActuator[] actuators = model.GetComponentsInChildren<MjActuator>();
            foreach (MjActuator actuator in actuators)
            {
                PathPlanManager.Actuators.Add(actuator);
            }

            MjSite[] sites = model.GetComponentsInChildren<MjSite>();
            if (sites.Length > 0)
            {
                foreach (var site in sites)
                {
                    MjSite[] siteChildren = site.transform.parent.GetComponentsInChildren<MjSite>();
                    if (siteChildren.Length == 1)
                    {
                        Debug.Log($"找到终端Site {site.name}");
                        endSite = site;
                        solver.endEffectorSite = endSite;
                        //MujocoLib.mj_name2id(MjScene.Instance.Model, (int)MujocoLib.mjtObj.mjOBJ_SITE, endSite.name);
                        //MujocoLib.mj_id2name(MjScene.Instance.Model, (int)MujocoLib.mjtObj.mjOBJ_SITE, 0);
                        break;
                    }
                }
            }

            BITStarPlanner[] rrts = UnityEngine.Object.FindObjectsOfType<BITStarPlanner>();
            if (rrts.Length > 0)
            {
                rrtPlanner = rrts[0];
                rrtPlanner.actuators = PathPlanManager.Actuators;
                rrtPlanner.ikSolver = solver;
                //testRrt.planner = rrtPlanner;
            }
            inited = true;
        }

        public static void MoveTo(Transform transform)
        {
            if (transform != null)
            {
                ModelCollisionHighlighter.SeletectedObjects.Add(transform);
            } 
            
            if (Move || Moving)
            {
                return;
            }
            //testRrt.target = transform;
            Move = true;

            if (MoveRecord == null)
            {
                MoveRecord = new MoveRecord();
                MoveRecord.Transform = model.transform;
                MoveRecord.InitActuators(Actuators);
            }
        }

        public static void Record(Transform target, List<double[]> paths)
        {
            if (MoveRecord == null)
            {
                return;
            }
            MoveRecord.Record(target, paths);
        }

        public static void FinishMove()
        {
            Move = false;
            Moving = false;
        }
    }
}
