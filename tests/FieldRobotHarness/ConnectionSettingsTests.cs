using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

public partial class DobotController
{
    public static async Task<int> TestConnectionSettings()
    {
        int count=0;
        void Check(bool value,string label) { if(!value) throw new Exception(label);count++;Console.WriteLine("PASS robot JSON "+label); }
        void Reject(Action action,string label)
        {
            try { action(); } catch(InvalidOperationException) { Check(true,label);return; }
            throw new Exception("Accepted "+label);
        }
        using var r=new DobotController();
        var s=new FieldCommissioningSettings {robotIp="127.0.0.1",dashboardPort=31001,feedbackPort=31002};
        r.ConfigureFieldConnection(s);
        Check(r.robotIP==s.robotIp && r.dashboardPort==31001 && r.feedbackPort==31002,"all endpoints follow configuration, not scene defaults");
        Check(!r.FieldConnectedTo(s),"copying settings does not fabricate a connected socket");
        r.ViewConnecting=true;
        Reject(()=>r.ConfigureFieldConnection(new FieldCommissioningSettings()),"pending connect cannot change targets");
        r.ViewConnecting=false;
        r.FieldWorkflowActive=true;
        Reject(()=>r.ConfigureFieldConnection(s),"field workflow owns connection settings");
        r.FieldWorkflowActive=false;
        r.ownerToken="fake lease";
        Reject(()=>r.ConfigureFieldConnection(s),"algorithm lease owns connection settings");
        r.ownerToken=null;
        var control=new TcpListener(IPAddress.Loopback,0);
        var feedback=new TcpListener(IPAddress.Loopback,0);
        control.Start(); feedback.Start();
        try
        {
            s.dashboardPort=((IPEndPoint)control.LocalEndpoint).Port;
            s.feedbackPort=((IPEndPoint)feedback.LocalEndpoint).Port;
            r.tcpClient=new TcpClient();
            await r.tcpClient.ConnectAsync(IPAddress.Loopback,s.dashboardPort);
            using var controlPeer=await control.AcceptTcpClientAsync();
            r.ConfigureFieldConnection(s);
            Check(!r.FieldConnectedTo(s),"control-only partial connection is not full connection");
            r.feedbackClient=new TcpClient();
            await r.feedbackClient.ConnectAsync(IPAddress.Loopback,s.feedbackPort);
            using var feedbackPeer=await feedback.AcceptTcpClientAsync();
            r.ConfigureFieldConnection(s);
            Check(r.FieldConnectedTo(s),"matching real loopback endpoints can be reused");
            var other=s.Copy(); other.feedbackPort=s.feedbackPort==65535 ? 65534 : s.feedbackPort+1;
            Reject(()=>r.ConfigureFieldConnection(other),"same IP with different feedback port requires explicit disconnect");
            Check(r.feedbackPort==s.feedbackPort && r.feedbackClient.Connected,"rejected change preserves previous feedback channel and fields");
            Check(!r.FieldConnectedTo(other),"field start checks actual feedback port, not assigned port alone");
            string description=r.DescribeFieldConnection(other);
            Check(description.Contains("JSON期望") && description.Contains("实际控制") && description.Contains(other.feedbackPort.ToString()) && description.Contains(s.feedbackPort.ToString()),"mismatch diagnostics include expected and actual endpoints");
            other=s.Copy();other.dashboardPort=s.dashboardPort==65535 ? 65534 : s.dashboardPort+1;
            Reject(()=>r.ConfigureFieldConnection(other),"different dashboard port also requires explicit disconnect");
            r.tcpClient.Close();r.feedbackClient.Close();
            r.ConfigureFieldConnection(other);
            Check(r.dashboardPort==other.dashboardPort && r.feedbackPort==other.feedbackPort,"after explicit close, next configuration can be applied");
        }
        finally { r.feedbackClient?.Close();control.Stop();feedback.Stop(); }
        return count;
    }
}
