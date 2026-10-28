// Production Gateway + Commissioning, strict loopback controller; no real hardware.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace UnityEngine
{
    public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string s) {} }
    public sealed class SerializeField : Attribute {}
    public static class Debug
    {
        public static readonly List<string> Logs = new();
        public static void Log(object o) { lock(Logs) Logs.Add(o.ToString()); }
        public static void LogWarning(object o) => Log(o);
        public static void LogError(object o) => Log(o);
    }
    public static class JsonUtility
    {
        static readonly JsonSerializerOptions options = new() { IncludeFields = true };
        public static string ToJson(object value) => JsonSerializer.Serialize(value, options);
        public static T FromJson<T>(string value) => JsonSerializer.Deserialize<T>(value, options);
    }
}
public partial class DobotController : IDisposable
{
    private TcpClient tcpClient, feedbackClient;
    private NetworkStream networkStream;
    private bool controlModeConfirmed = true;
    private readonly object feedbackLock = new();
    private int currentRobotMode = 5;
    private double lastFeedbackUnixSeconds;
    private double[] actualJoints = new double[6];
    private readonly CancellationTokenSource lifetime = new();
    private TcpListener server;
    private TcpClient peer;
    public bool Arrive = true, Feed = true, WrongId, EchoMismatch, SensorOnline = true;
    public bool HoldFrame, HoldServo, EmptyFrameId;
    public bool Drift;
    public bool DeferTrajectory;
    public void OverrideMotionId(ulong id) { lock(feedbackLock) currentCommandId=id; }
    public double ServoFraction = 1;
    public bool RotateToolAxis;
    public void SetNoisySpeed() { lock(feedbackLock) protocolJointSpeeds[0]=0.067851; }
    public async Task OriginalStep(bool stop)
    {
        var reply = await HandleRobotRequestAsync(algorithm, new RobotRequest { op="move_relative", token=ownerToken,
            delta=new[]{0.0,0.1,0.0}, duration=0.05, original_timing=true, stop_at_end=stop });
        if(reply.result!="interval_elapsed") throw new Exception("wrong timing completion semantics");
    }
    public async Task FinishStage() => await HandleRobotRequestAsync(algorithm, new RobotRequest { op="finish_stage", token=ownerToken });
    public Task BeginMeasurement(string stage, double distance) => BeginDiagnosticStageAsync(new RobotRequest { stage=stage, planned_distance_mm=distance });
    public Task EndMeasurement() => EndDiagnosticStageAsync();
    public async Task ProbeLatency(string query)
    {
        var result=await HandleRobotRequestAsync(algorithm,new RobotRequest { op="latency_query", query=query, user=0, tool=1 });
        if(result.benchmark_schema!="latency-v1-2026-10-08" || result.values.Length!=6 || result.dashboard_transport_ms<0)
            throw new Exception("invalid benchmark response");
    }
    public Task MeasuredStep() => HandleRobotRequestAsync(algorithm, new RobotRequest { op="move_relative", token=ownerToken, delta=new[]{-0.2,0.0,0.0}, duration=0.05, original_timing=true, stop_at_end=false });
    public bool CheckMeasurementStale()
    {
        var s=CopyRobotState(); s.age_s=1; return MeasurementValid(s);
    }
    public bool CheckMeasurementWrongFrame()
    {
        var s=CopyRobotState(); s.tool=49; return MeasurementValid(s);
    }
    private readonly TcpClient algorithm = new();
    public readonly List<string> Commands = new();
    public static string FormatSpeed(double v) => SpeedCommand(v);
    public static ulong ReadId(string reply) => ParseCommandId(reply);
    public static byte[] Packet(ulong stamp)
    {
        var b = new byte[1440];
        BitConverter.GetBytes((ushort)1440).CopyTo(b, 0);
        BitConverter.GetBytes(0x123456789ABCDEFUL).CopyTo(b, 48);
        BitConverter.GetBytes(stamp).CopyTo(b, 32);
        BitConverter.GetBytes(5UL).CopyTo(b, 24);
        return b;
    }
    public string ParseTest(byte[] b)
    {
        RecordFeedbackPacket(false); ParseFeedbackPacket(b);
        return StopDiagnostic(out _, -1, out _) + " " + FeedbackDiagnosticCounters();
    }
    public void ForceDelayedBaseline() { feedbackClockOffset -= 1; }
    public Task<int> ReadTest(Stream stream, byte[] buffer) => ReadExactAsync(stream, buffer, buffer.Length);
    public string DiagnosticCase(string kind)
    {
        lock(feedbackLock)
        {
            protocolFeedbackSequence=10;
            lastFeedbackUnixSeconds=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000.0;
            if(kind=="speed") protocolJointSpeeds[2]=0.02;
            if(kind=="nan") protocolJointSpeeds[0]=double.NaN;
            if(kind=="age") lastFeedbackUnixSeconds-=1;
            if(kind=="mode") currentRobotMode=7;
            if(kind=="error") robotError=1;
            if(kind=="collision") robotCollision=1;
            return StopDiagnostic(out _,kind=="frame" ? 10 : 9,out _);
        }
    }
    public async Task AcquireContact()
    { await HandleRobotRequestAsync(algorithm, new RobotRequest { op="acquire", simulate=false }); }
    public Task ReleaseContact() => HandleRobotRequestAsync(algorithm, new RobotRequest { op="release", token=ownerToken });
    public async Task SmallContactStep()
    { await HandleRobotRequestAsync(algorithm, new RobotRequest { op="move_relative", token=ownerToken, delta=new[]{0.0,0.1,0.0}, duration=0.05 }); }
    public async Task Open()
    {
        server = new TcpListener(IPAddress.Loopback, 0); server.Start();
        tcpClient = new TcpClient(); await tcpClient.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)server.LocalEndpoint).Port);
        peer = await server.AcceptTcpClientAsync(); networkStream = tcpClient.GetStream();
        lastFeedbackUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000.0;
        _ = Serve(); _ = FeedLoop();
    }
    async Task FeedLoop()
    {
        while (!lifetime.IsCancellationRequested)
        {
            lock (feedbackLock) if (Feed)
            {
                var b = Packet((ulong)Environment.TickCount64);
                if(Drift) actualJoints[0]+=0.002;
                b[1037] = (byte)(SensorOnline ? 1 : 0);
                b[1012] = (byte)actualUser; b[1013] = (byte)actualTool;
                BitConverter.GetBytes(currentCommandId).CopyTo(b,1112);
                for(int i=0;i<6;i++)
                {
                    BitConverter.GetBytes(actualJoints[i]).CopyTo(b,432+i*8);
                    BitConverter.GetBytes(actualToolPose[i]).CopyTo(b,624+i*8);
                    BitConverter.GetBytes(protocolJointSpeeds[i]).CopyTo(b,480+i*8);
                }
                RecordFeedbackPacket(false); ParseFeedbackPacket(b);
            }
            await Task.Delay(10);
        }
    }
    async Task Serve()
    {
        try
        {
            using var reader = new StreamReader(peer.GetStream());
            using var writer = new StreamWriter(peer.GetStream()) { AutoFlush = true };
            ulong id = 100;
            while (!lifetime.IsCancellationRequested)
            {
                var command = await reader.ReadLineAsync(); if (command == null) return;
                lock (Commands) Commands.Add(command);
                string data = ""; int error = 0;
                if (command.StartsWith("SpeedFactor"))
                { if (!Regex.IsMatch(command, @"^SpeedFactor\([1-9][0-9]?\)$")) error = -30001; }
                else if (command.StartsWith("MovJ"))
                {
                    data = (++id).ToString(CultureInfo.InvariantCulture);
                    if (Arrive && (!DeferTrajectory || command.EndsWith("cp=0)"))) lock (feedbackLock)
                    {
                        actualJoints = command.Split('{')[1].Split('}')[0].Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                        currentCommandId = WrongId ? id-1 : id;
                    }
                }
                else if (command.StartsWith("User(") || command.StartsWith("Tool("))
                {
                    data=(++id).ToString(CultureInfo.InvariantCulture);
                    if (!HoldFrame) lock(feedbackLock)
                    {
                        currentCommandId=id;
                        int value=int.Parse(Regex.Match(command,@"\d+").Value);
                        if(command.StartsWith("User")) actualUser=value; else actualTool=value;
                    }
                    if (EmptyFrameId) data="";
                }
                else if (command.StartsWith("GetPose(")) data=Numbers(actualToolPose);
                else if (command=="GetAngle()") data=Numbers(actualJoints);
                else if (command.StartsWith("GetForce(")) data="0,0,0,0,0,0";
                else if (command.StartsWith("RelPointTool("))
                {
                    var groups=Regex.Matches(command,@"\{([^}]+)\}");
                    var p=groups[0].Groups[1].Value.Split(',').Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();
                    var d=groups[1].Groups[1].Value.Split(',').Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();
                    if (RotateToolAxis) { (d[0], d[1])=(-d[1],d[0]); }
                    data=Numbers(p.Zip(d,(a,b)=>a+b).ToArray());
                }
                else if (command.StartsWith("ServoP("))
                {
                    data=(++id).ToString(CultureInfo.InvariantCulture);
                    lock(feedbackLock)
                    {
                        currentCommandId=id;
                        if (!HoldServo)
                        {
                            var target=command.Substring(7).Split(',').Take(6).Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();
                            for(int i=0;i<6;i++) actualToolPose[i]+=(target[i]-actualToolPose[i])*ServoFraction;
                        }
                    }
                }
                else if (command != "Stop()") error = -10000;
                await writer.WriteAsync($"{error},{{{data}}},{(EchoMismatch ? "Wrong()" : command)};");
                await writer.FlushAsync();
            }
        }
        catch (IOException) {} catch (ObjectDisposedException) {}
    }
    public void Dispose() { EndMotionDiagnostics(); lifetime.Cancel(); algorithm.Close(); tcpClient?.Close(); peer?.Close(); server?.Stop(); }
}
class Program
{
    private sealed class FragmentedStream : MemoryStream
    {
        public FragmentedStream(byte[] bytes) : base(bytes) { }
        public override Task<int> ReadAsync(byte[] b, int offset, int count, CancellationToken token)
            => base.ReadAsync(b, offset, Math.Min(count, 17), token);
    }
    static int count;
    static void Check(bool result, string name) { if (!result) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
    static async Task Reject(Func<Task> action, string name)
    { try { await action(); } catch { Check(true, name); return; } throw new Exception("Accepted " + name); }
    static FieldCommissioningSettings Settings() => new() { jointMinDeg=Enumerable.Repeat(-90f,6).ToArray(), jointMaxDeg=Enumerable.Repeat(90f,6).ToArray(), jointTimeoutSeconds=1 };
    static async Task Main()
    {
        using (var history = new DobotController())
        {
            Check(!history.HistoryCollisionObserved, "history does not invent collision without feedback");
            history.DiagnosticCase("collision");
            Check(history.HistoryCollisionObserved, "history classifies fresh physical collision flag");
            history.DiagnosticCase("age");
            Check(!history.HistoryCollisionObserved, "history ignores stale collision flag");
        }
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        using (var parser = new DobotController())
        {
            var b = DobotController.Packet(1000);
            using var fragments = new FragmentedStream(b);
            byte[] restored = new byte[1440];
            Check(await parser.ReadTest(fragments, restored) == 1440 && b.SequenceEqual(restored), "fragmented stream assembles exact feedback packet");
            using var truncated = new FragmentedStream(new byte[80]);
            Check(await parser.ReadTest(truncated, restored) == 80, "partial EOF is not a complete feedback packet");
            BitConverter.GetBytes(-145.6011).CopyTo(b,448);
            BitConverter.GetBytes(ulong.MaxValue).CopyTo(b,1112);
            BitConverter.GetBytes(0.02).CopyTo(b,496);
            string first = parser.ParseTest(b);
            Check(first.Contains("-145.601100") && first.Contains("J3_speed") && first.Contains("18446744073709551615"), "1440-byte signed joints, speed offset and uint64 ID");
            Check(parser.ParseTest(b).Contains("old_timestamp_total=1"), "duplicate raw timestamp rejected");
            Check(parser.ParseTest(DobotController.Packet(999)).Contains("old_timestamp_total=2"), "backward raw timestamp rejected");
            b = DobotController.Packet(1001); b[48]=0;
            Check(parser.ParseTest(b).Contains("bad_header_total=1"), "raw magic rejection counted");
            parser.ForceDelayedBaseline();
            Check(parser.ParseTest(DobotController.Packet(1001)).Contains("delayed_total=1"), "raw relative-delay rejection counted");
            await Reject(()=>{parser.ParseTest(new byte[10]); return Task.CompletedTask;}, "short feedback rejected");
        }
        string journalDir = Path.Combine(Path.GetTempPath(), "commissioning-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(journalDir);
        try
        {
            using var journal = new CommissioningJournal(journalDir, 80);
            journal.Append(new string('a',60));
            Check(File.ReadAllText(Path.Combine(journalDir,"stop-diagnostic.log")).Contains("aaa"), "diagnostics readable before completion/dispose");
            journal.Append(new string('b',60));
            Check(File.Exists(Path.Combine(journalDir,"stop-diagnostic.log.previous")), "bounded journal rotates evidence");
        }
        finally { Directory.Delete(journalDir,true); }
        using (var journal = new CommissioningJournal(Path.Combine(journalDir,"missing")))
        {
            journal.Append("must not throw or block a stop");
            Check(journal.Error != null, "journal IO failure is reported without throwing");
        }
        using (var parser = new DobotController())
        {
            var b = DobotController.Packet(1);
            BitConverter.GetBytes(double.NaN).CopyTo(b,480);
            Check(parser.ParseTest(b).Contains("J1_speed"), "raw nonfinite speed fails stop predicate");
        }
        Check(DobotController.FormatSpeed(1)=="SpeedFactor(1)", "production integer speed under comma-decimal culture");
        await Reject(()=>{DobotController.FormatSpeed(1.5); return Task.CompletedTask;}, "fractional speed rejected");
        Check(DobotController.ReadId("0,{18446744073709551615},MovJ();")==ulong.MaxValue, "queue ID keeps uint64 precision");
        await Reject(()=>{DobotController.ReadId("0,{},MovJ();"); return Task.CompletedTask;}, "missing queue ID rejected");
        var s = Settings(); var goal = new double[]{1,1,0,0,0,0};
        using(var r=new DobotController())
        {
            await r.Open(); await r.ProbeLatency("pose"); await r.ProbeLatency("angle"); await r.ProbeLatency("force");
            Check(r.Commands.All(c=>c.StartsWith("Get")),"latency probe only sends read queries, without acquire/Stop/frame changes");
            int sent=r.Commands.Count;
            await Reject(()=>r.ProbeLatency("ServoP"),"arbitrary benchmark command rejected");
            Check(r.Commands.Count==sent,"rejected probe sends no robot command");
            r.BeginFieldWorkflow(s);
            await Reject(()=>r.ProbeLatency("pose"),"latency probe cannot interfere with active workflow");
        }
        foreach (var fraction in new[]{0.0,0.5,1.0})
        {
            using var r=new DobotController { ServoFraction=fraction, HoldServo=fraction==0, RotateToolAxis=true };
            var setting=Settings(); setting.originalContactTiming=true;
            await r.Open(); r.BeginFieldWorkflow(setting); r.PermitFieldContact(true); await r.AcquireContact();
            string directory=Path.Combine(Path.GetTempPath(),"motion-measurement-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            r.BeginMotionDiagnostics(directory); await r.BeginMeasurement("INSERTING",.2);
            await r.MeasuredStep(); await r.FinishStage(); await r.EndMeasurement(); r.EndMotionDiagnostics();
            var rows=File.ReadAllLines(Path.Combine(directory,"motion.csv"));
            var end=rows.Single(x=>x.Contains("\"stage_end\""));
            double axial=double.Parse(end.Split(',')[30].Trim('"'),CultureInfo.InvariantCulture);
            Check(Math.Abs(axial-.2*fraction)<.00001,"actual rotated-tool displacement measured, fraction="+fraction);
            Check(rows.Any(x=>x.Contains("\"step_begin\"") && x.Contains("ServoP(")),"raw command and target saved");
            Check(rows.Any(x=>x.Contains("\"step_end\"")),"interval endpoint measured without claiming arrival");
            Check(!r.CheckMeasurementStale() && !r.CheckMeasurementWrongFrame(),"stale and wrong-frame measurements invalid");
        }
        using (var r = new DobotController())
        {
            var experimental = Settings(); experimental.originalContactTiming=true;
            await r.Open(); r.BeginFieldWorkflow(experimental); r.SetNoisySpeed();
            await r.ConfirmFieldIdleAsync(CancellationToken.None);
            Check(true,"experimental stationary position window accepts noisy QDActual");
            r.PermitFieldContact(true); await r.AcquireContact(); r.HoldServo=true;
            int before=r.Commands.Count(c=>c=="Stop()");
            await r.OriginalStep(false);
            Check(r.Commands.Count(c=>c=="Stop()")==before,"axis interval adds no Stop and no fabricated arrival");
            await r.OriginalStep(true);
            Check(r.Commands.Count(c=>c=="Stop()")==before+1,"search interval keeps one Stop");
            await r.FinishStage();
            Check(r.Commands.Count(c=>c=="Stop()")==before+2,"stage boundary stop retained");
            r.Drift=true;
            await Reject(()=>r.FinishStage(),"experimental stop rejects drifting position");
            r.Drift=false; r.Feed=false;
            await Reject(()=>r.FinishStage(),"experimental stop rejects stale feedback");
        }
        using (var r = new DobotController())
        {
            await r.Open(); r.BeginFieldWorkflow(s); r.PermitFieldContact(true); await r.AcquireContact();
            await Reject(()=>r.OriginalStep(false),"real original timing requires explicit Unity authorization");
        }
        foreach(var pair in new[]{("speed","J3_speed"),("nan","J1_speed"),("age","feedback_age"),("mode","mode_not_5"),("error","robot_error"),("collision","collision"),("frame","no_new_frame")})
        {
            using var diagnostic = new DobotController();
            Check(diagnostic.DiagnosticCase(pair.Item1).Contains(pair.Item2), "diagnostic identifies " + pair.Item1);
        }
        using (var r = new DobotController())
        {
            await r.Open(); r.BeginFieldWorkflow(s); await r.MoveFieldJointsAsync(goal,s,CancellationToken.None);
            Check(r.Commands[0]=="SpeedFactor(1)" && r.Commands[1].Contains("1.000000,1.000000"), "actual wire format passes strict controller");
            await r.EndFieldWorkflowAsync(); Check(!r.FieldWorkflowActive, "fresh stationary frames release lock");
        }
        foreach (string scenario in new[]{"no arrival", "wrong ID", "echo mismatch", "stale feedback", "step limit", "cancel"})
        {
            using var r = new DobotController(); await r.Open(); r.BeginFieldWorkflow(s);
            if (scenario=="no arrival" || scenario=="cancel") r.Arrive=false;
            if (scenario=="wrong ID") r.WrongId=true;
            if (scenario=="echo mismatch") r.EchoMismatch=true;
            if (scenario=="stale feedback") { r.Feed=false; await Task.Delay(300); }
            using var c = new CancellationTokenSource(); if(scenario=="cancel") c.CancelAfter(100);
            await Reject(()=>r.MoveFieldJointsAsync(scenario=="step limit" ? new double[]{6,0,0,0,0,0}:goal,s,c.Token), scenario+" rejected");
        }
        using (var r = new DobotController())
        {
            await r.Open(); r.BeginFieldWorkflow(s); r.Feed=false;
            await Reject(()=>r.ConfirmFieldIdleAsync(CancellationToken.None),"pre-motion stop fails without new feedback");
            Check(!r.Commands.Any(c=>c.StartsWith("MovJ")),"failed pre-motion stop sends no MovJ");
            await Reject(()=>r.EndFieldWorkflowAsync(),"cached idle cannot confirm Stop");
            Check(r.FieldWorkflowActive,"unconfirmed stop retains lock");
        }
        using (var r = new DobotController { SensorOnline=false })
        {
            await r.Open(); r.BeginFieldWorkflow(s);
            await Reject(()=>{ r.PermitFieldContact(true); return Task.CompletedTask; },"offline force sensor blocks contact");
        }
        using (var r = new DobotController())
        {
            s.feedbackPort=30005; await r.Open(); r.BeginFieldWorkflow(s);
            await Reject(()=>{ r.PermitFieldContact(true); return Task.CompletedTask; },"slow feedback port blocks contact");
        }
        s.feedbackPort=30004;
        // Only a loopback fake robot: intermediate feedback deliberately does not reach any sample.
        var adapted=ArmTrajectorySampling.Build(new[]{new[]{Math.PI/3,0,0,0,0,0}},new double[6],0,5,"目标1/CoarseArm");
        var adaptedPoints=adapted.Points.Select(q=>ArmTrajectorySampling.JointsDeg(q,0)).ToList();
        var fastPacing=new ArmTrajectoryTiming(100000,0,10,0,0);
        using(var r=new DobotController { DeferTrajectory=true })
        {
            await r.Open(); r.BeginFieldWorkflow(s);
            await r.ExecuteFieldTrajectoryAsync(adaptedPoints,new double[6],s,fastPacing,CancellationToken.None,
                adapted.SendIntervals(fastPacing),"目标1/CoarseArm");
            var commands=r.Commands.Where(x=>x.StartsWith("MovJ")).ToArray();
            Check(commands.Length==12,"adapted planned edge dispatches all twelve samples");
            Check(commands.Take(11).All(x=>x.Contains("cp=50")) && commands.Last().Contains("cp=0"),"adapted path keeps continuous intermediate CP and exact endpoint boundary");
            Check(!r.Commands.Contains("Stop()"),"resampling adds no individual Stop or waypoint arrival wait");
            Check(Math.Abs(r.RequireFieldTrajectoryHold()[0]-60)<1e-5,"adapted path still confirms fresh actual final joint angle");
        }
        foreach(string failure in new[]{"NaN","count","no final arrival"})
        {
            using var r=new DobotController {DeferTrajectory=true}; await r.Open(); r.BeginFieldWorkflow(s);
            var intervals=adapted.SendIntervals(fastPacing);
            if(failure=="NaN") intervals[1]=double.NaN;
            if(failure=="count") intervals.RemoveAt(0);
            if(failure=="no final arrival") r.Arrive=false;
            await Reject(()=>r.ExecuteFieldTrajectoryAsync(adaptedPoints,new double[6],s,fastPacing,CancellationToken.None,intervals),"adapted path rejects "+failure);
            if(failure!="no final arrival") Check(!r.Commands.Any(x=>x.StartsWith("MovJ")),"bad sampled schedule sends zero motion commands");
        }
        var pacing = new ArmTrajectoryTiming(5,0,10,.02,0);
        var path = new List<double[]> { new[]{.5,0,0,0,0,0}, new[]{1.0,0,0,0,0,0}, new[]{1.5,0,0,0,0,0} };
        using(var r=new DobotController { DeferTrajectory=true })
        {
            await r.Open(); r.BeginFieldWorkflow(s);
            await r.ExecuteFieldTrajectoryAsync(path,new double[6],s,pacing,CancellationToken.None);
            var moves=r.Commands.Where(x=>x.StartsWith("MovJ")).ToArray();
            Check(moves.Length==3 && moves[0].Contains("cp=50") && moves[1].Contains("cp=50") && moves[2].Contains("cp=0"),"continuous intermediate commands and exact final boundary");
            Check(!r.Commands.Contains("Stop()"),"continuous trajectory adds no per-point Stop");
            Check(Math.Abs(r.RequireFieldTrajectoryHold()[0]-1.5)<.00001,"only final command fresh actual arrival confirms hold");
            r.PermitFieldContact(true); await r.AcquireContact(); await r.SmallContactStep();
            await r.ReleaseContact(); r.PermitFieldContact(false);
            await r.ExecuteFieldTrajectoryAsync(new List<double[]> {new[]{1.0,0,0,0,0,0},new[]{.5,0,0,0,0,0},new double[6]},path.Last(),s,pacing,CancellationToken.None);
            Check(Math.Abs(r.RequireFieldTrajectoryHold()[0])<.00001,"contact lease release hands back to confirmed continuous return");
            r.Feed=false; await Task.Delay(300);
            await Reject(()=>{r.RequireFieldTrajectoryHold();return Task.CompletedTask;},"stale feedback revokes manual continue");
        }
        using(var r=new DobotController { DeferTrajectory=true })
        {
            await r.Open(); r.BeginFieldWorkflow(s);
            var many=Enumerable.Range(1,20).Select(i=>new[]{i*.01,0,0,0,0,0}).ToList();
            await Reject(()=>r.ExecuteFieldTrajectoryAsync(many,new double[6],s,new ArmTrajectoryTiming(5,0,0,0,0),CancellationToken.None),"stalled queue does not accept unlimited points");
            Check(r.Commands.Count(x=>x.StartsWith("MovJ"))==16,"outstanding trajectory window capped at 16");
        }
        foreach(string scenario in new[]{"no arrival","wrong ID","echo mismatch","cancel","wrong start","unsafe path"})
        {
            using var r=new DobotController(); await r.Open(); r.BeginFieldWorkflow(s);
            if(scenario=="no arrival") r.Arrive=false;
            if(scenario=="wrong ID") r.WrongId=true;
            if(scenario=="echo mismatch") r.EchoMismatch=true;
            using var cancelled=new CancellationTokenSource(); if(scenario=="cancel") cancelled.CancelAfter(50);
            var selected=scenario=="unsafe path" ? new List<double[]> {new[]{7.0,0,0,0,0,0}} : path;
            await Reject(()=>r.ExecuteFieldTrajectoryAsync(selected,scenario=="wrong start" ? new[]{1.0,0,0,0,0,0} : new double[6],s,pacing,cancelled.Token),"trajectory "+scenario+" rejected");
            if(scenario=="wrong start" || scenario=="unsafe path") Check(!r.Commands.Any(x=>x.StartsWith("MovJ")),"invalid path/start sends no movement");
            if(scenario=="cancel") Check(r.Commands.Count(x=>x.StartsWith("MovJ"))<3,"cancel stops further trajectory dispatch");
        }
        using(var r=new DobotController())
        {
            await r.Open(); r.BeginFieldWorkflow(s);
            var moving=r.ExecuteFieldTrajectoryAsync(path,new double[6],s,new ArmTrajectoryTiming(5,0,0,.2,0),CancellationToken.None);
            await Task.Delay(50); await r.StopFieldMotionAsync();
            await Reject(()=>moving,"latched physical stop cancels ongoing paced dispatch");
            Check(r.Commands.Count(x=>x.StartsWith("MovJ"))==1,"stop does not leave later trajectory points queued");
        }
        using(var r=new DobotController())
        {
            await r.Open(); r.BeginFieldWorkflow(s);
            await r.ExecuteFieldTrajectoryAsync(path,new double[6],s,pacing,CancellationToken.None);
            r.OverrideMotionId(999);
            await Reject(()=>{r.RequireFieldTrajectoryHold(); return Task.CompletedTask;},"other command after arrival revokes manual permission");
        }
        using (var r = new DobotController())
        {
            await r.Open(); r.BeginFieldWorkflow(s); r.PermitFieldContact(true);
            await r.AcquireContact(); await r.SmallContactStep();
            Check(r.Commands.Any(c=>c=="GetPose(user=0,tool=1)") && r.Commands.Any(c=>c.EndsWith("aheadtime=50.0,gain=500.0)")), "confirmed User/Tool and explicit pose frame before ServoP");
        }
        using (var r = new DobotController { EmptyFrameId=true })
        {
            await r.Open(); r.BeginFieldWorkflow(s); r.PermitFieldContact(true);
            await r.AcquireContact(); await r.SmallContactStep();
            Check(r.Commands.Any(c=>c.StartsWith("ServoP")), "empty User/Tool ID with fresh applied frames accepted");
        }
        using (var r = new DobotController { EmptyFrameId=true, HoldFrame=true })
        {
            await r.Open(); r.BeginFieldWorkflow(s); r.PermitFieldContact(true);
            await Reject(()=>r.AcquireContact(), "empty ID without applied Tool rejected");
            Check(!string.IsNullOrEmpty(r.ContactAcquisitionError), "acquisition root cause survives abort");
            Check(!r.Commands.Any(c=>c.StartsWith("ServoP")), "empty ID does not bypass frame guard");
        }
        using (var r = new DobotController { HoldFrame=true })
        {
            await r.Open(); r.BeginFieldWorkflow(s); r.PermitFieldContact(true);
            await Reject(()=>r.AcquireContact(),"frame ACK without application rejected");
            Check(!r.Commands.Any(c=>c.StartsWith("ServoP")), "unconfirmed frame sends no motion");
        }
        using (var r = new DobotController { HoldServo=true })
        {
            await r.Open(); r.BeginFieldWorkflow(s); r.PermitFieldContact(true); await r.AcquireContact();
            await Reject(()=>r.SmallContactStep(), "unmoved 0.1mm step is not accepted by 0.2mm tolerance");
            r.SensorOnline=false; await Task.Delay(40);
            await Reject(()=>r.SmallContactStep(), "sensor dropout blocks next movement");
        }
        Check(UnityEngine.Debug.Logs.Any(l=>l.Contains("[ACK]") && l.Contains("reply=0,{},Stop();")),"Stop ACK captured");
        Check(UnityEngine.Debug.Logs.Any(l=>l.Contains("[TIMEOUT]") && l.Contains("no_new_frame") && l.Contains("QDActual_raw=")),"timeout includes failed condition and speed snapshot");
        Check(UnityEngine.Debug.Logs.Any(l=>l.Contains("PRE_MOTION_STOP_FAILED")),"pre-motion failure stage captured");
        Check(UnityEngine.Debug.Logs.Any(l=>l.Contains("[TRANSPORT]") && !l.Contains("not_received")),"actual transport timestamps captured");
        Check(UnityEngine.Debug.Logs.Any(l=>l.Contains("[TIMEOUT]") && l.Contains("no_new_polls=") && l.Contains("rx_total=")),"timeout includes poll and receive counters");
        Console.WriteLine($"FIELD_ROBOT_TESTS_PASS {count}");
    }
}
