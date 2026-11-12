using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
namespace UnityEngine
{
    public static class Application { public static string persistentDataPath; }
    public static class JsonUtility
    {
        static readonly JsonSerializerOptions Options = new() { IncludeFields=true };
        public static string ToJson(object value) => JsonSerializer.Serialize(value, Options);
        public static T FromJson<T>(string text) => JsonSerializer.Deserialize<T>(text, Options);
        public static void FromJsonOverwrite(string text, object target)
        {
            using var doc=JsonDocument.Parse(text);
            foreach(var item in doc.RootElement.EnumerateObject())
            {
                var field=target.GetType().GetField(item.Name);
                if(field!=null && !field.IsLiteral) field.SetValue(target, JsonSerializer.Deserialize(item.Value.GetRawText(),field.FieldType,Options));
            }
        }
    }
    public class MonoBehaviour
    {
        public T GetComponent<T>() where T:class=>null;
        public static T FindObjectOfType<T>() where T:class=>null;
        public int GetInstanceID()=>42;
    }
    public class GameObject
    {
        public string name;
        public bool activeSelf=true;
        public readonly List<object> Items=new();
        public void SetActive(bool value)=>activeSelf=value;
        public T[] GetComponentsInChildren<T>(bool include) => Items.FindAll(v=>v is T).ConvertAll(v=>(T)v).ToArray();
    }
    public struct Vector2 { public float x,y; public Vector2(float x,float y) { this.x=x;this.y=y; } }
    public struct Rect { public float x,y,width,height; public Rect(float x,float y,float w,float h) { this.x=x;this.y=y;width=w;height=h; } }
    public class RectTransform { public Vector2 anchoredPosition,sizeDelta; }
    public class GUIStyle { public bool wordWrap; public GUIStyle() {} public GUIStyle(GUIStyle style) {} }
    public class GUISkin { public GUIStyle label=new(),box=new(),toggle=new(); }
    public static class GUI
    {
        public static bool enabled=true;
        public static GUISkin skin=new();
        public static Rect Window(int id,Rect rect,Action<int> draw,string title) { draw(id);return rect; }
        public static void DragWindow(Rect rect) {}
    }
    public static class Screen { public static int width=1280,height=720; }
    public static class Mathf
    {
        public static float Min(float a,float b)=>Math.Min(a,b);
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static float Clamp(float v,float a,float b)=>Math.Clamp(v,a,b);
    }
    public static class Debug { public static void LogWarning(object value) {} }
    public static class GUILayout
    {
        public static void BeginHorizontal() {} public static void EndHorizontal() {}
        public static void Label(string value,params object[] options) {}
        public static bool Button(string value,params object[] options)=>false;
        public static bool Toggle(bool b,string value,params object[] options)=>b;
        public static string TextField(string value,int max,params object[] options)=>value;
        public static Vector2 BeginScrollView(Vector2 value,params object[] options)=>value;
        public static void EndScrollView() {}
        public static object Height(float v)=>v; public static object Width(float v)=>v; public static object MinWidth(float v)=>v;
    }
}
namespace UnityEngine.UI
{
    public class Text { public string text; }
    public class Button
    {
        public UnityEngine.GameObject gameObject=new();
        public string name=>gameObject.name;
        public UnityEngine.RectTransform Rect=new();
        public ButtonClickedEvent onClick=new();
        public T GetComponent<T>() where T:class=>Rect as T;
        public T[] GetComponentsInChildren<T>(bool include)=>gameObject.GetComponentsInChildren<T>(include);
        public class ButtonClickedEvent
        {
            public readonly List<Action> Persistent=new(),Runtime=new();
            public void AddListener(Action value)=>Runtime.Add(value);
            public void RemoveListener(Action value)=>Runtime.Remove(value);
            public void Invoke() { foreach(var a in Persistent) a();foreach(var a in Runtime) a(); }
        }
    }
}
namespace TMPro
{
    public class TMP_Text { public string text; }
    public class TMP_InputField
    {
        public bool readOnly;
        public string text;
        public int Notifications;
        public void SetTextWithoutNotify(string value)=>text=value;
    }
}
public class FieldCommissioningPanel
{
    public static FieldCommissioningPanel Active;
    public bool MotionLocked;
    public int Stops;
    public void RequestStop()=>Stops++;
}
public class Simulation
{
    public bool IsRealRunPage=true;
    public UnityEngine.GameObject StatusReal=new();
    public ConnectCommander ConnectCommander;
    public DobotController Dobot;
    public RobokitController Robokit;
    public TMPro.TMP_InputField RobotIpInput=new(),ChassisIpInput=new();
}
public class ConnectCommander { public bool AnyTaskRunning;public DobotController dobotTCP;public RobokitController chassisTCP;public LiftCylinderController liftTCP; }
public class ServoTighteningController { public bool FieldWorkflowOwned; }
public class DeviceFake
{
    public bool ViewConnecting,ViewHasConnection,ViewEndpointsAligned=true;
    public string ViewConnectionDescription="fake endpoint";
    public int Connects,Disconnects;
    public TaskCompletionSource<bool> Pending;
    public bool FailConnect,FailDisconnect;
    public async Task ConnectAsync()
    {
        Connects++;ViewConnecting=true;
        try { if(FailConnect) throw new Exception("fake offline");if(Pending!=null) await Pending.Task;ViewHasConnection=true; }
        finally { ViewConnecting=false; }
    }
    public void Disconnect()
    {
        Disconnects++;
        if(FailDisconnect) throw new Exception("fake disconnect error");
        ViewHasConnection=false;Pending?.TrySetCanceled();
    }
}
public class DobotController:DeviceFake
{
    public string robotIP="192.168.192.19";
    public int dashboardPort=29999,feedbackPort=30005;
    public bool FieldWorkflowActive,ExternalControlActive,ViewControlReady;
    public int PowerRequests,EnableRequests;
    public void PowerOn()=>PowerRequests++;
    public void EnableRobot()=>EnableRequests++;
    public void ConfigureFieldConnection(FieldCommissioningSettings s)
    {
        s.ValidateRobotConnection();
        if(ViewHasConnection && (!ViewEndpointsAligned || robotIP!=s.robotIp || dashboardPort!=s.dashboardPort || feedbackPort!=s.feedbackPort))
            throw new InvalidOperationException("请先明确断开旧机械臂连接");
        robotIP=s.robotIp;dashboardPort=s.dashboardPort;feedbackPort=s.feedbackPort;
    }
}
public class RobokitController:DeviceFake
{
    public string robotIP="192.168.192.5";
    public bool FieldWorkflowOwned,ManualJogLocked,ConfirmedMotionActive,ViewAllPortsConnected;
    public int Stops,Relocations;
    public Task ConnectAllAsync()=>ConnectAsync();
    public void DisConnect()=>Disconnect();
    public Task StopManualJogAsync() { Stops++;return Task.CompletedTask; }
    public void ResetPosition()=>Relocations++;
}
public class LiftCylinderController:DeviceFake { public string ipAddress="192.168.192.88";public int port=502; }
