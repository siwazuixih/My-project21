using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public partial class RobokitController
{
    public bool FieldWorkflowOwned { get; set; }
    public bool FieldConnected => statusClient != null && statusClient.Connected && navClient != null && navClient.Connected;
    public bool FieldConnectedTo(string ip, bool requireStatus = true)
    {
        try
        {
            var address = System.Net.IPAddress.Parse(ip);
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            bool navigation = navClient != null && navClient.Connected && navStream != null && navStream.CanRead && navStream.CanWrite &&
                ChassisEndpointMatches(navClient, address);
            return navigation && (!requireStatus || (statusClient != null && statusClient.Connected &&
                ChassisEndpointMatches(statusClient, address)));
        }
        catch { return false; }
    }
    private static bool ChassisEndpointMatches(TcpClient client, System.Net.IPAddress expected)
    {
        var actual = ((System.Net.IPEndPoint)client.Client.RemoteEndPoint).Address;
        if (actual.IsIPv4MappedToIPv6) actual = actual.MapToIPv4();
        return actual.Equals(expected);
    }
    [Header("底盘完成确认（1020，task_type=7）")]
    public float statusPollSeconds = 0.2f;
    public float responseTimeoutSeconds = 3f;
    public float motionStartTimeoutSeconds = 10f;
    public float motionTimeoutSeconds = 120f;
    private ushort chassisSequence;
    private readonly SemaphoreSlim chassisRequestGate = new SemaphoreSlim(1, 1);
    private readonly Dictionary<ushort, ChassisPending> chassisPending = new Dictionary<ushort, ChassisPending>();
    private CancellationTokenSource chassisMotionCancellation;
    private int chassisEpoch;
    public int CancellationVersion { get; private set; }
    public bool ConfirmedMotionActive => chassisMotionCancellation != null;
    public string LastConfirmedMotionError { get; private set; } = "";
    private sealed class ChassisPending
    {
        public NetworkStream stream;
        public ushort responseApi;
        public TaskCompletionSource<string> completion = new TaskCompletionSource<string>();
    }
    [Serializable] private sealed class ChassisReply
    {
        public int ret_code;
        public string err_msg;
        public int task_status = -1;
        public int task_type = -1;
    }
    public static string ChassisStateName(int value)
    {
        string[] names = { "NONE", "WAITING", "RUNNING", "SUSPENDED", "COMPLETED", "FAILED", "CANCELED" };
        return value >= 0 && value < names.Length ? names[value] : "UNKNOWN";
    }
    private static ChassisReply DecodeChassisReply(string json)
    {
        var value = new ChassisReply();
        if (!string.IsNullOrWhiteSpace(json)) JsonUtility.FromJsonOverwrite(json, value);
        if (value.ret_code != 0) throw new IOException($"底盘ret_code={value.ret_code}: {value.err_msg}");
        return value;
    }
    private static async Task ReadChassisExactAsync(NetworkStream stream, byte[] bytes)
    {
        int offset = 0;
        while (offset < bytes.Length)
        {
            int size = await stream.ReadAsync(bytes, offset, bytes.Length - offset).ConfigureAwait(false);
            if (size == 0) throw new IOException("底盘连接已断开或响应不完整");
            offset += size;
        }
    }
    private bool CompleteChassisReply(NetworkStream stream, ushort sequence, ushort api, string json)
    {
        if (!chassisPending.TryGetValue(sequence, out var pending) || pending.stream != stream) return false;
        if (pending.responseApi != api) pending.completion.TrySetException(new IOException("底盘响应API不匹配"));
        else pending.completion.TrySetResult(json);
        return true;
    }
    private void FailChassisStream(NetworkStream stream, Exception error)
    {
        if (stream == null) return;
        var entries = new List<ChassisPending>(chassisPending.Values);
        foreach (var pending in entries)
            if (pending.stream == stream) pending.completion.TrySetException(error);
        stream.Close();
    }
    private async Task<string> RequestConfirmedAsync(ushort api, string json, CancellationToken cancel)
    {
        int epoch = chassisEpoch;
        await chassisRequestGate.WaitAsync(cancel);
        ushort sequence = 0;
        NetworkStream stream = null;
        bool sent = false;
        try
        {
            cancel.ThrowIfCancellationRequested();
            if (api >= 2000 && api != 3003 && epoch != chassisEpoch) throw new OperationCanceledException("旧底盘命令已取消");
            stream = api < 2000 ? statusStream : api < 3000 ? controlStream : navStream;
            if (stream == null) throw new IOException("底盘对应端口未连接");
            if (chassisSequence == ushort.MaxValue) throw new IOException("底盘流水号用尽，请停止后重新启动软件");
            sequence = ++chassisSequence; // 不复用旧流水号，迟到响应不能命中新请求
            var pending = new ChassisPending { stream = stream, responseApi = (ushort)(api + 10000) };
            chassisPending.Add(sequence, pending);
            byte[] packet = RobokitProtocol.Pack(api, json, sequence);
            sent = true;
            stream.Write(packet, 0, packet.Length);
            using (cancel.Register(() => pending.completion.TrySetCanceled()))
            {
                if (await Task.WhenAny(pending.completion.Task, Task.Delay(TimeSpan.FromSeconds(Math.Max(0.2, responseTimeoutSeconds)))) != pending.completion.Task)
                    throw new TimeoutException($"底盘API {api} seq={sequence} 应答超时");
                string reply = await pending.completion.Task;
                DecodeChassisReply(reply);
                // 厂商建议每端口请求间隔至少100~200ms；统一串行限制普通UI和自动查询。
                await Task.Delay(200, cancel);
                return reply;
            }
        }
        catch (TimeoutException) { if (sent) stream?.Close(); throw; }
        finally { chassisPending.Remove(sequence); chassisRequestGate.Release(); }
    }
    public void CancelConfirmedMotion()
    {
        CancellationVersion++;
        chassisEpoch++;
        chassisMotionCancellation?.Cancel();
    }
    // 兼容旧耗时等待，但仍核对发送应答/取消；不请求1020，也绝不声称已到位。
    public async Task<bool> ExecuteRelativeLegacyTimedAsync(ushort api, string json, double estimatedSeconds)
    {
        if (ConfirmedMotionActive) { Debug.LogError("已有底盘动作执行中"); return false; }
        if (api != 3055 && api != 3056) throw new ArgumentOutOfRangeException(nameof(api));
        if (double.IsNaN(estimatedSeconds) || double.IsInfinity(estimatedSeconds) || estimatedSeconds < 0 || estimatedSeconds > 600)
            throw new ArgumentOutOfRangeException(nameof(estimatedSeconds));
        var cancel = new CancellationTokenSource();
        chassisEpoch++; chassisMotionCancellation = cancel; LastConfirmedMotionError = "";
        bool sent = false;
        try
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            sent = true;
            await RequestConfirmedAsync(api, json, cancel.Token);
            Debug.Log($"[底盘旧模式] API={api} 已应答，预计总等待={estimatedSeconds:F2}s；不查询1020，未确认实际到位");
            while (elapsed.Elapsed.TotalSeconds < estimatedSeconds)
            {
                cancel.Token.ThrowIfCancellationRequested();
                if (navStream == null || !navStream.CanRead || !navStream.CanWrite) throw new IOException("底盘导航连接在旧模式等待期间断开");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, Math.Min(.1, estimatedSeconds - elapsed.Elapsed.TotalSeconds))), cancel.Token);
            }
            cancel.Token.ThrowIfCancellationRequested();
            Debug.Log($"[底盘旧模式] API={api} 估时等待结束（不是完成状态确认）");
            return true;
        }
        catch (Exception e)
        {
            LastConfirmedMotionError = e.Message;
            Debug.LogError("[底盘旧模式] 中止，不继续后续段：" + e.Message);
            if (sent)
            {
                try { await RequestConfirmedAsync(3003, "", CancellationToken.None); }
                catch (Exception stopError) { Debug.LogError("底盘停止请求未获确认，需要现场检查：" + stopError.Message); }
            }
            return false;
        }
        finally { chassisMotionCancellation = null; cancel.Dispose(); }
    }
    public async Task<bool> ExecuteRelativeConfirmedAsync(ushort api, string json)
    {
        if (ConfirmedMotionActive) { Debug.LogError("已有底盘闭环动作执行中"); return false; }
        if (api != 3055 && api != 3056) throw new ArgumentOutOfRangeException(nameof(api));
        var cancel = new CancellationTokenSource();
        chassisEpoch++;
        chassisMotionCancellation = cancel;
        LastConfirmedMotionError = "";
        bool sent = false;
        try
        {
            var before = DecodeChassisReply(await RequestConfirmedAsync(1020, "{\"simple\":false}", cancel.Token));
            if (before.task_status < 0 || before.task_status > 6 || before.task_status == 1 || before.task_status == 2 || before.task_status == 3)
                throw new IOException("底盘已有活动任务或状态无效，不覆盖已有任务");
            sent = true;
            await RequestConfirmedAsync(api, json, cancel.Token);
            Debug.Log($"[底盘闭环] API={api} 指令已应答，开始查询1020，尚未确认完成");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            bool sawRunning = false;
            int lastState = -1;
            while (timer.Elapsed.TotalSeconds < Math.Max(1, motionTimeoutSeconds))
            {
                cancel.Token.ThrowIfCancellationRequested();
                var status = DecodeChassisReply(await RequestConfirmedAsync(1020, "{\"simple\":false}", cancel.Token));
                if (timer.Elapsed.TotalSeconds >= Math.Max(1, motionTimeoutSeconds))
                    throw new TimeoutException("底盘状态响应到达时已超过运动期限");
                if (status.task_status != lastState)
                {
                    Debug.Log($"[底盘闭环] API={api} task_type={status.task_type} task_status={status.task_status}({ChassisStateName(status.task_status)}) elapsed_s={timer.Elapsed.TotalSeconds:F2}");
                    lastState = status.task_status;
                }
                if (status.task_status < 0 || status.task_status > 6 || status.task_type != 7)
                    throw new IOException("底盘未返回有效的平动/转动状态(task_type=7)，停止后续任务");
                if (status.task_status == 3 || status.task_status == 5 || status.task_status == 6)
                    throw new IOException("底盘任务" + ChassisStateName(status.task_status));
                if (status.task_status == 2) sawRunning = true;
                if (status.task_status == 4 && sawRunning) return true;
                if (!sawRunning && timer.Elapsed.TotalSeconds >= Math.Max(1, motionStartTimeoutSeconds))
                    throw new TimeoutException("未观察到本段RUNNING，不能将旧COMPLETED当作到位（短动作也不自动放行）");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.2, statusPollSeconds)), cancel.Token);
            }
            throw new TimeoutException("底盘运动未在期限内完成");
        }
        catch (Exception e)
        {
            LastConfirmedMotionError = e.Message;
            Debug.LogError("[底盘闭环] 中止，禁止进入下一段: " + e.Message);
            if (sent)
            {
                try { await RequestConfirmedAsync(3003, "", CancellationToken.None); }
                catch (Exception stopError) { Debug.LogError("底盘停止请求未获确认，需要现场检查: " + stopError.Message); }
            }
            return false;
        }
        finally { chassisMotionCancellation = null; cancel.Dispose(); }
    }
}
