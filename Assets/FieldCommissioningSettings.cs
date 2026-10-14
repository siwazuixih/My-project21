using System;
using System.IO;
using System.Net;
using UnityEngine;

[Serializable]
public sealed class FieldCommissioningSettings
{
    public string pythonExecutable = "/usr/bin/python3";
    public string robotIp = "192.168.192.19";
    public int dashboardPort = 29999, feedbackPort = 30004;
    public string toolIp = "192.168.192.21";
    public int toolPort = 1200;
    public string chassisIp = "192.168.192.5", liftIp = "192.168.192.88";
    public int liftPort = 502, toolNumber = 1;
    public int userNumber = 0;
    public float visionJ1Deg = 0, visionJ2Deg = 0;
    public float robotSpeedPercent = 1, jointToleranceDeg = 0.2f, jointTimeoutSeconds = 90;
    // 默认不授予任何关节运动范围。现场依据实际型号/软限位填写六个轴范围后才能运动。
    public float[] jointMinDeg = new float[6], jointMaxDeg = new float[6];
    public float maxJointStepDeg = 5;
    public float insertionMm = 20, withdrawalMm = 20, contactSpeedMmS = 1.2f, contactStepMm = 2;
    public float contactStageTimeoutSeconds = 30, tightenTimeoutSeconds = 60;
    public float beforeTightenSeconds = 5, beforeExitSeconds = 5;
    public float forceLimitN = 30, momentLimitNm = 10, lateralLimitN = 20;
    public float chassisTimeoutSeconds = 120, chassisStartTimeoutSeconds = 10;
    public float chassisSpeedMS = 0.3f, chassisTurnRadS = 0.5f, liftSpeedMmS = 50;
    public float liftMinMm = 0, liftMaxMm = 0; // 无默认行程授权，包含升降任务时必须填写。
    public int firstTarget = 1;
    // 显式测试模式：原脚本的有限时长ServoP节奏；不改变粗定位路径。
    public bool originalContactTiming = false;

    public FieldCommissioningSettings Copy() => JsonUtility.FromJson<FieldCommissioningSettings>(JsonUtility.ToJson(this));
    public static void Range(float x, float min, float max, string name)
    {
        if (float.IsNaN(x) || float.IsInfinity(x) || x < min || x > max)
            throw new ArgumentException(name + $"必须在[{min}, {max}]内且为有限数值");
    }
    public void Validate(bool motion)
    {
        foreach (var ip in new[] { robotIp, toolIp, chassisIp, liftIp })
            if (!IPAddress.TryParse(ip, out _)) throw new ArgumentException("设备IP不是有效地址: " + ip);
        foreach (int port in new[] { dashboardPort, feedbackPort, toolPort, liftPort })
            if (port < 1 || port > 65535) throw new ArgumentException("端口必须为1~65535");
        if (string.IsNullOrWhiteSpace(pythonExecutable) || !Path.IsPathRooted(pythonExecutable) || !File.Exists(pythonExecutable))
            throw new ArgumentException("请填写已安装Python的可执行文件绝对路径");
        if (toolNumber < 0 || toolNumber > 50 || firstTarget < 1) throw new ArgumentException("工具编号/目标编号无效");
        Range(robotSpeedPercent, 1, 10, "调试速度百分比");
        if (robotSpeedPercent != Math.Truncate(robotSpeedPercent)) throw new ArgumentException("调试速度百分比必须为整数");
        if (userNumber < 0 || userNumber > 50) throw new ArgumentException("User编号必须为0~50");
        Range(visionJ1Deg, -5, 5, "J1偏移"); Range(visionJ2Deg, -5, 5, "J2偏移");
        Range(jointToleranceDeg, 0.01f, 1, "关节到位误差"); Range(jointTimeoutSeconds, 1, 600, "关节超时");
        Range(maxJointStepDeg, 0.01f, 10, "相邻关节目标最大差值");
        Range(insertionMm, 0, 100, "卡入距离"); Range(withdrawalMm, 0, 100, "退出距离");
        Range(contactSpeedMmS, 0.1f, 5, "接触速度"); Range(contactStepMm, 0.1f, 5, "接触步长");
        if (contactStepMm / contactSpeedMmS > 5) throw new ArgumentException("接触步长/速度不得使单次运动超过5秒");
        Range(contactStageTimeoutSeconds, 1, 600, "接触阶段超时"); Range(tightenTimeoutSeconds, 1, 300, "拧紧超时");
        Range(beforeTightenSeconds, 0, 60, "拧紧前等待"); Range(beforeExitSeconds, 0, 60, "退出前等待");
        Range(forceLimitN, 0.1f, 30, "力上限"); Range(momentLimitNm, 0.1f, 10, "力矩上限");
        Range(lateralLimitN, 0.1f, forceLimitN, "横向阈值");
        Range(chassisTimeoutSeconds, 1, 600, "底盘超时"); Range(chassisStartTimeoutSeconds, 1, 60, "底盘启动超时");
        Range(chassisSpeedMS, 0.01f, 0.3f, "底盘速度"); Range(chassisTurnRadS, 0.01f, 0.5f, "底盘角速度");
        Range(liftSpeedMmS, 1, 50, "升降速度");
        Range(liftMinMm, -10000, 10000, "升降下限"); Range(liftMaxMm, -10000, 10000, "升降上限");
        if (jointMinDeg == null || jointMaxDeg == null || jointMinDeg.Length != 6 || jointMaxDeg.Length != 6)
            throw new ArgumentException("关节上下限分别必须填写6个角度");
        for (int i = 0; i < 6; i++)
        {
            Range(jointMinDeg[i], -360, 360, "关节下限"); Range(jointMaxDeg[i], -360, 360, "关节上限");
            if (motion && jointMinDeg[i] >= jointMaxDeg[i]) throw new ArgumentException("实物运行前必须依据设备填写六轴关节上下限");
        }
        if (contactStageTimeoutSeconds <= Math.Max(insertionMm, withdrawalMm) / contactSpeedMmS)
            throw new ArgumentException("接触阶段超时小于理论运动时间，请核对参数");
    }
    public void CheckJoints(double[] joints)
    {
        if (joints == null || joints.Length != 6) throw new ArgumentException("机械臂关节目标要求6维");
        for (int i = 0; i < 6; i++)
            if (double.IsNaN(joints[i]) || double.IsInfinity(joints[i]) || joints[i] < jointMinDeg[i] || joints[i] > jointMaxDeg[i])
                throw new ArgumentException($"J{i+1}目标{joints[i]}超出现场配置关节范围");
    }
}
