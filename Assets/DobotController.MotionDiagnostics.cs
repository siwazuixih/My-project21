using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

// Measurements use robot millimetres, never Unity transforms. No extra motion or compensation.
public partial class DobotController
{
    private StreamWriter motionWriter;
    private readonly System.Diagnostics.Stopwatch measurementClock = new System.Diagnostics.Stopwatch();
    private string measurementStage = "", motionIoError;
    private double[] stageOrigin, stageAxis, stepOrigin, stepTarget, previousMeasuredPose;
    private double stageStarted, stepStarted, requestedStage, stepDistance, stepDuration, lastMeasurement = -1, lastFlush;
    private ulong previousMeasuredStamp;
    private long previousMeasuredSequence = -1;
    private long stepAfterAck;
    private int measuredStep, motionRows;
    private bool feedbackFrameVerified;
    public string MotionDiagnosticSummary { get; private set; } = "尚无末端测量";
    public string ContactWorkflowError { get; private set; }
    public string MotionIoError => motionIoError;

    public void BeginMotionDiagnostics(string directory)
    {
        EndMotionDiagnostics();
        measurementStage = ""; stageOrigin = stageAxis = stepOrigin = stepTarget = previousMeasuredPose = null;
        motionIoError = null; feedbackFrameVerified = false; measuredStep = motionRows = 0;
        previousMeasuredSequence = -1; previousMeasuredStamp = 0; lastMeasurement = -1; lastFlush = 0;
        measurementClock.Restart(); MotionDiagnosticSummary = "等待插入/退出测量";
        try
        {
            if (string.IsNullOrEmpty(directory)) throw new IOException("报告目录为空");
            motionWriter = new StreamWriter(Path.Combine(directory, "motion.csv"), false, new UTF8Encoding(false));
            motionWriter.WriteLine("utc,elapsed_s,kind,stage,step,sequence,robot_timestamp_ms,age_s,user,tool,mode,valid,simulated,x_mm,y_mm,z_mm,rx_deg,ry_deg,rz_deg,j1_deg,j2_deg,j3_deg,j4_deg,j5_deg,j6_deg,target_x_mm,target_y_mm,target_z_mm,requested_step_mm,requested_duration_s,stage_axial_mm,stage_lateral_mm,step_actual_mm,target_error_mm,step_elapsed_s,step_average_speed_mm_s,stage_average_axial_speed_mm_s,window_speed_mm_s,stage_target_error_mm,note");
            motionWriter.Flush();
        }
        catch (Exception e) { MotionWriteFailed(e); }
    }

    public void EndMotionDiagnostics()
    {
        if (motionWriter != null && stageOrigin != null)
            WriteMotion("stage_incomplete", CopyRobotState(), null, false, "No successful explicit GetPose end measurement; do not treat requested distance as measured distance");
        try { motionWriter?.Dispose(); }
        catch (Exception e) { MotionWriteFailed(e); }
        finally { motionWriter = null; }
    }
    private void MotionWriteFailed(Exception e)
    {
        if (motionIoError == null) Debug.LogWarning("[MotionTrace][IO] " + e.Message);
        motionIoError = e.Message;
        try { motionWriter?.Dispose(); } catch { }
        motionWriter = null;
    }
    private static string MeasuredNumber(double value) => Finite(value) ? value.ToString("F6", CultureInfo.InvariantCulture) : "";
    private static double Separation(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < 3; i++) sum += (a[i] - b[i]) * (a[i] - b[i]);
        return Math.Sqrt(sum);
    }
    private static bool ValidPose(double[] p) => p != null && p.Length == 6 && !Array.Exists(p, x => !Finite(x));
    private bool MeasurementValid(RobotReply state) => state.simulated || (state.age_s >= 0 && state.age_s <= 0.25 &&
        state.user == contactUser && state.tool == contactTool && ValidPose(state.pose));
    private bool PoseMatches(double[] pose, RobotReply state)
    {
        if (!MeasurementValid(state) || !ValidPose(pose) || Separation(pose, state.pose) > 0.2) return false;
        for (int i = 3; i < 6; i++) if (Math.Abs(Math.IEEERemainder(pose[i] - state.pose[i], 360)) > 0.5) return false;
        return true;
    }
    private void StageDisplacement(double[] pose, out double axial, out double lateral)
    {
        axial = lateral = double.NaN;
        if (stageOrigin == null || stageAxis == null || !ValidPose(pose)) return;
        axial = 0;
        for (int i = 0; i < 3; i++) axial += (pose[i] - stageOrigin[i]) * stageAxis[i];
        double distance = Separation(pose, stageOrigin);
        lateral = Math.Sqrt(Math.Max(0, distance * distance - axial * axial));
    }
    private void WriteMotion(string kind, RobotReply state, double[] pose, bool valid, string note, double speed = double.NaN)
    {
        if (motionWriter == null) return;
        // Bounded ~10Hz trajectory. Always keep terminal summaries after the sample cap.
        if (motionRows >= 100000 && kind == "sample") return;
        try
        {
            StageDisplacement(valid ? pose : null, out double axial, out double lateral);
            var row = new List<string> { DateTime.UtcNow.ToString("O"), MeasuredNumber(measurementClock.Elapsed.TotalSeconds),
                kind, measurementStage, measuredStep.ToString(), state.sequence.ToString(), state.robot_timestamp_ms.ToString(),
                MeasuredNumber(state.age_s), state.user.ToString(), state.tool.ToString(), state.mode.ToString(), valid.ToString(), state.simulated.ToString() };
            for (int i = 0; i < 6; i++) row.Add(pose != null && pose.Length == 6 ? MeasuredNumber(pose[i]) : "");
            for (int i = 0; i < 6; i++) row.Add(state.joints != null && state.joints.Length == 6 ? MeasuredNumber(state.joints[i]) : "");
            for (int i = 0; i < 3; i++) row.Add(stepTarget == null ? "" : MeasuredNumber(stepTarget[i]));
            row.Add(MeasuredNumber(stepDistance)); row.Add(MeasuredNumber(stepDuration));
            row.Add(MeasuredNumber(axial)); row.Add(MeasuredNumber(lateral));
            row.Add(valid && stepOrigin != null ? MeasuredNumber(Separation(pose, stepOrigin)) : "");
            row.Add(valid && stepTarget != null ? MeasuredNumber(Separation(pose, stepTarget)) : "");
            row.Add(stepOrigin == null ? "" : MeasuredNumber(measurementClock.Elapsed.TotalSeconds - stepStarted));
            double stepTime = measurementClock.Elapsed.TotalSeconds - stepStarted;
            row.Add(valid && stepOrigin != null && stepTime > 0 ? MeasuredNumber(Separation(pose, stepOrigin) / stepTime) : "");
            double stageTime = measurementClock.Elapsed.TotalSeconds - stageStarted;
            row.Add(valid && stageTime > 0 ? MeasuredNumber(axial / stageTime) : "");
            row.Add(MeasuredNumber(speed));
            row.Add(valid ? MeasuredNumber(Math.Sqrt((requestedStage - axial) * (requestedStage - axial) + lateral * lateral)) : "");
            row.Add(note);
            motionWriter.WriteLine(string.Join(",", row.ConvertAll(x => "\"" + x.Replace("\"", "\"\"") + "\"")));
            motionRows++;
            if (kind != "sample" || measurementClock.Elapsed.TotalSeconds - lastFlush >= 1)
            { motionWriter.Flush(); lastFlush = measurementClock.Elapsed.TotalSeconds; }
        }
        catch (Exception e) { MotionWriteFailed(e); }
    }

    public void SampleMotionDiagnostics()
    {
        if (stageOrigin == null || measurementClock.Elapsed.TotalSeconds - lastMeasurement < 0.1) return;
        lastMeasurement = measurementClock.Elapsed.TotalSeconds;
        var s = CopyRobotState();
        bool valid = MeasurementValid(s) && feedbackFrameVerified && (s.simulated || s.sequence > previousMeasuredSequence);
        double speed = double.NaN;
        if (valid && previousMeasuredPose != null && s.robot_timestamp_ms > previousMeasuredStamp)
            speed = Separation(s.pose, previousMeasuredPose) / ((s.robot_timestamp_ms - previousMeasuredStamp) / 1000.0);
        WriteMotion("sample", s, s.pose, valid, valid ? "feedback; window speed, not instantaneous" : "stale/repeated/wrong frame/unverified pose frame", speed);
        if (valid)
        {
            StageDisplacement(s.pose, out double axial, out double lateral);
            MotionDiagnosticSummary = $"{measurementStage} 步{measuredStep} | 实际轴向{axial:F3}mm 横偏{lateral:F3}mm | 本步请求{stepDistance:F3}mm 实际{(stepOrigin == null ? 0 : Separation(s.pose, stepOrigin)):F3}mm 目标差{(stepTarget == null ? 0 : Separation(s.pose, stepTarget)):F3}mm";
            previousMeasuredPose = (double[])s.pose.Clone(); previousMeasuredStamp = s.robot_timestamp_ms; previousMeasuredSequence = s.sequence;
        }
        else { previousMeasuredPose = null; MotionDiagnosticSummary = measurementStage + "：反馈测量无效，查看motion.csv"; }
    }

    private async Task BeginDiagnosticStageAsync(RobotRequest request)
    {
        if (!measurementClock.IsRunning) measurementClock.Start();
        if (request.stage != "INSERTING" && request.stage != "EXITING") throw new ArgumentException("无效运动测量阶段");
        if (!Finite(request.planned_distance_mm) || request.planned_distance_mm < 0 || request.planned_distance_mm > 100)
            throw new ArgumentException("无效测量距离");
        if (!ownerSimulation) RequireRobotReady(false);
        var origin = await ReadPoseAsync();
        double originObservedAt = measurementClock.Elapsed.TotalSeconds;
        double direction = request.stage == "INSERTING" ? -1 : 1;
        double[] target;
        if (ownerSimulation) { target = (double[])origin.Clone(); target[0] += direction; }
        else target = ParseValues(await ExchangeDashboardAsync("RelPointTool(pose={" + Numbers(origin) + "},{" + Number(direction) + ",0,0,0,0,0})", true));
        if (stopLatched) throw new OperationCanceledException("测量启动已停止");
        if (!ownerSimulation) RequireRobotReady(false);
        stageOrigin = origin; stageAxis = new double[3];
        double norm = Separation(origin, target);
        if (norm < 0.000001) throw new IOException("工具方向计算无效");
        for (int i = 0; i < 3; i++) stageAxis[i] = (target[i] - origin[i]) / norm;
        measurementStage = request.stage; requestedStage = request.planned_distance_mm;
        measuredStep = 0; stepOrigin = stepTarget = previousMeasuredPose = null;
        previousMeasuredSequence = -1; previousMeasuredStamp = 0; stageStarted = originObservedAt;
        var state = CopyRobotState(); feedbackFrameVerified = PoseMatches(origin, state);
        stepDistance = stepDuration = 0;
        WriteMotion("stage_begin", state, origin, true, "GetPose explicit User/Tool; requested_mm=" + Number(requestedStage) + "; feedback_frame_verified=" + feedbackFrameVerified);
        Debug.Log($"[MotionTrace][STAGE_BEGIN] {measurementStage} requested_mm={Number(requestedStage)} user={contactUser} tool={contactTool} pose=[{Numbers(origin)}] feedback_frame_verified={feedbackFrameVerified}");
    }
    private async Task EndDiagnosticStageAsync()
    {
        if (stageOrigin == null) return;
        if (!ownerSimulation) RequireRobotReady(false);
        var pose = await ReadPoseAsync(); var s = CopyRobotState();
        if (!ownerSimulation) RequireRobotReady(false);
        if (!ownerSimulation && (s.user != contactUser || s.tool != contactTool)) throw new IOException("段末坐标系已改变");
        StageDisplacement(pose, out double axial, out double lateral);
        double elapsed = measurementClock.Elapsed.TotalSeconds - stageStarted;
        double targetError = Math.Sqrt((requestedStage - axial) * (requestedStage - axial) + lateral * lateral);
        string summary = $"{measurementStage} 请求{requestedStage:F3}mm，实测轴向{axial:F3}mm，横偏{lateral:F3}mm，整段目标差{targetError:F3}mm，耗时{elapsed:F3}s，整段平均轴向速度{(elapsed > 0 ? axial / elapsed : 0):F3}mm/s（包含通信/停等）";
        WriteMotion("stage_end", s, pose, true, summary + "; endpoint=GetPose; feedback_frame_verified=" + PoseMatches(pose, s));
        MotionDiagnosticSummary = summary;
        Debug.Log("[MotionTrace][STAGE_END] " + summary + " pose=[" + Numbers(pose) + "]");
        stageOrigin = stageAxis = stepOrigin = stepTarget = null;
    }
    private void BeginStepMeasurement(double[] origin, double[] target, double distance, double duration, string command, string ack, double originObservedAt)
    {
        if (stageOrigin == null) return;
        measuredStep++; stepOrigin = (double[])origin.Clone(); stepTarget = (double[])target.Clone();
        stepAfterAck = CopyRobotState().sequence;
        stepDistance = distance; stepDuration = duration; stepStarted = originObservedAt;
        WriteMotion("step_begin", CopyRobotState(), origin, true, "GetPose origin; command=" + command + "; reply=" + ack);
        Debug.Log($"[MotionTrace][STEP_BEGIN] {measurementStage} step={measuredStep} start=[{Numbers(origin)}] target=[{Numbers(target)}] command={command} reply={ack}");
    }
    private void EndStepMeasurement()
    {
        if (stepOrigin == null) return;
        var s = CopyRobotState(); bool valid = MeasurementValid(s) && feedbackFrameVerified && (s.simulated || s.sequence > stepAfterAck);
        double elapsed = measurementClock.Elapsed.TotalSeconds - stepStarted;
        WriteMotion("step_end", s, s.pose, valid, "time interval ended; not arrival; step average includes request overhead");
        Debug.Log($"[MotionTrace][STEP_END] {measurementStage} step={measuredStep} valid={valid} actual_mm={(valid ? MeasuredNumber(Separation(s.pose, stepOrigin)) : "unknown")} target_error_mm={(valid ? MeasuredNumber(Separation(s.pose, stepTarget)) : "unknown")} elapsed_s={Number(elapsed)}");
    }
    private void RecordContactWorkflowError(string error)
    {
        if (string.IsNullOrEmpty(ContactWorkflowError)) ContactWorkflowError = error;
        Debug.LogError("[RobotGateway][FIRST_FAILURE] " + ContactWorkflowError);
    }
}
