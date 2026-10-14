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
    public string taskName, dispatchError;
    public TaskType type;
    public int targetIndex;
    public TrajectoryStage stage;
    public List<double[]> armPoints = new();
    public double[] armStartPose;
    public List<UnityEngine.Vector3> chassisPoints = new();
    public UnityEngine.Vector3 initialForward;
    public float targetLiftHeight;
    public bool isDispatched;
}
public class RobotStub
{
    public Task MoveFieldJointsAsync(double[] p, FieldCommissioningSettings s, CancellationToken c) => throw new Exception("unexpected hardware path");
}
public class LiftStub { public float simToRealScale = 1000; }
public partial class ConnectCommander
{
    public List<TrajectoryTask> taskPlaylist = new();
    public int armJointStartIndex;
    public RobotStub dobotTCP = new();
    public LiftStub liftTCP = new();
    List<int> GetTargetOrder() => taskPlaylist.Select(t => t.targetIndex).Distinct().ToList();
    double[] ExtractArmJoints(double[] p) => p.Skip(armJointStartIndex).Take(6).Select(v => v*180/Math.PI).ToArray();
    void StartCoroutine(IEnumerator routine) => throw new Exception("unexpected hardware path");
    IEnumerator SendChassisSegmentedRoutine(TrajectoryTask t) { yield break; }
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
        Console.WriteLine($"FIELD_TESTS_PASS {count}");
    }
}
