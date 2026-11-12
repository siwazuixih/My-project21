using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed partial class DeviceConnectionPanel
{
    static int tests;
    static void SaveRobot(FieldCommissioningSettings s) => File.WriteAllText(Path.Combine(Application.persistentDataPath,FieldCommissioningSettings.FileName),JsonUtility.ToJson(s));
    static void Check(bool value,string label) { if(!value) throw new Exception(label);tests++;Console.WriteLine("PASS device panel "+label); }
    static DeviceConnectionPanel Fresh()
    {
        FieldCommissioningPanel.Active=null;
        SaveRobot(new FieldCommissioningSettings());
        var p=new DeviceConnectionPanel { simulation=new Simulation(),robot=new DobotController(),chassis=new RobokitController(),lift=new LiftCylinderController(),servo=new ServoTighteningController() };
        p.simulation.Dobot=p.robot;p.simulation.Robokit=p.chassis;
        p.simulation.ConnectCommander=new ConnectCommander { dobotTCP=p.robot,chassisTCP=p.chassis,liftTCP=p.lift };
        foreach(string name in new[]{"ConnectBtn","ConnectBaseBtn","DisconnectBtn","PowerOnBtn","EnableBtn","ResetBaseBtn","StartBtn"})
        {
            var b=new Button();b.gameObject.name=name;b.gameObject.Items.Add(new TMP_Text {text=name});b.gameObject.Items.Add(new Text {text=name});
            p.simulation.StatusReal.Items.Add(b);
        }
        p.ReloadDrafts();return p;
    }
    public static async Task RunTests()
    {
        var p=Fresh();var original=p.ToolbarButton("ConnectBtn").onClick;int legacy=0;
        original.Persistent.Add(()=>legacy++);original.Persistent.Add(()=>legacy++);original.Persistent.Add(()=>legacy++);
        p.InitializeToolbar();p.entry.onClick.Invoke();
        Check(legacy==0 && p.visible && p.robot.Connects+p.chassis.Connects+p.lift.Connects==0,"entry replaces ALL persistent calls and only opens panel");
        Check(p.Ports(0).Contains("30004") && p.robot.feedbackPort==30005,"opening shows JSON port without changing controller or connecting");
        Check(original.Persistent.Count==3,"original serialized event object not edited");
        Check(p.entry.GetComponentsInChildren<TMP_Text>(true)[0].text=="设备连接" && p.entry.GetComponentsInChildren<Text>(true)[0].text=="设备连接","both TMP and legacy labels renamed");
        foreach(string name in new[]{"ConnectBaseBtn","DisconnectBtn","PowerOnBtn","EnableBtn","ResetBaseBtn"})
            Check(!p.ToolbarButton(name).gameObject.activeSelf,"duplicate toolbar instance hidden: "+name);
        Check(p.ToolbarButton("StartBtn").gameObject.activeSelf,"start-run entry not hidden");
        Check(p.entry.Rect.anchoredPosition.x==-264 && p.entry.Rect.sizeDelta.x==152,"single device entry occupies compact right slot");
        p.RefreshAddressDisplays();
        Check(p.simulation.RobotIpInput.readOnly && p.simulation.ChassisIpInput.readOnly && p.simulation.RobotIpInput.text==p.robot.robotIP,"formal IP widgets are read-only mirrors");
        p.robot.robotIP="192.168.192.20";p.RefreshAddressDisplays();
        Check(p.simulation.RobotIpInput.text=="192.168.192.20" && p.simulation.RobotIpInput.Notifications==0,"external test settings reflected without writing callbacks/JSON");
        p=Fresh();p.InitializeToolbar();p.TogglePanel();p.OnGUI();
        Check(GUI.enabled && p.robot.Connects==0,"drawing window sends no commands and restores GUI state");
        p.simulation.IsRealRunPage=false;p.OnGUI();Check(!p.visible,"not displayed in simulation mode");
        foreach(string value in new[]{"","localhost","1","1.2","192.168.999.1","http://127.0.0.1","1.2.3.4:99"})
        {
            bool rejected=false;try {ParseAddress(value);}catch(ArgumentException){rejected=true;}
            Check(rejected,"reject malformed/non-numeric address: "+value);
        }
        Check(ParseAddress(" 192.168.192.19 ")=="192.168.192.19" && ParseAddress("::ffff:127.0.0.1")=="127.0.0.1","canonical IP parsing/IPv4-mapped IPv6");
        p=Fresh();p.drafts[1]="bad";await p.ConnectDeviceAsync(1);
        Check(p.chassis.Connects==0 && p.chassis.robotIP=="192.168.192.5" && p.notes[1].Contains("IP"),"invalid address never changes target or connects");
        p=Fresh();SaveRobot(new FieldCommissioningSettings {feedbackPort=0});await p.ConnectDeviceAsync(0);
        Check(p.robot.Connects==0 && p.notes[0].Contains("端口"),"invalid current port rejected");
        Check(p.robot.feedbackPort==30005,"invalid JSON does not overwrite existing controller fields");
        p=Fresh();p.robot.dashboardPort=0;await p.ConnectDeviceAsync(0);
        Check(p.robot.Connects==1 && p.robot.dashboardPort==29999 && p.robot.feedbackPort==30004,"JSON overrides old scene/controller ports before connection");
        p=Fresh();p.drafts[0]="192.168.192.99";SaveRobot(new FieldCommissioningSettings {robotIp="192.168.192.24",dashboardPort=31001,feedbackPort=31002});await p.ConnectDeviceAsync(0);
        Check(p.robot.robotIP=="192.168.192.24" && p.robot.dashboardPort==31001 && p.robot.feedbackPort==31002,"click rereads all three JSON endpoints, not stale draft or fixed 30004");
        Check(p.Ports(0).Contains("31002") && p.drafts[0]=="192.168.192.24","display tracks the exact configuration used by connection");
        p=Fresh();File.WriteAllText(p.RobotConfigurationPath,"{");await p.ConnectDeviceAsync(0);
        Check(p.robot.Connects==0 && p.robot.feedbackPort==30005,"malformed JSON fails closed without fallback to scene defaults");
        Check(p.drafts[0].Contains("读取失败") && p.Ports(0).Contains("无效"),"failed reload does not keep displaying stale JSON endpoints");
        foreach(string host in new[]{"localhost","1","127.1","1.2.3.4:29999",""})
        {
            p=Fresh();SaveRobot(new FieldCommissioningSettings {robotIp=host});await p.ConnectDeviceAsync(0);
            Check(p.robot.Connects==0 && p.robot.feedbackPort==30005,"invalid JSON robot IP rejected before applying settings: "+host);
        }
        foreach(int port in new[]{-1,65536})
        {
            p=Fresh();SaveRobot(new FieldCommissioningSettings {dashboardPort=port});await p.ConnectDeviceAsync(0);
            Check(p.robot.Connects==0 && p.robot.dashboardPort==29999,"out-of-range JSON dashboard port rejected: "+port);
        }
        p=Fresh();File.Delete(p.RobotConfigurationPath);await p.ConnectDeviceAsync(0);
        Check(p.robot.Connects==0 && p.notes[0].Contains("保存参数"),"missing file requires an explicit saved configuration");
        p=Fresh();File.WriteAllText(p.RobotConfigurationPath,"{\"robotIp\":\"192.168.192.19\",\"dashboardPort\":29999,\"feedbackPort\":30004,\"pythonExecutable\":\"/not/installed/python\",\"forceLimitN\":999}");await p.ConnectDeviceAsync(0);
        Check(p.robot.Connects==1,"connection validates only robot endpoints, not unrelated algorithm/runtime settings");
        p=Fresh();await p.ConnectDeviceAsync(1);
        Check(p.chassis.Connects==1 && p.robot.Connects==0 && p.lift.Connects==0,"chassis can connect independently");
        Check(p.robot.PowerRequests+p.robot.EnableRequests+p.chassis.Relocations==0,"connection never powers/enables/relocates");
        p=Fresh();p.robot.ViewHasConnection=true;SaveRobot(new FieldCommissioningSettings {robotIp="192.168.192.55"});await p.ConnectDeviceAsync(0);
        Check(p.robot.Connects==0 && p.robot.robotIP=="192.168.192.19" && p.notes[0].Contains("先明确断开"),"cannot silently replace a live address");
        p=Fresh();p.robot.ViewHasConnection=true;p.robot.ViewEndpointsAligned=false;await p.ConnectDeviceAsync(0);
        Check(p.robot.Connects==0,"actual socket/config mismatch not silently repaired by target switch");
        p=Fresh();p.robot.feedbackPort=30004;p.robot.ViewHasConnection=true;await p.ConnectDeviceAsync(0);
        Check(p.robot.Connects==1 && p.robot.Disconnects==0,"same endpoint uses existing reconnect/feedback-repair API");
        p=Fresh();p.robot.ViewHasConnection=true;await p.ConnectDeviceAsync(0);
        Check(p.robot.Connects==0 && p.robot.feedbackPort==30005,"live old feedback port is not silently switched even when IP is unchanged");
        p.DisconnectDevice(0);await p.ConnectDeviceAsync(0);
        Check(p.robot.Connects==1 && p.robot.feedbackPort==30004,"explicit disconnect followed by connect applies JSON feedback port");
        p=Fresh();p.robot.Pending=new();var pending=p.ConnectDeviceAsync(0);await p.ConnectDeviceAsync(0);
        Check(p.robot.Connects==1 && p.connecting[0],"pending connection not duplicated");
        Check(p.IsConnectionOperationInProgress,"formal start can observe pending connections");
        p.prepareConfirmed=p.relocationConfirmed=true;p.Update();
        Check(!p.prepareConfirmed && !p.relocationConfirmed,"pending connect clears stale preparation permission");
        p.DisconnectDevice(0);await pending;
        Check(p.robot.Disconnects==1 && !p.connecting[0] && p.notes[0].Contains("断开/取消"),"cancel does not get overwritten by late success/error note");
        p=Fresh();p.robot.Pending=new();pending=p.ConnectDeviceAsync(0);
        p.robot.robotIP="192.168.192.77";p.robot.Pending.SetResult(true);await pending;
        Check(p.robot.robotIP=="192.168.192.77" && p.notes[0].Contains("参数发生变化"),"concurrent test config change is retained and reported");
        p=Fresh();p.robot.FailConnect=true;await p.ConnectAllAsync();
        Check(p.robot.Connects==1 && p.chassis.Connects==1 && p.lift.Connects==1 && p.notes[0].Contains("fake offline"),"one-device failure does not suppress other attempts");
        Check(p.message.Contains("逐行") && !p.message.Contains("全部成功"),"batch completion not reported as all connected");
        p=Fresh();p.robot.FailDisconnect=true;p.DisconnectAll();
        Check(p.chassis.Disconnects==1 && p.lift.Disconnects==1 && p.notes[0].Contains("未完成"),"disconnect error does not abandon other idle devices");
        foreach(string owner in new[]{"field","task","robot","external","base","manual","motion","servo"})
        {
            p=Fresh();FieldCommissioningPanel.Active=new FieldCommissioningPanel {MotionLocked=owner=="field"};
            p.simulation.ConnectCommander.AnyTaskRunning=owner=="task";p.robot.FieldWorkflowActive=owner=="robot";p.robot.ExternalControlActive=owner=="external";
            p.chassis.FieldWorkflowOwned=owner=="base";p.chassis.ManualJogLocked=owner=="manual";p.chassis.ConfirmedMotionActive=owner=="motion";p.servo.FieldWorkflowOwned=owner=="servo";
            p.drafts[1]="192.168.192.56";await p.ConnectDeviceAsync(1);
            Check(p.chassis.Connects==0 && p.chassis.robotIP=="192.168.192.5","ownership blocks address/connection change: "+owner);
            p.DisconnectDevice(1);
            Check(p.chassis.Disconnects==0 && p.lift.Disconnects==0,"ownership preserves device transports: "+owner);
            if(owner=="field") Check(FieldCommissioningPanel.Active.Stops==1,"field disconnect routes to existing whole-flow stop");
            if(owner=="manual") Check(p.chassis.Stops==1,"manual disconnect routes to manual stop without clearing lock");
        }
        FieldCommissioningPanel.Active=null;
        p=Fresh();p.prepareConfirmed=p.relocationConfirmed=true;p.simulation.ConnectCommander.AnyTaskRunning=true;p.Update();
        Check(!p.prepareConfirmed && !p.relocationConfirmed,"workflow occupancy cannot preserve old preparation permission");
        p=Fresh();p.bindingConflict[0]=true;await p.ConnectDeviceAsync(0);p.DisconnectDevice(0);
        Check(p.robot.Connects+p.robot.Disconnects==0,"conflicting scene bindings fail closed");
        p=Fresh();p.robot.ViewControlReady=true;p.PrepareRobot(false);Check(p.robot.PowerRequests==0,"power requires explicit permission");
        p.prepareConfirmed=true;p.PrepareRobot(false);
        Check(p.robot.PowerRequests==1 && p.robot.EnableRequests==0 && !p.prepareConfirmed,"power request separate from enable and consumes permission");
        p.prepareConfirmed=true;p.PrepareRobot(true);Check(p.robot.EnableRequests==1,"enable uses original explicit API");
        p=Fresh();p.chassis.ViewAllPortsConnected=true;p.RelocateChassis();Check(p.chassis.Relocations==0,"relocation requires separate confirmation");
        p.relocationConfirmed=true;p.RelocateChassis();
        Check(p.chassis.Relocations==1 && !p.relocationConfirmed && p.message.Contains("不等于定位成功"),"relocation explicit and not claimed completed");
        p=Fresh();p.lift=null;await p.ConnectDeviceAsync(2);p.DisconnectDevice(2);p.InitializeToolbar();p.visible=true;p.OnGUI();
        Check(p.Description(2)=="未绑定设备" && GUI.enabled,"missing device renders and does not block other controls");
        Console.WriteLine(tests+" DEVICE_CONNECTION_UI checks PASS (production panel, fake devices/GUI; no hardware/Unity rendering)");
    }
}
class Program
{
    static async Task Main()
    {
        Application.persistentDataPath=Path.Combine(Path.GetTempPath(),"robot-json-ui-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Application.persistentDataPath);
        try { await DeviceConnectionPanel.RunTests(); }
        finally { Directory.Delete(Application.persistentDataPath,true); }
    }
}
