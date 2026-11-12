using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public partial class RobokitController
{
    public async Task TestJogConnect(int port)
    {
        robotIP = "127.0.0.1";
        await TestConnect(port);
        statusClient = navClient; // Only this harness shares one fake stream for both API routes.
    }
}

static class ManualJogTests
{
    static int count;
    static void Check(bool condition, string name)
    { if (!condition) throw new Exception("ManualJog: " + name); count++; Console.WriteLine("PASS manual jog " + name); }
    static double Num(JsonElement json, string field) => json.GetProperty(field).GetDouble();
    static void Reject(Action action, string name)
    {
        try { action(); } catch (ArgumentException) { Check(true, name); return; }
        throw new Exception("manual invalid parameter accepted: " + name);
    }
    static void Commands()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            foreach (var direction in new[] { ChassisJogDirection.Forward, ChassisJogDirection.Backward, ChassisJogDirection.Left, ChassisJogDirection.Right })
            {
                var c = ChassisJogCommand.Create(direction, 50, 50);
                var json = JsonDocument.Parse(c.Json).RootElement;
                double x = direction == ChassisJogDirection.Forward ? .05 : direction == ChassisJogDirection.Backward ? -.05 : 0;
                double y = direction == ChassisJogDirection.Left ? .05 : direction == ChassisJogDirection.Right ? -.05 : 0;
                Check(c.Api == 3055 && Num(json,"dist") == .05 && Num(json,"vx") == x && Num(json,"vy") == y && Num(json,"mode") == 0,
                    direction + " body-frame sign and mm-to-m/invariant JSON");
            }
            foreach (var direction in new[] { ChassisJogDirection.Counterclockwise, ChassisJogDirection.Clockwise })
            {
                var c = ChassisJogCommand.Create(direction,5,5);
                var json = JsonDocument.Parse(c.Json).RootElement;
                Check(c.Api == 3056 && Math.Abs(Num(json,"angle") - Math.PI/36) < 1e-12 &&
                    Math.Abs(Num(json,"vw") - (direction == ChassisJogDirection.Clockwise ? -1 : 1)*Math.PI/36) < 1e-12,
                    direction + " positive magnitude and signed radians");
            }
            foreach (var n in new[] { double.NaN, double.PositiveInfinity, -1, 0, .9, 501 })
                Reject(()=>ChassisJogCommand.Create(ChassisJogDirection.Forward,n,50),"invalid distance " + n);
            foreach (var n in new[] { double.NaN, double.NegativeInfinity, 0, 9, 101 })
                Reject(()=>ChassisJogCommand.Create(ChassisJogDirection.Left,50,n),"invalid speed " + n);
            Reject(()=>ChassisJogCommand.Create(ChassisJogDirection.Clockwise,.09,5),"angle below range");
            Reject(()=>ChassisJogCommand.Create(ChassisJogDirection.Clockwise,46,5),"angle above range");
            Reject(()=>ChassisJogCommand.Create(ChassisJogDirection.Clockwise,5,16),"angular speed above range");
            Reject(()=>ChassisJogCommand.Create((ChassisJogDirection)100,50,50),"unknown direction");
            Check(ChassisJogCommand.Create(ChassisJogDirection.Forward,500,100) != null &&
                ChassisJogCommand.Create(ChassisJogDirection.Counterclockwise,.1,1) != null,"inclusive parameter boundaries");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    static async Task ReadExact(NetworkStream s, byte[] bytes)
    { int n=0; while(n<bytes.Length) { int r=await s.ReadAsync(bytes,n,bytes.Length-n); if(r==0) throw new EndOfStreamException(); n+=r; } }

    static async Task Scenario(string kind)
    {
        var listener = new TcpListener(IPAddress.Loopback,0); listener.Start();
        var robot = new RobokitController();
        var motions = new List<(ushort Api,string Json)>();
        var recoveryQuery = new TaskCompletionSource<bool>();
        int stops=0, polls=0; bool stopped=false;
        var server = Task.Run(async()=>
        {
            using var client = await listener.AcceptTcpClientAsync(); using var stream=client.GetStream();
            try
            {
                while(true)
                {
                    var header=new byte[16]; await ReadExact(stream,header);
                    var bytes=new byte[RobokitProtocol.ParsePayloadLength(header)]; await ReadExact(stream,bytes);
                    ushort api=(ushort)(header[8]*256+header[9]), seq=(ushort)(header[2]*256+header[3]);
                    string json="{}";
                    if(api==3055 || api==3056)
                    {
                        motions.Add((api,Encoding.UTF8.GetString(bytes))); polls=0; stopped=false;
                        if(kind=="reject") json="{\"ret_code\":1,\"err_msg\":\"fake rejected\"}";
                        if(kind=="disconnect") return;
                    }
                    else if(api==3003) { stops++; stopped=true; if(kind=="stop-noack") continue; }
                    else if(api==1020)
                    {
                        if(kind=="recovery-stop-race") { recoveryQuery.TrySetResult(true); await Task.Delay(150); }
                        int state = kind=="preexisting" ? 2 : motions.Count==0 || stopped ? 4 : ++polls==1 ? 2 : 4;
                        if(kind=="short" && motions.Count>0) state=4;
                        if(kind=="running" && motions.Count>0 && !stopped) state=2;
                        json="{\"task_status\":"+state+",\"task_type\":7}";
                    }
                    var packet=RobokitProtocol.Pack((ushort)(api+10000),json,seq);
                    await stream.WriteAsync(packet,0,packet.Length);
                }
            }
            catch(IOException) { } catch(SocketException) { }
        });
        try
        {
            await robot.TestJogConnect(((IPEndPoint)listener.LocalEndpoint).Port);
            var command=ChassisJogCommand.Create(ChassisJogDirection.Left,50,50);
            if(kind=="success")
            {
                Check(!await robot.ExecuteManualJogAsync(command,false) && motions.Count==0,"no operator permission sends no motion");
                robot.FieldWorkflowOwned=true;
                Check(!await robot.ExecuteManualJogAsync(command,true) && motions.Count==0,"workflow owner blocks manual motion");
                robot.FieldWorkflowOwned=false;
                robot.robotIP="127.0.0.2";
                Check(!await robot.ExecuteManualJogAsync(command,true) && motions.Count==0,"mismatched endpoint blocked");
                robot.robotIP="127.0.0.1";
                var task=robot.ExecuteManualJogAsync(command,true);
                Check(robot.ManualJogLocked,"lock claimed before first await");
                Check(!await robot.ExecuteManualJogAsync(command,true),"repeated click not queued");
                robot.SendCommand(3056,"{}");
                Check(await task && !robot.ManualJogLocked && motions.Count==1,"one successful motion unlocks after running-completed");
                var expected=JsonDocument.Parse(command.Json).RootElement;
                var actual=JsonDocument.Parse(motions[0].Json).RootElement;
                Check(motions[0].Api==3055 && Num(actual,"vy")==Num(expected,"vy"),"actual wire payload equals validated left jog");
                await robot.StopManualJogAsync();
                Check(robot.ManualJogLocked && stops>0,"stop acknowledgement keeps human-review lock");
                Check(!await robot.AcknowledgeManualJogStoppedAsync(false) && robot.ManualJogLocked,"cannot unlock without human observation");
                Check(await robot.AcknowledgeManualJogStoppedAsync(true) && !robot.ManualJogLocked && motions.Count==1,"explicit recovery checks idle and never resumes");
                Check(await robot.ExecuteManualJogAsync(ChassisJogCommand.Create(ChassisJogDirection.Clockwise,5,5),true) && motions.Count==2 && motions[1].Api==3056,
                    "rotation reuses same connection after recovery");
            }
            else if(kind=="recovery-stop-race")
            {
                await robot.StopManualJogAsync();
                var recovery=robot.AcknowledgeManualJogStoppedAsync(true);
                await recoveryQuery.Task;
                Check(robot.ManualJogLocked,"recovery stays locked while awaiting state");
                await robot.ConnectAllAsync();
                Check(robot.TestNavOpen,"connect during recovery cannot replace stop transport");
                var stop=robot.StopManualJogAsync();
                Check(!await recovery && robot.ManualJogLocked,"new stop during recovery invalidates old idle result");
                await stop;
                Check(robot.ManualJogNeedsReview && motions.Count==0,"stop-recovery race never sends movement or unlocks");
            }
            else if(kind=="stop-noack")
            {
                await robot.StopManualJogAsync();
                Check(robot.ManualJogLocked && robot.ManualJogStatus.Contains("未获确认"),"failed stop retains lock");
                Check(!await robot.AcknowledgeManualJogStoppedAsync(true) && robot.ManualJogLocked,"no transport cannot falsely recover");
            }
            else
            {
                var task=robot.ExecuteManualJogAsync(command,true);
                if(kind=="running") { await Task.Delay(450); await robot.StopManualJogAsync(); }
                Check(!await task && robot.ManualJogNeedsReview,kind+" retains review lock");
                int before=motions.Count;
                Check(!await robot.ExecuteManualJogAsync(command,true) && motions.Count==before,kind+" cannot resend while locked");
                Check(!await robot.ExecuteRelativeLegacyTimedAsync(3055,"{}",.1),kind+" blocks legacy entry too");
                if(kind=="short") Check(await robot.AcknowledgeManualJogStoppedAsync(true) && motions.Count==1,"short missed RUNNING needs explicit idle review");
                if(kind=="preexisting") Check(motions.Count==0 && !await robot.AcknowledgeManualJogStoppedAsync(true),"preexisting active task is not replaced or unlocked");
            }
        }
        finally { robot.TestDispose(); listener.Stop(); await server; }
    }
    public static async Task Run()
    {
        Commands();
        Check(!await new RobokitController().ExecuteManualJogAsync(ChassisJogCommand.Create(ChassisJogDirection.Forward,50,50),true),"disconnected chassis sends no motion");
        foreach(string kind in new[]{"success","short","reject","running","disconnect","preexisting","stop-noack","recovery-stop-race"}) await Scenario(kind);
        Console.WriteLine(count+" manual jog tests PASS (production command/controller, loopback only)");
    }
}
