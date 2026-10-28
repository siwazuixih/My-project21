using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnityEngine
{
    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) {} }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s) {} }
    public class HideInInspector : Attribute {}
    public class MonoBehaviour
    {
        public static Dictionary<Type, object> Objects = new();
        public static T FindObjectOfType<T>() where T : class => Objects.GetValueOrDefault(typeof(T)) as T;
        public Coroutine Latest;
        public Coroutine StartCoroutine(IEnumerator value) => Latest = new Coroutine { Routine = value };
    }
    public class Coroutine { public IEnumerator Routine; }
    public static class Debug { public static void Log(object o) {} public static void LogWarning(object o) {} public static void LogError(object o) {} }
    public static class Time { public static float deltaTime = .1f; }
    public static class Mathf
    {
        public const float Deg2Rad = (float)(Math.PI/180);
        public const float Rad2Deg = (float)(180/Math.PI);
        public static float Atan2(float x,float z) => (float)Math.Atan2(x,z);
        public static float Abs(float x) => Math.Abs(x);
        public static float DeltaAngle(float a,float b) { float d=(b-a+540)%360-180; return d; }
        public static float Max(float a,float b) => Math.Max(a,b);
        public static float Clamp01(float t) => Math.Clamp(t,0,1);
        public static float SmoothStep(float a,float b,float t) => a+(b-a)*t*t*(3-2*t);
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c) { x=a;y=b;z=c; }
        public float sqrMagnitude => x*x+y*y+z*z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude<.000001f ? new() : new(x/magnitude,y/magnitude,z/magnitude);
        public static float Distance(Vector3 a,Vector3 b) => (a-b).magnitude;
        public static Vector3 operator -(Vector3 a,Vector3 b) => new() { x=a.x-b.x,y=a.y-b.y,z=a.z-b.z };
    }
    public struct Quaternion { public Vector3 eulerAngles; public static Quaternion LookRotation(Vector3 v) => new(); }
}
namespace Mujoco
{
    public class MjHingeJoint {}
    public class MjActuator { public float Control; public object Joint = new MjHingeJoint(); }
}
public class Solver { public List<Mujoco.MjActuator> actuators = Enumerable.Range(0,7).Select(_=>new Mujoco.MjActuator()).ToList(); }
public class Refs { public Solver ikSolver = new(); }
public class Chassis { public bool swapXZ,invertX,invertZ; public float headingOffset; public Mujoco.MjActuator actuatorX,actuatorZ,actuatorRot; }
public class Arm { public List<float> initialAngles = Enumerable.Repeat(0f,7).ToList(); }
public class MissionController { public Refs refs = new(); public Chassis chassis = new(); public Arm arm = new(); public int liftActuatorIndex=6; }
public class Simulation { public bool Locked; public void SetRealRunInputsLocked(bool value) => Locked=value; }
public class FieldCommissioningSettings { public bool useLegacyChassisTiming; public ChassisLegacyTiming legacyChassisTiming = new(); }
public class ChassisStub
{
    public int CancellationVersion;
    public bool Result=true;
    public string LastConfirmedMotionError="test-rejection";
    public readonly List<(ushort Api,bool Legacy,double Seconds)> Calls = new();
    public Action OnFirstCall;
    Task<bool> Call(ushort api,bool legacy,double seconds)
    { Calls.Add((api,legacy,seconds)); if(Calls.Count==1) OnFirstCall?.Invoke(); return Task.FromResult(Result); }
    public Task<bool> ExecuteRelativeConfirmedAsync(ushort api,string json) => Call(api,false,0);
    public Task<bool> ExecuteRelativeLegacyTimedAsync(ushort api,string json,double seconds) => Call(api,true,seconds);
}
public enum RunType { SIMULATION }
public enum RunRecordResult { Interrupted,Failed,Success }
public static class RunRecorder
{
    public static RunRecordResult? Result;
    public static bool IsCurrent(string id) => true;
    public static void FinishRun(RunRecordResult r,string reason,string id) => Result=r;
}
public static class RunHistoryCapture { public static string Begin(RunType t,string source,bool sim,int targets) => "test"; }
public class ServoTighteningController
{
    public sealed class CommandResult { public bool Success; public string Error=""; }
    public bool ProgramReady,FieldWorkflowOwned,OwnsSimulationProgram,SimulationOnlyService;
    public ConnectCommander OfflineWorkflowOwner;
    public int Starts,Cycles,Stops;
    public bool FailCycle;
    public TaskCompletionSource<CommandResult> PendingStart;
    public async Task<CommandResult> StartSimulationProgramAsync()
    {
        Starts++;
        if (PendingStart != null) await PendingStart.Task;
        ProgramReady=OwnsSimulationProgram=SimulationOnlyService=true;
        return new() { Success=true };
    }
    public Task<CommandResult> RunContactCycleAsync(bool simulation,CancellationToken token)
    {
        if (!simulation) throw new Exception("Real command attempted!");
        token.ThrowIfCancellationRequested(); Cycles++;
        return Task.FromResult(new CommandResult { Success=!FailCycle,Error=FailCycle?"NG":"" });
    }
    public Task<CommandResult> StopProgramAsync()
    { Stops++; ProgramReady=OwnsSimulationProgram=false; return Task.FromResult(new CommandResult { Success=true }); }
}
public partial class ConnectCommander
{
    public int armJointStartIndex;
    public List<TrajectoryTask> taskPlaylist = new();
    public bool AnyTaskRunning => isSequenceRunning;
    private string offlineHistoryId;
    public readonly List<string> Recorded = new();
    private bool chassisRouteRunning;
    public ChassisStub chassisTCP = new();
    public float chassisMoveSpeedX=.3f,chassisTurnSpeedW=.5f;
    public IEnumerator TestChassis(TrajectoryTask task,FieldCommissioningSettings settings=null) => SendChassisSegmentedRoutine(task,settings);
    private void RecordOfflineStep(string name,string result) => Recorded.Add(name);
}
class Program
{
    static int passed;
    static void Check(bool value,string name) { if(!value) throw new Exception(name); passed++; Console.WriteLine("PASS "+name); }
    static (ConnectCommander C,MissionController M,ServoTighteningController S,Simulation UI) Setup(int targets=2, double angle=.02)
    {
        UnityEngine.MonoBehaviour.Objects.Clear(); RunRecorder.Result=null;
        var c=new ConnectCommander(); var m=new MissionController(); var s=new ServoTighteningController(); var ui=new Simulation();
        UnityEngine.MonoBehaviour.Objects[typeof(ServoTighteningController)]=s;
        UnityEngine.MonoBehaviour.Objects[typeof(Simulation)]=ui;
        for(int i=0;i<targets;i++)
        {
            c.taskPlaylist.Add(new TrajectoryTask { targetIndex=i,stage=TrajectoryStage.CoarseArm,type=TaskType.Arm,
                taskName=$"coarse{i}",armPoints=new() {new double[] {angle,0,0,0,0,0,0}} });
            c.taskPlaylist.Add(new TrajectoryTask { targetIndex=i,stage=TrajectoryStage.ReturnArm,type=TaskType.Arm,
                taskName=$"return{i}",armPoints=new() {new double[7]} });
        }
        c.DispatchFullProcessValidation(m); return (c,m,s,ui);
    }
    static void Pump(ConnectCommander c,Func<bool> condition,Action tick=null)
    {
        for(int i=0;i<2000;i++)
        {
            if (condition()) return;
            if (!c.Latest.Routine.MoveNext()) { if(condition()) return; throw new Exception("Coroutine finished early"); }
            tick?.Invoke();
            Thread.Sleep(1);
        }
        throw new Exception("Coroutine did not reach test checkpoint");
    }
    static void Main()
    {
        var (c,m,s,ui)=Setup();
        Pump(c,()=>c.OfflineWaitingForContinue);
        Check(s.Starts==0 && s.Cycles==0 && ui.Locked,"coarse playback pauses before service startup and contact");
        for(int i=0;i<20;i++) c.Latest.Routine.MoveNext();
        Check(c.OfflineWaitingForContinue && s.Cycles==0 && Math.Abs(m.refs.ikSolver.actuators[0].Control-.02)<.00001,"wait never advances automatically");
        // A replan or visual parameter edit must not mutate this running snapshot.
        c.taskPlaylist.Clear(); c.mockVisionTrajectory[1].actuatorOffsets[0]=20;
        c.ContinueOfflineValidation(); c.ContinueOfflineValidation();
        float peak=0;
        Pump(c,()=>c.OfflineWaitingForContinue && c.OfflineStatus.Contains("目标 2"),()=>peak=Math.Max(peak,m.refs.ikSolver.actuators[0].Control));
        Check(s.Cycles==1 && c.Recorded.Count(n=>n.EndsWith(" / 人工继续"))==1,"double click permits exactly one simulated cycle");
        Check(peak>.025 && peak<.04,"small fine-tune really animates, radians conversion and frozen offsets retained");
        Check(c.Recorded.Contains("return0") && !c.Recorded.Contains("return1"),"return precedes next target fresh wait despite source list being cleared");
        c.ContinueOfflineValidation(); Pump(c,()=>!c.OfflineValidationActive);
        Check(s.Cycles==2 && s.Stops==1 && !ui.Locked && s.OfflineWorkflowOwner==null && RunRecorder.Result==RunRecordResult.Success,"all targets complete and release UI/service ownership");
        (c,m,s,ui)=Setup(); Pump(c,()=>c.OfflineWaitingForContinue);
        c.CancelFullProcessValidation(); c.ContinueOfflineValidation(); Pump(c,()=>!c.OfflineValidationActive);
        Check(s.Cycles==0 && s.Starts==0 && RunRecorder.Result==RunRecordResult.Interrupted && !ui.Locked,"cancel while waiting never starts tightening or next target");
        (c,m,s,ui)=Setup(); Pump(c,()=>c.OfflineWaitingForContinue);
        m.refs.ikSolver.actuators[6].Control=.001f;
        Pump(c,()=>!c.OfflineValidationActive);
        Check(s.Cycles==0 && RunRecorder.Result==RunRecordResult.Failed && !ui.Locked,"waiting pose drift fails with cleanup, including seventh axis");
        (c,m,s,ui)=Setup(); Pump(c,()=>c.OfflineWaitingForContinue);
        m.refs.ikSolver.actuators[0].Control=.03f; c.ContinueOfflineValidation();
        Pump(c,()=>!c.OfflineValidationActive);
        Check(s.Cycles==0 && RunRecorder.Result==RunRecordResult.Failed && c.OfflineStatus.Contains("姿态被改变"),"changed pose at click preserves failure instead of user-cancel result");
        (c,m,s,ui)=Setup(); s.FailCycle=true; Pump(c,()=>c.OfflineWaitingForContinue); c.ContinueOfflineValidation();
        Pump(c,()=>!c.OfflineValidationActive);
        Check(s.Cycles==1 && !c.Recorded.Contains("return0") && !c.Recorded.Contains("coarse1") && RunRecorder.Result==RunRecordResult.Failed,"contact failure prevents return and next target");
        (c,m,s,ui)=Setup(); s.PendingStart=new TaskCompletionSource<ServoTighteningController.CommandResult>();
        Pump(c,()=>c.OfflineWaitingForContinue); c.ContinueOfflineValidation(); Pump(c,()=>s.Starts==1);
        c.CancelFullProcessValidation(); for(int i=0;i<10;i++) c.Latest.Routine.MoveNext();
        Check(c.OfflineValidationActive && ui.Locked,"cancel during asynchronous startup retains ownership until cleanup");
        s.PendingStart.SetResult(new() {Success=true}); Pump(c,()=>!c.OfflineValidationActive);
        Check(s.Cycles==0 && s.Stops==1 && !ui.Locked,"late simulated startup is closed without contact execution");
        (c,m,s,ui)=Setup(1,Math.PI/3);
        int ticks=0;
        Pump(c,()=>c.OfflineWaitingForContinue,()=>ticks++);
        Check(Math.Abs(m.refs.ikSolver.actuators[0].Control-Math.PI/3)<1e-5 && ticks<15,
            "sparse sixty-degree simulation uses shared sampling check without multiplying animation duration");
        c.ContinueOfflineValidation(); Pump(c,()=>!c.OfflineValidationActive);
        Check(s.Cycles==1 && RunRecorder.Result==RunRecordResult.Success,"sparse offline coarse and return preserve manual workflow");
        (c,m,s,ui)=Setup(1,double.NaN);
        Pump(c,()=>!c.OfflineValidationActive);
        Check(s.Starts==0 && s.Cycles==0 && RunRecorder.Result==RunRecordResult.Failed && !ui.Locked,
            "invalid offline sample input fails and cleans up without starting service");
        TestChassisModes();
        Console.WriteLine($"OFFLINE_WORKFLOW_TESTS_PASS {passed}");
    }
    static void RunNested(IEnumerator iterator)
    {
        var stack=new Stack<IEnumerator>(); stack.Push(iterator);
        while(stack.Count>0)
        {
            if(!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if(stack.Peek().Current is IEnumerator child) stack.Push(child);
        }
    }
    static TrajectoryTask ChassisTask() => new() { type=TaskType.Chassis, initialForward=new(0,0,1),
        chassisPoints=new() { new(0,0,0),new(1,0,0),new(2,0,0) } };
    static void TestChassisModes()
    {
        var c=new ConnectCommander(); var task=ChassisTask();
        RunNested(c.TestChassis(task));
        Check(task.isDispatched && c.chassisTCP.Calls.Count==3 && c.chassisTCP.Calls.All(v=>!v.Legacy),"unconfigured chassis route always uses new status mode");
        Check(task.dispatchEvidence.Contains("完成状态已确认"),"new route evidence records API confirmation");
        c=new ConnectCommander(); task=ChassisTask(); var settings=new FieldCommissioningSettings { useLegacyChassisTiming=true };
        c.chassisTCP.OnFirstCall=()=> { settings.useLegacyChassisTiming=false; settings.legacyChassisTiming.translationBufferSeconds=99; c.chassisMoveSpeedX=.1f; task.chassisPoints.Clear(); };
        RunNested(c.TestChassis(task,settings));
        Check(task.isDispatched && c.chassisTCP.Calls.Count==3 && c.chassisTCP.Calls.All(v=>v.Legacy),"legacy route freezes mode and points despite edits during dispatch");
        Check(Math.Abs(c.chassisTCP.Calls[1].Seconds-(1/.3f+5.5))<.00001,"legacy route freezes speed and copied timing");
        Check(task.dispatchEvidence.Contains("未查询任务完成状态"),"legacy evidence never claims actual completion");
        c=new ConnectCommander(); task=ChassisTask(); c.chassisTCP.Result=false;
        RunNested(c.TestChassis(task));
        Check(!task.isDispatched && c.chassisTCP.Calls.Count==1 && !c.chassisTCP.Calls[0].Legacy,"new mode rejection never falls back to legacy or translation");
        c=new ConnectCommander(); task=ChassisTask(); c.chassisTCP.OnFirstCall=()=>c.chassisTCP.CancellationVersion++;
        RunNested(c.TestChassis(task,new() { useLegacyChassisTiming=true }));
        Check(!task.isDispatched && c.chassisTCP.Calls.Count==1 && task.dispatchError.Contains("取消"),"stop between legacy rotation and translation prevents next command");
    }
}
