using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Preview-scene only: no Play mode, no real project DB, no device/controller initialization.
public static class WulaoshiMigrationValidation
{
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception("[WljMigrationCheck] " + message); }

    [UnityEditor.MenuItem("Tools/Validation/Wulaoshi 21.5 migration (no hardware)")]
    public static void Run()
    {
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("HistoryValidationCanvas", typeof(RectTransform), typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1280, 720);
            var host = new GameObject("ProjectTabPanel", typeof(RectTransform)); host.transform.SetParent(root.transform, false);
            var sourceObject = new GameObject("Source", typeof(RectTransform), typeof(ProjectList)); sourceObject.transform.SetParent(host.transform, false);
            ProjectList source = sourceObject.GetComponent<ProjectList>();
            typeof(RunHistoryPanel).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { canvas, source });
            TaskList list = root.GetComponentInChildren<TaskList>(true);
            Require(list != null && list.TableContent != null && list.ItemPrefab != null, "history list bindings");
            Require(list.transform.parent == host.transform, "history follows existing project navigation owner");
            Require(list.BackBtn != null && list.RefreshBtn != null, "history navigation bindings");
            Require(list.GetComponent<RunTypeChose>()?.taskList == list, "mode filter bindings");
            var detail = list.DetailPanel;
            Require(detail != null && detail.StepItemPrefab != null && detail.TableContent != null && detail.BackBtn != null, "detail bindings");
            Require(list.TableContent.GetComponentInParent<ScrollRect>(true).horizontal, "narrow display horizontal scroll");
            Require(list.TableContent.GetComponentInParent<ScrollRect>(true).verticalScrollbar != null, "vertical scrollbar");
            var rowObject = UnityEngine.Object.Instantiate(list.ItemPrefab, list.TableContent);
            RunHistoryPanel.LayoutRecordRow(rowObject);
            var row = rowObject.GetComponent<RunRecordItem>();
            Require(row != null && row.DetailBtn != null && row.TimeCell != null && row.SummaryCell != null, "teacher prefab references");
            Require(row.DetailBtn.GetComponentInChildren<Text>(true).text == "查看详情", "detail is not misleadingly labelled motion replay");
            Require(row.TimeCell.font != null && row.TimeCell.font.HasCharacter('中'), "Chinese history font");
            var record = new RunRecord { Mode = RunType.SIMULATION, Simulated = true, Source = "离线测试", Result = "Success",
                Steps = new List<RunStepRecord> { new RunStepRecord(0, "目标一") { Result = "模拟完成" } } };
            row.Record = record;
            Require(row.TaskTypeCell.text == "离线模拟", "offline label");
            record.Mode = RunType.REALTIME; record.Simulated = false; record.Result = "Dispatched"; row.Record = record;
            Require(row.ResultCell.text.Contains("未验收"), "dispatch not shown as success");
            row.SetDetailReturn(list.gameObject); row.detailPanel = detail;
            typeof(RunRecordItem).GetMethod("OnDetailClicked", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(row, null);
            Require(detail.gameObject.activeSelf && !list.gameObject.activeSelf, "open detail");
            StepCellItem cell = detail.TableContent.GetComponentInChildren<StepCellItem>(true);
            Require(cell != null && cell.TorqueCell.text == "未采集" && cell.AngleCell.text == "未采集", "unknown measurements");
            typeof(StepDetailPanel).GetMethod("OnBackClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(detail, null);
            Require(list.gameObject.activeSelf && !detail.gameObject.activeSelf, "back to list");
            // Return target is not set by Build; set it explicitly without querying real project data.
            typeof(TaskList).GetField("_returnPanel", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(list, sourceObject);
            sourceObject.SetActive(false);
            typeof(TaskList).GetMethod("OnBackClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(list, null);
            Require(sourceObject.activeSelf && !list.gameObject.activeSelf, "back to projects");
            Debug.Log("[WljMigrationCheck] PASS: resources, bindings, Chinese font, narrow-screen scroll, mode/result labels, details and return navigation; no hardware/DB accessed.");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }
}
