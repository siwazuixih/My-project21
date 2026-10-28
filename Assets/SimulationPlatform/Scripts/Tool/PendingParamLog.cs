using UnityEngine;

// wlj parameter-change logger, with an explicit initial value and idle/exit flush.
public sealed class PendingParamLog
{
    private readonly string displayName;
    private readonly OperationType category;
    private Vector3? baseline, pending;
    private float changedAt;
    public PendingParamLog(string name, OperationType type = OperationType.仿真操作) { displayName = name; category = type; }
    public void Reset(Vector3 value) { baseline = value; pending = null; }
    public void OnValueChanged(Vector3 value)
    {
        if (!baseline.HasValue) { Reset(value); return; }
        if ((pending ?? baseline.Value) == value) return;
        pending = value; changedAt = Time.unscaledTime;
    }
    public void FlushIfIdle() { if (pending.HasValue && Time.unscaledTime - changedAt >= 0.6f) Flush(); }
    public void Flush()
    {
        if (!pending.HasValue || !baseline.HasValue) return;
        Vector3 old = baseline.Value, value = pending.Value;
        baseline = value; pending = null;
        if (old != value) OperationLogTool.RecordLog(category,
            $"修改参数 - {displayName}：({old.x:F2}, {old.y:F2}, {old.z:F2})→({value.x:F2}, {value.y:F2}, {value.z:F2})");
    }
}
