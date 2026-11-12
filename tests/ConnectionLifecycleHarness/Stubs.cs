using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace UnityEngine {
    public class HeaderAttribute : Attribute { public HeaderAttribute(string _) {} }
    public class SerializeField : Attribute {}
    public class MonoBehaviour {
        public Coroutine StartCoroutine(IEnumerator _) => new();
        public void StopCoroutine(Coroutine _) {}
    }
    public class Coroutine {}
    public class WaitForSeconds { public WaitForSeconds(float _) {} }
    public static class Mathf {
        public static float Max(float a, float b) => Math.Max(a,b);
        public static float Clamp(float v, float a, float b) => Math.Clamp(v,a,b);
        public static int RoundToInt(float v) => (int)Math.Round(v);
        public static int Min(int a, int b) => Math.Min(a,b);
    }
    public enum KeyCode { Alpha1, Alpha2, Alpha3 }
    public static class Input { public static bool GetKeyDown(KeyCode _) => false; }
    public static class Time { public static float realtimeSinceStartup = 100; }
    public static class Debug {
        public static readonly List<string> Lines = new();
        public static void Log(object value) { lock(Lines) Lines.Add(value?.ToString()); }
        public static void LogWarning(object value) => Log(value);
        public static void LogError(object value) => Log(value);
    }
}
namespace TMPro {
    public class TextMeshProUGUI { public string text; }
    public class TMP_InputField { public string text; }
}
namespace Modbus.Message { public class Marker {} }
namespace Modbus.Device {
    public class ModbusIpMaster : IDisposable {
        public sealed class Options { public int ReadTimeout, WriteTimeout, Retries; }
        public static readonly List<ModbusIpMaster> Created = new();
        public readonly Options Transport = new();
        public readonly List<ushort> Writes = new();
        public bool Disposed, FailRead;
        public static ModbusIpMaster CreateIp(TcpClient _) { var m = new ModbusIpMaster(); Created.Add(m); return m; }
        public void Dispose() { Disposed = true; }
        public void WriteSingleRegister(byte _, ushort a, ushort __) => Writes.Add(a);
        public void WriteMultipleRegisters(byte _, ushort a, ushort[] __) => Writes.Add(a);
        public ushort[] ReadHoldingRegisters(byte _, ushort __, ushort ___) {
            if (FailRead) throw new System.IO.IOException("fake read failure");
            return new ushort[] { 0, 0 };
        }
    }
}
public enum OperationType { 真实运行 }
public static class OperationLogTool { public static void RecordLog(OperationType _, string __) {} }
public static class UIUtil { public static bool IsPointerOverUI() => false; }
public class FieldCommissioningPanel {
    public static FieldCommissioningPanel Active;
    public bool MotionLocked;
    public int Stops;
    public void RequestStop() => Stops++;
}
// Only Dashboard protocol/ownership are replaced. Connect/Disconnect and packet parsing are production.
public partial class DobotController {
    public bool ExternalControlActive, FieldWorkflowActive;
    private int commandEpoch, actualUser, actualTool, sixForceOnline, robotError, robotCollision;
    private long dashboardConnectionGeneration;
    private long protocolFeedbackSequence;
    private ulong robotTimestamp, currentCommandId;
    private readonly double[] actualForce = new double[6], actualToolPose = new double[6], protocolJointSpeeds = new double[6];
    private readonly SemaphoreSlim requestGate = new(1,1);
    private void RevokeExternalControl() => ExternalControlActive = false;
    private Task AbortOwnerAsync() => Task.CompletedTask;
    private void ShutdownRobotGateway() {}
    private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    private static string Number(double v) => v.ToString();
    private static string Numbers(double[] v) => string.Join(",",v);
    private async Task<string> ExchangeDashboardAsync(string command, bool _) {
        await requestGate.WaitAsync();
        try {
            var stream = networkStream;
            var data = Encoding.ASCII.GetBytes(command+"\n");
            await stream.WriteAsync(data);
            var result = new StringBuilder(); var b = new byte[1];
            while(await stream.ReadAsync(b) > 0) { result.Append((char)b[0]); if(b[0]==';') return result.ToString(); }
            throw new System.IO.IOException("fake dashboard disconnected");
        } finally { requestGate.Release(); }
    }
}
