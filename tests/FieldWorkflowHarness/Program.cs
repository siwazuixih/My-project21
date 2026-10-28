using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnityEngine
{
    public struct Vector3 { public float x, y, z; public float sqrMagnitude => x*x+y*y+z*z; }
    public static class Debug { public static void Log(object o) { } }
    public static class JsonUtility
    {
        static JsonSerializerOptions options = new() { IncludeFields = true };
        public static string ToJson(object value) => JsonSerializer.Serialize(value, options);
        public static T FromJson<T>(string value) => JsonSerializer.Deserialize<T>(value, options);
    }
}
public enum TaskType { Chassis, Arm, Lift }
public enum TrajectoryStage { Unspecified, Navigation, Lift, CoarseArm, ReturnArm }
public class TrajectoryTask
{
    public string taskName, dispatchError, dispatchEvidence;
    public TaskType type;
    public int targetIndex;
    public TrajectoryStage stage;
    public List<double[]> armPoints = new();
    public double[] armStartPose;
    public ArmTrajectorySampling armSampling;
    public List<UnityEngine.Vector3> chassisPoints = new();
    public UnityEngine.Vector3 initialForward;
    public float targetLiftHeight;
    public bool isDispatched;
}
public class RobotStub
{
    public Task MoveFieldJointsAsync(double[] p, FieldCommissioningSettings s, CancellationToken c) => throw new Exception("unexpected hardware path");
    public Task ExecuteFieldTrajectoryAsync(IReadOnlyList<double[]> p, double[] start, FieldCommissioningSettings s, ArmTrajectoryTiming t, CancellationToken c,
        IReadOnlyList<double> intervals = null, string label = null) => throw new Exception("unexpected hardware path");
}
public class LiftStub { public float simToRealScale = 1000; }
public partial class ConnectCommander
{
    public List<TrajectoryTask> taskPlaylist = new();
    public int armJointStartIndex;
    public float armJointSpeedDeg=5, armCommandBufferTime=0, armFirstPointWaitTime=10, armMinWaitTime=.2f, armMaxWaitTime=0;
    public RobotStub dobotTCP = new();
    public LiftStub liftTCP = new();
    List<int> GetTargetOrder() => taskPlaylist.Select(t => t.targetIndex).Distinct().ToList();
    double[] ExtractArmJoints(double[] p) => p.Skip(armJointStartIndex).Take(6).Select(v => v*180/Math.PI).ToArray();
    void StartCoroutine(IEnumerator routine) => throw new Exception("unexpected hardware path");
    IEnumerator SendChassisSegmentedRoutine(TrajectoryTask t, FieldCommissioningSettings s = null) { yield break; }
    IEnumerator SendLiftRoutine(TrajectoryTask t, bool field = false) { yield break; }
}
class Program
{
    static int count;
    static void Check(bool value, string test) { if (!value) throw new Exception(test); Console.WriteLine("PASS " + test); count++; }
    static void Reject(Action action, string test)
    { try { action(); } catch (Exception) { Check(true, test); return; } throw new Exception("Accepted " + test); }
    static FieldCommissioningSettings Settings() => new() { jointMinDeg = Enumerable.Repeat(-90f,6).ToArray(), jointMaxDeg = Enumerable.Repeat(90f,6).ToArray(), pythonExecutable = "/usr/bin/python3" };
    static TrajectoryTask Coarse(int id = 0) => new() { targetIndex=id, type=TaskType.Arm, stage=TrajectoryStage.CoarseArm, armStartPose=new double[6], armPoints=new() { new double[] { .01,0,0,0,0,0 }, new double[] { .02,0,0,0,0,0 } } };
    static async Task Main()
    {
        count += ArmSamplingTests.Run();
        var s = Settings(); s.Validate(true); Check(true, "valid settings");
        Reject(() => new FieldCommissioningSettings().Validate(true), "default real limits locked");
        var bad = s.Copy(); bad.visionJ1Deg = float.NaN; Reject(() => bad.Validate(true), "NaN rejected");
        bad = s.Copy(); bad.contactSpeedMmS=.1f; Reject(() => bad.Validate(true), "oversized contact duration rejected");
        bad = s.Copy(); bad.forceLimitN=31; Reject(() => bad.Validate(true), "hard force ceiling");
        Reject(() => s.CheckJoints(new double[] {100,0,0,0,0,0}), "joint target range");
        var c = new ConnectCommander(); c.taskPlaylist.Add(Coarse());
        var plan = c.SnapshotFieldPlan(s, false);
        Check(plan[0].Count==2 && plan[0][1].stage==TrajectoryStage.ReturnArm && plan[0][1].armPoints.Count==3, "missing return reverses known coarse path");
        plan[0][0].armPoints[0][0]=.5;
        Check(c.taskPlaylist[0].armPoints[0][0]==.01, "plan is deep copied");
        c.taskPlaylist.Add(Coarse()); Reject(() => c.SnapshotFieldPlan(s,false), "duplicate stages rejected");
        c.taskPlaylist.RemoveAt(1); c.taskPlaylist[0].armPoints[1][0]=2;
        Reject(() => c.SnapshotFieldPlan(s,false), "unsafe plan rejected before execution");
        c.taskPlaylist.Clear(); c.taskPlaylist.Add(Coarse()); c.taskPlaylist.Add(Coarse(1));
        plan=c.SnapshotFieldPlan(s,true); Check(plan.Count==2, "multiple targets snapshot");
        c.taskPlaylist[1].armStartPose[0]=.1;
        Reject(() => c.SnapshotFieldPlan(s,true), "inconsistent next start rejected");
        var expected="CoarseArm,V,C,ReturnArm,D,CoarseArm,V,C,ReturnArm,D";
        var calls=new List<string>();
        await FieldWorkflowSequence.ExecuteAsync(plan, t => { calls.Add(t.stage.ToString()); return Task.CompletedTask; }, () => { calls.Add("V"); return Task.CompletedTask; }, () => { calls.Add("C"); return Task.CompletedTask; }, i => calls.Add("D"), CancellationToken.None);
        Check(string.Join(",",calls)==expected, "two targets ordered with return after contact");
        calls.Clear();
        try { await FieldWorkflowSequence.ExecuteAsync(plan, t => { calls.Add(t.stage.ToString()); return Task.CompletedTask; }, () => Task.CompletedTask, () => throw new Exception("NG"), i => calls.Add("D"), CancellationToken.None); } catch (Exception) { }
        Check(string.Join(",",calls)=="CoarseArm", "contact failure prevents return and next target");
        calls.Clear(); using var stop=new CancellationTokenSource();
        try { await FieldWorkflowSequence.ExecuteAsync(plan, t => { calls.Add(t.stage.ToString()); return Task.CompletedTask; }, () => { stop.Cancel(); return Task.CompletedTask; }, () => { calls.Add("C"); return Task.CompletedTask; }, i => calls.Add("D"), stop.Token); } catch (OperationCanceledException) { }
        Check(string.Join(",",calls)=="CoarseArm", "cancel after vision prevents contact");
        var timing = c.CaptureArmTrajectoryTiming();
        Check(Math.Abs(timing.DelaySeconds(new double[6],new double[]{.5,0,0,0,0,0})-.2)<.00001,"existing minimum send interval retained");
        Check(Math.Abs(timing.DelaySeconds(new double[6],new double[]{5,0,0,0,0,0})-1)<.00001,"angle-based pacing retained, not arrival");
        Check(timing.DelaySeconds(null,new double[6])==10,"first fallback retained");
        Reject(()=>new ArmTrajectoryTiming(0,0,0,0,0),"zero estimator rejected");
        Reject(()=>new ArmTrajectoryTiming(5,0,0,1,.2),"inconsistent interval clamp rejected");
        var gate = new ManualTargetGate();
        var paused = new double[6];
        Task waiting=gate.WaitAsync(1,paused,CancellationToken.None); int version=gate.Generation;
        Check(gate.Waiting && !waiting.IsCompleted,"manual gate actually waits, no automatic skip");
        Check(!gate.TryContinue(version,2,paused,.2),"wrong target cannot continue");
        Check(gate.TryContinue(version,1,paused,.2),"manual confirmation releases current target");
        Check(!gate.TryContinue(version,1,paused,.2),"double click releases at most once"); await waiting;
        waiting=gate.WaitAsync(2,paused,CancellationToken.None);
        Reject(()=>gate.TryContinue(gate.Generation,2,new double[]{1,0,0,0,0,0},.2),"changed pose invalidates continue");
        try { await waiting; throw new Exception("pose change ignored"); } catch(InvalidOperationException) { Check(!gate.Waiting,"changed pose permanently faults this gate"); }
        Check(!gate.TryContinue(gate.Generation,2,paused,.2),"returning to old pose does not revive invalidated permission");
        using var cancelled=new CancellationTokenSource();
        waiting=gate.WaitAsync(2,paused,cancelled.Token); cancelled.Cancel();
        try { await waiting; throw new Exception("cancel ignored"); } catch(OperationCanceledException) { Check(true,"stop cancels manual wait"); }
        Check(!gate.Waiting && !gate.TryContinue(gate.Generation,2,paused,.2),"stop cannot be resumed by clicking continue");
        waiting=gate.WaitAsync(3,paused,CancellationToken.None);
        Check(!gate.TryContinue(version,3,paused,.2),"old callback cannot release new target");
        gate.Cancel(); try { await waiting; } catch(OperationCanceledException) { }
        c.taskPlaylist.Clear(); c.taskPlaylist.Add(Coarse()); c.taskPlaylist.Add(Coarse(1));
        plan=c.SnapshotSemiAutomaticPlan(s,true);
        Check(plan[0][1].armStartPose.SequenceEqual(plan[0][0].armPoints.Last()),"reverse path has explicit coarse endpoint start");
        calls.Clear(); int currentTarget=0;
        Task sequence=FieldWorkflowSequence.ExecuteAsync(plan,
            t=>{ currentTarget=t.targetIndex+1; calls.Add(t.stage.ToString()); return Task.CompletedTask; },
            ()=>gate.WaitAsync(currentTarget,new double[6],CancellationToken.None),
            ()=>{ calls.Add("C"); return Task.CompletedTask; },i=>calls.Add("D"),CancellationToken.None);
        Check(string.Join(",",calls)=="CoarseArm" && gate.Waiting,"coarse arrival does not send contact/return/next target");
        gate.TryContinue(gate.Generation,1,new double[6],.2);
        for(int i=0;i<100 && !(gate.Waiting && gate.Target==2);i++) await Task.Delay(10);
        Check(gate.Waiting && gate.Target==2 && string.Join(",",calls)=="CoarseArm,C,ReturnArm,D,CoarseArm","next target waits for fresh human confirmation");
        gate.TryContinue(gate.Generation,2,new double[6],.2); await sequence;
        Check(string.Join(",",calls)=="CoarseArm,C,ReturnArm,D,CoarseArm,C,ReturnArm,D","semi automatic ordering without mock vision");
        using (var offline = new OfflineTargetSession())
        {
            var controls = new double[7];
            var wait = offline.WaitAsync(1, controls);
            int ticket = offline.Generation;
            Check(offline.Waiting && !wait.IsCompleted, "offline coarse completion pauses before fine tuning");
            Check(!offline.Continue(ticket, 2, controls), "offline wrong target rejected");
            Check(offline.Continue(ticket, 1, controls), "offline user releases current target");
            Check(!offline.Continue(ticket, 1, controls), "offline double click ignored"); await wait;
            wait = offline.WaitAsync(2, controls);
            Check(!offline.Continue(ticket, 2, controls), "offline prior target ticket cannot release next");
            var changed = (double[])controls.Clone(); changed[6] = .001;
            Reject(() => offline.Continue(offline.Generation, 2, changed), "offline seventh axis changes invalidate wait");
            try { await wait; } catch (OperationCanceledException) { }
            Check(!offline.Waiting && !offline.Continue(offline.Generation, 2, controls), "offline pose failure cannot be revived");
        }
        using (var offline = new OfflineTargetSession())
        {
            var controls = new double[7]; var wait = offline.WaitAsync(1, controls); offline.Cancel();
            try { await wait; } catch (OperationCanceledException) { }
            Check(!offline.Waiting && !offline.Continue(offline.Generation, 1, controls), "offline stop invalidates continuation");
        }
        var legacySettings = Settings();
        Check(!legacySettings.useLegacyChassisTiming && !UnityEngine.JsonUtility.FromJson<FieldCommissioningSettings>("{}").useLegacyChassisTiming,"new and missing-field configurations default to status mode");
        legacySettings.useLegacyChassisTiming = true;
        var frozenLegacy = legacySettings.Copy(); legacySettings.legacyChassisTiming.rotationBufferSeconds = 44;
        Check(frozenLegacy.useLegacyChassisTiming && frozenLegacy.legacyChassisTiming.rotationBufferSeconds==5,"legacy mode and timing are deep copied into run snapshot");
        Check(Math.Abs(frozenLegacy.legacyChassisTiming.RotationSeconds(Math.PI,.5)-(Math.PI/.5+5.5))<.00001,"legacy rotation uses original radians/speed plus buffers");
        Check(Math.Abs(frozenLegacy.legacyChassisTiming.TranslationSeconds(.3,.3)-6.5)<.00001,"legacy translation uses original metres/speed plus buffers");
        frozenLegacy.legacyChassisTiming=null; frozenLegacy.Validate(false);
        Check(frozenLegacy.legacyChassisTiming.commandBufferSeconds==.5f,"missing nested timing gets safe defaults");
        var invalidTiming=new ChassisLegacyTiming { rotationBufferSeconds=float.NaN };
        Reject(()=>invalidTiming.Validate(),"invalid legacy buffers rejected");
        invalidTiming=new ChassisLegacyTiming { maxWaitSeconds=1 };
        Reject(()=>invalidTiming.TranslationSeconds(1,.1),"legacy maximum cannot truncate estimated motion duration");
        Reject(()=>new ChassisLegacyTiming().TranslationSeconds(1,0),"legacy zero speed rejected");
        Console.WriteLine($"FIELD_TESTS_PASS {count}");
    }
}
