using System;

/// <summary>
/// Dobot机械臂事件参数类，包含所有事件相关数据
/// </summary>
public class DobotEventArgs : EventArgs
{
    // 连接相关
    public bool IsConnected { get; set; }
    public string PortType { get; set; } // "Dashboard" 或 "Feedback"

    // 命令和响应
    public string Command { get; set; }
    public bool Success { get; set; }
    public string Response { get; set; }

    // 反馈数据
    public int RobotMode { get; set; }
    public string RobotModeText { get; set; }
    public double SpeedScaling { get; set; }
    public byte VelocityRatio { get; set; }
    public double[] ActualJoints { get; set; }
    public double[] ActualJointSpeeds { get; set; }

    // 错误信息
    public string ErrorMessage { get; set; }
    public string Context { get; set; }

    // 通用消息
    public string Message { get; set; }

    // 事件类型枚举
    public enum EventType
    {
        ConnectionChanged,   // 连接状态变化
        CommandSent,         // 命令发送
        ResponseReceived,    // 响应接收
        FeedbackUpdated,     // 反馈数据更新
        ErrorOccurred,       // 错误发生
        ChassisPositionUpdated,  // 底盘位置更新
        ChassisNavStatusUpdated  // 底盘导航状态更新
    }

    // 底盘相关数据
    public float ChassisX { get; set; }
    public float ChassisY { get; set; }
    public float ChassisAngle { get; set; }
    public string ChassisNavState { get; set; }
    public float ChassisRemainDistance { get; set; }

    public EventType Type { get; set; }

    /// <summary>
    /// 创建连接状态变化事件参数
    /// </summary>
    public static DobotEventArgs CreateConnectionEvent(bool isConnected, string message, string portType)
    {
        return new DobotEventArgs
        {
            Type = EventType.ConnectionChanged,
            IsConnected = isConnected,
            Message = message,
            PortType = portType
        };
    }

    /// <summary>
    /// 创建命令发送事件参数
    /// </summary>
    public static DobotEventArgs CreateCommandEvent(string command, bool success, string errorMessage = null)
    {
        return new DobotEventArgs
        {
            Type = EventType.CommandSent,
            Command = command,
            Success = success,
            ErrorMessage = errorMessage
        };
    }

    /// <summary>
    /// 创建响应接收事件参数
    /// </summary>
    public static DobotEventArgs CreateResponseEvent(string response, string portType)
    {
        return new DobotEventArgs
        {
            Type = EventType.ResponseReceived,
            Response = response,
            PortType = portType
        };
    }

    /// <summary>
    /// 创建反馈数据更新事件参数
    /// </summary>
    public static DobotEventArgs CreateFeedbackEvent(int robotMode, string robotModeText,
        double speedScaling, byte velocityRatio, double[] actualJoints, double[] actualJointSpeeds)
    {
        return new DobotEventArgs
        {
            Type = EventType.FeedbackUpdated,
            RobotMode = robotMode,
            RobotModeText = robotModeText,
            SpeedScaling = speedScaling,
            VelocityRatio = velocityRatio,
            ActualJoints = actualJoints,
            ActualJointSpeeds = actualJointSpeeds
        };
    }

    /// <summary>
    /// 创建错误事件参数
    /// </summary>
    public static DobotEventArgs CreateErrorEvent(string errorMessage, string context)
    {
        return new DobotEventArgs
        {
            Type = EventType.ErrorOccurred,
            ErrorMessage = errorMessage,
            Context = context
        };
    }

    /// <summary>
    /// 创建底盘位置更新事件参数
    /// </summary>
    public static DobotEventArgs CreateChassisPositionEvent(float x, float y, float angle)
    {
        return new DobotEventArgs
        {
            Type = EventType.ChassisPositionUpdated,
            ChassisX = x,
            ChassisY = y,
            ChassisAngle = angle
        };
    }

    /// <summary>
    /// 创建底盘导航状态更新事件参数
    /// </summary>
    public static DobotEventArgs CreateChassisNavStatusEvent(string navState, float remainDistance)
    {
        return new DobotEventArgs
        {
            Type = EventType.ChassisNavStatusUpdated,
            ChassisNavState = navState,
            ChassisRemainDistance = remainDistance
        };
    }
}

/// <summary>
/// Dobot事件委托
/// </summary>
public delegate void DobotEventHandler(object sender, DobotEventArgs e);