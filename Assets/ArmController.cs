using UnityEngine;
using Mujoco;
using System.Collections;
using System.Collections.Generic;
using RobotLogic;

public class ArmController : MonoBehaviour
{
    private MissionController mgr;
    public string debug_PlannerStatus = "Ready";
    private Vector3 lockedArmTargetPos;
    private Quaternion lockedArmTargetRot;
    private Coroutine currentArmTask;
    public bool externalControlActive { get; private set; }

    public void Init(MissionController manager) { mgr = manager; }

    public IEnumerator InitArmRoutine()
    {
        Debug.Log("⚙️ 机械臂初始姿态缓慢对齐中...");
        if (mgr.refs.ikSolver == null || mgr.refs.ikSolver.actuators == null) {
            mgr.currentState = MissionState.Idle; yield break;
        }

        var acts = mgr.refs.ikSolver.actuators;
        List<float> starts = new List<float>(); List<float> ends = new List<float>();

        for (int i = 0; i < acts.Count; i++) {
            starts.Add(acts[i].Joint != null ? acts[i].Control : 0);
            acts[i].Control = starts[i]; 
            float target = (i < mgr.arm.initialAngles.Count) ? mgr.arm.initialAngles[i] : starts[i];
            if (acts[i].Joint is MjHingeJoint) target *= Mathf.Deg2Rad;
            ends.Add(target);
        }

        float t = 0;
        while (t < 1.0f) {
            t += Time.deltaTime / 2.0f;
            float cv = mgr.arm.motionCurve.Evaluate(t);
            for (int i = 0; i < acts.Count; i++) acts[i].Control = Mathf.Lerp(starts[i], ends[i], cv);
            yield return null;
        }
        mgr.currentState = MissionState.Idle;
    }

    public void StartArmSequence()
    {
        if (externalControlActive) return;
        CalculateObservationPose(mgr.refs.targetObject.position, mgr.chassisCtrl.GetRobotPosition(), transform.forward, out lockedArmTargetPos, out lockedArmTargetRot);

        List<double[]> cachedPlan = null;
        if (mgr.hasPrecalculated && mgr.currentMissionIndex < mgr.snapshots.Count) {
            if (mgr.snapshots[mgr.currentMissionIndex].ikSuccess) cachedPlan = mgr.snapshots[mgr.currentMissionIndex].precalcArmPlan;
        }

        if (mgr.arm.usePrecalculatedSolution && cachedPlan != null) {
            StartCoroutine(ExecutePath(cachedPlan, true)); 
        } else if (mgr.arm.useBitStarPlanner && mgr.refs.bitPlanner != null) {
            StartCoroutine(RunBitStarPlanning());
        } else {
            StartCoroutine(RunSimpleIKInterp());
        }
    }

    IEnumerator RunBitStarPlanning()
    {
        debug_PlannerStatus = "Planning...";
        if (mgr.diagUI.lineVis) mgr.diagUI.lineVis.positionCount = 0;
        List<double[]> path = mgr.arm.enableLookAt ? mgr.refs.bitPlanner.Plan(lockedArmTargetPos, lockedArmTargetRot) : mgr.refs.bitPlanner.Plan(lockedArmTargetPos);
        
        if (path != null && path.Count > 0) {
            debug_PlannerStatus = $"Run ({path.Count} pts)";
            if (mgr.debug.showRealtimePath && mgr.diagUI.lineVis) {
                List<Vector3> pts = mgr.refs.bitPlanner.GetPathInWorldSpace(path);
                if (pts != null) { mgr.diagUI.lineVis.positionCount = pts.Count; mgr.diagUI.lineVis.SetPositions(pts.ToArray()); }
            }
            yield return StartCoroutine(ExecutePath(path));
        } else {
            Debug.LogError("BIT* 失败，切换 IK 直连"); yield return StartCoroutine(RunSimpleIKInterp());
        }
    }

    IEnumerator RunSimpleIKInterp()
    {
        debug_PlannerStatus = "Simple IK...";
        double[] finalQ = mgr.arm.enableLookAt ? mgr.refs.ikSolver.SolveIK(lockedArmTargetPos, lockedArmTargetRot) : mgr.refs.ikSolver.SolveIK(lockedArmTargetPos);
        if (finalQ != null) yield return StartCoroutine(ExecutePath(new List<double[]> { finalQ }, true)); 
        else { Debug.LogError("IK 解算失败"); mgr.OnArmTaskFinished(); }
    }

    IEnumerator ExecutePath(List<double[]> path, bool simpleLerp = false)
    {
        if (externalControlActive) yield break;
        mgr.currentState = MissionState.ArmMoving;
        List<MjActuator> actuators = mgr.refs.ikSolver.actuators; 
        int nv = actuators.Count;
        double[] currentQ = new double[nv];
        for(int i=0; i<nv; i++) currentQ[i] = (double)actuators[i].Control;

        foreach (var fullQposState in path) 
        {
            if (externalControlActive) yield break;
            double[] targetControls = new double[nv];
            int safeLength = Mathf.Min(fullQposState.Length, nv);
            for (int i = 0; i < safeLength; i++) targetControls[i] = fullQposState[i];
            for (int i = safeLength; i < nv; i++) targetControls[i] = actuators[i].Control;

            if (simpleLerp && path.Count == 1) {
                List<float> startVals = new List<float>();
                foreach(var a in actuators) startVals.Add((float)a.Control);
                float duration = EstimateMoveDuration(startVals, targetControls, nv);
                float t = 0;
                while (t < 1.0f) {
                    if (externalControlActive) yield break;
                    t += Time.deltaTime / duration;
                    float cv = mgr.arm.motionCurve.Evaluate(Mathf.Clamp01(t));
                    for (int i = 0; i < nv; i++) actuators[i].Control = Mathf.Lerp(startVals[i], (float)targetControls[i], cv);
                    yield return null;
                }
            } else {
                while (!HasReached(currentQ, targetControls, nv)) {
                    if (externalControlActive) yield break;
                    StepTowards(ref currentQ, targetControls, mgr.arm.jointSpeed * Time.deltaTime, nv);
                    for (int j = 0; j < nv; j++) actuators[j].Control = (float)currentQ[j];
                    yield return null;
                }
            }
        }

        float timer = 0f;
        while (timer < 2.0f) {
            timer += Time.deltaTime;
            if (mgr.refs.armEndEffector != null && Vector3.Distance(mgr.refs.armEndEffector.position, lockedArmTargetPos) < mgr.arm.physicalTolerance) break;
            yield return null;
        }

        debug_PlannerStatus = "Success";
        mgr.OnArmTaskFinished();
    }

    public IEnumerator ResetArmRoutine(System.Action onComplete)
    {
        yield return new WaitForSeconds(0.5f);
        List<MjActuator> acts = mgr.refs.ikSolver.actuators;
        List<float> starts = new List<float>(); List<float> ends = new List<float>();

        for(int i=0; i<acts.Count; i++) {
            starts.Add((float)acts[i].Control);
            float target = (i < mgr.arm.initialAngles.Count) ? mgr.arm.initialAngles[i] : starts[i];
            if (acts[i].Joint is MjHingeJoint) target *= Mathf.Deg2Rad;
            ends.Add(target);
        }

        float duration = EstimateMoveDuration(starts, ends, acts.Count);
        float t = 0;
        while (t < 1.0f) {
            t += Time.deltaTime / duration;
            float cv = mgr.arm.motionCurve.Evaluate(Mathf.Clamp01(t));
            for (int i = 0; i < acts.Count; i++) acts[i].Control = Mathf.Lerp(starts[i], ends[i], cv);
            yield return null;
        }
        yield return new WaitForSeconds(mgr.mission.intervalBetweenTasks);
        onComplete?.Invoke();
    }

    public void CalculateObservationPose(Vector3 target, Vector3 robot, Vector3 fwd, out Vector3 p, out Quaternion r)
    {
        Vector3 dir = mgr.arm.useManualObservation ? mgr.arm.manualObservationVec.normalized : (robot - target).normalized;
        p = target + (dir * mgr.arm.observationDistance);
        if(!mgr.arm.useManualObservation) p.y += 0; 
        Vector3 look = target - p;
        if(look == Vector3.zero) look = Vector3.forward;
        r = Quaternion.LookRotation(look) * Quaternion.FromToRotation(mgr.arm.faceAxis, Vector3.forward);
    }

    public static Vector3 CalculateObservationPosition(
        Vector3 target,
        Vector3 robot,
        bool useManualObservation,
        Vector3 manualObservationVector,
        float observationDistance)
    {
        Vector3 direction = useManualObservation
            ? manualObservationVector.normalized
            : (robot - target).normalized;
        return target + direction * observationDistance;
    }

    private bool HasReached(double[] cur, double[] tar, int nv) {
        for (int i = 0; i < nv; i++) if (Mathf.Abs((float)(cur[i] - tar[i])) > 0.01f) return false;
        return true;
    }
    private void StepTowards(ref double[] cur, double[] tar, float step, int nv) {
        float maxD = 0f; 
        for (int i = 0; i < nv; i++) { float d = Mathf.Abs((float)(tar[i] - cur[i])); if (d > maxD) maxD = d; }
        if (maxD < 0.0001f) return;
        float r = (step > maxD ? maxD : step) / maxD;
        for (int i = 0; i < nv; i++) cur[i] += (tar[i] - cur[i]) * r;
    }

    private float EstimateMoveDuration(List<float> startVals, double[] targetVals, int count) {
        float maxDelta = 0f;
        for (int i = 0; i < count; i++) {
            float delta = Mathf.Abs((float)targetVals[i] - startVals[i]);
            if (delta > maxDelta) maxDelta = delta;
        }
        return Mathf.Max(maxDelta / Mathf.Max(mgr.arm.jointSpeed, 0.001f), 0.2f);
    }

    private float EstimateMoveDuration(List<float> startVals, List<float> targetVals, int count) {
        float maxDelta = 0f;
        for (int i = 0; i < count; i++) {
            float delta = Mathf.Abs(targetVals[i] - startVals[i]);
            if (delta > maxDelta) maxDelta = delta;
        }
        return Mathf.Max(maxDelta / Mathf.Max(mgr.arm.jointSpeed, 0.001f), 0.2f);
    }

    public int GetActuatorQposAddr(MjActuator act) {
        if (act.Joint is MjHingeJoint h) return h.QposAddress;
        if (act.Joint is MjSlideJoint s) return s.QposAddress;
        return -1;
    }

    public unsafe void ResetArmQposInSimulation() {
        var acts = mgr.refs.ikSolver.actuators;
        for (int i = 0; i < acts.Count; i++) {
            if (i < mgr.arm.initialAngles.Count) {
                float initVal = mgr.arm.initialAngles[i]; var act = acts[i];
                if (act.Joint is MjHingeJoint h) MjScene.Instance.Data->qpos[h.QposAddress] = initVal * Mathf.Deg2Rad;
                else if (act.Joint is MjSlideJoint s) MjScene.Instance.Data->qpos[s.QposAddress] = initVal;
            }
        }
        MujocoLib.mj_forward(MjScene.Instance.Model, MjScene.Instance.Data);
    }

    public void StopAndResetControls()
    {
        StopAllCoroutines();
        debug_PlannerStatus = "Ready (Reset)";
        var acts = mgr.refs.ikSolver.actuators;
        for (int i = 0; i < acts.Count; i++) {
            if (i < mgr.arm.initialAngles.Count) {
                float target = mgr.arm.initialAngles[i];
                if (acts[i].Joint is MjHingeJoint) target *= Mathf.Deg2Rad;
                acts[i].Control = target;
            }
        }
    }

    public void SetExternalControlActive(bool active)
    {
        externalControlActive = active;
        if (active)
        {
            StopAllCoroutines();
            debug_PlannerStatus = "Real Robot Follow";
        }
        else
        {
            debug_PlannerStatus = "Ready";
        }
    }
}
