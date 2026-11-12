// Production follower + arm playback + extracted production Simulation entry.
// Unity/MuJoCo/robot IO below are test doubles; never load a scene or open a socket.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RobotLogic;
using Mujoco;
using UnityEngine;

namespace UnityEngine
{
    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) {} }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s) {} }
    public class RangeAttribute : Attribute { public RangeAttribute(float a,float b) {} }
    public class MinAttribute : Attribute { public MinAttribute(float a) {} }
    public class MonoBehaviour
    {
        public Transform transform = new();
        public List<IEnumerator> Routines = new();
        public Coroutine StartCoroutine(IEnumerator value) { Routines.Add(value); return new Coroutine(value); }
        public void StopAllCoroutines() => Routines.Clear();
    }
    public class Coroutine { public IEnumerator Routine; public Coroutine(IEnumerator r) { Routine=r; } }
    public class WaitForSeconds { public WaitForSeconds(float s) {} }
    public class GameObject { public Transform transform = new(); public static implicit operator bool(GameObject g)=>g!=null; }
    public class Transform { public Vector3 position, forward=Vector3.forward; }
    public class LineRenderer { public int positionCount; public void SetPositions(Vector3[] p) {} public static implicit operator bool(LineRenderer l)=>l!=null; }
    public class AnimationCurve
    { public float Evaluate(float t)=>Math.Clamp(t,0,1); public static AnimationCurve EaseInOut(float a,float b,float c,float d)=>new(); }
    public struct Color
    {
        public static Color white,green,red;
        public Color(float a,float b,float c,float d) {}
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c) { x=a;y=b;z=c; }
        public static Vector3 forward=>new(0,0,1);
        public static Vector3 zero=>new();
        public float magnitude=>(float)Math.Sqrt(x*x+y*y+z*z);
        public Vector3 normalized=>magnitude==0 ? zero : this*(1/magnitude);
        public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float v)=>new(a.x*v,a.y*v,a.z*v);
        public static bool operator ==(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
        public static bool operator !=(Vector3 a,Vector3 b)=>!(a==b);
        public override bool Equals(object o)=>o is Vector3 v&&this==v;
        public override int GetHashCode()=>HashCode.Combine(x,y,z);
    }
    public struct Quaternion
    {
        public static Quaternion LookRotation(Vector3 v)=>new();
        public static Quaternion FromToRotation(Vector3 a,Vector3 b)=>new();
        public static Quaternion operator *(Quaternion a,Quaternion b)=>new();
    }
    public static class Mathf
    {
        public const float Deg2Rad=(float)(Math.PI/180),Rad2Deg=(float)(180/Math.PI);
        public static float Abs(float a)=>Math.Abs(a);
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static int Min(int a,int b)=>Math.Min(a,b);
        public static float Clamp01(float a)=>Math.Clamp(a,0,1);
        public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
        public static float MoveTowards(float a,float b,float step)=>a+Math.Sign(b-a)*Math.Min(Math.Abs(b-a),step);
        public static float DeltaAngle(float a,float b)=>(b-a+540)%360-180;
    }
    public static class Time { public static float deltaTime=.02f; }
    public static class Debug
    {
        public static List<string> Lines=new();
        public static void Log(object o)=>Lines.Add(o.ToString());
        public static void LogWarning(object o)=>Log(o);
        public static void LogError(object o)=>Log(o);
    }
}
namespace TMPro { public class TextMeshProUGUI { public string text; } }
namespace Mujoco
{
    public class MjJoint { public int QposAddress; }
    public class MjHingeJoint : MjJoint {}
    public class MjSlideJoint : MjJoint {}
    public class MjActuator { public MjJoint Joint=new MjHingeJoint(); public float Control; }
    public unsafe struct MjModel {}
    public unsafe struct MjData { public double* qpos; }
    public unsafe class MjScene { public static MjScene Instance=new(); public MjModel* Model; public MjData* Data; }
    public unsafe static class MujocoLib { public static void mj_forward(MjModel* m,MjData* d)=>throw new Exception("unexpected native call"); }
}
public class MujocoStaticIKSolver
{
    public List<MjActuator> actuators=Enumerable.Range(0,7).Select(_=>new MjActuator()).ToList();
    public double[] SolveIK(Vector3 p)=>throw new Exception("unexpected IK");
    public double[] SolveIK(Vector3 p,Quaternion q)=>SolveIK(p);
}
public class BITStarPlanner
{
    public List<double[]> Plan(Vector3 p)=>throw new Exception("unexpected replanning");
    public List<double[]> Plan(Vector3 p,Quaternion q)=>Plan(p);
    public List<Vector3> GetPathInWorldSpace(List<double[]> p)=>new();
}
public class ChassisController { public Vector3 GetRobotPosition()=>new(); }
public class RobotDiagnosticUI { public LineRenderer lineVis; }
public class MissionController
{
    public CoreReferences refs=new() { targetObject=new(),ikSolver=new(),armEndEffector=new() };
    public ArmSettings arm=new(); public MissionSettings mission=new(); public DebugSettings debug=new();
    public ChassisController chassisCtrl=new(); public RobotDiagnosticUI diagUI=new();
    public ArmController armCtrl=new(); public bool hasPrecalculated=true;
    public int currentMissionIndex,Completed; public MissionState currentState;
    public List<DiagnosisSnapshot> snapshots=new();
    public void RecordArmPlanningFailure(string reason)=>throw new Exception(reason);
    public void OnArmTaskFinished() { Completed++; currentState=MissionState.Finished; }
}
public class DobotController
{
    public bool FieldWorkflowActive,ExternalControlActive;
    public bool TryCopyActualJoints(double[] p,float age)=>false;
}
public class LiftCylinderController
{ public float currentHeightMm,simToRealScale=1000; public bool HasFreshFeedback(float age)=>false; }
public class FieldCommissioningPanel { public static FieldCommissioningPanel Active; public bool MotionLocked; }
public class ConnectCommander { public bool AnyTaskRunning; }
public class ToggleStub { public bool isOn; public void SetIsOnWithoutNotify(bool v)=>isOn=v; }
public partial class Simulation
{
    public RealRobotFollower RealRobotFollower;
    public MissionController MissionController;
    public ConnectCommander ConnectCommander=new();
    public DobotController Dobot=new(); public ToggleStub SyncToggle=new();
    public bool Enter()=>PrepareSimulationPlayback();
}
class Program
{
    static int count;
    static void Check(bool ok,string name) { if(!ok) throw new Exception(name); Console.WriteLine("PASS "+name);count++; }
    static void Drain(IEnumerator r)
    {
        int n=0;
        while(r.MoveNext()) { if(++n>2000) throw new Exception("animation stalled"); if(r.Current is Coroutine c) Drain(c.Routine); }
    }
    static void Main()
    {
        var m=new MissionController(); m.armCtrl.Init(m);
        m.snapshots.Add(new DiagnosisSnapshot { ikSuccess=true,precalcArmPlan=new() { new double[7],new double[] {.3,.2,0,0,0,0,.01} } });
        var f=new RealRobotFollower { mission=m };
        var s=new Simulation { MissionController=m,RealRobotFollower=f };
        f.EnableRealRobotFollow(); m.armCtrl.StartArmSequence();
        Check(f.followEnabled&&m.armCtrl.externalControlActive&&m.armCtrl.Routines.Count==0,"reproduce real-follow suppressing local arm playback");
        Check(Debug.Lines.Any(x=>x.Contains("未播放规划轨迹")),"conflict is visible, not silent");
        Check(s.Enter()&&!f.followEnabled&&!m.armCtrl.externalControlActive&&!s.SyncToggle.isOn,"simulation entry releases both follow and external-control flag");
        m.armCtrl.StartArmSequence(); Drain(m.armCtrl.Routines.Single()); m.armCtrl.Routines.Clear();
        Check(m.Completed==1&&Math.Abs(m.refs.ikSolver.actuators[0].Control-.3)<.011,"actual production cached joint playback moves model actuator");
        f.EnableRealRobotFollow(); f.followEnabled=false; // historical incomplete disable
        Check(s.Enter()&&!m.armCtrl.externalControlActive,"stale external flag repaired even when follow checkbox is already off");
        foreach(var owner in new[] { "field","queue","robot","algorithm" })
        {
            f.EnableRealRobotFollow(); FieldCommissioningPanel.Active=new();
            FieldCommissioningPanel.Active.MotionLocked=owner=="field";
            s.ConnectCommander.AnyTaskRunning=owner=="queue";
            s.Dobot.FieldWorkflowActive=owner=="robot"; s.Dobot.ExternalControlActive=owner=="algorithm";
            Check(!s.Enter()&&f.followEnabled&&m.armCtrl.externalControlActive,"cannot steal model control from "+owner);
        }
        FieldCommissioningPanel.Active=null; s.ConnectCommander.AnyTaskRunning=false;
        s.Dobot.FieldWorkflowActive=false; s.Dobot.ExternalControlActive=false;
        s.RealRobotFollower=null;
        Check(s.Enter()&&!m.armCtrl.externalControlActive,"missing follower mapping still releases intended arm model");
        Console.WriteLine($"ALL {count} PASS (fake Unity actuators only; no sockets/native simulation)");
    }
}
