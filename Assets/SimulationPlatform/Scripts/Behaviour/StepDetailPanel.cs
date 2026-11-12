using System.Collections.Generic;
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 运行记录执行明细面板：展示某条运行记录的步骤明细（StepCellItem 列表），
/// 以及运行时间（TimeText）与耗时（SpanText）；提供返回按钮回到之前的面板。
/// </summary>
public class StepDetailPanel : MonoBehaviour
{
    /// <summary>步骤明细列表内容容器</summary>
    public Transform TableContent;

    /// <summary>步骤项 prefab（挂载 StepCellItem 组件）</summary>
    public GameObject StepItemPrefab;

    /// <summary>显示记录运行时间</summary>
    public Text TimeText;

    /// <summary>显示耗时</summary>
    public Text SpanText;

    /// <summary>返回按钮：隐藏当前面板，显示之前的面板</summary>
    public Button BackBtn;
    public Button OpenDataBtn, ExportDataBtn;
    public Text DataStatusText;
    private RunRecord dataRecord;
    private bool exporting;
    [Serializable] private sealed class DataManifest
    { public bool completed=false; public string schema="",runId="",status="",error=""; public long droppedRows=0; }

    /// <summary>返回目标面板（运行时传入，优先于 Inspector 绑定）</summary>
    private GameObject _returnPanel;

    void Start()
    {
        if (BackBtn != null)
        {
            BackBtn.onClick.AddListener(OnBackClick);
        }
        OpenDataBtn?.onClick.AddListener(OpenDataDirectory);
        ExportDataBtn?.onClick.AddListener(ExportData);
    }

    /// <summary>
    /// 打开执行明细面板并加载运行记录步骤。
    /// </summary>
    /// <param name="record">运行记录</param>
    /// <param name="returnPanel">返回时显示的面板</param>
    public void Open(RunRecord record, GameObject returnPanel)
    {
        _returnPanel = returnPanel;
        dataRecord = record;
        ClearTable();

        if (record == null)
        {
            return;
        }

        if (TimeText != null)
        {
            TimeText.text = record.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
        if (SpanText != null)
        {
            SpanText.text = $"{record.DurationSeconds:F1} 秒 | {record.Source} | {record.Result}（完整说明见首行）";
        }

        AddStepItem(new RunStepRecord(0, "运行摘要") { Result = record.Result,
            Detail = $"{record.ResultReason}\n{record.RealParamsSummary}\n报告目录：{record.ReportDirectory}\n数据目录：{record.DataDirectory}" }, 0);

        if (record.Steps != null)
        {
            for (int i = 0; i < record.Steps.Count; i++)
            {
                AddStepItem(record.Steps[i], i + 1);
            }
        }

        gameObject.SetActive(true);
        _ = RefreshDataStatusAsync(record);
    }

    private string DataPath(RunRecord record) => string.IsNullOrEmpty(record?.DataDirectory) ? record?.ReportDirectory : record.DataDirectory;
    private static async Task<string> ReadMetadataAsync(string directory)
    {
        if (string.IsNullOrEmpty(directory)) return null;
        return await Task.Run(() =>
        {
            string path = Path.Combine(directory,"metadata.json");
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 131072) throw new IOException("数据索引过大，拒绝在界面读取");
            using (var input = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(input)) return reader.ReadToEnd();
        });
    }
    private async Task RefreshDataStatusAsync(RunRecord record)
    {
        try
        {
            var live = RunDataCapture.Current;
            if (live != null && live.RunId == record?.Id)
            { if (DataStatusText != null) DataStatusText.text="数据：" + live.Status; return; }
            string json = await ReadMetadataAsync(DataPath(record));
            if (this == null || dataRecord != record) return;
            var manifest = json == null ? null : JsonUtility.FromJson<DataManifest>(json);
            if (manifest != null && (manifest.schema != RunDataSession.Schema || manifest.runId != record?.Id))
                throw new IOException("档案格式/运行编号不匹配，拒绝关联其他运行的数据");
            string status = manifest == null ? string.IsNullOrEmpty(record?.DataDirectory) ? "旧记录/尚无本版数据档案" : "索引尚未写出或保存失败，数据不完整；请检查日志" :
                !string.IsNullOrEmpty(manifest.error) ? "保存失败：" + manifest.error : manifest.completed ? manifest.status + (manifest.droppedRows > 0 ? "；丢失记录数：" + manifest.droppedRows : "") :
                "记录中、正在刷盘或未正常结束；不可当作完整档案";
            if (!string.IsNullOrEmpty(record?.DataError)) status = "保存失败：" + record.DataError;
            if (DataStatusText != null) DataStatusText.text = "数据：" + status;
        }
        catch (Exception e) { if (this != null && dataRecord == record && DataStatusText != null) DataStatusText.text="数据状态读取失败：" + e.Message; }
    }
    private void OpenDataDirectory()
    {
        try
        {
            string path = DataPath(dataRecord);
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) throw new IOException("本条记录的数据目录不存在；旧记录不会补造历史数据");
            Application.OpenURL(new Uri(Path.GetFullPath(path) + Path.DirectorySeparatorChar).AbsoluteUri);
            _ = RefreshDataStatusAsync(dataRecord);
        }
        catch (Exception e) { if(DataStatusText != null) DataStatusText.text=e.Message; Debug.LogWarning("[RunData] " + e.Message); }
    }
    private async void ExportData()
    {
        if (exporting || dataRecord == null) return;
        exporting = true;
        var record = dataRecord;
        try
        {
            string directory = DataPath(record);
            string json = await ReadMetadataAsync(directory);
            if (json == null) throw new IOException("无本版数据档案，不能导出");
            var manifest = JsonUtility.FromJson<DataManifest>(json);
            if (manifest.schema != RunDataSession.Schema || manifest.runId != record.Id)
                throw new IOException("档案格式/运行编号不匹配，拒绝导出其他运行的数据");
            if (!manifest.completed) throw new IOException("记录尚未刷盘结束或上次异常退出；请稍后重开详情，或打开目录检查不完整文件");
            string destination = await RunDataFiles.ExportAsync(directory,Path.Combine(Application.persistentDataPath,"RunDataExports"));
            if (this != null && dataRecord == record && DataStatusText != null)
                DataStatusText.text = "已导出：" + destination + "（原始完整性：" + manifest.status + "）";
            Debug.Log("[RunData] 已导出：" + destination);
        }
        catch (Exception e) { if(this != null && dataRecord == record && DataStatusText != null) DataStatusText.text="导出失败：" + e.Message; Debug.LogWarning("[RunData] 导出失败："+e.Message); }
        finally { exporting=false; }
    }

    /// <summary>
    /// 添加一条步骤到列表（序号递增）。
    /// </summary>
    private void AddStepItem(RunStepRecord step, int number)
    {
        if (StepItemPrefab == null || TableContent == null)
        {
            Debug.LogError("StepDetailPanel 的 StepItemPrefab 或 TableContent 未设置");
            return;
        }

        GameObject item = Instantiate(StepItemPrefab, TableContent);
        StepCellItem cellItem = item.GetComponent<StepCellItem>();
        if (cellItem != null)
        {
            cellItem.SetData(step, number);
            // 生成时手动赋值序号
            cellItem.SetNumber(number);
            if (number == 0 && cellItem.NumCell != null) cellItem.NumCell.text = "—";
            RunHistoryPanel.LayoutStepRow(item);
        }
        else
        {
            Debug.LogWarning("步骤项 prefab 上未挂载 StepCellItem 组件");
        }
    }

    private void ClearTable()
    {
        if (TableContent != null)
        {
            foreach (Transform child in TableContent)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }
    }

    /// <summary>
    /// 返回按钮事件：隐藏当前面板，显示之前的面板。
    /// </summary>
    private void OnBackClick()
    {
        gameObject.SetActive(false);
        if (_returnPanel != null)
        {
            _returnPanel.SetActive(true);
        }
        else
        {
            Debug.LogWarning("StepDetailPanel 未设置返回面板（returnPanel）");
        }
    }
}
