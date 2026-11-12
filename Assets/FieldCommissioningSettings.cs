using System;
using System.IO;
using System.Net;
using UnityEngine;

[Serializable]
public sealed class FieldCommissioningSettings
{
    public const string FileName = "field-commissioning.json";
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
    // 对已规划路径自适应细分的角度步长，不限制整段粗定位的总转角。
    public float maxJointStepDeg = 5;
    public float insertionMm = 20, withdrawalMm = 20, contactSpeedMmS = 1.2f, contactStepMm = 2;
    public float contactStageTimeoutSeconds = 90, tightenTimeoutSeconds = 60;
    public float exitStageTimeoutSeconds = 90;
    // Existing lateralLimitN remains the hard ceiling; never silently raise 20 to vendor 26.
    public float contactLateralTrigger = 18, contactLateralClear = 12, contactLateralSlow = 12;
    public bool positiveXInsertionConfirmed = false;
    public float beforeTightenSeconds = 5, beforeExitSeconds = 5;
    public float forceLimitN = 30, momentLimitNm = 10, lateralLimitN = 20;
    public float chassisTimeoutSeconds = 120, chassisStartTimeoutSeconds = 10;
    // 缺少新字段的旧JSON仍默认为false，即保留任务状态确认，不静默降级。
    public bool useLegacyChassisTiming = false;
    public ChassisLegacyTiming legacyChassisTiming = new ChassisLegacyTiming();
    public float chassisSpeedMS = 0.3f, chassisTurnRadS = 0.5f, liftSpeedMmS = 50;
    public float liftMinMm = 0, liftMaxMm = 0; // 无默认行程授权，包含升降任务时必须填写。
    public int firstTarget = 1;
    // 显式测试模式：原脚本的有限时长ServoP节奏；不改变粗定位路径。
    public bool originalContactTiming = false;

    public FieldCommissioningSettings Copy() => JsonUtility.FromJson<FieldCommissioningSettings>(JsonUtility.ToJson(this));
    public static FieldCommissioningSettings Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{", StringComparison.Ordinal))
            throw new ArgumentException("现场配置必须为JSON对象，不能是null/数组/空文本");
        var settings = new FieldCommissioningSettings();
        JsonUtility.FromJsonOverwrite(json, settings); // missing new fields keep explicit defaults, NOT zero
        return settings;
    }
    public static FieldCommissioningSettings ReadRobotConnectionFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("未找到现场JSON，请先在“现场联调 / 参数”保存参数，再连接机械臂。", path);
        var settings = Parse(File.ReadAllText(path));
        settings.ValidateRobotConnection(); // 连接不依赖Python安装、运动限位或其他设备参数。
        return settings;
    }
    public void ValidateRobotConnection()
    {
        if (!IPAddress.TryParse(robotIp, out var ip) ||
            (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && robotIp.Split('.').Length != 4))
            throw new ArgumentException("JSON的robotIp必须为有效的数字IP地址");
        if (dashboardPort < 1 || dashboardPort > 65535 || feedbackPort < 1 || feedbackPort > 65535)
            throw new ArgumentException("JSON的控制/反馈端口（dashboardPort、feedbackPort）必须为1~65535");
    }
    public void RequireContactDirection()
    {
        if (!positiveXInsertionConfirmed) throw new ArgumentException("新版方向与旧版相反：请核对+X卡入/-X退出，勾选新版方向并保存参数");
    }
    public double ContactCycleBudget => contactStageTimeoutSeconds + exitStageTimeoutSeconds + tightenTimeoutSeconds + beforeTightenSeconds + beforeExitSeconds + 35;
    public double EffectiveInsertionSpeed => Math.Min(.4, contactSpeedMmS);
    public double EffectiveExitSpeed => Math.Min(.6, contactSpeedMmS);
    public string ContactEffectiveSummary =>
        $"V2：卡入速度上限{EffectiveInsertionSpeed:F2}mm/s，段长≤{Math.Min(.10, contactStepMm):F2}mm；" +
        $"退出速度上限{EffectiveExitSpeed:F2}mm/s，段长≤{Math.Min(.15, contactStepMm):F2}mm。\n" +
        $"有载继续降速/缩步；轴向软停卡入{Math.Min(12, forceLimitN * .6):F1}N/退出{Math.Min(18, forceLimitN * .6):F1}N；" +
        $"搜索触发{Math.Min(12, contactLateralTrigger):F2}/解除{Math.Min(Math.Min(8, contactLateralClear), Math.Min(12, contactLateralTrigger) * .65):F2}（加权评价）；最多8轮、横向偏移≤0.30mm。\n" +
        "JSON速度/步长为请求上限，不是实际平均速度；逐段查询/停止与搜索耗时另计。";
    public static void Range(float x, float min, float max, string name)
    {
        if (float.IsNaN(x) || float.IsInfinity(x) || x < min || x > max)
            throw new ArgumentException(name + $"必须在[{min}, {max}]内且为有限数值");
    }
    public void Validate(bool motion, bool contactBudget = true)
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
        // V2实际段长还受有效速度*0.25s约束，不用未封顶的请求值计算段时长。
        Range(contactStageTimeoutSeconds, 1, 600, "接触阶段超时"); Range(tightenTimeoutSeconds, 1, 300, "拧紧超时");
        Range(exitStageTimeoutSeconds, 1, 600, "优化退出超时");
        Range(beforeTightenSeconds, 0, 60, "拧紧前等待"); Range(beforeExitSeconds, 0, 60, "退出前等待");
        Range(forceLimitN, 0.1f, 30, "力上限"); Range(momentLimitNm, 0.1f, 10, "力矩上限");
        Range(lateralLimitN, 0.1f, forceLimitN, "横向阈值");
        Range(contactLateralTrigger, 0.01f, 30, "横向搜索触发评价值");
        Range(contactLateralClear, 0, 30, "横向搜索解除评价值");
        Range(contactLateralSlow, 0, contactLateralTrigger, "横向减速评价值");
        if (contactLateralClear >= contactLateralTrigger || contactLateralTrigger >= Math.Min(26, lateralLimitN))
            throw new ArgumentException("横向评价须满足：解除值 < 搜索触发值 < 硬停止值（lateralLimitN）");
        Range(chassisTimeoutSeconds, 1, 600, "底盘超时"); Range(chassisStartTimeoutSeconds, 1, 60, "底盘启动超时");
        if (legacyChassisTiming == null) legacyChassisTiming = new ChassisLegacyTiming();
        if (useLegacyChassisTiming) legacyChassisTiming.Validate();
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
        if (contactBudget) ValidateContactBudget(true, true);
    }
    // 保存/连接/纯关节运行不执行接触，不能被另一阶段的预算挡住。
    // 实际接触入口及Python启动入口另按将执行的方向检查，不削弱实际运动保护。
    public void ValidateContactBudget(bool insert, bool withdraw)
    {
        Range(contactSpeedMmS, .1f, 5, "接触速度");
        if (insert) CheckContactBudget(insertionMm, EffectiveInsertionSpeed, contactStageTimeoutSeconds, "卡入", "contactStageTimeoutSeconds", 1);
        if (withdraw) CheckContactBudget(withdrawalMm, EffectiveExitSpeed, exitStageTimeoutSeconds, "退出", "exitStageTimeoutSeconds", 90);
    }
    private static void CheckContactBudget(float distance, double speed, float budget, string stage, string field, float minimum)
    {
        Range(distance, 0, 100, stage + "距离"); Range(budget, minimum, 600, stage + "超时");
        double theoretical = distance / speed;
        if (budget <= theoretical)
            throw new ArgumentException($"V2{stage}：行程{distance:F2}mm，有效速度上限{speed:F2}mm/s，" +
                $"理论至少{theoretical:F1}s，但{field}={budget:F1}s。尚未下发运动；请核对行程并补足预算（查询/停止/搜索另计），或使用面板“补足V2预算并保存”。");
    }
    // 只在操作者显式点击时补足；不自动改行程、速度、方向许可或任何力限。
    // +30s仅为初始余量，不是对逐段查询/搜索耗时的完成保证。
    public void SupplementContactBudgets()
    {
        Validate(false, false);
        float insert = SuggestedContactBudget(insertionMm / EffectiveInsertionSpeed);
        float exit = SuggestedContactBudget(withdrawalMm / EffectiveExitSpeed);
        contactStageTimeoutSeconds = Math.Max(contactStageTimeoutSeconds, insert);
        exitStageTimeoutSeconds = Math.Max(exitStageTimeoutSeconds, exit);
        ValidateContactBudget(true, true);
    }
    private static float SuggestedContactBudget(double theoretical)
    {
        double suggested = Math.Ceiling(Math.Max(90, theoretical + 30));
        if (suggested > 600)
            throw new ArgumentException("按当前行程/有效速度补足预算将超过600s；请现场重新核对行程，不能通过放宽速度/力限绕过。");
        return (float)suggested;
    }
    public void CheckJoints(double[] joints)
    {
        if (joints == null || joints.Length != 6) throw new ArgumentException("机械臂关节目标要求6维");
        for (int i = 0; i < 6; i++)
            if (double.IsNaN(joints[i]) || double.IsInfinity(joints[i]) || joints[i] < jointMinDeg[i] || joints[i] > jointMaxDeg[i])
                throw new ArgumentException($"J{i+1}目标{joints[i]}超出现场配置关节范围");
    }
}
