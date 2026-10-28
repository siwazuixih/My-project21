using System;
using System.Reflection;
using System.Threading;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 仅预览场景、反射设置内存状态；不启动Start/Play，不连接网络，不访问生产数据库。
public static class SemiAutomaticValidation
{
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field,
        BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, value);
    private static void Require(bool value, string reason)
    { if (!value) throw new Exception("[SemiAutomaticCheck] " + reason); }
    [UnityEditor.MenuItem("Tools/Validation/Semi automatic toolbar (no hardware)")]
    public static void Run()
    {
        var preview = EditorSceneManager.NewPreviewScene();
        var oldActive = FieldCommissioningPanel.Active;
        FieldCommissioningPanel panel = null;
        ManualTargetGate gate = null;
        ManualTargetGate chassisGate = null;
        OfflineTargetSession offline = null;
        using (var cancellation = new CancellationTokenSource())
        {
            try
            {
                var root = new GameObject("SemiAutomaticPreview");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
                panel = root.AddComponent<FieldCommissioningPanel>();
                typeof(FieldCommissioningPanel).GetProperty("Active").GetSetMethod(true).Invoke(null, new object[] { panel });
                Set(panel, "<Busy>k__BackingField", true); Set(panel, "semiAutomatic", true); Set(panel, "cancellation", cancellation);
                gate = (ManualTargetGate)typeof(FieldCommissioningPanel).GetField("manualGate", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel);
                var waiting = gate.WaitAsync(1, new double[6], cancellation.Token);
                var servo = root.AddComponent<ServoTighteningController>(); servo.FieldWorkflowOwned = true;
                var toggle = new GameObject("Toggle", typeof(RectTransform), typeof(Button)); toggle.transform.SetParent(root.transform);
                var text = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)); text.transform.SetParent(toggle.transform);
                var forward = new GameObject("Forward", typeof(RectTransform), typeof(Button)); forward.transform.SetParent(root.transform);
                var reverse = new GameObject("Reverse", typeof(RectTransform), typeof(Button)); reverse.transform.SetParent(root.transform);
                var stop = new GameObject("Stop", typeof(RectTransform), typeof(Button)); stop.transform.SetParent(root.transform);
                Set(servo, "toggleButton", toggle.GetComponent<Button>()); Set(servo, "toggleButtonText", text.GetComponent<TextMeshProUGUI>());
                Set(servo, "forwardButton", forward.GetComponent<Button>()); Set(servo, "reverseButton", reverse.GetComponent<Button>());
                Set(servo, "stopButton", stop.GetComponent<Button>());
                var update = typeof(ServoTighteningController).GetMethod("UpdateButtonState", BindingFlags.NonPublic | BindingFlags.Instance);
                update.Invoke(servo, null);
                Require(toggle.GetComponent<Button>().interactable && text.GetComponent<TextMeshProUGUI>().text == "确认并拧紧", "waiting continuation label/enabled");
                Require(!forward.GetComponent<Button>().interactable && !reverse.GetComponent<Button>().interactable, "manual rotations locked");
                Require(stop.GetComponent<Button>().interactable, "stop available even before service readiness");
                gate.TryContinue(gate.Generation, 1, new double[6], .2);
                update.Invoke(servo, null);
                Require(!toggle.GetComponent<Button>().interactable, "duplicate continuation disabled");
                Set(panel, "<Busy>k__BackingField", false); servo.FieldWorkflowOwned = false;
                update.Invoke(servo, null);
                Require(toggle.GetComponent<Button>().interactable && text.GetComponent<TextMeshProUGUI>().text == "启动程序", "idle lifecycle restored");
                Set(panel, "<Busy>k__BackingField", true); Set(panel, "semiAutomatic", false); servo.FieldWorkflowOwned = true;
                chassisGate = (ManualTargetGate)typeof(FieldCommissioningPanel).GetField("legacyChassisGate", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel);
                var chassisWait = chassisGate.WaitAsync(1, new double[6], cancellation.Token);
                update.Invoke(servo, null);
                Require(toggle.GetComponent<Button>().interactable && text.GetComponent<TextMeshProUGUI>().text == "确认底盘", "legacy chassis confirmation routed separately from tightening");
                Require(stop.GetComponent<Button>().interactable && !forward.GetComponent<Button>().interactable, "legacy wait retains stop and manual exclusion");
                chassisGate.TryContinue(chassisGate.Generation, 1, new double[6], .2); update.Invoke(servo, null);
                Require(!toggle.GetComponent<Button>().interactable, "legacy double confirmation disabled");
                Set(panel, "<Busy>k__BackingField", false); servo.FieldWorkflowOwned = false;
                Set(panel, "configText", JsonUtility.ToJson(new FieldCommissioningSettings())); Set(panel, "confirmed", true);
                var setChassisMode = typeof(FieldCommissioningPanel).GetMethod("SetLegacyChassisMode", BindingFlags.NonPublic | BindingFlags.Instance);
                setChassisMode.Invoke(panel, new object[] { true });
                string configured = (string)typeof(FieldCommissioningPanel).GetField("configText", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel);
                Require(JsonUtility.FromJson<FieldCommissioningSettings>(configured).useLegacyChassisTiming &&
                    !(bool)typeof(FieldCommissioningPanel).GetField("confirmed", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel), "mode selection persists to JSON and revokes safety confirmation");
                setChassisMode.Invoke(panel, new object[] { false });
                var commander = root.AddComponent<ConnectCommander>();
                offline = new OfflineTargetSession(); Set(commander, "offlineSession", offline);
                servo.OfflineWorkflowOwner = commander;
                var offlineWait = offline.WaitAsync(1, new double[7]);
                update.Invoke(servo, null);
                Require(toggle.GetComponent<Button>().interactable && text.GetComponent<TextMeshProUGUI>().text == "继续（模拟）", "offline continuation available without Python readiness");
                Require(stop.GetComponent<Button>().interactable && !forward.GetComponent<Button>().interactable && !reverse.GetComponent<Button>().interactable, "offline cancel available and manual rotations excluded");
                offline.Continue(offline.Generation, 1, new double[7]); update.Invoke(servo, null);
                Require(!toggle.GetComponent<Button>().interactable, "offline duplicate continuation disabled");
                servo.OfflineWorkflowOwner = null; Set(commander, "offlineSession", null);

                var simulation = root.AddComponent<Simulation>();
                var row = new GameObject("StepRun", typeof(RectTransform), typeof(UnityEngine.UI.Text)); row.transform.SetParent(root.transform);
                row.GetComponent<UnityEngine.UI.Text>().text = "单步运行";
                var child = new GameObject("ToggleTarget", typeof(RectTransform), typeof(Toggle)); child.transform.SetParent(row.transform);
                var unrelated = new GameObject("OtherLabel", typeof(RectTransform), typeof(UnityEngine.UI.Text)); unrelated.transform.SetParent(root.transform);
                unrelated.GetComponent<UnityEngine.UI.Text>().text = "单步运行";
                simulation.StepRunToggle = child.GetComponent<Toggle>();
                var mode = typeof(Simulation).GetMethod("UpdateModeUI", BindingFlags.NonPublic | BindingFlags.Instance);
                mode.Invoke(simulation, new object[] { false });
                Require(row.GetComponent<UnityEngine.UI.Text>().text == "单目标" && unrelated.GetComponent<UnityEngine.UI.Text>().text == "单步运行", "parent row label changed without touching unrelated labels");
                mode.Invoke(simulation, new object[] { true });
                Require(row.GetComponent<UnityEngine.UI.Text>().text == "单步运行", "simulation label restored");
                var sampling = ArmTrajectorySampling.Build(new[] { new double[] { Math.PI/3,0,0,0,0,0,0 } },
                    new double[7], 0, 5, "预览/采样");
                var report = sampling.CaptureReport(new ArmTrajectoryTiming(5,.5,10,.2,0));
                var decoded = JsonUtility.FromJson<ArmTrajectorySampling.Report>(JsonUtility.ToJson(report));
                Require(decoded.originalCount == 1 && decoded.sampledCount == 12 && decoded.sampled.Count == 12 &&
                    Math.Abs(decoded.sampled[11].jointsDeg[0]-60) < 1e-8 && decoded.sampled[11].sendIntervalSeconds == 0,
                    "Unity JSON preserves sampled trajectory degrees, provenance and final interval");
                Debug.Log("[SemiAutomaticCheck] PASS: real/offline continuation, manual exclusion, stop readiness, duplicate rejection, parent label, sampled trajectory JSON and lifecycle restore; no hardware/DB accessed.");
            }
            finally
            {
                gate?.Cancel();
                chassisGate?.Cancel();
                offline?.Dispose();
                if (panel != null) Set(panel, "<Busy>k__BackingField", false);
                typeof(FieldCommissioningPanel).GetProperty("Active").GetSetMethod(true).Invoke(null, new object[] { oldActive });
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }
    }
}
