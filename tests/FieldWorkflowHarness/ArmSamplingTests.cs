using System;
using System.Collections.Generic;
using System.Linq;

static class ArmSamplingTests
{
    static int passed;
    static void Check(bool value, string name)
    { if (!value) throw new Exception(name); Console.WriteLine("PASS sampling " + name); passed++; }
    static string Reject(Action action, string name)
    {
        try { action(); }
        catch (ArgumentException e) { Check(true,name); return e.Message; }
        catch (InvalidOperationException e) { Check(true,name); return e.Message; }
        throw new Exception("Accepted: " + name);
    }
    static FieldCommissioningSettings Settings(float step=5) => new() {
        jointMinDeg=Enumerable.Repeat(-360f,6).ToArray(), jointMaxDeg=Enumerable.Repeat(360f,6).ToArray(), maxJointStepDeg=step };
    static double[] Degrees(double j1, double j2=0, double lift=0) => new[] {j1*Math.PI/180,j2*Math.PI/180,0,0,0,0,lift};
    static TrajectoryTask Coarse(double[] start, params double[][] points) => new() {
        taskName="目标1:抓取", type=TaskType.Arm, stage=TrajectoryStage.CoarseArm, targetIndex=0,
        armStartPose=start, armPoints=points.ToList() };
    static bool Bounded(IReadOnlyList<double[]> points, double[] start, double step, int offset=0)
    {
        var previous=start;
        foreach(var point in points) {
            for(int j=0;j<6;j++) if(Math.Abs(point[offset+j]-previous[offset+j])*180/Math.PI>step+1e-7) return false;
            previous=point;
        }
        return true;
    }
    public static int Run()
    {
        // Exact rounded radian values captured in the local Editor log, no device feedback fabricated.
        var start=new[]{0.0,Math.PI/4,-Math.PI/2,-Math.PI/2,-Math.PI/2,0,0};
        var goal=new[]{.8516,-.5759,.6046,-.0303,-4.4395,-.7255,0};
        var c=new ConnectCommander(); var raw=Coarse(start,start,goal); c.taskPlaylist.Add(raw);
        var plan=c.SnapshotSemiAutomaticPlan(Settings(),false);
        var sampled=plan[0][0]; var returning=plan[0][1];
        Check(sampled.armPoints.Count==34 && sampled.armSampling.OriginalCount==2,"logged sparse two-point edge expands to 34 points at 5 degrees");
        Check(sampled.armSampling.MaximumSourceAxis==5 && sampled.armSampling.MaximumSourceDeltaDeg>164,"maximum raw delta remains diagnostic, not rejection");
        Check(Bounded(sampled.armPoints,start,5) && Bounded(returning.armPoints,goal,5),"coarse and generated reverse path respect sample step");
        Check(sampled.armPoints.Last().SequenceEqual(goal) && returning.armPoints.Last().SequenceEqual(start),"exact goal and reset endpoints retained");
        Check(raw.armPoints.Count==2 && raw.armPoints[1].SequenceEqual(goal),"source planner cache not rewritten");
        Check(!ReferenceEquals(sampled.armPoints.Last(),goal),"sampled points deep copied");
        plan=c.SnapshotSemiAutomaticPlan(Settings(10),false);
        Check(plan[0][0].armPoints.Count==18,"10-degree configuration adapts density rather than rejecting total angle");

        var timing=new ArmTrajectoryTiming(5,.5,10,.2,0);
        var sampling=sampled.armSampling;
        var intervals=sampling.SendIntervals(timing);
        double edgeBudget=timing.DelaySeconds(ArmTrajectorySampling.JointsDeg(start,0),ArmTrajectorySampling.JointsDeg(goal,0));
        Check(Math.Abs(intervals.Skip(1).Sum()-edgeBudget)<1e-9,"original edge buffer and angle budget distributed once");
        Check(intervals.Skip(1).All(x=>Math.Abs(x-edgeBudget/33)<1e-9),"new points do not multiply fixed buffers");
        var capped=sampling.SendIntervals(new ArmTrajectoryTiming(5,.5,10,.2,1));
        Check(Math.Abs(capped.Skip(1).Sum()-1)<1e-9,"maximum wait clamp applied once to original edge");
        var report=sampling.CaptureReport(timing);
        Check(report.originalCount==2 && report.sampledCount==34 && report.original[1].jointsDeg[4]<-254,"report retains original and adapted six-axis degrees");
        Check(report.sampled.Last().sendIntervalSeconds==0 && report.sampled.Last().sourcePoint==2,"report final point has no extra send wait");
        string json=UnityEngine.JsonUtility.ToJson(report);
        Check(json.Contains("sourcePoint") && json.Contains("jointsDeg") && json.Contains("sampledCount"),"diagnostic report has serializable point provenance");
        goal[0]=99;
        Check(sampling.CaptureReport(timing).original[1].jointsDeg[0]<49,"raw report frozen against later source edits");
        goal[0]=.8516;

        var corner=Degrees(20,10,.02); var finish=Degrees(-20,15,.04);
        var turns=ArmTrajectorySampling.Build(new[]{corner,finish},Degrees(0),0,5,"转折测试");
        Check(turns.Points.Count==12 && turns.Points[3].SequenceEqual(corner),"original turn retained, not shortcut across corner");
        Check(turns.Points.Last().SequenceEqual(finish) && Bounded(turns.Points,Degrees(0),5),"multi-edge geometry and endpoint retained");
        Check(Math.Abs(turns.Points[0][6]-.005)<1e-12,"seventh slide axis interpolated in metres without degree conversion");
        var dense=ArmTrajectorySampling.Build(new[]{Degrees(1),Degrees(2)},Degrees(0),0,5,"dense");
        Check(dense.Points.Count==2 && dense.SendIntervals(timing).All(v=>Math.Abs(v-.7)<1e-9),"already dense paths retain original pacing and point count");
        var liftOnly=ArmTrajectorySampling.Build(new[]{Degrees(0,0,100)},Degrees(0),0,5,"lift");
        Check(liftOnly.Points.Count==1,"non-arm control does not determine six-axis sampling count");
        var offsetStart=new double[8]; var offsetTarget=new double[8]; offsetTarget[2]=20*Math.PI/180;
        Check(ArmTrajectorySampling.Build(new[]{offsetTarget},offsetStart,2,5,"offset").Points.Count==4,"configured arm axis offset respected");
        var longWay=ArmTrajectorySampling.Build(new[]{Degrees(-179)},Degrees(179),0,5,"no-wrap");
        Check(longWay.Points.Count==72 && Math.Abs(longWay.MaximumSourceDeltaDeg-358)<1e-9,"no automatic angle wrapping or alternate IK branch");
        Check(ArmTrajectorySampling.Build(new[]{Degrees(.02)},Degrees(0),0,.01f,"minimum").Points.Count>=2,"float 0.01 configuration accepted without precision trap");

        c.taskPlaylist.Clear(); c.taskPlaylist.Add(Coarse(start,start,goal));
        var explicitReturn=new TrajectoryTask { type=TaskType.Arm, stage=TrajectoryStage.ReturnArm, targetIndex=0,
            armStartPose=(double[])goal.Clone(),armPoints=new() };
        for(int i=1;i<=10;i++) {
            double t=i/10.0, ease=t*t*(3-2*t);
            explicitReturn.armPoints.Add(Enumerable.Range(0,7).Select(j=>goal[j]+(start[j]-goal[j])*ease).ToArray());
        }
        c.taskPlaylist.Add(explicitReturn); plan=c.SnapshotSemiAutomaticPlan(Settings(),false);
        Check(plan[0][1].armPoints.Count>10 && Bounded(plan[0][1].armPoints,goal,5),"fixed ten-point reset also gets adaptive subdivision");
        explicitReturn.armStartPose[0]+=.001;
        plan=c.SnapshotSemiAutomaticPlan(Settings(),false);
        Check(plan[0][1].armStartPose.SequenceEqual(goal) && Bounded(plan[0][1].armPoints,goal,5),
            "return start within existing tolerance normalized before sampling, not afterwards");
        explicitReturn.armStartPose[0]+=.1;
        Check(Reject(()=>c.SnapshotSemiAutomaticPlan(Settings(),false),"wrong declared return start rejected before interpolation").Contains("声明起点"),"return mismatch reason retained");
        explicitReturn.armStartPose=null;
        Check(Reject(()=>c.SnapshotSemiAutomaticPlan(Settings(),false),"undeclared return cannot silently bridge a gap").Contains("拒绝自动补接"),"missing return start diagnosis explicit");
        c.taskPlaylist.Clear(); c.taskPlaylist.Add(Coarse(Degrees(0),Degrees(20)));
        c.taskPlaylist.Add(new TrajectoryTask {type=TaskType.Arm,stage=TrajectoryStage.ReturnArm,targetIndex=0,
            armStartPose=Degrees(20),armPoints=new(){Degrees(1)}});
        Check(Reject(()=>c.SnapshotSemiAutomaticPlan(Settings(),false),"return must still reach original start").Contains("J1"),"reset endpoint diagnosis includes axis");
        c.taskPlaylist.Clear(); c.taskPlaylist.Add(Coarse(Degrees(0),Degrees(361)));
        Check(Reject(()=>c.SnapshotSemiAutomaticPlan(Settings(),false),"absolute joint limits not bypassed").Contains("目标1/CoarseArm/采样点"),"limit diagnostic includes target stage and point");
        c.taskPlaylist.Clear(); c.taskPlaylist.Add(Coarse(Degrees(0),null,Degrees(1)));
        Check(Reject(()=>c.SnapshotSemiAutomaticPlan(Settings(),false),"null original coarse point rejected before reverse cloning").Contains("原点1"),"bad original point retains source point diagnosis");

        Reject(()=>ArmTrajectorySampling.Build(new[]{Degrees(1)},null,0,5,"null"),"null start rejected");
        Reject(()=>ArmTrajectorySampling.Build(new double[][] {null},Degrees(0),0,5,"null"),"null point rejected");
        Reject(()=>ArmTrajectorySampling.Build(new[]{new double[6]},Degrees(0),0,5,"dim"),"dimension change rejected");
        Reject(()=>ArmTrajectorySampling.Build(new[]{Degrees(1)},Degrees(0),-1,5,"offset"),"negative arm offset rejected");
        Reject(()=>ArmTrajectorySampling.Build(new[]{Degrees(1)},Degrees(0),2,5,"offset"),"out of range arm offset rejected");
        foreach(double value in new[]{double.NaN,double.PositiveInfinity}) {
            var bad=Degrees(1); bad[6]=value;
            Reject(()=>ArmTrajectorySampling.Build(new[]{bad},Degrees(0),0,5,"finite"),"nonfinite extra control rejected "+value);
        }
        foreach(double value in new[]{double.NaN,double.PositiveInfinity,0,100})
            Reject(()=>ArmTrajectorySampling.Build(new[]{Degrees(1)},Degrees(0),0,value,"step"),"invalid step rejected "+value);
        Reject(()=>ArmTrajectorySampling.Build(new[]{Degrees(1000000)},Degrees(0),0,.01f,"cap"),"point explosion rejected before allocation");
        string detail=Reject(()=>ArmTrajectorySampling.CheckStep(new double[6],new[]{6.0,0,0,0,0,0},5,"目标2/CoarseArm",3),"executor still rejects unsampled jumps");
        Check(detail.Contains("目标2/CoarseArm") && detail.Contains("点3→4") && detail.Contains("J1") && detail.Contains("6.000000"),"step diagnostic contains target point axis and actual difference");
        ArmTrajectorySampling.CheckStep(new double[6],new[]{5.0+1e-10,0,0,0,0,0},5,"rounding",0);
        Check(true,"roundoff at exact step boundary tolerated");
        Reject(()=>ArmTrajectorySampling.CheckStep(new double[6],new double[6],double.NaN,"bad",0),"NaN cannot disable step checking");
        Reject(()=>ArmTrajectorySampling.CheckStep(new double[6],new[]{double.NaN,0,0,0,0,0},5,"bad",0),"NaN sample cannot evade comparison");
        return passed;
    }
}
