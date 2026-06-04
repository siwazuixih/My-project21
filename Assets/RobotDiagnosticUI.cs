using UnityEngine;
using RobotLogic;

[RequireComponent(typeof(LineRenderer))]
public class RobotDiagnosticUI : MonoBehaviour
{
    private MissionController mgr;
    public LineRenderer lineVis { get; private set; }

    public float debug_LastPosErr = 0f;
    public float debug_LastAngErr = 0f;
    
    // 💡 任务清单面板的滚动控制
    private Vector2 playlistScroll = Vector2.zero;

    public void Init(MissionController manager)
    {
        mgr = manager;
        lineVis = GetComponent<LineRenderer>();
        InitializeVisuals();
    }

    void InitializeVisuals()
    {
        if (mgr.refs.realRobotBase != null) SetupTrail(mgr.refs.realRobotBase, Color.blue, 5.0f);
        if (mgr.refs.armEndEffector != null) SetupTrail(mgr.refs.armEndEffector.gameObject, mgr.debug.trailColor, 0.5f);
    }

    private void SetupTrail(GameObject go, Color color, float time)
    {
        if (go.GetComponent<TrailRenderer>()) return;
        TrailRenderer tr = go.AddComponent<TrailRenderer>();
        tr.material = new Material(Shader.Find("Sprites/Default"));
        tr.startColor = color; tr.endColor = new Color(color.r, color.g, color.b, 0);
        tr.startWidth = 0.02f; tr.endWidth = 0.0f;
        tr.time = time; tr.autodestruct = false;
    }

    void Update()
    {
        if (lineVis) {
            lineVis.enabled = mgr.debug.showRealtimePath;
            lineVis.startColor = mgr.debug.realtimePathColor; lineVis.endColor = mgr.debug.realtimePathColor;
        }
    }

    public void RunDetailedDiagnosis(int index)
    {
        if (index >= mgr.snapshots.Count) return;
        var snap = mgr.snapshots[index];
        Vector3 actualPos = mgr.chassisCtrl.GetRobotPosition();
        
        float posErr = Vector3.Distance(new Vector3(actualPos.x, 0, actualPos.z), new Vector3(snap.precalcChassisPos.x, 0, snap.precalcChassisPos.z));
        float angErr = Mathf.DeltaAngle(mgr.chassisCtrl.currentFacingAngle, snap.precalcChassisAngle);

        debug_LastPosErr = posErr;
        debug_LastAngErr = angErr;
    }

    void OnDrawGizmos()
    {
        if (mgr == null) return;
        if (mgr.debug.showGlobalPlan && mgr.hasPrecalculated) {
            Gizmos.color = mgr.debug.globalPathColor;
            foreach (var route in mgr.globalPathCache) {
                if (route == null) continue;
                for (int i = 0; i < route.Length - 1; i++) Gizmos.DrawLine(route[i], route[i + 1]);
            }
        }

        if (mgr.debug.showGhostRobot && mgr.snapshots.Count > 0) {
            foreach (var snap in mgr.snapshots) {
                Gizmos.color = snap.ikSuccess ? mgr.debug.ghostColor : Color.red;
                Gizmos.DrawWireSphere(snap.precalcChassisPos, 0.5f);
                Gizmos.color = snap.ikSuccess ? Color.cyan : Color.red;
                Gizmos.DrawLine(snap.precalcChassisPos, snap.precalcArmTarget);
            }
        }

        if (mgr.debug.showChassisLines && mgr.chassisCtrl != null && mgr.chassisCtrl.pathCorners != null && mgr.chassisCtrl.currentCornerIndex < mgr.chassisCtrl.pathCorners.Length) {
            Gizmos.color = mgr.debug.realtimePathColor;
            for (int i = 0; i < mgr.chassisCtrl.pathCorners.Length - 1; i++) Gizmos.DrawLine(mgr.chassisCtrl.pathCorners[i], mgr.chassisCtrl.pathCorners[i+1]);
            Gizmos.DrawLine(mgr.chassisCtrl.GetRobotPosition(), mgr.chassisCtrl.pathCorners[mgr.chassisCtrl.currentCornerIndex]);
        }
    }

    void OnGUI()
    {
        if (mgr == null || !mgr.debug.showGUI) return;
        
        // ================= 左侧：状态监控面板 =================
        GUIStyle style = new GUIStyle(); style.fontSize = 16; style.normal.textColor = Color.white; style.fontStyle = FontStyle.Bold;
        
        GUI.Box(new Rect(10, 10, 320, 350), "🤖 任务控制面板"); 
        GUILayout.BeginArea(new Rect(20, 40, 300, 320));
        
        GUILayout.Label($"当前状态: {mgr.currentState}", style);
        
        if (mgr.currentState == MissionState.ArmMoving || mgr.currentState == MissionState.ArmPlanning) {
            style.normal.textColor = Color.yellow; 
            GUILayout.Label($"机械臂状态: {mgr.armCtrl.debug_PlannerStatus}", style);
        } else {
            style.normal.textColor = Color.white;
            GUILayout.Label($"当前目标: {mgr.currentMissionIndex + 1}/{mgr.mission.targets.Count}", style);
        }

        GUILayout.Space(5);
        style.normal.textColor = Color.gray;
        GUILayout.Label("⌨️ 按 [R] 键可重置仿真系统", style);

        GUILayout.Space(10);
        style.fontSize = 14;
        style.normal.textColor = debug_LastPosErr > 0.05f ? Color.red : Color.green;
        GUILayout.Label($"位置误差: {debug_LastPosErr*100:F1} cm");
        style.normal.textColor = Mathf.Abs(debug_LastAngErr) > 2.0f ? Color.red : Color.green;
        GUILayout.Label($"角度误差: {debug_LastAngErr:F1} 度");
        if (mgr.hasPrecalculated) {
             style.normal.textColor = Color.green; GUILayout.Label("[预计算已就绪]");
        }
        if (mgr.mission.stepByStepMode) {
             style.normal.textColor = new Color(1f, 0.6f, 0f); GUILayout.Label("⚙️ 当前模式: 分步调试 (Step-By-Step)");
        } else {
             style.normal.textColor = Color.cyan; GUILayout.Label("⚙️ 当前模式: 全自动 (Full Auto)");
        }

        GUILayout.Space(15);
        style.normal.textColor = Color.white;
        style.fontSize = 15;
        GUILayout.Label("--- 🔧 执行末端参数 ---", style);
        
        style.fontSize = 14;
        style.normal.textColor = new Color(0.8f, 0.8f, 0.8f); 
        GUILayout.Label("接头规格: G6*0.6");
        GUILayout.Label("最大力矩: 5.0 N·m");
        GUILayout.Label("拧紧角度: 90°");
        
        GUILayout.EndArea();

        // ================= 右侧：全系统任务调度清单 =================
        RenderTaskPlaylist();
    }

    void RenderTaskPlaylist()
    {
        if (mgr.connect.Commander == null || mgr.connect.Commander.taskPlaylist.Count == 0) return;

        GUIStyle headStyle = new GUIStyle(); headStyle.fontSize = 16; headStyle.normal.textColor = Color.cyan; headStyle.fontStyle = FontStyle.Bold;
        
        GUI.Box(new Rect(Screen.width - 340, 10, 330, 500), "📂 动作库与真机调度");
        GUILayout.BeginArea(new Rect(Screen.width - 330, 40, 310, 450));
        
        GUILayout.Label("预计算已打包，点击下方按钮下发：", headStyle);
        GUILayout.Space(10);

        // 创建滚动视图防止任务过多超出屏幕
        playlistScroll = GUILayout.BeginScrollView(playlistScroll);
        
        // RobotDiagnosticUI.cs - 修改 RenderTaskPlaylist 方法中的 for 循环部分

        for (int i = 0; i < mgr.connect.Commander.taskPlaylist.Count; i++)
        {
            var task = mgr.connect.Commander.taskPlaylist[i];
            
            // 1. 智能分配真机面板颜色
            if (task.isDispatched) {
                GUI.color = Color.gray; // 已发送的任务变灰
            }
            else if (task.type == TaskType.Chassis) {
                GUI.color = new Color(1f, 0.6f, 0f); // 底盘用橙色
            }
            else if (task.type == TaskType.Lift) {
                GUI.color = new Color(0.5f, 0f, 1f); // 🌟 新增：升降缸用高亮紫色
            }
            else {
                GUI.color = Color.cyan; // 机械臂用青色
            }

            // 2. 根据任务类型分配图标和文字描述
            string icon = "🦾";
            string ptsCount = "";

            if (task.type == TaskType.Chassis) {
                icon = "🚜";
                ptsCount = $"{task.chassisPoints.Count}节点";
            }
            else if (task.type == TaskType.Lift) {
                icon = "🛗"; // 🌟 新增：专属升降缸图标
                ptsCount = $"{task.targetLiftHeight:F2}米"; // 🌟 新增：直接显示目标高度
            }
            else {
                icon = "🦾";
                ptsCount = $"{task.armPoints.Count}点";
            }
            
            // 3. 生成调度按钮
            if (GUILayout.Button($"{icon} {task.taskName} ({ptsCount})", GUILayout.Height(30)))
            {
                mgr.connect.Commander.DispatchTask(i);
            }
            
            // 恢复默认颜色
            GUI.color = Color.white; 
            GUILayout.Space(2);
        }        
        GUILayout.EndScrollView();

        GUILayout.Space(10);
        
        // 智能感知流水线状态锁
        if (mgr.connect.Commander.isSequenceRunning)
        {
            GUI.color = Color.yellow; // 运行中呈现警告黄
            GUILayout.Button("⏳ 自动化序列实车动作中，请保持监控...", GUILayout.Height(40));
        }
        else
        {
            GUI.color = Color.green; // 待机状态呈现安全绿
            if (GUILayout.Button("▶ 一键下发当前目标点完整动作组", GUILayout.Height(40)))
            {
                mgr.connect.Commander.DispatchNextTargetSequence();
            }
        }
        GUI.color = Color.white; // 恢复默认画布颜色
        
        GUILayout.EndArea();
    }
}