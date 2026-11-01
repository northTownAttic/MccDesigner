using MccDesigner.Models;
using MccDesigner.Services;

var passed = 0;
void Check(string name, Action test) { test(); Console.WriteLine($"PASS {name}"); passed++; }
void Equal(double expected, double actual) { if (!double.IsFinite(actual) || Math.Abs(expected - actual) > 1e-8) throw new Exception($"Expected {expected}, got {actual}"); }
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
OperationDefinition Work(string resource = MachineAxes.Stage1, double duration = 2) => new() { Resource = resource, DurationSeconds = duration };
OperationDefinition Move(string resource = MachineAxes.Stage1, double duration = 2, double from = 0, double to = 100) => new() { Resource = resource, IsMotion = true, DurationSeconds = duration, StartPosition = from, EndPosition = to };
OperationDefinition Scan(string resource = MachineAxes.Stage1, double duration = 2) => new() { Resource = resource, RequiresScannerHead = true, DurationSeconds = duration };
var scheduler = new Scheduler();
Check("decimal sequential timing", () => { var r = scheduler.Calculate([Work(duration:3.5), Move(duration:2.5)]); Equal(3.5, r[1].StartTime); Equal(6, r[1].EndTime); });
Check("empty schedule", () => Equal(0, scheduler.Calculate([]).Count));
foreach (var invalid in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
    Check($"reject duration {invalid}", () => Reject(() => scheduler.Calculate([Work(duration:invalid)])));
Check("reject invalid position", () => Reject(() => scheduler.Calculate([Move(to:101)])));
Check("reject nonfinite position", () => Reject(() => scheduler.Calculate([Move(from:double.NaN)])));
Check("reject unknown resource", () => Reject(() => scheduler.Calculate([Work("Other")])));
Check("reject redundant head scan flag", () => Reject(() => scheduler.Calculate([Scan(MachineAxes.Head)])));
Check("reject duplicate IDs", () => { var op = Work(); Reject(() => scheduler.Calculate([op, op])); });
Check("reject null operation", () => Reject(() => scheduler.Calculate([null!])));
Check("reject time overflow", () => Reject(() => scheduler.Calculate([Work(duration:double.MaxValue), Work(duration:double.MaxValue)])));
Check("reject unrepresentable positive increment", () => Reject(() => scheduler.Calculate([Work(duration:1e20), Work(duration:1)])));
var schedule = scheduler.Calculate([Work(duration:1), Move(), Work(duration:3)]);
Check("all axes initially zero", () => { var s = MachineStateEvaluator.Evaluate(schedule,0); Equal(0,s.Stage1X); Equal(0,s.Stage2X); Equal(0,s.HeadY); });
Check("stage X midpoint interpolation", () => Equal(50,MachineStateEvaluator.Evaluate(schedule,2).Stage1X));
Check("stage X retains end pose", () => Equal(100,MachineStateEvaluator.Evaluate(schedule,3).Stage1X));
Check("stage X retains idle pose", () => Equal(100,MachineStateEvaluator.Evaluate(schedule,4).Stage1X));
Check("stage X retains completed pose", () => Equal(100,MachineStateEvaluator.Evaluate(schedule,10).Stage1X));
Check("head remains fixed during stage movement", () => Equal(0,MachineStateEvaluator.Evaluate(schedule,2).HeadY));
Check("head Y midpoint without stage movement", () => { var r=scheduler.Calculate([Move(MachineAxes.Head)]); var s=MachineStateEvaluator.Evaluate(r,1); Equal(50,s.HeadY); Equal(0,s.Stage1X); Equal(0,s.Stage2X); });
Check("head Y return", () => { var r=scheduler.Calculate([Move(MachineAxes.Head),Move(MachineAxes.Head,from:100,to:0)]); Equal(50,MachineStateEvaluator.Evaluate(r,3).HeadY); Equal(0,MachineStateEvaluator.Evaluate(r,4).HeadY); });
Check("parallel stage X axes", () => { var a=Move(); var b=Move(MachineAxes.Stage2,duration:4); b.WaitForPrevious=false; var r=scheduler.Calculate([a,b]); var s=MachineStateEvaluator.Evaluate(r,1); Equal(50,s.Stage1X); Equal(25,s.Stage2X); });
Check("same resource stays serialized", () => { var b=Work(); b.WaitForPrevious=false; var r=scheduler.Calculate([Work(duration:8),b]); Equal(8,r[1].StartTime); });
Check("scan reserves stage and head", () => { var r=scheduler.Calculate([Scan()]); Assert(r[0].RequiredResources.SequenceEqual(new[]{MachineAxes.Stage1,MachineAxes.Head})); });
Check("head transfer waits for scan", () => { var move=Move(MachineAxes.Head); move.WaitForPrevious=false; var r=scheduler.Calculate([Scan(duration:12),move]); Equal(12,r[1].StartTime); });
Check("scan waits for stage preparation", () => { var scan=Scan(); scan.WaitForPrevious=false; var r=scheduler.Calculate([Work(duration:8),scan]); Equal(8,r[1].StartTime); });
Check("stage return and Y transfer overlap", () => { var returning=Move(from:100,to:0); returning.WaitForPrevious=false; var r=scheduler.Calculate([Move(),Move(MachineAxes.Head),returning]); Equal(2,r[1].StartTime); Equal(2,r[2].StartTime); });
Check("independent work fills gap", () => { var fill=Work(MachineAxes.Stage2,3); fill.WaitForPrevious=false; var r=scheduler.Calculate([Work(duration:8),Work(MachineAxes.Stage2,2),fill]); Equal(0,r[2].StartTime); });
Check("both-resource availability", () => { var scan=Scan(duration:7); scan.WaitForPrevious=false; var r=scheduler.Calculate([Work(MachineAxes.Stage2,10),Scan(duration:2),scan]); Equal(10,r[1].StartTime); Equal(0,r[2].StartTime); });
Check("previous row is not global barrier", () => { var b=Work(MachineAxes.Stage2,3); b.WaitForPrevious=false; var r=scheduler.Calculate([Work(duration:20),b,Work(MachineAxes.Stage2)]); Equal(3,r[2].StartTime); });
Check("reject missing stage1 initial positioning", () => Reject(() => scheduler.Calculate([Move(from:20,to:80)])));
Check("reject stage position discontinuity", () => Reject(() => scheduler.Calculate([Move(),Move()])));
Check("reject head position discontinuity", () => Reject(() => scheduler.Calculate([Move(MachineAxes.Head),Move(MachineAxes.Head)])));
Check("reject stage2 scan without Y transfer", () => Reject(() => scheduler.Calculate([Scan(MachineAxes.Stage2)])));
Check("reject stage1 scan after moving head to stage2", () => Reject(() => scheduler.Calculate([Move(MachineAxes.Head),Scan()])));
Check("scan stage2 after Y transfer", () => { var r=scheduler.Calculate([Move(MachineAxes.Head),Scan(MachineAxes.Stage2)]); Equal(2,r[1].StartTime); Equal(100,MachineStateEvaluator.Evaluate(r,3).HeadY); });
Check("head between lanes cannot scan", () => Reject(() => scheduler.Calculate([Move(MachineAxes.Head,to:50),Scan(MachineAxes.Stage2)])));
Check("versioned JSON preserves axis model and flags", () => { var copy=ProjectSerializer.Deserialize(ProjectSerializer.Serialize(DemoProject.Create())); Assert(copy[3].RequiresScannerHead); Assert(copy[4].Resource==MachineAxes.Head); Assert(!copy[7].WaitForPrevious); Equal(39,scheduler.Calculate(copy).Max(x=>x.EndTime)); });
Check("legacy stationary stage JSON stays sequential", () => { var copy=ProjectSerializer.Deserialize("""[{"Name":"A","DurationSeconds":8},{"Name":"B","Resource":"Stage 2","DurationSeconds":4}]"""); Equal(8,scheduler.Calculate(copy)[1].StartTime); });
Check("legacy head-X JSON never reinterpreted as Y", () => Reject(() => ProjectSerializer.Deserialize("""[{"Resource":"Gantry / Head","IsMotion":true}]""")));
Check("reject unsupported project version", () => Reject(() => ProjectSerializer.Deserialize("""{"FormatVersion":99,"AxisModel":"FixedGantry_StageX_HeadY","Operations":[]}""")));
Check("reject missing versioned operations", () => Reject(() => ProjectSerializer.Deserialize("""{"FormatVersion":2,"AxisModel":"FixedGantry_StageX_HeadY"}""")));
Check("demo reservations, cycle and head fixed during both scans", () => {
    var r=scheduler.Calculate(DemoProject.Create()); Equal(39,r.Max(x=>x.EndTime));
    Equal(0,r[0].StartTime); Equal(0,r[1].StartTime); Equal(21,r[4].StartTime); Equal(23,r[5].StartTime); Equal(35.5,r[6].StartTime); Equal(21,r[7].StartTime); Equal(35.5,r[8].StartTime);
    var s1=MachineStateEvaluator.Evaluate(r,14.75); Equal(50,s1.Stage1X); Equal(0,s1.HeadY);
    var transfer=MachineStateEvaluator.Evaluate(r,22); Equal(50,transfer.HeadY);
    var s2=MachineStateEvaluator.Evaluate(r,29.25); Equal(50,s2.Stage2X); Equal(100,s2.HeadY);
    foreach(var resource in MachineAxes.Resources) {
        var intervals=r.Where(x=>x.RequiredResources.Contains(resource)).OrderBy(x=>x.StartTime).ToList();
        for(var i=1;i<intervals.Count;i++) Assert(intervals[i].StartTime>=intervals[i-1].EndTime);
    }
});
MachineSettings LargeMachine() => new()
{
    StageXMin = -500, StageXMax = 2000, Stage1InitialX = 50, Stage2InitialX = -100,
    HeadYMin = -100, HeadYMax = 600, Stage1Y = -50, Stage2Y = 450, HeadInitialY = -50,
    GantryXStart = 200, GantryXEnd = 600, GantryYStart = -120, GantryYEnd = 620,
    HeadX = 400, HeadWidth = 30, HeadHeight = 20
};
List<OperationDefinition> LargeCycle() =>
[
    new() { Resource = MachineAxes.Stage1, DurationSeconds = 4, IsMotion = true, RequiresScannerHead = true, StartPosition = 50, EndPosition = 1250 },
    Move(MachineAxes.Head, duration:2, from:-50, to:450),
    new() { Resource = MachineAxes.Stage2, DurationSeconds = 4, IsMotion = true, RequiresScannerHead = true, StartPosition = -100, EndPosition = 1500 }
];
Check("mm coordinates above 100 and negative initial values", () => {
    var m = LargeMachine(); var r = scheduler.Calculate(LargeCycle(), m);
    var initial = MachineStateEvaluator.Evaluate(r, 0, m);
    Equal(50, initial.Stage1X); Equal(-100, initial.Stage2X); Equal(-50, initial.HeadY);
    Equal(650, MachineStateEvaluator.Evaluate(r, 2, m).Stage1X);
    Equal(200, MachineStateEvaluator.Evaluate(r, 5, m).HeadY);
    Equal(700, MachineStateEvaluator.Evaluate(r, 8, m).Stage2X);
    Equal(450, MachineStateEvaluator.Evaluate(r, 8, m).HeadY);
});
Check("mm travel limits are enforced", () => Reject(() => scheduler.Calculate([Move(from:50,to:2001)], LargeMachine())));
Check("custom lane Y prevents premature scan", () => Reject(() => scheduler.Calculate([Scan(MachineAxes.Stage2)], LargeMachine())));
Check("empty scene retains configured initial pose", () => { var s=MachineStateEvaluator.Evaluate([],0,LargeMachine()); Equal(50,s.Stage1X); Equal(-100,s.Stage2X); Equal(-50,s.HeadY); });
Check("gantry width is 40 mm and head fixed X is 40 mm by default", () => { var m=new MachineSettings(); m.Validate(); Equal(40,m.GantryXEnd-m.GantryXStart); Equal(40,m.HeadX); });
Check("cancel editing copy preserves original", () => { var m=new MachineSettings(); var copy=m.Copy(); copy.HeadX=50; Equal(40,m.HeadX); });
Check("reject reversed gantry edges", () => { var m=new MachineSettings(); m.GantryXEnd=19; Reject(m.Validate); });
Check("reject zero head size", () => { var m=new MachineSettings(); m.HeadWidth=0; Reject(m.Validate); });
Check("reject head outside fixed gantry X", () => { var m=new MachineSettings(); m.HeadX=60; Reject(m.Validate); });
Check("reject head Y envelope outside gantry", () => { var m=new MachineSettings(); m.HeadYMax=1000; Reject(m.Validate); });
Check("reject coincident lane centers", () => { var m=new MachineSettings(); m.Stage2Y=0; Reject(m.Validate); });
Check("reject nonfinite or unrenderable dimensions", () => { var m=new MachineSettings(); m.GantryYEnd=double.PositiveInfinity; Reject(m.Validate); m.GantryYEnd=1e12; Reject(m.Validate); });
Check("version 3 saves mm machine geometry and operations", () => {
    var json = ProjectSerializer.Serialize(LargeCycle(), LargeMachine());
    var copy = ProjectSerializer.DeserializeProject(json);
    Assert(!copy.ImportedLegacyUnits); Equal(2000,copy.Machine.StageXMax); Equal(450,copy.Machine.Stage2Y);
    Equal(200,copy.Machine.GantryXStart); Equal(600,copy.Machine.GantryXEnd); Equal(30,copy.Machine.HeadWidth);
    Equal(1250,copy.Operations[0].EndPosition); Equal(10,scheduler.Calculate(copy.Operations,copy.Machine).Max(x=>x.EndTime));
    using var doc=System.Text.Json.JsonDocument.Parse(json);
    Equal(3,doc.RootElement.GetProperty("FormatVersion").GetInt32()); Assert(doc.RootElement.GetProperty("Units").GetString()=="mm");
});
Check("version 2 import preserves user coordinates and reports units", () => {
    var root=System.Text.Json.Nodes.JsonNode.Parse(ProjectSerializer.Serialize(DemoProject.Create()))!;
    root["FormatVersion"]=2; root.AsObject().Remove("Machine"); root.AsObject().Remove("Units");
    var copy=ProjectSerializer.DeserializeProject(root.ToJsonString());
    Assert(copy.ImportedLegacyUnits); Equal(100,copy.Operations[3].EndPosition); Equal(100,copy.Machine.Stage2Y);
});
Check("version 3 rejects missing geometry and incompatible units", () => {
    var root=System.Text.Json.Nodes.JsonNode.Parse(ProjectSerializer.Serialize([]))!;
    root["Units"]="cm"; Reject(()=>ProjectSerializer.DeserializeProject(root.ToJsonString()));
    root["Units"]="mm"; root.AsObject().Remove("Machine"); Reject(()=>ProjectSerializer.DeserializeProject(root.ToJsonString()));
});
Check("millimetre projection preserves equal X Y scale on resize", () => {
    foreach(var size in new[]{(400d,220d),(1000d,500d)}) {
        var p=new MachineProjection(new MachineSettings(),size.Item1,size.Item2);
        Equal(p.X(60)-p.X(20),p.Y(20)-p.Y(60));
        Assert(p.X(0)>=0 && p.X(100)<=size.Item1 && p.Y(115)>=0 && p.Y(-15)<=size.Item2);
    }
});
Check("large machine projection contains configured geometry", () => {
    var p=new MachineProjection(LargeMachine(),600,300);
    Assert(p.X(-500)>=0 && p.X(2000)<=600 && p.Y(620)>=0 && p.Y(-120)<=300);
    Equal((p.X(600)-p.X(200))/400,(p.Y(-50)-p.Y(450))/500);
});
var closedCycle = scheduler.Calculate(DemoProject.Create());
Check("single pass permits non-home ending", () => {
    var session=new PlaybackSession(); session.Start(scheduler.Calculate([Move()]),new(),new());
    session.Advance(2); Assert(session.State==PlaybackState.Completed); Equal(2,session.CycleTime); Equal(1,session.CompletedCycles);
});
Check("finite repeating cycle reports progress and final end", () => {
    var session=new PlaybackSession(); session.Start(closedCycle,new(),new(){RepeatCount=3});
    session.Advance(39); Equal(1,session.CompletedCycles); Equal(2,session.CurrentCycle); Equal(0,session.CycleTime);
    session.Advance(83); Equal(2,session.CompletedCycles); Equal(3,session.CurrentCycle); Equal(5,session.CycleTime);
    session.Advance(200); Assert(session.State==PlaybackState.Completed); Equal(117,session.ElapsedSeconds); Equal(39,session.CycleTime); Equal(3,session.CompletedCycles); Equal(3,session.CurrentCycle);
});
Check("infinite playback survives multiple skipped ticks", () => {
    var session=new PlaybackSession(); session.Start(closedCycle,new(),new(){Infinite=true});
    session.Advance(39*1234+5.5); Assert(session.State==PlaybackState.Running); Equal(1234,session.CompletedCycles); Equal(1235,session.CurrentCycle); Equal(5.5,session.CycleTime);
});
Check("pause freezes time and resume continues", () => {
    var session=new PlaybackSession(); session.Start(closedCycle,new(),new(){RepeatCount=2});
    session.Advance(10); session.Pause(); session.Advance(30); Equal(10,session.ElapsedSeconds);
    session.Resume(); session.Advance(11); Equal(11,session.ElapsedSeconds); Equal(11,session.CycleTime);
});
Check("stop freezes pose and next start resets counters", () => {
    var session=new PlaybackSession(); session.Start(closedCycle,new(),new(){Infinite=true});
    session.Advance(45); session.Stop(); session.Advance(100); session.Resume();
    Assert(session.State==PlaybackState.Stopped); Equal(6,session.CycleTime); Equal(1,session.CompletedCycles);
    session.Start(closedCycle,new(),new()); Equal(0,session.CycleTime); Equal(0,session.CompletedCycles);
});
Check("reset clears all playback progress", () => {
    var session=new PlaybackSession(); session.Start(closedCycle,new(),new(){Infinite=true}); session.Advance(100); session.Reset();
    Assert(session.State==PlaybackState.Ready); Equal(0,session.ElapsedSeconds); Equal(0,session.CycleTime); Equal(0,session.CompletedCycles); Equal(1,session.CurrentCycle);
});
Check("repeat rejects missing stage return", () => {
    var session=new PlaybackSession(); Reject(()=>session.Start(scheduler.Calculate([Move()]),new(),new(){RepeatCount=2}));
});
Check("repeat rejects missing head return", () => {
    var session=new PlaybackSession(); Reject(()=>session.Start(scheduler.Calculate([Move(MachineAxes.Head)]),new(),new(){Infinite=true}));
});
Check("repeat error identifies all non-returned axes", () => {
    var schedule=scheduler.Calculate([Move(),Move(MachineAxes.Stage2),Move(MachineAxes.Head)]);
    try { PlaybackSession.ValidateCycleClosure(schedule,new()); } catch(InvalidOperationException e) {
        Assert(MachineAxes.Resources.All(e.Message.Contains)); return;
    }
    throw new Exception("Expected closure rejection");
});
Check("repeat validates custom mm initial positions", () => {
    var m=LargeMachine(); var work=LargeCycle();
    work.Add(Move(MachineAxes.Stage1,from:1250,to:50));
    work.Add(Move(MachineAxes.Stage2,from:1500,to:-100));
    work.Add(Move(MachineAxes.Head,from:450,to:-50));
    var r=scheduler.Calculate(work,m); var session=new PlaybackSession(); session.Start(r,m,new(){RepeatCount=2});
    var duration=r.Max(x=>x.EndTime); session.Advance(duration);
    var pose=MachineStateEvaluator.Evaluate(r,session.CycleTime,m); Equal(50,pose.Stage1X); Equal(-100,pose.Stage2X); Equal(-50,pose.HeadY);
});
Check("repeat seam does not teleport any axis", () => {
    var before=MachineStateEvaluator.Evaluate(closedCycle,39,new()); var after=MachineStateEvaluator.Evaluate(closedCycle,0,new());
    Equal(before.Stage1X,after.Stage1X); Equal(before.Stage2X,after.Stage2X); Equal(before.HeadY,after.HeadY);
});
Check("reject invalid repetition count and empty cycle", () => {
    var session=new PlaybackSession(); Reject(()=>session.Start(closedCycle,new(),new(){RepeatCount=0}));
    Reject(()=>session.Start(closedCycle,new(),new(){RepeatCount=1_000_001})); Reject(()=>session.Start([],new(),new()));
});
Check("session snapshots playback options", () => {
    var options=new PlaybackOptions(){RepeatCount=2}; var session=new PlaybackSession(); session.Start(closedCycle,new(),options);
    options.RepeatCount=1; session.Advance(39); Assert(session.State==PlaybackState.Running);
});
Check("completed session ignores later ticks", () => {
    var session=new PlaybackSession(); session.Start(closedCycle,new(),new()); session.Advance(40); session.Advance(100);
    Equal(39,session.ElapsedSeconds); Equal(1,session.CompletedCycles);
});
Check("playback settings survive project roundtrip", () => {
    var loaded=ProjectSerializer.DeserializeProject(ProjectSerializer.Serialize(DemoProject.Create(),new(),new(){RepeatCount=5,Infinite=true}));
    Equal(5,loaded.Playback.RepeatCount); Assert(loaded.Playback.Infinite);
});
Check("existing project without playback settings defaults to one pass", () => {
    var root=System.Text.Json.Nodes.JsonNode.Parse(ProjectSerializer.Serialize(DemoProject.Create()))!; root.AsObject().Remove("Playback");
    var loaded=ProjectSerializer.DeserializeProject(root.ToJsonString()); Equal(1,loaded.Playback.RepeatCount); Assert(!loaded.Playback.Infinite);
});
Check("invalid saved repetition count rejected", () => {
    var root=System.Text.Json.Nodes.JsonNode.Parse(ProjectSerializer.Serialize(DemoProject.Create()))!; root["Playback"]!["RepeatCount"]=0;
    Reject(()=>ProjectSerializer.DeserializeProject(root.ToJsonString()));
});
Check("seek pauses running session and updates machine pose", () => {
    var session=new PlaybackSession(); session.Start(closedCycle,new(),new()); session.Advance(5); session.SeekCycleTime(14.75);
    Assert(session.State==PlaybackState.Paused); Equal(14.75,session.CycleTime); Equal(14.75,session.ElapsedSeconds);
    var pose=MachineStateEvaluator.Evaluate(closedCycle,session.CycleTime); Equal(50,pose.Stage1X); Equal(0,pose.HeadY);
});
Check("seek backward and resume uses selected offset", () => {
    var session=new PlaybackSession(); session.Start(closedCycle,new(),new(){RepeatCount=3}); session.Advance(50);
    session.SeekCycleTime(3); Equal(2,session.CurrentCycle); Equal(1,session.CompletedCycles); Equal(42,session.ElapsedSeconds);
    session.Resume(); session.Advance(44); Equal(5,session.CycleTime); Equal(2,session.CurrentCycle);
});
Check("seek final endpoint completes and dragging back reopens same cycle", () => {
    var session=new PlaybackSession(); session.Start(closedCycle,new(),new()); session.SeekCycleTime(39);
    Assert(session.State==PlaybackState.Completed); Equal(1,session.CompletedCycles);
    session.SeekCycleTime(10); Assert(session.State==PlaybackState.Paused); Equal(0,session.CompletedCycles); Equal(10,session.ElapsedSeconds);
});
Check("seek boundary in infinite mode resumes next cycle", () => {
    var session=new PlaybackSession(); session.Start(closedCycle,new(),new(){Infinite=true}); session.Advance(83);
    session.SeekCycleTime(39); Assert(session.State==PlaybackState.Paused); Equal(3,session.CompletedCycles); Equal(117,session.ElapsedSeconds);
    session.Resume(); session.Advance(117.25); Equal(4,session.CurrentCycle); Equal(.25,session.CycleTime);
});
Check("seek from stopped state enables continuation", () => {
    var session=new PlaybackSession(); session.Start(closedCycle,new(),new()); session.Advance(5); session.Stop();
    session.SeekCycleTime(10); session.Resume(); session.Advance(11); Equal(11,session.CycleTime);
});
Check("selected starting time respects repeat validation", () => {
    var session=new PlaybackSession(); session.Start(closedCycle,new(),new(){RepeatCount=2},10);
    Equal(10,session.CycleTime); Equal(10,session.ElapsedSeconds); session.Advance(11); Equal(11,session.CycleTime);
    Reject(()=>session.Start(scheduler.Calculate([Move()]),new(),new(){RepeatCount=2},1));
});
Check("timeline click and cursor coordinates agree after resize", () => {
    foreach(var width in new[]{500d,900d,1400d}) {
        var layout=new TimelineLayout(width,39);
        Equal(14.75,layout.TimeAt(layout.XAt(14.75))); Equal(0,layout.TimeAt(0)); Equal(39,layout.TimeAt(layout.Width+100));
        Equal(39,layout.TimeAt(layout.XAt(39))); Equal(900,Math.Min(900,layout.Width));
    }
});
Check("timeline subsecond and empty duration are bounded", () => {
    var layout=new TimelineLayout(900,.25); Equal(.25,layout.TimeAt(875)); Equal(.125,layout.TimeAt(layout.XAt(.125)));
    Equal(0,new TimelineLayout(900,0).TimeAt(500));
});
Check("invalid seek times leave session intact", () => {
    var session=new PlaybackSession(); session.Start(closedCycle,new(),new()); session.Advance(5);
    foreach(var invalid in new[]{-1d,40,double.NaN,double.PositiveInfinity}) {
        try { session.SeekCycleTime(invalid); } catch(ArgumentOutOfRangeException) { Equal(5,session.CycleTime); continue; }
        throw new Exception("Expected invalid seek rejection");
    }
});
Check("simulator zoom enlarges actual mm geometry without changing aspect", () => {
    var normal=new MachineProjection(new(),500,250); var enlarged=new MachineProjection(new(),500,250,2);
    Equal(2*(normal.X(60)-normal.X(20)),enlarged.X(60)-enlarged.X(20));
    Equal(2*(normal.Y(0)-normal.Y(100)),enlarged.Y(0)-enlarged.Y(100));
    Equal(enlarged.X(60)-enlarged.X(20),enlarged.Y(20)-enlarged.Y(60));
});
Check("rail stays fixed while stage body follows X position", () => {
    var m=new MachineSettings(); var r=scheduler.Calculate([Move()]);
    var rail=MachineGeometry.Rail(m,MachineAxes.Stage1);
    Equal(0,rail.XStart); Equal(100,rail.XEnd); Equal(4,rail.Height);
    var initial=MachineGeometry.Stage(m,MachineAxes.Stage1,MachineStateEvaluator.Evaluate(r,0,m).Stage1X);
    var middle=MachineGeometry.Stage(m,MachineAxes.Stage1,MachineStateEvaluator.Evaluate(r,1,m).Stage1X);
    Equal(0,initial.CenterX); Equal(50,middle.CenterX); Equal(20,middle.Width); Equal(20,middle.Height);
    Assert(rail==MachineGeometry.Rail(m,MachineAxes.Stage1));
});
Check("individual stage dimensions and rail endpoints survive JSON", () => {
    var m=new MachineSettings(){Rail1XStart=-50,Rail1XEnd=150,Rail2XStart=-100,Rail2XEnd=200,
        RailWidthY=5,Stage1LengthX=30,Stage1WidthY=24,Stage2LengthX=40,Stage2WidthY=18};
    var copy=ProjectSerializer.DeserializeProject(ProjectSerializer.Serialize([],m)).Machine;
    Equal(-50,copy.Rail1XStart); Equal(200,copy.Rail2XEnd); Equal(5,copy.RailWidthY);
    var body=MachineGeometry.Stage(copy,MachineAxes.Stage2,60); Equal(40,body.XStart); Equal(80,body.XEnd); Equal(18,body.Height);
});
Check("older mm projects inherit rail ranges without changing operation coordinates", () => {
    var root=System.Text.Json.Nodes.JsonNode.Parse(ProjectSerializer.Serialize(LargeCycle(),LargeMachine()))!;
    foreach(var name in new[]{"Rail1XStart","Rail1XEnd","Rail2XStart","Rail2XEnd","RailWidthY","Stage1LengthX","Stage1WidthY","Stage2LengthX","Stage2WidthY"})
        root["Machine"]!.AsObject().Remove(name);
    var copy=ProjectSerializer.DeserializeProject(root.ToJsonString());
    Equal(-500,copy.Machine.Rail1XStart); Equal(2000,copy.Machine.Rail2XEnd); Equal(1250,copy.Operations[0].EndPosition);
});
Check("stage centre travel must remain supported by both rails", () => {
    var m=new MachineSettings(){Rail2XEnd=90}; Reject(m.Validate);
    m.Rail2XEnd=100; m.Stage1LengthX=0; Reject(m.Validate);
});
Check("projection includes full stage overhang and rail extents", () => {
    var m=new MachineSettings(){Rail1XStart=-80,Rail1XEnd=180,Stage1LengthX=80,Stage2WidthY=60};
    var map=new MachineProjection(m,600,300);
    Assert(map.X(-80)>=0 && map.X(180)<=600); Assert(map.Y(130)>=0);
    Assert(map.X(m.StageXMin-m.Stage1LengthX/2)>=0 && map.X(m.StageXMax+m.Stage1LengthX/2)<=600);
});
Check("invalid machine draft remains drawable while strict validation rejects it", () => {
    var m=new MachineSettings(){HeadX=180,HeadInitialY=220,Stage1InitialX=-150,Stage2Y=300};
    var errors=m.GetValidationErrors(); Assert(errors.Count>=4); Reject(m.Validate);
    var draft=MachinePreviewDraft.Create(m); var projection=MachineProjection.ForPreview(draft.Drawing,800,600);
    Assert(projection.X(184)<800 && projection.X(-160)>=0 && projection.Y(310)>=0);
    Equal(180,m.HeadX); Equal(220,m.HeadInitialY); Equal(-150,m.Stage1InitialX);
    Reject(()=>new MachineProjection(m,800,600));
});
Check("all simultaneous constraint violations include names and current values", () => {
    var m=new MachineSettings(){HeadX=180,Stage1InitialX=-150,Stage2Y=300};
    var errors=m.GetValidationErrors();
    Assert(errors.Any(x=>x.Contains("Head X") && x.Contains("176")));
    Assert(errors.Any(x=>x.Contains("Stage 1 초기 X") && x.Contains("-150")));
    Assert(errors.Any(x=>x.Contains("2열 Y") && x.Contains("300")));
});
Check("reversed ranges and negative dimensions have finite preview projection", () => {
    var m=new MachineSettings(){GantryXStart=60,GantryXEnd=20,Rail1XStart=100,Rail1XEnd=0,Stage1LengthX=-20,HeadHeight=0};
    Reject(m.Validate); var p=MachineProjection.ForPreview(m,800,600);
    Assert(double.IsFinite(p.Scale) && p.Scale>0); Assert(double.IsFinite(p.X(60)) && double.IsFinite(p.Y(0)));
    Equal(-20,m.Stage1LengthX); Equal(60,m.GantryXStart);
});
Check("preview numeric fallback never modifies original draft or previous image", () => {
    var previous=new MachineSettings(){HeadX=45}; var m=new MachineSettings(){HeadX=double.NaN,Stage1LengthX=double.PositiveInfinity,GantryYEnd=1e12};
    var draft=MachinePreviewDraft.Create(m,previous);
    Equal(45,draft.Drawing.HeadX); Equal(20,draft.Drawing.Stage1LengthX); Equal(115,draft.Drawing.GantryYEnd);
    Equal(3,draft.FallbackFields.Count); Assert(double.IsNaN(m.HeadX)); Equal(45,previous.HeadX);
    Assert(double.IsFinite(MachineProjection.ForPreview(draft.Drawing,800,600).Scale));
    Reject(()=>ProjectSerializer.Serialize([],m));
});
Check("correcting draft restores strict applicability", () => {
    var m=new MachineSettings(){HeadX=180}; Assert(m.GetValidationErrors().Count>0);
    m.HeadX=40; m.Validate(); Equal(0,m.GetValidationErrors().Count);
    ProjectSerializer.DeserializeProject(ProjectSerializer.Serialize([],m));
});
Check("shared scan predecessor starts head transfer and unload together after scan2", () => {
    var loading=Work(MachineAxes.Stage1,1); var vision=Move(MachineAxes.Stage1,2,0,20);
    var prep2=Work(MachineAxes.Stage2,2); // Creates an earlier idle Stage1 gap, which unload must not fill.
    var scan1=Move(MachineAxes.Stage1,3.8,20,60); scan1.RequiresScannerHead=true;
    var returning=Move(MachineAxes.Stage1,.7,60,20);
    var scan2=Move(MachineAxes.Stage1,3.8,20,60); scan2.RequiresScannerHead=true;
    var transfer=Move(MachineAxes.Head,1.7); transfer.PredecessorId=scan2.Id;
    var unload=Move(MachineAxes.Stage1,1,60,100); unload.PredecessorId=scan2.Id;
    var home=Move(MachineAxes.Stage1,2,100,0);
    var r=scheduler.Calculate([loading,vision,prep2,scan1,returning,scan2,transfer,unload,home]);
    Equal(r[5].EndTime,r[6].StartTime); Equal(r[5].EndTime,r[7].StartTime);
    Equal(r[7].EndTime,r[8].StartTime); Equal(1.7,r[6].Duration);
});
Check("explicit predecessor replaces previous row wait", () => {
    var prerequisite=Work(MachineAxes.Stage1,5); var unrelated=Work(MachineAxes.Stage2,100);
    var child=Work(MachineAxes.Stage1,2); child.PredecessorId=prerequisite.Id;
    var r=scheduler.Calculate([prerequisite,unrelated,child]); Equal(5,r[2].StartTime);
});
Check("explicit predecessor also waits when previous checkbox is off", () => {
    var prerequisite=Work(MachineAxes.Stage1,5); var child=Work(MachineAxes.Stage2,2);
    child.WaitForPrevious=false; child.PredecessorId=prerequisite.Id;
    Equal(5,scheduler.Calculate([prerequisite,child])[1].StartTime);
});
Check("later-row predecessor is topologically resolved preserving result row order", () => {
    var prerequisite=Work(MachineAxes.Stage2,5); prerequisite.WaitForPrevious=false;
    var child=Work(MachineAxes.Stage1,2); child.PredecessorId=prerequisite.Id;
    var r=scheduler.Calculate([child,prerequisite]); Equal(5,r[0].StartTime); Equal(0,r[1].StartTime);
    Assert(r[0].Id==child.Id && r[1].Id==prerequisite.Id);
});
Check("same resource still serializes children sharing predecessor", () => {
    var prerequisite=Work(MachineAxes.Stage1,3); var a=Work(MachineAxes.Stage2,4); var b=Work(MachineAxes.Stage2,2);
    a.PredecessorId=prerequisite.Id; b.PredecessorId=prerequisite.Id;
    var r=scheduler.Calculate([prerequisite,a,b]); Equal(3,r[1].StartTime); Equal(7,r[2].StartTime);
});
Check("reject missing and self predecessor", () => {
    var op=Work(); op.PredecessorId=Guid.NewGuid(); Reject(()=>scheduler.Calculate([op]));
    op.PredecessorId=op.Id; Reject(()=>scheduler.Calculate([op]));
});
Check("reject explicit dependency cycle", () => {
    var a=Work(); var b=Work(MachineAxes.Stage2); a.PredecessorId=b.Id; b.PredecessorId=a.Id;
    Reject(()=>scheduler.Calculate([a,b]));
});
Check("reject cycle involving implicit previous-row edge", () => {
    var a=Work(); var b=Work(MachineAxes.Stage2); a.PredecessorId=b.Id;
    Reject(()=>scheduler.Calculate([a,b]));
});
Check("deleting predecessor reports missing reference instead of running early", () => {
    var parent=Work(); var child=Work(MachineAxes.Stage2); child.PredecessorId=parent.Id;
    scheduler.Calculate([parent,child]); Reject(()=>scheduler.Calculate([child]));
});
Check("explicit references survive reordering and JSON roundtrip", () => {
    var parent=Work(MachineAxes.Stage2,5); parent.WaitForPrevious=false;
    var child=Work(MachineAxes.Stage1,2); child.PredecessorId=parent.Id;
    var copy=ProjectSerializer.Deserialize(ProjectSerializer.Serialize([child,parent]));
    Assert(copy[0].PredecessorId==copy[1].Id); Equal(5,scheduler.Calculate(copy)[0].StartTime);
});
Check("clearing explicit predecessor restores previous-row behavior", () => {
    var parent=Work(MachineAxes.Stage1,3); var middle=Work(MachineAxes.Stage2,10); var child=Work(MachineAxes.Stage1);
    child.PredecessorId=parent.Id; Equal(3,scheduler.Calculate([parent,middle,child])[2].StartTime);
    child.PredecessorId=Guid.Empty; Equal(13,scheduler.Calculate([parent,middle,child])[2].StartTime);
});
Check("explicit reference notifications update checkbox state and are not saved as derived flags", () => {
    var op=Work(); var changed=new List<string?>(); op.PropertyChanged+=(_,e)=>changed.Add(e.PropertyName);
    op.PredecessorId=Guid.NewGuid(); Assert(op.HasExplicitPredecessor);
    Assert(changed.Contains(nameof(OperationDefinition.HasExplicitPredecessor)));
    var json=System.Text.Json.JsonSerializer.Serialize(op); Assert(!json.Contains("HasExplicitPredecessor"));
});
Check("independent preprocessors supply both stages at the first interval", () => {
    var session=new MediaProductionSession(DemoProject.Create(),new(),new(){IntervalSeconds=30},new());
    session.Advance(29); Equal(0,session.ConsumedMedia); session.Advance(30);
    Equal(2,session.SuppliedMedia); Equal(2,session.ConsumedMedia); Equal(0,session.WaitingMedia);
    var load=session.VisibleOperations.Single(x=>x.Resource==MachineAxes.Stage1); Equal(30,load.StartTime);
    session.Advance(60); var load2=session.VisibleOperations.Single(x=>x.Name.StartsWith("Stage 2 Loading")); Equal(30,load2.StartTime);
});
Check("batch supply can load both stages at same time", () => {
    var session=new MediaProductionSession(DemoProject.Create(),new(),new(){IntervalSeconds=30,BatchSize=2},new());
    session.Advance(30); Equal(2,session.ConsumedMedia);
    foreach(var load in session.VisibleOperations.Where(x=>x.Name.Contains("Loading"))) Equal(30,load.StartTime);
});
Check("supply at start and media waiting count are explicit", () => {
    var session=new MediaProductionSession(DemoProject.Create(),new(),new(){IntervalSeconds=1,BatchSize=5,SupplyAtStart=true},new());
    Equal(10,session.SuppliedMedia); Equal(2,session.ConsumedMedia); Equal(8,session.WaitingMedia);
    session.Advance(1); Equal(18,session.WaitingMedia);
});
Check("next stage1 cycle loads before other stage completes previous cycle", () => {
    var session=new MediaProductionSession(DemoProject.Create(),new(),new(){IntervalSeconds=10},new(){RepeatCount=2},false);
    session.Advance(40);
    var load=session.VisibleOperations.Single(x=>x.Name=="Stage 1 Loading [2회]"); Equal(34.5,load.StartTime);
    Equal(1,session.CompletedStage1); Equal(0,session.CompletedStage2);
});
Check("supply-aware single cycle is fully planned with loading wait", () => {
    var session=new MediaProductionSession(DemoProject.Create(),new(),new(){IntervalSeconds=30},new(),true);
    session.RunToCompletion(); Assert(session.State==PlaybackState.Completed); Equal(69,session.ElapsedSeconds);
    Equal(1,session.CompletedStage1); Equal(1,session.CompletedStage2); Equal(9,session.VisibleOperations.Count);
    var pose=session.MachineState; Equal(0,pose.Stage1X); Equal(0,pose.Stage2X); Equal(0,pose.HeadY);
});
Check("production pause resume and stop freeze event processing", () => {
    var session=new MediaProductionSession(DemoProject.Create(),new(),new(),new());
    session.Advance(20); session.Pause(); session.Advance(50); Equal(20,session.ElapsedSeconds);
    session.Resume(); session.Advance(30); Equal(2,session.ConsumedMedia);
    session.Stop(); session.Advance(60); Equal(30,session.ElapsedSeconds);
});
Check("production keeps bounded history during many cycles", () => {
    var session=new MediaProductionSession(DemoProject.Create(),new(),new(){IntervalSeconds=1},new(){Infinite=true});
    session.Advance(20000); Assert(session.CompletedStage1>400); Assert(session.RetainedJobCount<=527);
    Assert(session.WaitingMedia>0); Assert(session.VisibleOperations.Count<=503);
});
Check("production requires explicit loading flags and loading roots", () => {
    var ops=DemoProject.Create(); ops[0].IsLoading=false;
    Reject(()=>new MediaProductionSession(ops,new(),new(),new()));
    ops=DemoProject.Create(); ops[1].WaitForPrevious=true;
    Reject(()=>new MediaProductionSession(ops,new(),new(),new()));
});
Check("invalid supply parameters are rejected", () => {
    Reject(()=>new MediaSupplySettings(){IntervalSeconds=0}.Validate());
    Reject(()=>new MediaSupplySettings(){IntervalSeconds=double.NaN}.Validate());
    Reject(()=>new MediaSupplySettings(){BatchSize=0}.Validate());
});
Check("supply settings and loading flags survive project save", () => {
    var json=ProjectSerializer.Serialize(DemoProject.Create(),new(),new(),new(){Enabled=true,IntervalSeconds=12.5,BatchSize=3,SupplyAtStart=true});
    var copy=ProjectSerializer.DeserializeProject(json); Assert(copy.Supply.Enabled); Equal(12.5,copy.Supply.IntervalSeconds);
    Equal(3,copy.Supply.BatchSize); Assert(copy.Supply.SupplyAtStart); Assert(copy.Operations[0].IsLoading && copy.Operations[1].IsLoading);
    var root=System.Text.Json.Nodes.JsonNode.Parse(json)!; root.AsObject().Remove("Supply");
    Assert(!ProjectSerializer.DeserializeProject(root.ToJsonString()).Supply.Enabled);
});
Check("fractional supply intervals do not duplicate or lose boundary arrivals", () => {
    var session=new MediaProductionSession(DemoProject.Create(),new(),new(){IntervalSeconds=.7},new());
    session.Advance(2.1); Equal(6,session.SuppliedMedia); Equal(2,session.ConsumedMedia); Equal(4,session.WaitingMedia);
    session.Advance(7); Equal(20,session.SuppliedMedia);
});
Check("single preview skips idle supply events during very long processing", () => {
    var ops=DemoProject.Create(); ops[3].DurationSeconds=1_000_000;
    var session=new MediaProductionSession(ops,new(),new(){IntervalSeconds=.001},new(),true);
    session.RunToCompletion(); Assert(session.State==PlaybackState.Completed); Equal(9,session.VisibleOperations.Count);
    Assert(session.WaitingMedia>100_000_000);
});
Check("production template retains same-resource exclusivity across cycles", () => {
    var session=new MediaProductionSession(DemoProject.Create(),new(),new(){IntervalSeconds=1,BatchSize=2},new(){RepeatCount=3});
    session.Advance(100);
    foreach(var resource in MachineAxes.Resources) {
        var jobs=session.VisibleOperations.Where(x=>x.RequiredResources.Contains(resource)).OrderBy(x=>x.StartTime).ToList();
        for(var i=1;i<jobs.Count;i++) Assert(jobs[i].StartTime>=jobs[i-1].EndTime);
    }
});
Check("independent lane feed removes shared supply delay and explicit reference removes stage2 home wait", () => {
    var m=new MachineSettings(){StageXMax=2000,HeadYMax=1000,Stage2Y=1000,GantryYEnd=1015};
    var ops=new List<OperationDefinition>
    {
        new(){Name="Stage 1 Loading",Resource=MachineAxes.Stage1,DurationSeconds=1,IsLoading=true,WaitForPrevious=false},
        Move(MachineAxes.Stage1,2,0,260),
        new(){Name="Stage 2 Loading",Resource=MachineAxes.Stage2,DurationSeconds=1,IsLoading=true,WaitForPrevious=false},
        Move(MachineAxes.Stage2,2,0,260),
        Move(MachineAxes.Stage1,3.8,260,800),Move(MachineAxes.Stage1,.7,800,260),Move(MachineAxes.Stage1,3.8,260,800),
        Move(MachineAxes.Head,1.7,0,1000),Move(MachineAxes.Stage1,1,800,1600),Move(MachineAxes.Stage1,2,1600,0),
        Move(MachineAxes.Stage2,3.8,260,800),Move(MachineAxes.Stage2,.7,800,260),Move(MachineAxes.Stage2,3.8,260,800),
        Move(MachineAxes.Head,1.7,1000,0),Move(MachineAxes.Stage2,1,800,1600),Move(MachineAxes.Stage2,2,1600,0)
    };
    foreach(var i in new[]{4,6,10,12}) ops[i].RequiresScannerHead=true;
    ops[8].PredecessorId=ops[6].Id; ops[14].PredecessorId=ops[12].Id;
    var original=new MediaProductionSession(ops,m,new(),new(),true); original.RunToCompletion();
    Equal(33,original.VisibleOperations.Single(x=>x.Id==ops[4].Id).StartTime);
    Equal(44.3,original.VisibleOperations.Single(x=>x.Id==ops[10].Id).StartTime);
    Equal(55.6,original.ElapsedSeconds);
    ops[4].PredecessorId=ops[1].Id; ops[10].PredecessorId=ops[7].Id;
    var fixedPlan=new MediaProductionSession(ops,m,new(),new(),true); fixedPlan.RunToCompletion();
    Equal(33,fixedPlan.VisibleOperations.Single(x=>x.Id==ops[4].Id).StartTime);
    Equal(43,fixedPlan.VisibleOperations.Single(x=>x.Id==ops[10].Id).StartTime);
    Equal(41.3,fixedPlan.VisibleOperations.Single(x=>x.Id==ops[7].Id).StartTime);
    Equal(41.3,fixedPlan.VisibleOperations.Single(x=>x.Id==ops[8].Id).StartTime);
    Equal(54.3,fixedPlan.ElapsedSeconds);
});
Check("lane preprocessors run in parallel with distinct periods and separate stock", () => {
    var supply=new MediaSupplySettings(){Stage1IntervalSeconds=10,Stage2IntervalSeconds=100};
    var session=new MediaProductionSession(DemoProject.Create(),new(),supply,new(){RepeatCount=2});
    session.Advance(30); Equal(3,session.SuppliedFor(MachineAxes.Stage1)); Equal(0,session.SuppliedFor(MachineAxes.Stage2));
    Equal(1,session.ConsumedFor(MachineAxes.Stage1)); Equal(0,session.ConsumedFor(MachineAxes.Stage2));
    Equal(2,session.WaitingFor(MachineAxes.Stage1));
    session.Advance(100); Equal(1,session.ConsumedFor(MachineAxes.Stage2));
    Equal(100,session.VisibleOperations.Single(x=>x.Name=="Stage 2 Loading [1회]").StartTime);
});
Check("ready stage consumes already supplied lane media without restarting thirty second timer", () => {
    var session=new MediaProductionSession(DemoProject.Create(),new(),new(){IntervalSeconds=30},new(){RepeatCount=2});
    session.Advance(70);
    Equal(60,session.VisibleOperations.Single(x=>x.Name=="Stage 1 Loading [2회]").StartTime);
    Equal(69,session.VisibleOperations.Single(x=>x.Name=="Stage 2 Loading [2회]").StartTime);
});
Check("complete finite production chart contains future cycles before playback", () => {
    var ops=DemoProject.Create(); var plan=ProductionPlan.Create(ops,new(),new(),new(){RepeatCount=3});
    Assert(plan.Complete); Equal(27,plan.Operations.Count); Equal(9,plan.FirstCycle.Count);
    Assert(plan.Operations.Any(x=>x.Name.EndsWith("[3회]")));
    var session=new MediaProductionSession(ops,new(),new(),new(){RepeatCount=3});
    Equal(0,session.VisibleOperations.Count); Equal(0,session.ElapsedSeconds);
    Assert(plan.Operations.Any(x=>x.StartTime>0));
    session.Advance(10); Equal(27,plan.Operations.Count);
});
Check("forecast includes pending operations and never consumes real media or moves live axes", () => {
    var session=new MediaProductionSession(DemoProject.Create(),new(),new(),new(){Infinite=true});
    var before=session.MachineState; var future=session.Forecast();
    Equal(27,future.Operations.Count); Equal(0,session.ElapsedSeconds); Equal(0,session.ConsumedMedia); Assert(session.MachineState==before);
    session.Advance(40); var consumed=session.ConsumedMedia; var pose=session.MachineState; var version=session.Version;
    var forecast=session.Forecast(); Assert(forecast.Operations.Any(x=>x.StartTime>40));
    Equal(40,session.ElapsedSeconds); Equal(consumed,session.ConsumedMedia); Equal(version,session.Version); Assert(session.MachineState==pose);
});
Check("forecast times agree with later real execution", () => {
    var session=new MediaProductionSession(DemoProject.Create(),new(),new(),new(){RepeatCount=3});
    session.Advance(40); var future=session.Forecast(); session.RunToCompletion();
    foreach(var planned in future.Operations) {
        var actual=session.VisibleOperations.Single(x=>x.Id==planned.Id && x.Name==planned.Name);
        Equal(planned.StartTime,actual.StartTime); Equal(planned.EndTime,actual.EndTime);
    }
});
Check("infinite forecast stays bounded after hundreds of completed cycles", () => {
    var session=new MediaProductionSession(DemoProject.Create(),new(),new(){IntervalSeconds=1},new(){Infinite=true});
    session.Advance(20000); var future=session.Forecast();
    Assert(future.Operations.Count<550); Assert(session.RetainedJobCount<=527);
    Assert(future.Operations.Any(x=>x.EndTime>session.ElapsedSeconds));
});
Check("per-lane intervals survive save and older shared interval becomes each lane interval", () => {
    var json=ProjectSerializer.Serialize(DemoProject.Create(),new(),new(),new(){Enabled=true,Stage1IntervalSeconds=20,Stage2IntervalSeconds=35});
    var copy=ProjectSerializer.DeserializeProject(json); Equal(20,copy.Supply.Stage1IntervalSeconds); Equal(35,copy.Supply.Stage2IntervalSeconds);
    var root=System.Text.Json.Nodes.JsonNode.Parse(json)!; root["Supply"]!.AsObject().Remove("Stage1IntervalSeconds"); root["Supply"]!.AsObject().Remove("Stage2IntervalSeconds");
    root["Supply"]!["IntervalSeconds"]=30;
    var legacy=ProjectSerializer.DeserializeProject(root.ToJsonString()); Equal(30,legacy.Supply.Stage1IntervalSeconds); Equal(30,legacy.Supply.Stage2IntervalSeconds);
});
Console.WriteLine($"{passed} checks passed.");
