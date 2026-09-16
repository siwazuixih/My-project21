using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Unity 独占机器人 TCP；外部算法只连接回环地址。所有异步处理回到 Unity 同步上下文。
public partial class DobotController
{
    [Header("外部算法统一入口（本机）")]
    [SerializeField] private int robotGatewayPort = 19120;
    [SerializeField] private bool allowExternalRealMotion = false;
    [SerializeField] private int contactTool = 1;
    [SerializeField] private float maxRelativeStepMm = 5f;
    [SerializeField] private float maxContactSpeedMmS = 5f;
    [SerializeField] private float positionToleranceMm = 0.2f;
    [SerializeField] private float hardForceN = 30f;
    [SerializeField] private float hardMomentNm = 10f;
    [SerializeField] private float lateralTriggerN = 20f;
    private readonly SemaphoreSlim dashboardGate = new SemaphoreSlim(1, 1);
    private readonly double[] actualForce = new double[6];
    private readonly double[] actualToolPose = new double[6];
    private int robotError, robotCollision;
    private TcpListener robotListener;
    private TcpClient ownerClient;
    private string ownerToken;
    private bool ownerSimulation, gatewayClosing, motionBusy, stopLatched;
    private int commandEpoch;
    private readonly double[] simulatedPose = new double[6];
    public bool ExternalControlActive => ownerToken != null;
    public int RobotGatewayPort => robotGatewayPort;

    [Serializable] private sealed class RobotRequest
    {
        public string id, op, token;
        public bool simulate;
        public double[] delta;
        public double duration = 0.5;
        public int tool = 1;
        public bool enforce_lateral;
    }
    [Serializable] private sealed class RobotReply
    {
        public string id, token, error, result;
        public bool ok, simulated;
        public int mode, robot_error, collision;
        public double age_s, timestamp_s;
        public double[] pose, force, joints, values;
    }

    private void OnEnable()
    {
        gatewayClosing = false;
        try
        {
            robotListener = new TcpListener(IPAddress.Loopback, robotGatewayPort);
            robotListener.Start(4);
            _ = AcceptAlgorithmsAsync(robotListener);
            Debug.Log($"[RobotGateway] 127.0.0.1:{robotGatewayPort} 已启动，真实运动许可={allowExternalRealMotion}");
        }
        catch (Exception e) { Debug.LogError("[RobotGateway] 启动失败: " + e.Message); }
    }
    private void OnDisable() { ShutdownRobotGateway(); }
    private void ShutdownRobotGateway()
    {
        if (gatewayClosing) return;
        gatewayClosing = true;
        robotListener?.Stop();
        ownerClient?.Close();
        if (ExternalControlActive) _ = AbortOwnerAsync();
    }
    private void RevokeExternalControl()
    {
        stopLatched = true;
        commandEpoch++;
        ownerClient?.Close();
        if (ExternalControlActive) _ = AbortOwnerAsync();
    }

    private async Task AcceptAlgorithmsAsync(TcpListener listener)
    {
        try
        {
            while (!gatewayClosing)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = ServeAlgorithmAsync(client);
            }
        }
        catch (Exception e) { if (!gatewayClosing) Debug.LogWarning("[RobotGateway] " + e.Message); }
    }
    private async Task ServeAlgorithmAsync(TcpClient client)
    {
        // 一连接一会话；请求严格串行。断线或10秒无请求将停止并释放。
        using (client)
        using (var stream = client.GetStream())
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
        {
            try
            {
                while (!gatewayClosing)
                {
                    var read = ReadJsonLineAsync(stream);
                    if (await Task.WhenAny(read, Task.Delay(10000)) != read) throw new TimeoutException("算法会话心跳超时");
                    string line = await read;
                    if (line == null) break;
                    RobotRequest request = JsonUtility.FromJson<RobotRequest>(line);
                    bool trace = request != null && request.op != "status";
                    var traceTimer = System.Diagnostics.Stopwatch.StartNew();
                    string traceMode = request?.op == "acquire"
                        ? (request.simulate ? "SIM_REQUEST" : "REAL_REQUEST")
                        : (ownerSimulation && ExternalControlActive ? "SIM" : "REAL_OR_UNOWNED");
                    if (trace)
                        Debug.Log($"[RobotTrace][Unity][RX] id={request.id} op={request.op} mode={traceMode} delta_mm=[{(request.delta == null ? "" : Numbers(request.delta))}] duration_s={Number(request.duration)} tool={request.tool} enforce_lateral={request.enforce_lateral}");
                    RobotReply reply;
                    try
                    {
                        if (request == null || string.IsNullOrEmpty(request.id)) throw new Exception("请求缺少id");
                        reply = await HandleRobotRequestAsync(client, request);
                        reply.ok = true;
                    }
                    catch (Exception e)
                    {
                        reply = new RobotReply { error = e.Message, result = "failed" };
                        if (ownerClient == client && request != null && request.op == "move_relative")
                        {
                            stopLatched = true;
                            try { await StopOwnedRobotAsync(); } catch (Exception stopError) { reply.error += "; Stop: " + stopError.Message; }
                        }
                    }
                    reply.id = request?.id;
                    await writer.WriteLineAsync(JsonUtility.ToJson(reply));
                    if (trace)
                        Debug.Log($"[RobotTrace][Unity][TX] id={request.id} op={request.op} mode={traceMode} ok={reply.ok} result={reply.result} values=[{(reply.values == null ? "" : Numbers(reply.values))}] units={(request.op == "wrench" ? "N,Nm" : "mm,deg")} error={reply.error} handling_ms={traceTimer.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}");
                }
            }
            catch (Exception e) { if (!gatewayClosing) Debug.LogWarning("[RobotGateway] 会话结束: " + e.Message); }
            finally { if (ownerClient == client) await AbortOwnerAsync(); }
        }
    }
    private static async Task<string> ReadJsonLineAsync(NetworkStream stream)
    {
        var bytes = new System.Collections.Generic.List<byte>();
        var one = new byte[1];
        while (bytes.Count < 8192)
        {
            if (await stream.ReadAsync(one, 0, 1) == 0) return null;
            if (one[0] == 10) return Encoding.UTF8.GetString(bytes.ToArray());
            bytes.Add(one[0]);
        }
        throw new IOException("请求超过8192字节");
    }
    private async Task<RobotReply> HandleRobotRequestAsync(TcpClient client, RobotRequest r)
    {
        if (r.op == "stop")
        {
            stopLatched = true;
            commandEpoch++;
            await StopOwnedRobotAsync();
            return new RobotReply { result = "stopped" };
        }
        if (r.op == "acquire")
        {
            if (ExternalControlActive) throw new Exception("其他阶段正在占用机械臂");
            if (!r.simulate)
            {
                if (!allowExternalRealMotion) throw new Exception("Unity未开放外部算法真实运动许可");
                RequireRobotReady(false);
            }
            ownerToken = Guid.NewGuid().ToString("N");
            ownerClient = client;
            ownerSimulation = r.simulate;
            stopLatched = false;
            commandEpoch++; // 使交接前等待发送的普通命令失效
            try
            {
                // 等待交接前已经发出的请求收尾，再重新确认机器人空闲。
                await dashboardGate.WaitAsync();
                dashboardGate.Release();
                if (!ownerSimulation) RequireRobotReady(false);
                // 工具编号在Unity侧统一，算法不能任意切换。
                if (!ownerSimulation) await ExchangeDashboardAsync($"Tool({contactTool})", true);
            }
            catch { ownerToken = null; ownerClient = null; throw; }
            return new RobotReply { token = ownerToken, simulated = ownerSimulation, result = "acquired" };
        }
        if (r.op == "status") return CopyRobotState();
        if (client != ownerClient || r.token != ownerToken || ownerToken == null) throw new Exception("无当前阶段运动权限");
        if (r.op == "release")
        {
            await StopOwnedRobotAsync();
            ownerToken = null;
            ownerClient = null;
            commandEpoch++;
            return new RobotReply { result = "released" };
        }
        if (stopLatched) throw new Exception("会话已停止，请释放后重新开始阶段");
        if (r.op == "pose") return new RobotReply { values = await ReadPoseAsync(), simulated = ownerSimulation };
        if (r.op == "wrench")
        {
            if (r.tool != contactTool) throw new Exception("算法工具编号与Unity contactTool不一致");
            return new RobotReply { values = ownerSimulation ? new double[6] : ParseValues(await ExchangeDashboardAsync($"GetForce({contactTool})", true)), simulated = ownerSimulation };
        }
        if (r.op == "move_relative") return await MoveRelativeAsync(r);
        throw new Exception("不支持的操作: " + r.op);
    }

    private RobotReply CopyRobotState()
    {
        if (ownerSimulation && ExternalControlActive)
            return new RobotReply { simulated = true, mode = 5, age_s = 0, pose = (double[])simulatedPose.Clone(), force = new double[6], joints = new double[6] };
        lock (feedbackLock)
            return new RobotReply { mode = currentRobotMode, robot_error = robotError, collision = robotCollision,
                timestamp_s = lastFeedbackUnixSeconds,
                age_s = lastFeedbackUnixSeconds > 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - lastFeedbackUnixSeconds : 1e9,
                pose = (double[])actualToolPose.Clone(), force = (double[])actualForce.Clone(), joints = (double[])actualJoints.Clone() };
    }
    private void RequireRobotReady(bool running)
    {
        var s = CopyRobotState();
        if (s.age_s > 0.25 || s.robot_error != 0 || s.collision != 0 || (s.mode != 5 && !(running && s.mode == 7)))
            throw new Exception("反馈过期、报警或机械臂状态不允许运动");
        double force = 0, moment = 0;
        for (int i = 0; i < 3; i++) { force += s.force[i] * s.force[i]; moment += s.force[i + 3] * s.force[i + 3]; }
        if (!Finite(force) || !Finite(moment) || Math.Sqrt(force) >= hardForceN || Math.Sqrt(moment) >= hardMomentNm)
            throw new Exception("反馈力/力矩无效或超过Unity配置阈值");
    }
    private async Task<double[]> ReadPoseAsync()
    {
        return ownerSimulation ? (double[])simulatedPose.Clone() : ParseValues(await ExchangeDashboardAsync("GetPose()", true));
    }
    private async Task<RobotReply> MoveRelativeAsync(RobotRequest r)
    {
        if (motionBusy) throw new Exception("已有运动执行中");
        if (r.delta == null || r.delta.Length != 3 || !Finite(r.duration) || r.duration < 0.05 || r.duration > 5)
            throw new Exception("delta要求3个毫米数值，duration要求0.05~5秒");
        double distance = 0;
        foreach (double d in r.delta) { if (!Finite(d)) throw new Exception("非有限位移"); distance += d * d; }
        distance = Math.Sqrt(distance);
        if (distance > maxRelativeStepMm || distance / r.duration > maxContactSpeedMmS) throw new Exception("单步位移/速度超过Unity配置");
        motionBusy = true;
        int epoch = commandEpoch;
        try
        {
            if (!ownerSimulation) RequireRobotReady(false);
            double[] start = await ReadPoseAsync();
            double[] target;
            if (ownerSimulation)
            {
                target = (double[])start.Clone();
                for (int i = 0; i < 3; i++) target[i] += r.delta[i];
            }
            else
            {
                target = ParseValues(await ExchangeDashboardAsync("RelPointTool(pose={" + Numbers(start) + "},{" + Numbers(r.delta) + ",0,0,0})", true));
                if (stopLatched || epoch != commandEpoch) throw new Exception("运动已取消");
                await ExchangeDashboardAsync("ServoP(" + Numbers(target) + ",t=" + Number(r.duration) + ",aheadtime=50,gain=500)", true);
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < r.duration)
            {
                if (stopLatched || epoch != commandEpoch || gatewayClosing) throw new Exception("运动已取消");
                if (!ownerSimulation)
                {
                    RequireRobotReady(true);
                    if (r.enforce_lateral)
                    {
                        var f = CopyRobotState().force;
                        double lateral = Math.Sqrt(f[1]*f[1]+f[2]*f[2]) + 0.25*Math.Sqrt(f[3]*f[3]+f[5]*f[5]);
                        if (lateral >= lateralTriggerN) throw new Exception("轴向运动横向负载超限");
                    }
                }
                await Task.Delay(20);
            }
            if (stopLatched || epoch != commandEpoch || gatewayClosing) throw new Exception("运动已取消");
            if (ownerSimulation) Array.Copy(target, simulatedPose, 6);
            else
            {
                // 收到应答不是到位；同坐标系GetPose闭环核验，再停止并确认空闲。
                bool reached = false;
                while (timer.Elapsed.TotalSeconds < r.duration + 3)
                {
                    if (stopLatched || epoch != commandEpoch) throw new Exception("运动已取消");
                    RequireRobotReady(true);
                    double[] actual = await ReadPoseAsync();
                    double error = 0;
                    for (int i = 0; i < 3; i++) error += Math.Pow(actual[i] - target[i], 2);
                    if (Math.Sqrt(error) <= positionToleranceMm) { reached = true; break; }
                    await Task.Delay(40);
                }
                if (!reached) throw new Exception("运动到位超时");
                await StopOwnedRobotAsync();
            }
            return new RobotReply { result = "completed", values = await ReadPoseAsync(), simulated = ownerSimulation };
        }
        finally { motionBusy = false; }
    }
    private async Task StopOwnedRobotAsync()
    {
        if (ownerSimulation && ExternalControlActive) return;
        await ExchangeDashboardAsync("Stop()", true);
        for (int i = 0; i < 50; i++)
        {
            var state = CopyRobotState();
            if (state.age_s <= 0.25 && state.mode == 5) return;
            await Task.Delay(40);
        }
        throw new Exception("未确认机械臂停止，保留占用状态");
    }
    private async Task AbortOwnerAsync()
    {
        string revokedToken = ownerToken;
        stopLatched = true;
        commandEpoch++;
        try
        {
            await StopOwnedRobotAsync();
            if (ownerToken == revokedToken)
            {
                ownerToken = null;
                ownerClient = null;
            }
        }
        catch (Exception e) { Debug.LogError("[RobotGateway] " + e.Message); }
    }
    private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    private static string Number(double x) => x.ToString("F6", CultureInfo.InvariantCulture);
    private static string Numbers(double[] x) => string.Join(",", Array.ConvertAll(x, Number));
    private static double[] ParseValues(string reply)
    {
        var match = Regex.Match(reply, @"\{([^}]*)\}");
        if (!match.Success) throw new Exception("机械臂返回数据缺少数值");
        var values = Array.ConvertAll(match.Groups[1].Value.Split(','), v => double.Parse(v, CultureInfo.InvariantCulture));
        if (values.Length != 6 || Array.Exists(values, v => !Finite(v))) throw new Exception("机械臂返回数据不是6个有限数值");
        return values;
    }

    // 唯一Dashboard读写位置。超时后关闭连接，防止迟到应答被下一请求误认。
    private async Task<string> ExchangeDashboardAsync(string command, bool external)
    {
        int epoch = commandEpoch;
        await dashboardGate.WaitAsync();
        try
        {
            if (external && command != "Stop()" && (stopLatched || epoch != commandEpoch)) throw new Exception("当前阶段已停止");
            if (!external && (ExternalControlActive || epoch != commandEpoch) && command != "Stop()") throw new Exception("当前阶段不允许普通运动指令");
            if (networkStream == null || tcpClient == null || !tcpClient.Connected) throw new IOException("机械臂未连接");
            var stream = networkStream;
            var operation = ExchangePacketAsync(stream, command);
            if (await Task.WhenAny(operation, Task.Delay(3000)) != operation)
            {
                stream.Close();
                try { await operation; } catch { }
                throw new TimeoutException("Dashboard应答超时，连接已关闭");
            }
            string reply = await operation;
            if (!reply.TrimStart().StartsWith("0,")) throw new IOException("机械臂拒绝命令: " + reply);
            return reply;
        }
        finally { dashboardGate.Release(); }
    }
    private static async Task<string> ExchangePacketAsync(NetworkStream stream, string command)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(command + "\n");
        await stream.WriteAsync(bytes, 0, bytes.Length);
        var reply = new StringBuilder();
        byte[] one = new byte[1];
        while (reply.Length < 16384)
        {
            if (await stream.ReadAsync(one, 0, 1) == 0) throw new IOException("Dashboard断开");
            char c = (char)one[0];
            if ((c == '\n' || c == '\r') && reply.Length == 0) continue;
            reply.Append(c);
            if (c == ';' || c == '\n') return reply.ToString();
        }
        throw new IOException("Dashboard应答过长");
    }
}
