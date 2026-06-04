using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class RuntimeConsole : MonoBehaviour
{
    [Header("把刚刚建的 ConsoleLogText 拖到这里")]
    public TextMeshProUGUI logTextDisplay;

    [Header("最多显示多少行日志？(防止内存爆炸)")]
    public int maxLines = 50;

    // 用一个列表来缓存当前屏幕上的日志文字
    private List<string> logLines = new List<string>();

    // 当这个脚本被激活时，向 Unity 申请“监听所有日志”
    void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    // 当脚本被关闭或销毁时，取消监听（极其重要的好习惯！）
    void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    // 这就是我们的“窃听器”核心处理厂
    void HandleLog(string logString, string stackTrace, LogType type)
    {
        // 1. 根据日志类型上色
        string colorHex = "#FFFFFF"; // 默认白色
        if (type == LogType.Error || type == LogType.Exception) 
            colorHex = "#FF4444"; // 错误标红
        else if (type == LogType.Warning) 
            colorHex = "#FFFF00"; // 警告标黄
        
        // 2. 拼装这行文字（加上时间戳和颜色标签）
        string timeStr = System.DateTime.Now.ToString("HH:mm:ss");
        string newLine = $"<color={colorHex}>[{timeStr}] {logString}</color>";

        // 3. 塞进我们的笔记本里
        logLines.Add(newLine);

        // 4. 重点保护：如果日志太多，就把最老的一条删掉，防止程序卡死
        if (logLines.Count > maxLines)
        {
            logLines.RemoveAt(0);
        }

        // 5. 把笔记本里的所有文字，拼成一个大字符串，塞给 UI 文本框
        if (logTextDisplay != null)
        {
            logTextDisplay.text = string.Join("\n", logLines);
        }
    }
}