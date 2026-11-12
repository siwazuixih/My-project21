using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Text evidence only. Never sends a device query or waits for disk on a control thread.
public sealed class RunDataSession
{
    public const string Schema = "device-run-data-v1";
    public sealed class Info
    {
        public string runId, projectId, source, version, buildId, mode, configuration;
        public bool simulated;
    }
    private sealed class Row { public string file, header, text; }
    private sealed class Part
    {
        public StreamWriter writer;
        public int number;
        public long bytes;
        public string header;
    }
    private readonly Info info;
    private readonly BlockingCollection<Row> queue;
    private readonly Dictionary<string, Part> parts = new Dictionary<string, Part>();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly object gate = new object();
    private readonly long partLimit;
    private readonly string started = DateTime.UtcNow.ToString("O");
    private long eventId, commandId, dropped, written, pending, pendingAtClose;
    private long robotRows, liftRows, commandRows, previousRobotSequence = -1, previousRobotConnection = -1;
    private long queuedCharacters;
    private const int QueueCharacterLimit = 2 * 1024 * 1024;
    private int target;
    private string stage = "准备", error = "", outcome = "", ended = "";
    private bool closed, finished;
    private double robotSample = -1, liftSample = -1;
    public string DirectoryPath { get; }
    public string RunId => info.runId;
    public bool Simulated => info.simulated;
    public Task Completion { get; }
    public string Status { get { lock (gate) return State(); } }
    public string Error { get { lock (gate) return error; } }
    private string State() => error.Length > 0 ? "保存失败/数据不完整：" + error :
        dropped > 0 || pendingAtClose > 0 ? "数据不完整" : finished ? "保存完成" : closed ? "正在刷盘" : "记录中";

    public RunDataSession(string directory, Info description, int capacity = 4096, long maxPartBytes = 20 * 1024 * 1024)
    {
        // Copy, not retain a mutable caller-owned configuration object.
        info = new Info { runId = description.runId, projectId = description.projectId, source = description.source,
            version = description.version, buildId=description.buildId, mode=description.mode,
            simulated = description.simulated, configuration = description.configuration };
        DirectoryPath = Path.GetFullPath(directory);
        partLimit = Math.Max(256, maxPartBytes);
        queue = new BlockingCollection<Row>(Math.Max(1, capacity));
        Completion = Task.Run((Action)WriteLoop);
    }
    public void Context(int targetNumber, string phase)
    {
        lock (gate)
        {
            if (closed) return;
            target = targetNumber; stage = phase ?? "";
            EventLocked("phase", "", "", "", "", "", "");
        }
    }
    public void Phase(string phase) { lock (gate) Context(target,phase); }
    public void Evidence(string source, string json)
    {
        lock (gate)
        {
            if (!closed) EventLocked("algorithm_result", source, "", "", "mm,s,N,Nm,deg", json, "");
        }
    }
    public Command BeginCommand(string device, string endpoint, long connection, string payload, string units, string nativeSequence = "")
    {
        lock (gate)
        {
            if (closed || info.simulated) return null;
            var command = new Command(this, ++commandId, target, stage, device, endpoint, connection, payload, units, nativeSequence);
            pending++;
            command.Emit("send_attempt", "");
            return command;
        }
    }
    public sealed class Command : IDisposable
    {
        private readonly RunDataSession owner;
        private readonly long id, connection;
        private readonly int target;
        private readonly string phase, device, endpoint, payload, units;
        private string native;
        private int done;
        internal Command(RunDataSession owner, long id, int target, string phase, string device, string endpoint,
            long connection, string payload, string units, string native)
        { this.owner=owner; this.id=id; this.target=target; this.phase=phase; this.device=device; this.endpoint=endpoint;
            this.connection=connection; this.payload=payload; this.units=units; this.native=native; }
        internal void Emit(string kind, string detail)
        {
            lock (owner.gate)
                owner.EventLocked(kind, device, endpoint, payload, units, detail, native, id, connection, target, phase);
        }
        public void Written() => Emit("write_completed", "OS write completed; not device acceptance or arrival");
        public void Reply(string raw) => Emit("reply_received", raw);
        public void Prepared(string nativeSequence,string detail)
        { lock(owner.gate) { native=nativeSequence; Emit("packet_prepared",detail); } }
        public void Accepted(string nativeSequence = null)
        { lock(owner.gate) { if(nativeSequence != null) native=nativeSequence; Emit("accepted", "Reply checked; not physical arrival/stop confirmation"); } }
        public void Failed(Exception e) => Emit("failed", e.GetType().Name + ": " + e.Message);
        public void Dispose() { if (Interlocked.Exchange(ref done, 1) == 0) lock(owner.gate) owner.pending--; }
    }
    private void EventLocked(string kind, string device, string endpoint, string payload, string units, string detail,
        string native, long command = 0, long connection = 0, int? commandTarget = null, string commandStage = null)
    {
        string row = "{\"schema\":" + Json(Schema) + ",\"utc\":" + Json(DateTime.UtcNow.ToString("O")) +
            ",\"elapsed_s\":" + Number(clock.Elapsed.TotalSeconds) + ",\"event_id\":" + (++eventId) +
            ",\"run_id\":" + Json(info.runId) + ",\"target\":" + (commandTarget ?? target) +
            ",\"stage\":" + Json(commandStage ?? stage) + ",\"device\":" + Json(device) + ",\"endpoint\":" + Json(endpoint) +
            ",\"connection\":" + connection + ",\"command_id\":" + command + ",\"native_sequence\":" + Json(native) +
            ",\"kind\":" + Json(kind) + ",\"payload\":" + Json(payload) + ",\"units\":" + Json(units) + ",\"detail\":" + Json(detail) + "}";
        EnqueueLocked(new Row {file="commands.jsonl", text=row});
    }
    private const string CommonHeader = "utc,elapsed_s,run_id,target,stage,endpoint,connection,sequence,device_timestamp_ms,age_s,valid,note";
    public void Robot(string endpoint, long connection, long sequence, ulong timestamp, double age, bool valid,
        int mode, int user, int tool, double[] joints, double[] speeds, double[] pose, ulong activeCommandId = 0)
    {
        lock (gate)
        {
            if (closed || info.simulated || clock.Elapsed.TotalSeconds - robotSample < .1) return;
            robotSample = clock.Elapsed.TotalSeconds;
            valid &= FiniteSix(joints) && FiniteSix(speeds) && FiniteSix(pose);
            bool isNew = valid && (sequence != previousRobotSequence || connection != previousRobotConnection);
            previousRobotSequence=sequence; previousRobotConnection=connection;
            string row = CsvCommon(endpoint, connection, sequence, timestamp.ToString(CultureInfo.InvariantCulture), age, valid,
                valid ? "sampled existing feedback; not raw packet capture" : "missing/stale/invalid feedback") +
                "," + isNew + "," + mode + "," + user + "," + tool + Values(joints) + Values(speeds) + Values(pose) + "," + activeCommandId.ToString(CultureInfo.InvariantCulture);
            EnqueueLocked(new Row {file="robot-feedback.csv", header=CommonHeader + ",new_sample,mode,user,tool,j1_deg,j2_deg,j3_deg,j4_deg,j5_deg,j6_deg,v1_deg_s,v2_deg_s,v3_deg_s,v4_deg_s,v5_deg_s,v6_deg_s,x_mm,y_mm,z_mm,rx_deg,ry_deg,rz_deg,active_command_id", text=row});
        }
    }
    public void Lift(string endpoint, long connection, long sequence, double position, double velocity, double torque,
        double positionAge, double velocityAge, double torqueAge, bool connected)
    {
        lock (gate)
        {
            if (closed || info.simulated || clock.Elapsed.TotalSeconds - liftSample < .2) return;
            liftSample = clock.Elapsed.TotalSeconds;
            bool p = connected && positionAge >= 0 && positionAge <= .6 && Finite(position);
            bool v = connected && velocityAge >= 0 && velocityAge <= .6 && Finite(velocity);
            bool t = connected && torqueAge >= 0 && torqueAge <= .6 && Finite(torque);
            string row = CsvCommon(endpoint,connection,sequence,"",positionAge,p && v && t,"independent register reads; not simultaneous") +
                "," + (p ? Number(position) : "") + "," + (v ? Number(velocity) : "") + "," + (t ? Number(torque) : "") +
                "," + p + "," + v + "," + t + "," + Number(velocityAge) + "," + Number(torqueAge);
            EnqueueLocked(new Row {file="lift-feedback.csv", header=CommonHeader + ",height_mm,velocity_mm_s,torque_raw,position_valid,velocity_valid,torque_valid,velocity_age_s,torque_age_s",text=row});
        }
    }
    private string CsvCommon(string endpoint, long connection, long sequence, string timestamp, double age, bool valid, string note)
        => Csv(DateTime.UtcNow.ToString("O")) + "," + Number(clock.Elapsed.TotalSeconds) + "," + Csv(info.runId) + "," + target +
        "," + Csv(stage) + "," + Csv(endpoint) + "," + connection + "," + sequence + "," + timestamp + "," + Number(age) + "," + valid + "," + Csv(note);
    private void EnqueueLocked(Row row)
    {
        if (closed || error.Length > 0) return;
        // Bound both count and text size; a chassis response may be much larger than a joint command.
        if (queuedCharacters + row.text.Length > QueueCharacterLimit || !queue.TryAdd(row)) dropped++;
        else queuedCharacters += row.text.Length;
    }
    public Task Finish(string result)
    {
        lock (gate)
        {
            if (!closed)
            {
                EventLocked("recording_end", "", "", "", "", result, "");
                outcome = result ?? ""; ended = DateTime.UtcNow.ToString("O"); pendingAtClose = pending;
                closed = true; queue.CompleteAdding();
            }
            return Completion;
        }
    }
    private void WriteLoop()
    {
        bool ownsManifest = false;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            using (File.Open(Path.Combine(DirectoryPath,"metadata.json"),FileMode.CreateNew)) { }
            ownsManifest = true; WriteMetadata();
            double flushed = 0;
            while (!queue.IsCompleted)
            {
                if (queue.TryTake(out var row, 100))
                { lock(gate) queuedCharacters-=row.text.Length; Write(row); }
                if (clock.Elapsed.TotalSeconds - flushed >= 1)
                {
                    foreach (var part in parts.Values) part.writer?.Flush();
                    WriteMetadata(); flushed = clock.Elapsed.TotalSeconds;
                }
            }
        }
        catch (Exception e) { lock (gate) { error = e.Message; } }
        finally
        {
            foreach (var part in parts.Values)
                try { part.writer?.Dispose(); } catch (Exception e) { lock(gate) error = e.Message; }
            lock (gate) { finished = true; if (!closed && error.Length == 0) error = "writer ended before session close"; }
            if (ownsManifest) try { WriteMetadata(); } catch (Exception e) { lock(gate) error = e.Message; }
        }
    }
    private void Write(Row row)
    {
        if (!parts.TryGetValue(row.file,out var part)) { part = new Part {header=row.header}; parts.Add(row.file,part); }
        int bytes = Encoding.UTF8.GetByteCount(row.text) + 1;
        if (part.writer == null || part.bytes + bytes > partLimit)
        {
            part.writer?.Dispose(); part.number++; part.bytes = 0;
            string name = part.number == 1 ? row.file : Path.GetFileNameWithoutExtension(row.file) + "_" + part.number.ToString("D4") + Path.GetExtension(row.file);
            part.writer = new StreamWriter(new FileStream(Path.Combine(DirectoryPath,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false));
            part.writer.NewLine = "\n";
            if (part.header != null) { part.writer.WriteLine(part.header); part.bytes += Encoding.UTF8.GetByteCount(part.header) + 1; }
        }
        part.writer.WriteLine(row.text); part.bytes += bytes;
        lock (gate)
        {
            written++;
            if(row.file=="commands.jsonl") commandRows++; else if(row.file=="robot-feedback.csv") robotRows++; else liftRows++;
        }
    }
    private void WriteMetadata()
    {
        string json;
        lock (gate)
            json = "{\"schema\":" + Json(Schema) + ",\"runId\":" + Json(info.runId) + ",\"projectId\":" + Json(info.projectId) +
                ",\"source\":" + Json(info.source) + ",\"simulated\":" + (info.simulated ? "true":"false") +
                ",\"version\":" + Json(info.version) + ",\"buildId\":" + Json(info.buildId) + ",\"mode\":" + Json(info.mode) +
                ",\"configuration\":" + Json(info.configuration) + ",\"startedUtc\":" + Json(started) +
                ",\"endedUtc\":" + Json(ended) + ",\"completed\":" + (closed && finished ? "true":"false") +
                ",\"status\":" + Json(State()) + ",\"error\":" + Json(error) + ",\"outcome\":" + Json(outcome) +
                ",\"rowsWritten\":" + written + ",\"droppedRows\":" + dropped + ",\"pendingCommands\":" + pending + ",\"pendingAtClose\":" + pendingAtClose +
                ",\"commandRows\":" + commandRows + ",\"robotRows\":" + robotRows + ",\"liftRows\":" + liftRows +
                ",\"robotHz\":10,\"liftHz\":5,\"partLimitBytes\":" + partLimit +
                ",\"queueCharacterLimit\":" + QueueCharacterLimit +
                ",\"notes\":\"Sampled application evidence, not lossless packet capture; accepted is not physical completion. Lift torque retains existing raw controller units.\"}";
        string path = Path.Combine(DirectoryPath,"metadata.json");
        File.WriteAllText(path + ".tmp",json,new UTF8Encoding(false));
        File.Replace(path + ".tmp",path,null);
    }
    internal static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    private static bool FiniteSix(double[] v) => v != null && v.Length == 6 && !Array.Exists(v,x=>!Finite(x));
    private static string Values(double[] v)
    { var s = new StringBuilder(); for(int i=0;i<6;i++) s.Append(',').Append(v != null && v.Length==6 ? Number(v[i]):""); return s.ToString(); }
    private static string Number(double n) => Finite(n) ? n.ToString("R",CultureInfo.InvariantCulture) : "";
    private static string Csv(string s) => "\"" + (s ?? "").Replace("\"","\"\"") + "\"";
    internal static string Json(string s)
    {
        var b = new StringBuilder("\"");
        foreach(char c in s ?? "")
            if(c=='"' || c=='\\') b.Append('\\').Append(c);
            else if(c<32) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c);
        return b.Append('"').ToString();
    }
}

// Captured command handles retain their original session even if another run starts.
public static class RunDataCapture
{
    public static RunDataSession Current { get; private set; }
    public static Action EnsureManualSession;
    public static Action EndManualSession;
    public static Action<string> ReportSaveError;
    private static RunDataSession reportedErrorSession;
    public static void CheckSaveError()
    {
        var s=Current;
        if(s==null || s==reportedErrorSession) return;
        string error=s.Error;
        if(error.Length==0 && s.Status=="数据不完整") error="缓存队列达到上限，已有记录丢失";
        if(error.Length==0) return;
        reportedErrorSession=s; ReportSaveError?.Invoke(error);
    }
    public static void Begin(RunDataSession session)
    { Current?.Finish("被新记录替换，旧记录不代表运动完成"); Current = session; reportedErrorSession=null; }
    public static Task Finish(string runId, string outcome)
    {
        var s = Current;
        if (s == null || s.RunId != runId) return Task.CompletedTask;
        Current = null; return s.Finish(outcome);
    }
    public static void Context(string runId, int target, string stage)
    { var s=Current; if(s != null && s.RunId==runId) s.Context(target,stage); }
    public static string Endpoint(TcpClient client, string fallback)
    { try { return client?.Client.RemoteEndPoint?.ToString() ?? fallback; } catch { return fallback; } }
    public static RunDataSession.Command Command(string device,string endpoint,long connection,string payload,string units,string native="",bool ensureManual=true)
    {
        try { if(ensureManual) EnsureManualSession?.Invoke(); return Current?.BeginCommand(device,endpoint,connection,payload,units,native); }
        catch { return null; } // Recording must not replace the original device error or prevent Stop.
    }
}
