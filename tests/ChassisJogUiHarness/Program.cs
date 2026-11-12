using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// These replacements exercise the production UI handlers, not Unity rendering or real sockets.
namespace UnityEngine
{
    public struct Vector2 { }
    public struct Rect { public float x,y,width,height; public Rect(float x,float y,float w,float h) { this.x=x;this.y=y;width=w;height=h; } }
    public struct Color { public float r,g,b; public Color(float r,float g,float b) { this.r=r;this.g=g;this.b=b; } }
    public class GUIStyle { public bool wordWrap; public GUIStyle() {} public GUIStyle(GUIStyle other) {} }
    public class GUISkin { public GUIStyle label=new(),box=new(),toggle=new(); }
    public static class Screen { public static int width=1280,height=720; }
    public static class Mathf { public static float Min(float a,float b)=>Math.Min(a,b); public static float Max(float a,float b)=>Math.Max(a,b); }
    public static class Debug { public static void LogWarning(object value) {} }
    public static class GUI
    {
        public static bool enabled=true;
        public static Color backgroundColor;
        public static GUISkin skin=new();
        public static string Click;
        public static bool Button(Rect rect,string label) { if(label=="底盘手动") LastEntry=rect; return Press(label); }
        public static Rect LastEntry;
        public static bool Press(string label) { if(enabled && Click==label) { Click=null; return true; } return false; }
    }
    public static class GUILayout
    {
        public static int ScrollDepth,StopDepth=-1;
        public static bool StopEnabled;
        public static void BeginArea(Rect r,GUIStyle s) {} public static void EndArea() {}
        public static void BeginHorizontal() {} public static void EndHorizontal() {} public static void FlexibleSpace() {}
        public static void Label(string s,params object[] options) {}
        public static Vector2 BeginScrollView(Vector2 s,params object[] options) { ScrollDepth++; return s; }
        public static void EndScrollView() { ScrollDepth--; }
        public static string TextField(string s,int max,params object[] options)=>s;
        public static bool Toggle(bool b,string text,params object[] options)=>b;
        public static bool Button(string s,params object[] options)
        { if(s.StartsWith("停止底盘")) { StopDepth=ScrollDepth;StopEnabled=GUI.enabled; } return GUI.Press(s); }
        public static object Width(float v)=>v; public static object Height(float v)=>v; public static object MinHeight(float v)=>v;
    }
}
public class Simulation
{
    public bool IsRealRunPage=true,Locked;
    public int PrepareCalls;
    public void PrepareFieldMode() { PrepareCalls++; }
    public void SetRealRunInputsLocked(bool value) { Locked=value; }
}
public class DobotController { public bool FieldWorkflowActive; }
public class ServoTighteningController { public bool FieldWorkflowOwned; }
public class ConnectCommander
{
    public bool NonManualTaskRunning,OfflineValidationActive;
    public RobokitController chassisTCP=new();
}
public class RobokitController
{
    public string robotIP="127.0.0.1",ManualJogStatus="fake";
    public bool ManualJogLocked,ManualJogActive,ManualJogNeedsReview,ConfirmedMotionActive,FieldWorkflowOwned,Connected=true;
    public int Connects,Stops,Motions,Recoveries;
    public ChassisJogCommand Last;
    public TaskCompletionSource<bool> Pending;
    public bool FieldConnectedTo(string ip,bool status=true)=>Connected;
    public Task ConnectAllAsync() { Connects++; return Task.CompletedTask; }
    public async Task<bool> ExecuteManualJogAsync(ChassisJogCommand c,bool permitted)
    { Last=c;Motions++;ManualJogLocked=ManualJogActive=true;bool r=Pending==null || await Pending.Task;ManualJogActive=false;ManualJogLocked=!r;ManualJogNeedsReview=!r;return r; }
    public Task StopManualJogAsync() { Stops++;ManualJogLocked=ManualJogNeedsReview=true;return Task.CompletedTask; }
    public Task<bool> AcknowledgeManualJogStoppedAsync(bool observed)
    { Recoveries++; if(observed) ManualJogLocked=ManualJogNeedsReview=false;return Task.FromResult(observed); }
}
public sealed partial class FieldCommissioningPanel
{
    public bool Busy;
    private bool connectionBusy,visible,confirmed;
    private Simulation simulation=new();
    private ConnectCommander commander=new();
    private DobotController robot=new();
    private ServoTighteningController servo=new();
    private int flowStops;
    public void RequestStop() { flowStops++; }
    public static async Task<int> Tests()
    {
        int count=0;
        void Check(bool value,string label) { if(!value) throw new Exception(label);count++;Console.WriteLine("PASS jog UI "+label); }
        FieldCommissioningPanel Fresh()=>new() { chassisJogConfirmed=true,confirmed=true };
        var p=Fresh();
        p.simulation.IsRealRunPage=false;UnityEngine.GUI.Click="底盘手动";p.DrawChassisJogEntry();
        Check(!p.chassisJogVisible && UnityEngine.GUI.Click!=null,"not offered on simulation page");
        p=Fresh();p.visible=true;UnityEngine.GUI.Click="底盘手动";p.DrawChassisJogEntry();
        Check(p.chassisJogVisible && !p.visible,"opening manual pane hides field pane");
        Check(UnityEngine.GUI.LastEntry.x==170 && UnityEngine.GUI.LastEntry.y==115,"entry sits beside field parameters");
        Check(UnityEngine.GUILayout.StopDepth==0 && UnityEngine.GUILayout.StopEnabled,"stop is outside form scroll");
        Check(p.commander.chassisTCP.Motions==0,"opening pane sends no movement");
        UnityEngine.GUI.Click="底盘手动";p.DrawChassisJogEntry();Check(!p.chassisJogVisible,"idle panel can collapse");
        p=Fresh();p.chassisJogConfirmed=false;await p.StartJogAsync(ChassisJogDirection.Forward);
        Check(p.commander.chassisTCP.Motions==0,"UI permission required");
        foreach(string owner in new[]{"busy","check","arm","servo","chassis","task","offline"})
        {
            p=Fresh();p.Busy=owner=="busy";p.connectionBusy=owner=="check";p.robot.FieldWorkflowActive=owner=="arm";
            p.servo.FieldWorkflowOwned=owner=="servo";p.commander.chassisTCP.FieldWorkflowOwned=owner=="chassis";
            p.commander.NonManualTaskRunning=owner=="task";p.commander.OfflineValidationActive=owner=="offline";
            await p.StartJogAsync(ChassisJogDirection.Forward);
            Check(p.commander.chassisTCP.Motions==0,"owner blocks handler: "+owner);
        }
        p=Fresh();p.chassisJogDistance="1,5";await p.StartJogAsync(ChassisJogDirection.Forward);
        Check(p.commander.chassisTCP.Motions==0 && p.chassisJogUiMessage.Contains("句点"),"comma input rejected before send");
        p=Fresh();p.chassisJogSpeed="NaN";await p.StartJogAsync(ChassisJogDirection.Forward);
        Check(p.commander.chassisTCP.Motions==0,"non-finite UI input blocked");
        p=Fresh();await p.StartJogAsync(ChassisJogDirection.Forward);
        Check(p.commander.chassisTCP.Last.Json.Contains("0.05") && p.commander.chassisTCP.Motions==1,"defaults produce one 50mm/50mm-s request");
        Check(!p.confirmed && !p.simulation.Locked && p.simulation.PrepareCalls==1,"jog invalidates formal permission and releases UI on success");
        p=Fresh();p.chassisJogDistance="bad";await p.StartJogAsync(ChassisJogDirection.Clockwise);
        Check(p.commander.chassisTCP.Last.Api==3056,"rotation uses angle fields independently");
        p=Fresh();p.commander.chassisTCP.Pending=new();var inFlight=p.StartJogAsync(ChassisJogDirection.Left);
        Check(p.chassisJogUiBusy && p.simulation.Locked,"manual action locks inputs before await");
        await p.StartJogAsync(ChassisJogDirection.Right);Check(p.commander.chassisTCP.Motions==1,"handler does not queue second click");
        p.chassisJogVisible=true;UnityEngine.GUI.Click="关闭";p.DrawChassisJogEntry();
        Check(p.chassisJogVisible,"active pane cannot hide via close");
        p.commander.chassisTCP.Pending.SetResult(false);await inFlight;
        Check(p.simulation.Locked && !p.chassisJogConfirmed,"failure keeps input lock and clears permission");
        p.chassisJogObservedStopped=true;await p.RecoverJogAsync();
        Check(!p.simulation.Locked && !p.confirmed && !p.chassisJogConfirmed && !p.chassisJogObservedStopped,"explicit recovery resets permission without motion");
        p=Fresh();await p.ConnectJogChassisAsync();
        Check(p.commander.chassisTCP.Connects==1 && p.commander.chassisTCP.Motions==0,"connect button only connects existing chassis");
        p=Fresh();p.chassisJogVisible=true;UnityEngine.GUI.Click="停止底盘（软件停止，非硬件急停）";p.DrawChassisJogEntry();
        Check(p.commander.chassisTCP.Stops==1 && p.simulation.Locked && !p.confirmed,"manual stop enters review and clears formal permission");
        p=Fresh();p.chassisJogVisible=true;p.Busy=true;UnityEngine.GUI.Click="停止底盘（软件停止，非硬件急停）";p.DrawChassisJogEntry();
        Check(p.flowStops==1 && p.commander.chassisTCP.Stops==0,"formal owner stop routed to whole workflow");
        p=Fresh();p.chassisJogVisible=true;p.commander.chassisTCP=null;UnityEngine.GUI.Click=null;p.DrawChassisJogEntry();
        Check(UnityEngine.GUI.enabled && !UnityEngine.GUILayout.StopEnabled,"missing chassis renders safely and restores GUI state");
        return count;
    }
}
class Program
{
    static async Task Main() { Console.WriteLine(await FieldCommissioningPanel.Tests()+" production jog UI handler checks PASS (no Unity rendering/hardware)"); }
}
