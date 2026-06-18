using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class ServoToolUnityController : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateDebugController()
    {
        if (FindObjectOfType<ServoToolUnityController>() != null) return;

        var go = new GameObject("Servo Tool Unity Controller");
        DontDestroyOnLoad(go);
        go.AddComponent<ServoToolUnityController>();
    }

    [Header("Unity -> Python Bridge")]
    public string bridgeHost = "127.0.0.1";
    public int bridgePort = 9100;
    public float commandTimeoutSeconds = 2f;

    [Header("Python Bridge -> Servo Tool")]
    public string toolHost = "192.168.192.21";
    public int toolPort = 1200;

    [Header("Python Bridge Process")]
    public bool showDebugButtons = true;
    public string pythonExecutable = "python3";
    public string bridgeRelativePath = "ExternalCode/servo_tcp_client_fault_control_v12_28Nm_minimal_slope_jam.py";

    [Header("Runtime Status")]
    public string lastResponse = "Not connected";

    private Process bridgeProcess;
    private bool commandRunning;

    public void StartBridgeProcess()
    {
        if (bridgeProcess != null && !bridgeProcess.HasExited)
        {
            lastResponse = "Bridge already running";
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string scriptPath = Path.Combine(projectRoot, bridgeRelativePath);

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                Arguments = Quote(scriptPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = false,
                WorkingDirectory = projectRoot,
            };
            info.EnvironmentVariables["SERVO_TOOL_IP"] = toolHost;
            info.EnvironmentVariables["SERVO_TOOL_PORT"] = toolPort.ToString();
            info.EnvironmentVariables["SERVO_BRIDGE_HOST"] = bridgeHost;
            info.EnvironmentVariables["SERVO_BRIDGE_PORT"] = bridgePort.ToString();
            bridgeProcess = Process.Start(info);
            _ = WatchBridgeProcessAsync(bridgeProcess);
            lastResponse = "Started Python bridge";
        }
        catch (Exception e)
        {
            lastResponse = "Start bridge failed: " + e.Message;
            UnityEngine.Debug.LogError(lastResponse);
        }
    }

    public void ConnectTool() => _ = SendCommandAsync("connect");
    public void ForwardTool() => _ = SendCommandAsync("forward");
    public void ReverseTool() => _ = SendCommandAsync("reverse");
    public void StopTool() => _ = SendCommandAsync("stop");
    public void QueryStatus() => _ = SendCommandAsync("status");

    public async Task SendCommandAsync(string command)
    {
        RefreshProcessStatus();

        if (commandRunning)
        {
            lastResponse = "Command already running";
            return;
        }

        commandRunning = true;
        lastResponse = "Sending " + command + "...";

        string response = await Task.Run(() => SendCommandBlocking(command));
        lastResponse = response;
        commandRunning = false;

        if (response.StartsWith("Command failed"))
            UnityEngine.Debug.LogWarning("[ServoTool] " + response);
        else
            UnityEngine.Debug.Log("[ServoTool] " + command + " -> " + response);
    }

    private string SendCommandBlocking(string command)
    {
        try
        {
            using (var client = new TcpClient())
            {
                int timeoutMs = Math.Max(250, Mathf.RoundToInt(commandTimeoutSeconds * 1000f));
                client.SendTimeout = timeoutMs;
                client.ReceiveTimeout = timeoutMs;

                IAsyncResult connectResult = client.BeginConnect(bridgeHost, bridgePort, null, null);
                if (!connectResult.AsyncWaitHandle.WaitOne(timeoutMs))
                    return "Command failed: connect timeout";

                client.EndConnect(connectResult);

                using (NetworkStream stream = client.GetStream())
                {
                    string payload = "{\"cmd\":\"" + EscapeJson(command) + "\"}\n";
                    byte[] bytes = Encoding.UTF8.GetBytes(payload);
                    stream.Write(bytes, 0, bytes.Length);

                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadLine() ?? "No response";
                    }
                }
            }
        }
        catch (Exception e)
        {
            return "Command failed: " + e.Message;
        }
    }

    private void OnGUI()
    {
        if (!showDebugButtons) return;
        RefreshProcessStatus();

        GUILayout.BeginArea(new Rect(1520, Screen.height -990, 360, 200), GUI.skin.box);
        GUILayout.Label("Servo Tool Debug");
        GUILayout.Label("Target: " + toolHost + ":" + toolPort);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Start Bridge", GUILayout.Height(30))) StartBridgeProcess();
        GUI.enabled = !commandRunning;
        if (GUILayout.Button("Connect", GUILayout.Height(30))) ConnectTool();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Forward", GUILayout.Height(34))) ForwardTool();
        if (GUILayout.Button("Reverse", GUILayout.Height(34))) ReverseTool();
        if (GUILayout.Button("Stop", GUILayout.Height(34))) StopTool();
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Status", GUILayout.Height(28))) QueryStatus();
        GUI.enabled = true;
        GUILayout.Label(lastResponse);
        GUILayout.EndArea();
    }

    private void OnDestroy()
    {
        if (bridgeProcess != null && !bridgeProcess.HasExited)
        {
            try { bridgeProcess.Kill(); }
            catch { }
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private async Task WatchBridgeProcessAsync(Process process)
    {
        if (process == null) return;

        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        await Task.Run(() => process.WaitForExit());
        string stderr = await stderrTask;

        string detail = stderr.Trim();
        if (detail.Length > 300) detail = detail.Substring(0, 300);

        lastResponse = "Bridge exited: " + process.ExitCode + " " + detail;
        UnityEngine.Debug.LogWarning("[ServoTool] " + lastResponse);
    }

    private void RefreshProcessStatus()
    {
        if (bridgeProcess == null) return;
        if (!bridgeProcess.HasExited) return;
        if (!lastResponse.StartsWith("Bridge exited")) return;
    }
}
