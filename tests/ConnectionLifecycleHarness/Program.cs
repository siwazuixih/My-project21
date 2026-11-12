using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

sealed class Pump : SynchronizationContext {
    readonly ConcurrentQueue<Action> callbacks = new();
    public override void Post(SendOrPostCallback action, object state) => callbacks.Enqueue(()=>action(state));
    public void Tick() { while(callbacks.TryDequeue(out var action)) action(); }
}
sealed class Peer : IDisposable {
    readonly TcpListener listener;
    readonly CancellationTokenSource stop = new();
    readonly List<TcpClient> clients = new();
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public int Accepted;
    public bool HoldAck;
    public Peer(bool feedback) {
        listener = new TcpListener(IPAddress.Loopback,0); listener.Start();
        _ = Run(feedback);
    }
    async Task Run(bool feedback) {
        try {
            while(!stop.IsCancellationRequested) {
                var client = await listener.AcceptTcpClientAsync(); clients.Add(client); Accepted++;
                _ = Serve(client,feedback);
            }
        } catch(ObjectDisposedException) {} catch(SocketException) {} catch(OperationCanceledException) {}
    }
    async Task Serve(TcpClient client, bool feedback) {
        try {
            using(client) using(var stream = client.GetStream()) {
                if(feedback) {
                    ulong stamp=100;
                    while(!stop.IsCancellationRequested) {
                        var packet = new byte[1440];
                        Array.Copy(BitConverter.GetBytes((ushort)1440),0,packet,0,2);
                        Array.Copy(BitConverter.GetBytes((ulong)5),0,packet,24,8);
                        Array.Copy(BitConverter.GetBytes(stamp),0,packet,32,8);
                        Array.Copy(BitConverter.GetBytes(0x123456789ABCDEFUL),0,packet,48,8);
                        await stream.WriteAsync(packet); stamp+=20;
                        await Task.Delay(20,stop.Token);
                    }
                } else {
                    using var reader = new StreamReader(stream,Encoding.ASCII, false,1024,true);
                    string command;
                    while((command=await reader.ReadLineAsync()) != null) {
                        while(HoldAck) await Task.Delay(10,stop.Token);
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("0,{},"+command+";"));
                    }
                }
            }
        } catch(IOException) {} catch(ObjectDisposedException) {} catch(OperationCanceledException) {} catch(SocketException) {}
    }
    public void DropClients() { foreach(var c in clients) c.Close(); }
    public void Dispose() { stop.Cancel(); listener.Stop(); DropClients(); }
}
static class Program {
    static int passed;
    static void Check(bool value,string name) { if(!value) throw new Exception(name); passed++; Console.WriteLine("PASS "+name); }
    static object Field(object o,string name) => o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
    static void Invoke(object o,string name) => o.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
    static async Task Until(Func<bool> condition) {
        var time=Stopwatch.StartNew();
        while(!condition()) { if(time.Elapsed.TotalSeconds>3) throw new Exception("condition timeout"); await Task.Delay(10); }
    }
    static async Task Run() {
        string archive=Path.Combine(Path.GetTempPath(),"connection-data-hook-"+Guid.NewGuid().ToString("N"));
        RunDataCapture.Begin(new RunDataSession(archive,new RunDataSession.Info {runId="connection-hook",projectId="fake",source="loopback and fake Modbus"}));
        using(var endpoint = new Peer(false)) {
            using var client = await DeviceTcpConnection.OpenAsync("127.0.0.1",endpoint.Port,1,CancellationToken.None);
            Check(client.Connected,"async TCP connects to own loopback peer");
            Check(DeviceTcpConnection.Matches(client,"127.0.0.1",endpoint.Port),"actual endpoint match");
            Check(!DeviceTcpConnection.Matches(client,"127.0.0.2",endpoint.Port),"wrong endpoint rejected");
            Check(DeviceTcpConnection.IsOpen(client) && DeviceTcpConnection.Describe(client).Contains(endpoint.Port.ToString()),"read-only endpoint display reports actual remote port");
            Check(!DeviceTcpConnection.IsOpen(null) && DeviceTcpConnection.AbsentOrMatches(null,"127.0.0.1",1),"absent endpoint is not falsely displayed connected");
        }
        using(var canceled = new CancellationTokenSource()) {
            canceled.Cancel();
            try { await DeviceTcpConnection.OpenAsync("127.0.0.1",1,1,canceled.Token); throw new Exception("cancel accepted"); }
            catch(OperationCanceledException) { Check(true,"pre-canceled connect creates no connection"); }
        }
        foreach(float timeout in new[]{0f,float.NaN,float.PositiveInfinity}) {
            try { await DeviceTcpConnection.OpenAsync("127.0.0.1",1,timeout,CancellationToken.None); throw new Exception("bad timeout accepted"); }
            catch(ArgumentOutOfRangeException) { Check(true,"invalid deadline rejected"); }
        }
        using(var closedPort = new TcpListener(IPAddress.Loopback,0)) {
            closedPort.Start(); int port=((IPEndPoint)closedPort.LocalEndpoint).Port; closedPort.Stop();
            var elapsed=Stopwatch.StartNew();
            try { using var c=await DeviceTcpConnection.OpenAsync("127.0.0.1",port,.2f,CancellationToken.None); throw new Exception("closed port accepted"); }
            catch(SocketException) { Check(elapsed.Elapsed.TotalSeconds<2,"refused connection returns promptly"); }
        }
        using(var dashboard=new Peer(false)) using(var feedback=new Peer(true)) {
            var robot=new DobotController {robotIP="127.0.0.1",dashboardPort=dashboard.Port,feedbackPort=feedback.Port};
            await robot.ConnectAsync(); await Until(()=>robot.HasFreshFeedback(1));
            Invoke(robot,"RefreshFeedbackUI");
            Check(Field(robot,"tcpClient")!=null && Field(robot,"feedbackClient")!=null,"both robot ports connected");
            Check(robot.ViewHasConnection && robot.ViewControlReady && robot.ViewEndpointsAligned && robot.ViewConnectionDescription.Contains(feedback.Port.ToString()),"formal robot view shows actual control and feedback endpoints");
            robot.robotIP="127.0.0.2";
            Check(!robot.ViewEndpointsAligned && !robot.ViewControlReady && robot.ViewConnectionDescription.Contains("不一致"),"formal robot view exposes configuration/socket mismatch");
            robot.robotIP="127.0.0.1";
            await robot.ConnectAsync();
            Check(dashboard.Accepted==1 && feedback.Accepted==1,"repeat robot connect reuses both ports");
            feedback.DropClients();
            await Until(()=>!robot.HasFreshFeedback(1)); Invoke(robot,"RefreshFeedbackUI");
            Check(Field(robot,"feedbackClient")==null,"feedback EOF releases its client");
            Check(robot.ViewControlReady && robot.ViewEndpointsAligned && robot.ViewConnectionDescription.Contains("反馈 未连接"),"missing feedback stays repairable without pretending fully ready");
            await robot.ConnectAsync(); await Until(()=>robot.HasFreshFeedback(1));
            Check(dashboard.Accepted==1 && feedback.Accepted==2,"feedback repaired without replacing healthy dashboard");
            var connections=new List<DobotEventArgs>(); robot.OnDobotEvent+=(_,e)=>connections.Add(e);
            Invoke(robot,"RefreshFeedbackUI"); connections.Clear(); ((NetworkStream)Field(robot,"networkStream")).Close();
            Invoke(robot,"RefreshFeedbackUI");
            Check(connections.Exists(e=>e.Type==DobotEventArgs.EventType.ConnectionChanged && e.PortType=="Dashboard" && !e.IsConnected),
                "dashboard failure publishes disconnected state even while feedback remains fresh");
            await robot.ConnectAsync(); await Until(()=>robot.HasFreshFeedback(1));
            FieldCommissioningPanel.Active=new FieldCommissioningPanel(); robot.FieldWorkflowActive=true;
            robot.Disconnect();
            Check(FieldCommissioningPanel.Active.Stops==1 && Field(robot,"tcpClient")!=null,"running disconnect requests stop and retains connection");
            robot.FieldWorkflowActive=false; FieldCommissioningPanel.Active=null;
            robot.Disconnect();
            Check(Field(robot,"tcpClient")==null && Field(robot,"feedbackClient")==null && !robot.HasFreshFeedback(1),"manual disconnect closes both ports/freshness");
            Check(!robot.ViewHasConnection && !robot.ViewControlReady,"formal robot connection view clears on disconnect");
            await robot.ConnectAsync(); await Until(()=>robot.HasFreshFeedback(1)); Invoke(robot,"OnDestroy");
            Check(Field(robot,"tcpClient")==null && Field(robot,"feedbackClient")==null,"scene destruction closes both robot ports");
        }
        using(var dashboard=new Peer(false){HoldAck=true}) using(var feedback=new Peer(true)) {
            var robot=new DobotController {robotIP="127.0.0.1",dashboardPort=dashboard.Port,feedbackPort=feedback.Port};
            var connect=robot.ConnectAsync(); await Until(()=>dashboard.Accepted==1);
            robot.Disconnect(); dashboard.HoldAck=false; await connect; await Task.Delay(50);
            Check(Field(robot,"tcpClient")==null && Field(robot,"feedbackClient")==null && !(bool)Field(robot,"controlModeConfirmed"),"late connect/ACK cannot resurrect disconnected robot");
        }
        using(var device=new Peer(false)) {
            var lift=new LiftCylinderController {ipAddress="127.0.0.1",port=device.Port,autoMonitor=false};
            await lift.ConnectAsync(); var first=Modbus.Device.ModbusIpMaster.Created[^1];
            Check(lift.ViewHasConnection && lift.ViewEndpointsAligned && lift.ViewConnectionDescription.Contains(device.Port.ToString()),"formal lift view reads actual endpoint without an extra query");
            lift.ipAddress="127.0.0.2";
            Check(!lift.ViewEndpointsAligned && lift.ViewConnectionDescription.Contains("不一致"),"formal lift view distinguishes address mismatch");
            lift.ipAddress="127.0.0.1";
            await Until(()=>device.Accepted==1); // TCP connect may complete before the fake server's Accept continuation runs.
            await lift.ConnectAsync();
            Check(ReferenceEquals(first,Modbus.Device.ModbusIpMaster.Created[^1]) && device.Accepted==1,"repeat lift connection reuses master");
            Check(first.Transport.ReadTimeout==1000 && first.Transport.WriteTimeout==1000 && first.Transport.Retries==0,"Modbus IO bounded, no silent write retries");
            lift.MoveToPosition(.01f,1); await Task.Delay(20); lift.Disconnect(); await lift.ConnectAsync();
            var second=Modbus.Device.ModbusIpMaster.Created[^1]; await Task.Delay(600);
            Check(first.Disposed && second.Writes.Count==0,"old lift sequence never writes target/trigger to new master");
            second.FailRead=true; Check(lift.ReadCurrentPosition()==0 && !lift.IsConnected() && second.Disposed,"lift IO failure releases connection, not false fresh zero");
            await lift.ConnectAsync(); var third=Modbus.Device.ModbusIpMaster.Created[^1]; Invoke(lift,"OnDestroy");
            Check(third.Disposed && !lift.IsConnected(),"lift destruction disposes master and TCP");
            await lift.ConnectAsync(); var fourth=Modbus.Device.ModbusIpMaster.Created[^1]; fourth.FailRead=true;
            lift.heightTextDisplay=new TMPro.TextMeshProUGUI {text="old measurement"};
            var monitoring=(System.Collections.IEnumerator)typeof(LiftCylinderController)
                .GetMethod("MonitorRoutine",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(lift,null);
            Check(!monitoring.MoveNext() && lift.heightTextDisplay.text.Contains("未连接") && !lift.HasFreshFeedback(1),
                "failed monitor exits without overwriting disconnected UI with cached values");
        }
        using(var device=new Peer(false)) {
            var lift=new LiftCylinderController {ipAddress="127.0.0.1",port=device.Port,autoMonitor=false};
            await lift.ConnectAsync(); lift.MoveToPosition(.012f,2); await Task.Delay(650); lift.Disconnect();
        }
        await RunDataCapture.Finish("connection-hook","fake lifecycle finished");
        string commands=File.ReadAllText(Path.Combine(archive,"commands.jsonl"));
        Check(commands.Contains("address=2025") && commands.Contains("float_CDAB=12") && commands.Contains("address=2001") && commands.Contains("float_CDAB=2"),
            "production Modbus wrapper records decoded mm/speed and actual registers");
        string robotData=File.ReadAllText(Path.Combine(archive,"robot-feedback.csv"));
        Check(robotData.Contains("j1_deg") && robotData.Contains("sampled existing feedback"),"full robot update publishes sampled feedback evidence without an extra query");
        Console.WriteLine($"{passed} connection lifecycle tests PASS (owned loopback only; Modbus fake; no real devices)");
    }
    static void Main() {
        var pump=new Pump(); SynchronizationContext.SetSynchronizationContext(pump);
        var task=Run();
        while(!task.IsCompleted) { pump.Tick(); Thread.Sleep(1); }
        pump.Tick(); task.GetAwaiter().GetResult();
    }
}
