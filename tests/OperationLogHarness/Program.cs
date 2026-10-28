using System;
using System.IO;
using System.Linq;
class Program
{
    static int passed;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    static void Main()
    {
        PathTool.Root = Path.Combine(Path.GetTempPath(), "unity-operation-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(PathTool.Root);
        string blocker = Path.Combine(PathTool.Root, "Data"); File.WriteAllText(blocker, "blocks directory");
        OperationLogTool.RecordLog(OperationType.真实运行, "模拟停止请求");
        Check(UnityEngine.Debug.Errors == 1, "first log IO failure does not throw type-initializer exception");
        File.Move(blocker, blocker + ".test-backup");
        OperationLogTool.RecordLog(OperationType.仿真操作, "测试日志");
        Check(OperationLogTool.GetLogCount() == 1, "normal operation resumes after directory failure");
        Check(OperationLogTool.QueryLogs(operationType:"仿真操作").Single().Detail == "测试日志", "new category persists as readable name");
        Check((int)OperationType.系统操作 == 3, "old enum numeric value preserved");
        string model = Path.Combine(PathTool.Root, "model.glb"); File.WriteAllBytes(model, new byte[2048]);
        Check(OperationLogTool.FormatModelFileInfo(model) == "model.glb(2KB)", "file name and size");
        Check(OperationLogTool.FormatModelFileInfo(Path.Combine(PathTool.Root, "missing.glb")) == "missing.glb", "missing file does not fail logging");
        Check(OperationLogTool.FormatModelFileInfo(null) == "", "null file path");
        Console.WriteLine($"OperationLogHarness: {passed} passed. Temporary DB: {PathTool.Root}");
    }
}
public static class PathTool
{
    public static string Root;
    public static string GetExecutableDirPath() => Root;
    public static string ResolvePhysicalPath(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Root, path));
}
public static class AccountManager
{
    public static string GetCurrentAccountName() => "test-account";
    public static string GetCurrentUserName() => "test-user";
}
namespace UnityEngine
{
    public static class Debug
    {
        public static int Errors;
        public static void Log(object value) { }
        public static void LogWarning(object value) { }
        public static void LogError(object value) { Errors++; }
    }
}
