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
    private int contactUser, actualUser, actualTool, sixForceOnline;
    private ulong currentCommandId, robotTimestamp;
    private long protocolFeedbackSequence;
    private readonly double[] protocolJointSpeeds = new double[6];
    private bool contactFrameConfirmed;
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
        public string id, op, token, stage, query;
        public int user;
        public double planned_distance_mm;
        public bool simulate;
        public double[] delta;
        public double duration = 0.5;
        public int tool = 1;
        public bool enforce_lateral;
        public bool original_timing;
        public bool stop_at_end = true;
    }
    [Serializable] private sealed class RobotReply
    {
        public string id, token, error, result;
        public bool ok, simulated;
        public int mode, robot_error, collision;
        public int six_force_online, user, tool;
        public ulong command_id, robot_timestamp_ms;
        public long sequence;
        public double age_s, timestamp_s;
        public double[] pose, force, joints, values;
        public string benchmark_schema, robot_endpoint;
        public double gateway_processing_ms, dashboard_exchange_ms, dashboard_gate_ms, dashboard_transport_ms;
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
            var lineReader = new GatewayLineReader(stream);
            try
            {
                while (!gatewayClosing)
                {
                    var read = lineReader.ReadAsync();
                    if (await Task.WhenAny(read, Task.Delay(10000)) != read) throw new TimeoutException("算法会话心跳超时");
                    string line = await read;
                    if (line == null) break;
                    RobotRequest request = JsonUtility.FromJson<RobotRequest>(line);
                    bool trace = request != null && request.op != "status" && request.op != "latency_echo" && request.op != "latency_query";
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
                        if (ownerClient == client) RecordContactWorkflowError(e.Message);
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
    // 字节读取不接触Unity对象，不应每个字节等待一次Unity帧调度。
    // 每条完整消息返回后，ServeAlgorithmAsync仍回Unity主线程处理状态和日志。
    private sealed class GatewayLineReader
    {
        private readonly NetworkStream stream;
        private readonly byte[] buffer = new byte[4096];
        private int offset, count;
        public GatewayLineReader(NetworkStream stream) { this.stream = stream; }
        public async Task<string> ReadAsync()
        {
            using (var line = new MemoryStream())
            {
                while (true)
                {
                    if (offset == count)
                    {
                        count = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                        offset = 0;
                        if (count == 0)
                        {
                            if (line.Length > 0) throw new IOException("请求在换行前断开");
                            return null;
                        }
                    }
                    int end = offset;
                    while (end < count && buffer[end] != 10) end++;
                    if (line.Length + end - offset > 8192) throw new IOException("请求超过8192字节");
                    line.Write(buffer, offset, end - offset);
                    offset = end;
                    if (offset < count)
                    {
                        offset++;
                        return new UTF8Encoding(false, true).GetString(line.ToArray());
                    }
                }
            }
        }
    }
    private async Task<RobotReply> HandleRobotRequestAsync(TcpClient client, RobotRequest r)
    {
        // Diagnostic probes never acquire a motion lease, change frames or send motion commands.
        if (r.op == "latency_echo" || r.op == "latency_query")
        {
            if (FieldWorkflowActive || ExternalControlActive || motionBusy) throw new InvalidOperationException("延迟测试要求先结束机械臂任务");
            var measured = System.Diagnostics.Stopwatch.StartNew();
            var result = new RobotReply { result = r.op, benchmark_schema = "latency-v1-2026-10-08",
                robot_endpoint = tcpClient?.Client?.RemoteEndPoint?.ToString() ?? "not_connected" };
            if (r.op == "latency_query")
            {
                if (r.user < 0 || r.user > 50 || r.tool < 0 || r.tool > 50) throw new ArgumentException("User/Tool编号必须为0~50");
                string command;
                switch (r.query)
                {
                    case "pose": command = $"GetPose(user={r.user},tool={r.tool})"; break;
                    case "angle": command = "GetAngle()"; break;
                    case "force": command = $"GetForce({r.tool})"; break;
                    default: throw new ArgumentException("延迟测试仅允许pose/angle/force只读查询");
                }
                var timing = new DashboardTiming();
                result.values = ParseValues(await ExchangeDashboardAsync(command, false, 0, timing));
                result.dashboard_exchange_ms = timing.exchangeMs;
                result.dashboard_gate_ms = timing.gateWaitMs;
                result.dashboard_transport_ms = timing.transportMs;
            }
            result.gateway_processing_ms = measured.Elapsed.TotalMilliseconds;
            return result;
        }
        if (r.op == "stop")
        {
            stopLatched = true;
            commandEpoch++;
            await StopOwnedRobotAsync();
            return new RobotReply { result = "stopped" };
        }
        if (r.op == "acquire")
        {
            if (FieldWorkflowActive && r.simulate) throw new Exception("实物流程期间禁止模拟会话占用反馈");
            if (ExternalControlActive) throw new Exception("其他阶段正在占用机械臂");
            if (!r.simulate)
            {
                if (!allowExternalRealMotion) throw new Exception("Unity未开放外部算法真实运动许可");
                RequireRobotReady(false);
            }
            ownerToken = Guid.NewGuid().ToString("N");
            contactFrameConfirmed = false;
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
                if (!ownerSimulation)
                {
                    RequireContactSensor();
                    contactFrameConfirmed = false;
                    await SelectContactFrameAsync("User", contactUser);
                    await SelectContactFrameAsync("Tool", contactTool);
                    contactFrameConfirmed = true;
                }
            }
            catch (Exception e)
            {
                // User/Tool也可能已经入队，停止未确认时不可释放占用。
                ContactAcquisitionError = e.Message;
                Debug.LogError("[RobotGateway][ACQUIRE_FAILED] " + e.Message);
                await AbortOwnerAsync();
                throw;
            }
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
        if (r.op == "begin_stage") { await BeginDiagnosticStageAsync(r); return new RobotReply { result = "measurement_started" }; }
        if (r.op == "end_stage") { await EndDiagnosticStageAsync(); return new RobotReply { result = "measurement_ended" }; }
        if (r.op == "finish_stage")
        {
            if (!ownerSimulation && !OriginalContactTimingActive) throw new Exception("未启用原算法节奏测试模式");
            await StopOwnedRobotAsync();
            return new RobotReply { result = "stage_stopped", simulated = ownerSimulation };
        }
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
            return new RobotReply { simulated = true, mode = 5, six_force_online = 1, age_s = 0, pose = (double[])simulatedPose.Clone(), force = new double[6], joints = new double[6] };
        lock (feedbackLock)
            return new RobotReply { mode = currentRobotMode, robot_error = robotError, collision = robotCollision,
                six_force_online = sixForceOnline, user = actualUser, tool = actualTool,
                command_id = currentCommandId, robot_timestamp_ms = robotTimestamp, sequence = protocolFeedbackSequence,
                timestamp_s = lastFeedbackUnixSeconds,
                age_s = lastFeedbackUnixSeconds > 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - lastFeedbackUnixSeconds : 1e9,
                pose = (double[])actualToolPose.Clone(), force = (double[])actualForce.Clone(), joints = (double[])actualJoints.Clone() };
    }
    private void RequireRobotReady(bool running)
    {
        var s = CopyRobotState();
        if (s.age_s < 0 || s.age_s > 0.25 || s.robot_error != 0 || s.collision != 0 || (s.mode != 5 && !(running && s.mode == 7)))
            throw new Exception($"反馈过期、报警或状态不允许运动: age_s={s.age_s:F3} mode={s.mode} error={s.robot_error} collision={s.collision}");
        if (ExternalControlActive && !ownerSimulation)
        {
            RequireContactSensor();
            if (contactFrameConfirmed && (s.user != contactUser || s.tool != contactTool))
                throw new Exception("实际User/Tool已改变，禁止继续接触运动");
        }
        double force = 0, moment = 0;
        for (int i = 0; i < 3; i++) { force += s.force[i] * s.force[i]; moment += s.force[i + 3] * s.force[i + 3]; }
        if (!Finite(force) || !Finite(moment) || Math.Sqrt(force) >= hardForceN || Math.Sqrt(moment) >= hardMomentNm)
            throw new Exception("反馈力/力矩无效或超过Unity配置阈值");
    }
    private async Task<double[]> ReadPoseAsync()
    {
        return ownerSimulation ? (double[])simulatedPose.Clone() : ParseValues(await ExchangeDashboardAsync($"GetPose(user={contactUser},tool={contactTool})", true));
    }
    private async Task<RobotReply> MoveRelativeAsync(RobotRequest r)
    {
        if (r.original_timing && !ownerSimulation && !OriginalContactTimingActive)
            throw new Exception("Unity未授权原算法节奏测试模式");
        if (motionBusy) throw new Exception("已有运动执行中");
        if (r.delta == null || r.delta.Length != 3 || !Finite(r.duration) || r.duration < 0.05 || r.duration > 5)
            throw new Exception("delta要求3个毫米数值，duration要求0.05~5秒");
        double distance = 0;
        foreach (double d in r.delta) { if (!Finite(d)) throw new Exception("非有限位移"); distance += d * d; }
        distance = Math.Sqrt(distance);
        if (distance > maxRelativeStepMm || distance / r.duration > maxContactSpeedMmS) throw new Exception("单步位移/速度超过Unity配置");
        motionBusy = true;
        int epoch = commandEpoch;
        ulong motionId = 0;
        long afterAck = 0;
        try
        {
            if (!ownerSimulation) RequireRobotReady(r.original_timing);
            double[] start = await ReadPoseAsync();
            double originObservedAt = measurementClock.Elapsed.TotalSeconds;
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
                string command = "ServoP(" + Numbers(target) + ",t=" + Number(r.duration) + ",aheadtime=50.0,gain=500.0)";
                string reply = await ExchangeDashboardAsync(command, true);
                motionId = ParseCommandId(reply);
                BeginStepMeasurement(start, target, distance, r.duration, command, reply, originObservedAt);
                afterAck = CopyRobotState().sequence;
            }
            if (ownerSimulation) BeginStepMeasurement(start, target, distance, r.duration, "SIM", "SIM", originObservedAt);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < r.duration)
            {
                SampleMotionDiagnostics();
                if (stopLatched || epoch != commandEpoch || gatewayClosing) throw new Exception("运动已取消");
                if (!ownerSimulation)
                {
                    RequireRobotReady(true);
                    if (r.enforce_lateral)
                    {
                        // 反馈表没有承诺ActualTCPForce与指定Tool的YZ轴等价，方向阈值使用显式工具系。
                        var f = ParseValues(await ExchangeDashboardAsync($"GetForce({contactTool})", true));
                        double lateral = Math.Sqrt(f[1]*f[1]+f[2]*f[2]) + 0.25*Math.Sqrt(f[3]*f[3]+f[5]*f[5]);
                        if (lateral >= lateralTriggerN) throw new Exception("轴向运动横向负载超限");
                    }
                }
                await Task.Delay(r.original_timing ? 5 : 20);
            }
            if (stopLatched || epoch != commandEpoch || gatewayClosing) throw new Exception("运动已取消");
            if (ownerSimulation) Array.Copy(target, simulatedPose, 6);
            EndStepMeasurement();
            if (r.original_timing)
            {
                // 原算法：等待t并监控，不把时间结束伪称为实测到位。
                if (r.stop_at_end)
                {
                    if (!ownerSimulation) await ExchangeDashboardAsync("Stop()", true);
                    await Task.Delay(20);
                }
                if (stopLatched || epoch != commandEpoch || gatewayClosing) throw new Exception("运动已取消");
                if (!ownerSimulation) RequireRobotReady(!r.stop_at_end);
                Debug.Log($"[OriginalTiming] duration={Number(r.duration)} elapsed_s={Number(timer.Elapsed.TotalSeconds)} stop_at_end={r.stop_at_end} result=interval_elapsed (不是实测到位)");
                return new RobotReply { result = "interval_elapsed", simulated = ownerSimulation };
            }
            else if (!ownerSimulation)
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
                    double tolerance = distance > 0 ? Math.Min(positionToleranceMm, distance * 0.25) : positionToleranceMm;
                    bool orientationReached = true;
                    for (int i = 3; i < 6; i++)
                        orientationReached &= Math.Abs(Math.IEEERemainder(actual[i] - target[i], 360)) <= 0.1;
                    var state = CopyRobotState();
                    if (state.sequence > afterAck && state.command_id == motionId && state.mode == 5 &&
                        Math.Sqrt(error) <= tolerance && orientationReached) { reached = true; break; }
                    await Task.Delay(40);
                }
                if (!reached) throw new Exception("运动到位超时");
                await StopOwnedRobotAsync();
            }
            return new RobotReply { result = "completed", values = await ReadPoseAsync(), simulated = ownerSimulation };
        }
        finally { motionBusy = false; }
    }
    private int stopDiagnosticId;
    private string StopDiagnostic(out RobotReply state, long previousSequence, out bool stopped)
        => StopDiagnostic(out state, previousSequence, out stopped, out _, out _);
    private string StopDiagnostic(out RobotReply state, long previousSequence, out bool stopped, out string[] reasons, out double[] speeds)
    {
        ulong stamp;
        // 状态与速度来自同一个锁内快照，避免比较两帧数据。
        lock (feedbackLock)
        {
            state = CopyRobotState();
            speeds = (double[])protocolJointSpeeds.Clone();
            stamp = robotTimestamp;
        }
        stopped = !Array.Exists(speeds, v => !Finite(v) || Math.Abs(v) > 0.01);
        var failed = new System.Collections.Generic.List<string>();
        if (state.sequence <= previousSequence) failed.Add("no_new_frame");
        if (!(state.age_s <= 0.25)) failed.Add("feedback_age");
        if (state.mode != 5) failed.Add("mode_not_5");
        if (state.robot_error != 0) failed.Add("robot_error");
        if (state.collision != 0) failed.Add("collision");
        for (int i = 0; i < speeds.Length; i++)
            if (!Finite(speeds[i]) || Math.Abs(speeds[i]) > 0.01) failed.Add("J" + (i + 1) + "_speed");
        reasons = failed.ToArray();
        return $"seq={state.sequence} robot_timestamp_ms={stamp} age_s={Number(state.age_s)} mode={state.mode} error={state.robot_error} collision={state.collision} command_id={state.command_id} joints_deg=[{Numbers(state.joints)}] QDActual_raw=[{Numbers(speeds)}] failed=[{string.Join(",", failed)}]";
    }
    private async Task StopOwnedRobotAsync()
    {
        if (ownerSimulation && ExternalControlActive) return;
        int traceId = Interlocked.Increment(ref stopDiagnosticId);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        string before = StopDiagnostic(out _, -1, out _);
        bool positionPolicy = OriginalContactTimingActive;
        Debug.Log($"[StopTrace#{traceId}][FEEDBACK_BEGIN] {FeedbackDiagnosticCounters()}");
        Debug.Log($"[StopTrace#{traceId}][REQUEST] utc={DateTime.UtcNow:O} command=Stop() {before} policy=age<=0.25,abs(QDActual)<=0.01,mode=5,error=0,collision=0,3_new_samples; 软件阈值非API完成判据");
        if (positionPolicy) Debug.Log($"[StopTrace#{traceId}][POLICY_OVERRIDE] original-timing试验：QDActual仅诊断；新鲜空闲无报警+六轴位置跨度<=0.001deg持续0.5s且至少3个新样本。非硬件安全停止证明");
        try
        {
            string reply = await ExchangeDashboardAsync("Stop()", true, traceId);
            Debug.Log($"[StopTrace#{traceId}][ACK] utc={DateTime.UtcNow:O} elapsed_ms={Number(timer.Elapsed.TotalMilliseconds)} reply={reply.Trim()}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[StopTrace#{traceId}][COMMAND_FAILED] {e.Message} {FeedbackDiagnosticCounters()}");
            throw;
        }
        long afterAck = CopyRobotState().sequence;
        int stableFrames = 0;
        int observedSamples = 0, badSamples = 0;
        int noNewPolls = 0, maxStable = 0, nonFiniteSpeeds = 0;
        double peakSpeed = 0, maxAge = 0;
        var reasonCounts = new System.Collections.Generic.SortedDictionary<string, int>();
        string Summary() => $"no_new_polls={noNewPolls} max_stable={maxStable} peak_abs_QDActual={Number(peakSpeed)} nonfinite_speed_values={nonFiniteSpeeds} max_age_s={Number(maxAge)} new_sample_failures=[{string.Join(",", System.Linq.Enumerable.Select(reasonCounts, p => p.Key + "=" + p.Value))}] {FeedbackDiagnosticCounters()}";
        string last = "";
        double nextLog = 0;
        double positionSince = -1;
        double[] low = null, high = null;
        for (int i = 0; i < 50; i++)
        {
            last = StopDiagnostic(out var state, afterAck, out bool stopped, out var reasons, out var speeds);
            if (positionPolicy && (state.age_s < 0 || state.age_s > 0.25))
            { positionSince = -1; stableFrames = 0; }
            maxAge = Math.Max(maxAge, state.age_s);
            if (state.sequence > afterAck)
            {
                foreach (string reason in reasons)
                    reasonCounts[reason] = reasonCounts.TryGetValue(reason, out int count) ? count + 1 : 1;
                foreach (double speed in speeds)
                    if (Finite(speed)) peakSpeed = Math.Max(peakSpeed, Math.Abs(speed)); else nonFiniteSpeeds++;
                afterAck = state.sequence;
                observedSamples++;
                bool valid = state.age_s >= 0 && state.age_s <= 0.25 && state.mode == 5 && state.robot_error == 0 && state.collision == 0;
                if (positionPolicy)
                {
                    valid &= state.joints != null && state.joints.Length == 6 && !Array.Exists(state.joints, v => !Finite(v));
                    if (!valid) { positionSince = -1; stableFrames = 0; }
                    else
                    {
                        bool reset = positionSince < 0;
                        if (!reset) for (int j = 0; j < 6; j++)
                        {
                            low[j] = Math.Min(low[j], state.joints[j]); high[j] = Math.Max(high[j], state.joints[j]);
                            if (high[j] - low[j] > 0.001) reset = true;
                        }
                        if (reset)
                        {
                            low = (double[])state.joints.Clone(); high = (double[])state.joints.Clone();
                            positionSince = timer.Elapsed.TotalSeconds; stableFrames = 1;
                        }
                        else stableFrames++;
                    }
                }
                else stableFrames = valid && stopped ? stableFrames + 1 : 0;
                if (stableFrames == 0) badSamples++;
                maxStable = Math.Max(maxStable, stableFrames);
                if (stableFrames >= 3 && (!positionPolicy || timer.Elapsed.TotalSeconds - positionSince >= 0.5))
                {
                    if (positionPolicy) last += $" position_window_s={Number(timer.Elapsed.TotalSeconds - positionSince)} position_samples={stableFrames} QDActual_failed_is_diagnostic_only=true";
                    Debug.Log($"[StopTrace#{traceId}][PASS] observed={observedSamples} bad={badSamples} stable={stableFrames}/3 elapsed_ms={Number(timer.Elapsed.TotalMilliseconds)} {last} {Summary()}");
                    return;
                }
            }
            else noNewPolls++;
            if (positionPolicy) last += $" position_window_s={Number(positionSince < 0 ? 0 : timer.Elapsed.TotalSeconds - positionSince)} position_samples={stableFrames} QDActual_failed_is_diagnostic_only=true";
            if (timer.Elapsed.TotalSeconds >= nextLog)
            {
                Debug.Log($"[StopTrace#{traceId}][WAIT] observed={observedSamples} bad={badSamples} stable={stableFrames}/3 {last}");
                nextLog = timer.Elapsed.TotalSeconds + 0.25;
            }
            await Task.Delay(40);
        }
        string failure = $"StopTrace#{traceId} observed={observedSamples} bad={badSamples} stable={stableFrames}/3 {last} {Summary()}";
        Debug.LogError("[StopTrace][TIMEOUT] " + failure);
        throw new Exception("未确认机械臂停止，保留占用状态；" + failure);
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
    private static string SpeedCommand(double percent)
    {
        if (!Finite(percent) || percent < 1 || percent > 100 || percent != Math.Truncate(percent))
            throw new ArgumentException("SpeedFactor必须为1~100整数百分比");
        return "SpeedFactor(" + ((int)percent).ToString(CultureInfo.InvariantCulture) + ")";
    }
    private static ulong ParseCommandId(string reply)
    {
        var match = Regex.Match(reply, @"^\s*0\s*,\s*\{\s*(\d+)\s*\}");
        if (!match.Success || !ulong.TryParse(match.Groups[1].Value, out ulong id))
            throw new IOException("队列命令缺少有效ResultID: " + reply);
        return id;
    }
    private void RequireContactSensor()
    {
        if (CopyRobotState().six_force_online != 1)
            throw new InvalidOperationException("六维力传感器离线/异常，禁止真实接触运动");
    }
    public string ContactAcquisitionError { get; private set; }
    private async Task SelectContactFrameAsync(string name, int value)
    {
        string reply = await ExchangeDashboardAsync($"{name}({value})", true);
        // Empty ResultID is accepted only for User/Tool, never motion commands.
        bool emptyId = Regex.IsMatch(reply, @"^\s*0\s*,\s*\{\s*\}\s*,");
        ulong id = emptyId ? 0 : ParseCommandId(reply);
        Debug.Log($"[RobotGateway][FRAME_ACK] {reply} confirmation={(emptyId ? "fresh_frame_state" : "command_id_and_frame_state")}");
        long afterAck = CopyRobotState().sequence;
        int epoch = commandEpoch;
        int stable = 0;
        for (int i = 0; i < 75; i++)
        {
            if (stopLatched || commandEpoch != epoch) throw new OperationCanceledException("坐标系切换已停止");
            RequireRobotReady(true);
            var s = CopyRobotState();
            if (s.sequence > afterAck)
            {
                afterAck = s.sequence;
                bool matches = (emptyId || s.command_id == id) && s.mode == 5 &&
                    (name == "User" ? s.user : s.tool) == value;
                stable = matches ? stable + 1 : 0;
                if (stable >= 3)
                {
                    Debug.Log($"[RobotGateway][FRAME_CONFIRMED] {name}={value} sequence={s.sequence}");
                    return;
                }
            }
            await Task.Delay(40);
        }
        throw new TimeoutException(name + "坐标系切换未执行完成");
    }
    private static double[] ParseValues(string reply)
    {
        var match = Regex.Match(reply, @"\{([^}]*)\}");
        if (!match.Success) throw new Exception("机械臂返回数据缺少数值");
        var values = Array.ConvertAll(match.Groups[1].Value.Split(','), v => double.Parse(v, CultureInfo.InvariantCulture));
        if (values.Length != 6 || Array.Exists(values, v => !Finite(v))) throw new Exception("机械臂返回数据不是6个有限数值");
        return values;
    }

    // 唯一Dashboard读写位置。超时后关闭连接，防止迟到应答被下一请求误认。
    private sealed class DashboardTiming
    {
        public string writeBegin = "not_started", writeEnd = "not_completed", replyEnd = "not_received";
        public double gateWaitMs, transportMs, exchangeMs;
    }
    private async Task<string> ExchangeDashboardAsync(string command, bool external, int stopTraceId = 0, DashboardTiming latencyTiming = null)
    {
        int epoch = commandEpoch;
        var timing = latencyTiming ?? (stopTraceId == 0 ? null : new DashboardTiming());
        var queued = System.Diagnostics.Stopwatch.StartNew();
        await dashboardGate.WaitAsync();
        double gateMs = queued.Elapsed.TotalMilliseconds;
        if (timing != null) timing.gateWaitMs = gateMs;
        try
        {
            if (external && command != "Stop()" && (stopLatched || epoch != commandEpoch)) throw new Exception("当前阶段已停止");
            if (!external && (ExternalControlActive || FieldWorkflowActive || epoch != commandEpoch) && command != "Stop()") throw new Exception("当前阶段不允许普通运动指令");
            if (networkStream == null || tcpClient == null || !tcpClient.Connected) throw new IOException("机械臂未连接");
            var stream = networkStream;
            var operation = ExchangePacketAsync(stream, command, timing);
            if (await Task.WhenAny(operation, Task.Delay(3000)) != operation)
            {
                stream.Close();
                try { await operation; } catch { }
                throw new TimeoutException("Dashboard应答超时，连接已关闭");
            }
            string reply = await operation;
            var parsed = Regex.Match(reply, @"^\s*(-?\d+)\s*,\s*\{[^}]*\}\s*,\s*(.*?)\s*;\s*$", RegexOptions.Singleline);
            if (!parsed.Success) throw new IOException("Dashboard应答格式错误: " + reply);
            if (!string.Equals(Regex.Replace(parsed.Groups[2].Value, @"\s+", ""), Regex.Replace(command, @"\s+", ""), StringComparison.OrdinalIgnoreCase))
            { stream.Close(); throw new IOException("Dashboard应答与请求不匹配，连接已关闭: " + reply); }
            if (parsed.Groups[1].Value != "0") throw new IOException("机械臂拒绝命令: " + reply);
            if (timing != null) timing.exchangeMs = queued.Elapsed.TotalMilliseconds;
            return reply;
        }
        finally
        {
            dashboardGate.Release();
            if (timing != null && stopTraceId != 0)
                Debug.Log($"[StopTrace#{stopTraceId}][TRANSPORT] gate_wait_ms={Number(gateMs)} write_begin_utc={timing.writeBegin} write_complete_utc={timing.writeEnd} reply_complete_utc={timing.replyEnd} dispatch_utc={DateTime.UtcNow:O}");
        }
    }
    private static async Task<string> ExchangePacketAsync(NetworkStream stream, string command, DashboardTiming timing = null)
    {
        var transport = timing == null ? null : System.Diagnostics.Stopwatch.StartNew();
        byte[] bytes = Encoding.UTF8.GetBytes(command + "\n");
        if (timing != null) timing.writeBegin = DateTime.UtcNow.ToString("O");
        await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        if (timing != null) timing.writeEnd = DateTime.UtcNow.ToString("O");
        var reply = new StringBuilder();
        byte[] one = new byte[1];
        while (reply.Length < 16384)
        {
            if (await stream.ReadAsync(one, 0, 1).ConfigureAwait(false) == 0) throw new IOException("Dashboard断开");
            char c = (char)one[0];
            if ((c == '\n' || c == '\r') && reply.Length == 0) continue;
            reply.Append(c);
            if (c == ';')
            {
                if (timing != null) timing.replyEnd = DateTime.UtcNow.ToString("O");
                if (timing != null) timing.transportMs = transport.Elapsed.TotalMilliseconds;
                return reply.ToString();
            }
        }
        throw new IOException("Dashboard应答过长");
    }
}
