// The chassis transport harness never opens the production operation-log database.
public enum OperationType { 真实运行 }
public static class OperationLogTool
{
    public static void RecordLog(OperationType type, string message) { }
}
public class FieldCommissioningPanel
{
    public static FieldCommissioningPanel Active;
    public int Stops;
    public void RequestStop() { Stops++; }
}
