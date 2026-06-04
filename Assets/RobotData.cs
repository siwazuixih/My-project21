using UnityEngine;
using Mujoco;
using System.Collections.Generic;

namespace RobotLogic
{
    [System.Serializable]
    public class CoreReferences
    {
        [Header("核心组件")]
        public Transform targetObject;          
        public MujocoStaticIKSolver ikSolver;   
        public BITStarPlanner bitPlanner;       
        public GameObject realRobotBase;        
        public Transform armEndEffector;        
    }

    [System.Serializable]
    public class ChassisSettings
    {
        [Header("执行器 (MjActuator)")]
        public MjActuator actuatorX;
        public MjActuator actuatorZ;
        public MjActuator actuatorRot;

        [Header("运动参数")]
        public float moveSpeed = 0.5f;
        public float turnSpeed = 2.0f;
        public float alignThreshold = 2.0f;
        public float stopDistance = 0.05f;

        [Header("交互逻辑")]
        public float armReachDistance = 1.5f; 
        public bool autoStartArm = true;
        public float inertiaDelay = 0.5f;

        [Header("方向校准")]
        public bool invertX = false;
        public bool invertZ = false;
        public bool swapXZ = false;
        [Range(-180, 180)] public float headingOffset = 0.0f;

        [Header("动态避障")]
        public bool enableDynamicAvoidance = true;
        public float avoidanceUpdateInterval = 0.5f;
    }

    [System.Serializable]
    public class ArmSettings
    {
        [Header("初始姿态")]
        public List<float> initialAngles = new List<float>();

        [Header("运动控制")]
        public bool usePrecalculatedSolution = true; 
        public bool useBitStarPlanner = true;
        public float jointSpeed = 0.8f;
        public AnimationCurve motionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public float physicalTolerance = 0.05f;

        [Header("姿态感知 (Eye-in-Hand)")]
        public bool enableLookAt = true;
        public float observationDistance = 0.3f;
        public Vector3 faceAxis = new Vector3(0, 0, 1);
        
        [Header("手动观测模式")]
        public bool useManualObservation = false;
        public Vector3 manualObservationVec = new Vector3(0, 1, 0);
    }

    [System.Serializable]
    public class MissionSettings
    {
        [Header("执行模式")]
        public bool stepByStepMode = false;

        [Header("多目标巡航")]
        public List<Transform> targets = new List<Transform>();
        public bool loopMission = false;
        public bool resetArmBeforeMoving = true;
        public float intervalBetweenTasks = 1.0f;
    }

    [System.Serializable]
    public class DebugSettings
    {
        public bool showGUI = true;
        [Header("Gizmos 开关")]
        public bool showGlobalPlan = true;
        public bool showRealtimePath = true;
        public bool showChassisLines = true;
        public bool showArmTrail = true;
        public bool showGhostRobot = true;

        [Header("颜色样式")]
        public Color globalPathColor = Color.white;
        public Color realtimePathColor = Color.green;
        public Color trailColor = Color.red;
        public Color ghostColor = new Color(1, 1, 0, 0.3f);
    }

    [System.Serializable]
    public class Connect
    {
        [Header("通讯")]
        public ConnectCommander Commander;
    }

    public struct DiagnosisSnapshot
    {
        public int taskId;
        public Vector3 precalcChassisPos;   
        public float precalcChassisAngle;   
        public Vector3 precalcArmTarget;    
        public bool ikSuccess;
        public List<double[]> precalcArmPlan; 
    }

    public enum MissionState 
    { 
        Initializing, Idle, WaitingToStartPath, ChassisMoving, 
        Stabilizing, WaitingForInput, ArmPlanning, ArmMoving, 
        ResettingArm, WaitingForNextTarget, Finished 
    }
}