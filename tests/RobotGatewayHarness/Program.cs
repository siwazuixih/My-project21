// Runs the actual gateway source against Unity-free host stubs. No robot connection.
using System;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
namespace UnityEngine
{
    public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string text) {} }
    public sealed class SerializeField : Attribute {}
    public static class Debug
    {
        public static void Log(object text) => Console.WriteLine(text);
        public static void LogWarning(object text) => Console.WriteLine(text);
        public static void LogError(object text) => Console.WriteLine(text);
    }
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        public static T FromJson<T>(string text) => JsonSerializer.Deserialize<T>(text, Options);
        public static string ToJson(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);
    }
}
public partial class DobotController
{
    public bool FieldWorkflowActive => false;
    private bool OriginalContactTimingActive => false;
    private NetworkStream networkStream;
    private TcpClient tcpClient;
    private readonly object feedbackLock = new object();
    private int currentRobotMode;
    private double lastFeedbackUnixSeconds;
    private double[] actualJoints = new double[6];
    public void StartForTest(int port) { robotGatewayPort = port; OnEnable(); }
    public void EndForTest() { OnDisable(); }
}
class Program
{
    static async Task Main(string[] args)
    {
        var gateway = new DobotController();
        gateway.StartForTest(args.Length == 0 ? 19129 : int.Parse(args[0]));
        Console.WriteLine("HARNESS_READY");
        try { while (Console.ReadLine() != "quit") await Task.Delay(20); }
        finally { gateway.EndForTest(); }
    }
}
