using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

static class Program
{
    static int count;
    static readonly string Root=Path.Combine(Path.GetTempPath(),"run-data-"+Guid.NewGuid().ToString("N"));
    static string Dir(string name) => Path.Combine(Root,name);
    static void Check(bool value,string message) { if(!value) throw new Exception("FAIL "+message); count++; Console.WriteLine("PASS "+message); }
    static RunDataSession New(string name,bool simulated=false,int capacity=4096,long bytes=20*1024*1024)
        => new(Dir(name),new RunDataSession.Info {runId=name,projectId="项目,甲",source="test,not hardware",simulated=simulated,version="test",configuration="中文\n\"配置\"\\snapshot"},capacity,bytes);
    static JsonElement Meta(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir(name),"metadata.json"))).RootElement.Clone();
    static JsonElement[] Events(string name) => Directory.GetFiles(Dir(name),"commands*.jsonl").OrderBy(x=>x)
        .SelectMany(File.ReadAllLines).Select(x=>JsonDocument.Parse(x).RootElement.Clone()).OrderBy(x=>x.GetProperty("event_id").GetInt64()).ToArray();
    static string[] Csv(string line)
    {
        var values=new System.Collections.Generic.List<string>(); var cell=new StringBuilder(); bool quoted=false;
        for(int i=0;i<line.Length;i++)
            if(line[i]=='"') { if(quoted && i+1<line.Length && line[i+1]=='"') {cell.Append('"');i++;} else quoted=!quoted; }
            else if(line[i]==',' && !quoted) {values.Add(cell.ToString());cell.Clear();} else cell.Append(line[i]);
        values.Add(cell.ToString()); return values.ToArray();
    }
    static async Task Main()
    {
        Directory.CreateDirectory(Root);
        var s=New("one"); s.Context(3,"粗定位,阶段\"一\"");
        using(var c=s.BeginCommand("robot","127.0.0.1:1",7,"MovJ(joint={1,2,3,4,5,6})","deg"))
        { c.Written(); c.Reply("0,{42},MovJ(...);"); c.Accepted("42"); }
        using(var c=s.BeginCommand("chassis","127.0.0.1:2",8,"API=3056 {\"angle\":0.1}","rad"))
        { c.Prepared("9","5A-01"); c.Written(); c.Reply("{\"ret_code\":-1}"); c.Failed(new IOException("拒绝\n原因")); }
        double[] zero=new double[6];
        for(int i=0;i<50;i++) s.Robot("robot",7,1,100,.01,true,5,0,1,zero,zero,zero);
        s.Lift("lift",2,3,0,0,0,double.NaN,double.NaN,double.NaN,false);
        await s.Finish("test ended, not motion completed");
        var meta=Meta("one"); var events=Events("one");
        Check(s.Status=="保存完成" && meta.GetProperty("completed").GetBoolean(),"writer drains before completed metadata");
        Check(meta.GetProperty("configuration").GetString().Contains("\n\"配置\""),"UTF8 configuration snapshot and JSON escaping");
        Check(meta.GetProperty("robotRows").GetInt64()==1,"robot feedback is sampled, not one disk write per frame");
        Check(meta.GetProperty("pendingAtClose").GetInt64()==0 && meta.GetProperty("droppedRows").GetInt64()==0,"normal completion has no pending/lost evidence");
        Check(events.All(e=>e.GetProperty("run_id").GetString()=="one" && e.GetProperty("target").GetInt32()==3),"all command phases retain run/target context");
        Check(events.Where(e=>e.GetProperty("device").GetString()=="robot").Select(e=>e.GetProperty("kind").GetString())
            .SequenceEqual(new[]{"send_attempt","write_completed","reply_received","accepted"}),"attempt/write/reply/acceptance are distinct, not arrival");
        Check(events.Single(e=>e.GetProperty("kind").GetString()=="accepted").GetProperty("native_sequence").GetString()=="42","controller command ID preserved");
        Check(events.Any(e=>e.GetProperty("kind").GetString()=="failed" && e.GetProperty("detail").GetString().Contains("拒绝\n")),"raw reply and exception preserved without multiline JSON corruption");
        Check(events.Where(e=>e.GetProperty("device").GetString()=="chassis").All(e=>e.GetProperty("units").GetString()=="rad"),"chassis units not mislabeled as joint degrees");
        string[] robot=File.ReadAllLines(Path.Combine(Dir("one"),"robot-feedback.csv"));
        Check(Csv(robot[0]).Length==Csv(robot[1]).Length && Csv(robot[1])[4]=="粗定位,阶段\"一\"","CSV header alignment and quote/comma escaping");
        string[] lift=File.ReadAllLines(Path.Combine(Dir("one"),"lift-feedback.csv")); var lr=Csv(lift[1]);
        Check(lr[10]=="False" && lr[12]=="" && lr[13]=="" && lr[14]=="","failed lift feedback stays empty, not fake zero");
        Check(meta.GetProperty("liftRows").GetInt64()==1,"lift sample count indexed without loading CSV");
        Check(events.Zip(events.Skip(1),(a,b)=>a.GetProperty("elapsed_s").GetDouble()<=b.GetProperty("elapsed_s").GetDouble()).All(x=>x),"monotonic session timestamps");

        var algorithm=New("algorithm-result"); algorithm.Context(2,"接触/EXITING");
        algorithm.Evidence("contact-20261023","{\"result\":\"completed\",\"moved_mm\":0.45}");
        await algorithm.Finish("done");
        var evidence=Events("algorithm-result").Single(e=>e.GetProperty("kind").GetString()=="algorithm_result");
        Check(evidence.GetProperty("detail").GetString().Contains("moved_mm") && evidence.GetProperty("target").GetInt32()==2,"algorithm result evidence keeps real measurements and target without becoming a device command");

        var live=New("live");
        using(var c=live.BeginCommand("robot","r",1,"live target","deg")) {c.Written(); c.Accepted();}
        await Task.Delay(1250);
        Check(!Meta("live").GetProperty("completed").GetBoolean() && Events("live").Length==3,"periodic flush exposes data before run end without claiming completed");
        await live.Finish("done");
        var description=new RunDataSession.Info {runId="immutable",projectId="before",configuration="snapshot"};
        var immutable=new RunDataSession(Dir("immutable"),description); description.projectId="after"; description.configuration="changed";
        await immutable.Finish("done");
        Check(Meta("immutable").GetProperty("projectId").GetString()=="before" && Meta("immutable").GetProperty("configuration").GetString()=="snapshot","session metadata does not follow later parameter mutation");

        var old=New("old"); RunDataCapture.Begin(old); var pending=RunDataCapture.Command("robot","old",1,"old target","deg");
        var replacement=New("new"); RunDataCapture.Begin(replacement);
        pending.Reply("late old reply"); pending.Dispose();
        using(var c=RunDataCapture.Command("lift","new",2,"new height","mm")) { c.Written(); c.Accepted(); }
        await RunDataCapture.Finish("new","finished"); await old.Completion;
        Check(Events("new").All(e=>!e.GetProperty("payload").GetString().Contains("old")),"late callback cannot contaminate a new run");
        Check(Meta("old").GetProperty("pendingAtClose").GetInt64()==1 && old.Status=="数据不完整","unfinished command at close permanently marks incomplete");
        await RunDataCapture.Finish("old","stale finish"); Check(RunDataCapture.Current==null,"stale finish never changes replacement session");

        var sampled=New("samples");
        sampled.Robot("r",1,9,1,.02,true,5,0,1,zero,zero,zero); await Task.Delay(120);
        sampled.Robot("r",1,9,1,.14,true,5,0,1,zero,zero,zero); await Task.Delay(120);
        sampled.Robot("r",2,1,2,1,false,5,0,1,zero,zero,zero);
        sampled.Lift("l",1,1,0,2,3,.01,.01,.01,true);
        await sampled.Finish("done");
        var samples=File.ReadAllLines(Path.Combine(Dir("samples"),"robot-feedback.csv")).Skip(1).Select(Csv).ToArray();
        Check(samples.Length==3 && samples[0][12]=="True" && samples[1][12]=="False","repeated feedback sequence explicitly distinguished");
        Check(samples[2][10]=="False","stale feedback marked invalid even with plausible cached angles");
        var validLift=Csv(File.ReadAllLines(Path.Combine(Dir("samples"),"lift-feedback.csv"))[1]);
        Check(validLift[12]=="0" && validLift[15]=="True","measured valid lift zero remains zero");

        var sim=New("simulation",true); Check(sim.BeginCommand("robot","r",1,"MovJ","deg")==null,"simulated session rejects real transport attachment");
        sim.Robot("r",1,1,1,0,true,5,0,0,zero,zero,zero); await sim.Finish("numerical simulation");
        Check(Meta("simulation").GetProperty("simulated").GetBoolean() && Meta("simulation").GetProperty("robotRows").GetInt64()==0,"simulation never fabricates real feedback");

        var rolled=New("rollover",bytes:1024);
        for(int i=0;i<40;i++) using(var c=rolled.BeginCommand("robot","r",1,"target "+i,"deg")) c.Accepted();
        await rolled.Finish("done");
        Check(Directory.GetFiles(Dir("rollover"),"commands*.jsonl").Length>1 && Events("rollover").Length==81,"file rotation preserves every command record");
        var csvParts=New("csv-parts",bytes:1024); csvParts.Context(1,new string('x',1200));
        for(int i=0;i<4;i++) {csvParts.Robot("r",1,i,(ulong)i,.01,true,5,0,1,zero,zero,zero); await Task.Delay(110);}
        await csvParts.Finish("done");
        var csvFiles=Directory.GetFiles(Dir("csv-parts"),"robot-feedback*.csv");
        Check(csvFiles.Length==4 && csvFiles.Select(f=>File.ReadLines(f).First()).Distinct().Count()==1,"every CSV volume includes a matching header");
        var burst=New("burst",capacity:1);
        for(int i=0;i<5000;i++) using(var c=burst.BeginCommand("robot","r",1,"burst "+i,"deg")) c.Written();
        await burst.Finish("burst");
        Check(Meta("burst").GetProperty("droppedRows").GetInt64()>0 && burst.Status=="数据不完整","bounded queue overflow is visible, never silent success");
        var huge=New("huge"); using(var c=huge.BeginCommand("r","r",1,new string('x',3*1024*1024),"deg")) c.Accepted(); await huge.Finish("done");
        Check(Meta("huge").GetProperty("droppedRows").GetInt64()>0,"text-size bound protects memory as well as row-count bound");

        File.WriteAllText(Dir("blocked"),"do not overwrite"); var failed=New("blocked"); await failed.Completion; await failed.Finish("failed");
        Check(failed.Error.Length>0 && failed.Status.Contains("保存失败"),"disk failure cannot throw into control caller");
        Check(File.ReadAllText(Dir("blocked"))=="do not overwrite","failed directory initialization preserves existing file");
        int warnings=0; RunDataCapture.Begin(failed); RunDataCapture.ReportSaveError=_=>warnings++;
        RunDataCapture.CheckSaveError(); RunDataCapture.CheckSaveError(); await RunDataCapture.Finish("blocked","closed");
        Check(warnings==1,"save failure reports once without blocking control/Stop cleanup");
        var collision=New("one"); await collision.Completion; await collision.Finish("collision");
        Check(collision.Error.Length>0 && Meta("one").GetProperty("runId").GetString()=="one","existing session files are never overwritten");

        File.WriteAllText(Path.Combine(Dir("one"),"private.txt"),"must not export");
        string export=await RunDataFiles.ExportAsync(Dir("one"),Dir("exports"));
        using(var zip=ZipFile.OpenRead(export))
        {
            Check(zip.Entries.Any(e=>e.Name=="metadata.json") && zip.Entries.Any(e=>e.Name=="robot-feedback.csv"),"ZIP includes manifest and feedback data");
            Check(!zip.Entries.Any(e=>e.Name=="private.txt"),"export excludes unrelated project/private files");
        }
        string export2=await RunDataFiles.ExportAsync(Dir("one"),Dir("exports"));
        Check(export!=export2 && !Directory.GetFiles(Dir("exports"),"*.partial").Any(),"repeat export is unique and atomically published");
        var unicode=New("中文 路径"); unicode.Context(1,"中文阶段"); await unicode.Finish("done");
        Check(File.Exists(await RunDataFiles.ExportAsync(Dir("中文 路径"),Dir("导出 文件"))),"Unicode and space paths work for storage and ZIP export");
        Check(!RunDataFiles.IsDataFile("report.json") && !RunDataFiles.IsDataFile("commands.jsonl.tmp") &&
            !RunDataFiles.IsDataFile("commands_private.jsonl") && RunDataFiles.IsDataFile("commands_0002.jsonl"),"temporary/private-prefix files excluded, numbered parts included");
        var culture=CultureInfo.CurrentCulture; CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
        try {
            var locale=New("locale"); using(var c=locale.BeginCommand("r","r",1,"x=1.5","mm")) c.Written(); await locale.Finish("done");
            Check(Events("locale").All(e=>e.GetProperty("elapsed_s").ValueKind==JsonValueKind.Number),"timestamps remain machine-readable under comma decimal locale");
        } finally {CultureInfo.CurrentCulture=culture;}
        Console.WriteLine($"RUN_DATA_TESTS_PASS {count}; no network/devices; isolated temp: {Root}");
    }
}
