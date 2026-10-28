using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnityEngine
{
    public class MonoBehaviour { }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) {} }
    public class SerializeField : Attribute { }
    public enum KeyCode { C, R, L, S, N, G, M }
    public static class Input { public static bool GetKeyDown(KeyCode k) => false; }
    public static class Debug
    {
        public static void Log(object s) => Console.WriteLine(s);
        public static void LogError(object s) => Console.WriteLine(s);
        public static void LogWarning(object s) => Console.WriteLine(s);
    }
    public static class JsonUtility
    {
        public static void FromJsonOverwrite(string json, object target)
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var field in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (doc.RootElement.TryGetProperty(field.Name, out var value))
                    field.SetValue(target, JsonSerializer.Deserialize(value.GetRawText(), field.FieldType));
        }
        public static T FromJson<T>(string json) where T : new()
        { var value = new T(); FromJsonOverwrite(json, value); return value; }
    }
}
namespace TMPro { public class TMP_InputField { public string text = ""; } }
public delegate void DobotEventHandler(object sender, DobotEventArgs args);
public class DobotEventArgs : EventArgs
{
    public static DobotEventArgs CreateConnectionEvent(params object[] v) => new DobotEventArgs();
    public static DobotEventArgs CreateErrorEvent(params object[] v) => new DobotEventArgs();
    public static DobotEventArgs CreateChassisPositionEvent(params object[] v) => new DobotEventArgs();
    public static DobotEventArgs CreateChassisNavStatusEvent(params object[] v) => new DobotEventArgs();
}
public partial class RobokitController
{
    public async Task TestConnect(int port)
    {
        navClient = new TcpClient();
        await navClient.ConnectAsync(IPAddress.Loopback, port);
        statusStream = navStream = navClient.GetStream();
        _ = ReceiveLoopAsync(navStream, port);
        responseTimeoutSeconds = 0.4f;
        motionStartTimeoutSeconds = 1;
        motionTimeoutSeconds = 2;
    }
}
// Unity式单线程上下文，状态字典及事件不会被测试线程并发访问。
sealed class Pump : SynchronizationContext
{
    readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
    public override void Post(SendOrPostCallback action, object value) => queue.Enqueue(() => action(value));
    public void Tick() { while (queue.TryDequeue(out var action)) action(); }
}
class Program
{
    static async Task ReadExact(NetworkStream s, byte[] b)
    { int n = 0; while (n < b.Length) { int r = await s.ReadAsync(b, n, b.Length - n); if (r == 0) throw new EndOfStreamException(); n += r; } }
    static async Task Scenario(string kind, bool expected, ushort motionApi = 3055, bool legacy = false)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int motionCount = 0, stopCount = 0, statusCount = 0;
        var robot = new RobokitController();
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            int poll = 0;
            try
            {
                while (true)
                {
                    var header = new byte[16]; await ReadExact(stream, header);
                    var data = new byte[RobokitProtocol.ParsePayloadLength(header)]; await ReadExact(stream, data);
                    ushort api = (ushort)((header[8] << 8) | header[9]);
                    ushort seq = (ushort)((header[2] << 8) | header[3]);
                    string response = "";
                    if (api == 1020)
                    {
                        statusCount++;
                        if (!Encoding.UTF8.GetString(data).Contains("false")) throw new Exception("simple must be false");
                        int state = motionCount == 0 ? 4 : ++poll == 1 ? 2 : 4;
                        if (motionCount > 0)
                        {
                            if (kind == "stale") state = 4;
                            if (kind == "failed") state = 5;
                            if (kind == "paused") state = 3;
                            if (kind == "canceled") state = 6;
                            if (kind == "running") state = 2;
                            if (kind == "badstate") state = 9;
                            if (kind == "disconnect") return;
                        }
                        response = kind == "missing" && motionCount > 0 ? "{}" : $"{{\"task_status\":{state},\"task_type\":7}}";
                        if (kind == "wrongtype" && motionCount > 0) response = "{\"task_status\":4,\"task_type\":3}";
                    }
                    else if (api == 3003) stopCount++;
                    else
                    {
                        motionCount++;
                        response = kind == "rejected" ? "{\"ret_code\":1,\"err_msg\":\"denied\"}" : "";
                        if (kind == "noack") continue;
                        if (legacy && kind == "disconnect") return;
                    }
                    if (kind == "wrongseq" && motionCount > 0) seq += 50;
                    byte[] packet = RobokitProtocol.Pack((ushort)(api + 10000), response, seq);
                    // 每次都拆开包头/包体，以覆盖旧接收实现丢半包的问题。
                    await stream.WriteAsync(packet, 0, 3);
                    await Task.Delay(5);
                    await stream.WriteAsync(packet, 3, packet.Length - 3);
                }
            }
            catch (IOException) { }
            catch (SocketException) { }
        });
        try
        {
            await robot.TestConnect(((IPEndPoint)listener.LocalEndpoint).Port);
            if (legacy && (!robot.FieldConnectedTo("127.0.0.1", false) || robot.FieldConnectedTo("127.0.0.1") || robot.FieldConnectedTo("127.0.0.2", false)))
                throw new Exception("legacy requires the correct navigation endpoint, not the status port");
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var motion = legacy ? robot.ExecuteRelativeLegacyTimedAsync(motionApi, "{}", .65) : robot.ExecuteRelativeConfirmedAsync(motionApi, "{}");
            if (legacy && (kind == "stop" || kind == "extra"))
            {
                await Task.Delay(260);
                if (kind == "stop") robot.StopMovement();
                else
                {
                    robot.SendCommand(3056, "{}");
                    if (await robot.ExecuteRelativeLegacyTimedAsync(3056, "{}", .3)) throw new Exception("overlapping legacy motion accepted");
                }
            }
            bool actual = await motion;
            if (actual != expected) throw new Exception(kind + " unexpected result " + actual);
            if (legacy && statusCount != 0) throw new Exception("legacy must not query 1020");
            if (legacy && expected && elapsed.Elapsed.TotalSeconds < .63) throw new Exception("legacy estimated duration was shortened");
            if (motionCount != 1) throw new Exception(kind + " motion resent");
            if (!expected && kind != "disconnect" && kind != "wrongseq" && kind != "noack" && stopCount == 0) throw new Exception(kind + " stop not requested");
            Console.WriteLine("PASS " + (legacy ? "legacy " : "status ") + kind + " api=" + motionApi);
        }
        finally { robot.DisConnect(); listener.Stop(); await server; }
    }
    static async Task Run()
    {
        await Scenario("success", true);
        await Scenario("success", true, 3056);
        foreach (string kind in new[] { "stale", "failed", "paused", "canceled", "missing", "rejected", "running", "disconnect", "wrongseq", "wrongtype", "badstate" })
            await Scenario(kind, false);
        await Scenario("success", true, 3055, true);
        await Scenario("success", true, 3056, true);
        await Scenario("extra", true, 3055, true);
        foreach (string kind in new[] { "stop", "rejected", "wrongseq", "disconnect", "noack" })
            await Scenario(kind, false, 3055, true);
        Console.WriteLine("21 chassis tests PASS (loopback only, legacy never queries status)");
    }
    static int Main()
    {
        var pump = new Pump(); SynchronizationContext.SetSynchronizationContext(pump);
        var run = Run(); var deadline = DateTime.UtcNow.AddSeconds(75);
        while (!run.IsCompleted && DateTime.UtcNow < deadline) { pump.Tick(); Thread.Sleep(1); }
        if (!run.IsCompleted) throw new TimeoutException("Test watchdog");
        run.GetAwaiter().GetResult(); return 0;
    }
}
