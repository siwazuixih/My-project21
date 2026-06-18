using Mujoco;
using TMPro;
using UnityEngine;

public class RealRobotFollower : MonoBehaviour
{
    [Header("反馈数据源")]
    public DobotController dobot;
    public LiftCylinderController lift;
    public MissionController mission;

    [Header("仿真执行器映射")]
    public bool useMissionActuatorMapping = true;
    public MjActuator[] armActuators = new MjActuator[6];
    public MjActuator liftActuator;
    public int armStartIndex = 0;
    public int liftActuatorIndex = 6;

    [Header("机械臂校准")]
    public float[] jointSigns = { 1, 1, 1, 1, 1, 1 };
    public float[] jointZeroOffsetsDeg = { 0, 0, 0, 0, 0, 0 };
    public float armFollowSpeedDegPerSec = 90f;
    public float jointDeadbandDeg = 0.05f;

    [Header("升降缸校准")]
    public float liftSign = 1f;
    public float liftZeroOffsetMm = 0f;
    public float liftFollowSpeedMmPerSec = 100f;
    public float liftDeadbandMm = 0.2f;

    [Header("模式与安全")]
    public bool followEnabled = false;
    public float feedbackTimeout = 1f;
    public bool requireBothFeedbackSources = true;
    public bool armFeedbackFresh;
    public bool liftFeedbackFresh;
    public string followStatus = "Disabled";
    public TextMeshProUGUI statusTextDisplay;
    private readonly double[] armFeedbackJoints = new double[6];

    void Awake()
    {
        EnsureCalibrationArrays();
        ResolveActuatorMapping();
    }

    void Start()
    {
        if (followEnabled)
            EnableRealRobotFollow();
    }

    void LateUpdate()
    {
        if (!followEnabled)
        {
            UpdateStatus("Disabled");
            return;
        }

        ResolveActuatorMapping();
        armFeedbackFresh = dobot != null &&
                           dobot.TryCopyActualJoints(armFeedbackJoints, feedbackTimeout);
        liftFeedbackFresh = lift != null && lift.HasFreshFeedback(feedbackTimeout);

        if (requireBothFeedbackSources && (!armFeedbackFresh || !liftFeedbackFresh))
        {
            UpdateStatus("Waiting for feedback");
            return;
        }

        if (armFeedbackFresh) FollowArm();
        if (liftFeedbackFresh) FollowLift();

        UpdateStatus(armFeedbackFresh && liftFeedbackFresh
            ? "Following real robot"
            : "Partial feedback");
    }

    public void EnableRealRobotFollow()
    {
        ResolveActuatorMapping();
        mission?.armCtrl?.SetExternalControlActive(true);
        followEnabled = true;
        UpdateStatus("Waiting for feedback");
        Debug.Log("[实物跟随] 已开启，仿真将只读取实物反馈，不向实物下发指令。");
    }

    public void DisableRealRobotFollow()
    {
        followEnabled = false;
        mission?.armCtrl?.SetExternalControlActive(false);
        UpdateStatus("Disabled");
        Debug.Log("[实物跟随] 已关闭。");
    }

    public void SetRealRobotFollow(bool enabled)
    {
        if (enabled) EnableRealRobotFollow();
        else DisableRealRobotFollow();
    }

    private void FollowArm()
    {
        float maxStep = Mathf.Max(armFollowSpeedDegPerSec, 0.01f) *
                        Mathf.Deg2Rad * Time.deltaTime;

        for (int i = 0; i < 6; i++)
        {
            MjActuator actuator = armActuators[i];
            if (actuator == null) continue;

            float targetDeg = (float)armFeedbackJoints[i] * jointSigns[i] +
                              jointZeroOffsetsDeg[i];
            float targetRad = targetDeg * Mathf.Deg2Rad;
            float current = (float)actuator.Control;

            if (Mathf.Abs(Mathf.DeltaAngle(current * Mathf.Rad2Deg, targetDeg)) <= jointDeadbandDeg)
                continue;

            actuator.Control = Mathf.MoveTowards(current, targetRad, maxStep);
        }
    }

    private void FollowLift()
    {
        if (liftActuator == null) return;

        float targetMm = lift.currentHeightMm * liftSign + liftZeroOffsetMm;
        float currentMm = (float)liftActuator.Control * lift.simToRealScale;
        if (Mathf.Abs(targetMm - currentMm) <= liftDeadbandMm) return;

        float targetMeters = targetMm / Mathf.Max(lift.simToRealScale, 0.0001f);
        float maxStepMeters = Mathf.Max(liftFollowSpeedMmPerSec, 0.01f) /
                              Mathf.Max(lift.simToRealScale, 0.0001f) *
                              Time.deltaTime;
        liftActuator.Control = Mathf.MoveTowards(
            (float)liftActuator.Control,
            targetMeters,
            maxStepMeters);
    }

    private void ResolveActuatorMapping()
    {
        if (!useMissionActuatorMapping || mission == null ||
            mission.refs == null || mission.refs.ikSolver == null ||
            mission.refs.ikSolver.actuators == null)
            return;

        var actuators = mission.refs.ikSolver.actuators;
        EnsureActuatorArray();

        for (int i = 0; i < 6; i++)
        {
            int index = armStartIndex + i;
            if (index >= 0 && index < actuators.Count)
                armActuators[i] = actuators[index];
        }

        if (liftActuatorIndex >= 0 && liftActuatorIndex < actuators.Count)
            liftActuator = actuators[liftActuatorIndex];
    }

    private void EnsureCalibrationArrays()
    {
        EnsureActuatorArray();
        if (jointSigns == null || jointSigns.Length != 6)
            jointSigns = new float[] { 1, 1, 1, 1, 1, 1 };
        if (jointZeroOffsetsDeg == null || jointZeroOffsetsDeg.Length != 6)
            jointZeroOffsetsDeg = new float[6];
    }

    private void EnsureActuatorArray()
    {
        if (armActuators == null || armActuators.Length != 6)
            armActuators = new MjActuator[6];
    }

    private void UpdateStatus(string status)
    {
        followStatus = status;
        if (statusTextDisplay != null)
            statusTextDisplay.text = $"Real Follow: {status}";
    }

    void OnDisable()
    {
        if (followEnabled)
            DisableRealRobotFollow();
    }
}
