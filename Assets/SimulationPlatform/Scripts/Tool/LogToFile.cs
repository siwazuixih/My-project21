using System;
using System.IO;
using System.Threading;
using UnityEngine;

public class LogToFile : MonoBehaviour
{
    public static LogToFile Instance;

    private string logFilePath;
    private StreamWriter logWriter;
    private object logLock = new object();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // 切换场景不销毁
            InitLogFile(); // 初始化日志文件
            RegisterGlobalExceptionHandlers();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        Application.logMessageReceived += OnLogMessageReceived;
        Application.logMessageReceivedThreaded += OnLogMessageReceivedThreaded;
        Thread.GetDomain().UnhandledException += OnUnhandledException;
    }

    // 初始化日志文件（创建路径和文件）
    private void InitLogFile()
    {
        // 日志文件路径：PersistentDataPath + 日期命名（避免覆盖旧日志）
        string logDir = Path.Combine(PathTool.GetExecutableDirPath(), "Logs");
        if (!Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir); // 创建Logs文件夹
        }

        // 文件名格式：YYYY-MM-DD_HH-mm-ss.log
        string fileName = $"Log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";
        logFilePath = Path.Combine(logDir, fileName);

        // 创建文件并打开写入流（Append: true 表示追加模式，false 表示覆盖）
        logWriter = new StreamWriter(logFilePath, true)
        {
            AutoFlush = true // 自动刷新缓冲区（确保日志实时写入）
        };

        // 写入日志头部（标记日志开始时间）
        WriteLogToFile($"=== 日志开始 ===");
        WriteLogToFile($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        WriteLogToFile($"应用路径：{PathTool.GetExecutableDirPath()}");
        WriteLogToFile($"================\n");
        Debug.Log($"日志保存路径：{logDir}");
    }

    // 日志回调事件：当Unity输出任何日志时触发
    private void OnLogMessageReceived(string logMessage, string stackTrace, LogType logType)
    {
        // 格式化日志内容（包含类型、时间、消息、堆栈信息）
        string logTypeStr = logType switch
        {
            LogType.Error => "[ERROR]",
            LogType.Warning => "[WARNING]",
            LogType.Assert => "[ASSERT]",
            LogType.Exception => "[EXCEPTION]",
            _ => "[LOG]" // 普通日志
        };

        string timeStr = DateTime.Now.ToString("HH:mm:ss.fff"); // 精确到毫秒
        string fullLog = $"[{timeStr}] {logTypeStr}：{logMessage}\n";

        // 错误和异常需要附加堆栈信息
        if (logType == LogType.Error || logType == LogType.Exception)
        {
            fullLog += $"堆栈跟踪：{stackTrace}\n";
        }

        // 写入文件
        WriteLogToFile(fullLog);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Exception ex = e.ExceptionObject as Exception;
        if (ex != null)
        {
            string timeStr = DateTime.Now.ToString("HH:mm:ss.fff");
            string crashLog = $"[{timeStr}] [CRASH] 未处理的异常，应用即将崩溃！\n";
            crashLog += $"异常类型：{ex.GetType().FullName}\n";
            crashLog += $"异常消息：{ex.Message}\n";
            crashLog += $"堆栈跟踪：{ex.StackTrace}\n";
            
            if (ex.InnerException != null)
            {
                crashLog += $"内部异常：{ex.InnerException.Message}\n";
                crashLog += $"内部堆栈：{ex.InnerException.StackTrace}\n";
            }
            
            crashLog += $"应用程序域：{sender}\n";
            crashLog += $"是否终止：{e.IsTerminating}\n";
            crashLog += $"================\n";

            WriteLogToFile(crashLog);
            ForceFlush();
        }
    }

    private void OnLogMessageReceivedThreaded(string logMessage, string stackTrace, LogType logType)
    {
        string logTypeStr = logType switch
        {
            LogType.Error => "[ERROR]",
            LogType.Warning => "[WARNING]",
            LogType.Assert => "[ASSERT]",
            LogType.Exception => "[EXCEPTION]",
            _ => "[LOG]"
        };

        string timeStr = DateTime.Now.ToString("HH:mm:ss.fff");
        string fullLog = $"[{timeStr}] {logTypeStr}：{logMessage}\n";

        if (logType == LogType.Error || logType == LogType.Exception)
        {
            fullLog += $"堆栈跟踪：{stackTrace}\n";
        }

        WriteLogToFile(fullLog);
    }

    private void WriteLogToFile(string content)
    {
        lock (logLock)
        {
            if (logWriter == null)
            {
                return;
            }

            try
            {
                logWriter.WriteLine(content);
            }
            catch (Exception e)
            {
                try
                {
                    UnityEngine.Debug.LogError($"日志写入失败：{e.Message}");
                }
                catch
                {
                }
            }
        }
    }

    private void ForceFlush()
    {
        lock (logLock)
        {
            if (logWriter != null)
            {
                try
                {
                    logWriter.Flush();
                    logWriter.BaseStream.Flush();
                }
                catch
                {
                }
            }
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            Application.logMessageReceived -= OnLogMessageReceived;
            Application.logMessageReceivedThreaded -= OnLogMessageReceivedThreaded;
            Thread.GetDomain().UnhandledException -= OnUnhandledException;

            if (logWriter != null)
            {
                WriteLogToFile($"\n================\n=== 日志结束 ===");
                ForceFlush();
                try
                {
                    logWriter.Close();
                    logWriter.Dispose();
                }
                catch
                {
                }
                logWriter = null;
            }

            try
            {
                UnityEngine.Debug.Log($"日志已保存到：{logFilePath}");
            }
            catch
            {
            }
        }
    }
}