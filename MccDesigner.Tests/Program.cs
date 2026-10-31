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
Check("versioned JSON preserves axis model and flags", () => { var copy=ProjectSerializer.Deserialize(ProjectSerializer.Serialize(DemoProject.Create())); Assert(copy[3].RequiresScannerHead); Assert(copy[4].Resource==MachineAxes.Head); Assert(!copy[6].WaitForPrevious); Equal(35.5,scheduler.Calculate(copy).Max(x=>x.EndTime)); });
Check("legacy stationary stage JSON stays sequential", () => { var copy=ProjectSerializer.Deserialize("""[{"Name":"A","DurationSeconds":8},{"Name":"B","Resource":"Stage 2","DurationSeconds":4}]"""); Equal(8,scheduler.Calculate(copy)[1].StartTime); });
Check("legacy head-X JSON never reinterpreted as Y", () => Reject(() => ProjectSerializer.Deserialize("""[{"Resource":"Gantry / Head","IsMotion":true}]""")));
Check("reject unsupported project version", () => Reject(() => ProjectSerializer.Deserialize("""{"FormatVersion":99,"AxisModel":"FixedGantry_StageX_HeadY","Operations":[]}""")));
Check("reject missing versioned operations", () => Reject(() => ProjectSerializer.Deserialize("""{"FormatVersion":2,"AxisModel":"FixedGantry_StageX_HeadY"}""")));
Check("demo reservations, cycle and head fixed during both scans", () => {
    var r=scheduler.Calculate(DemoProject.Create()); Equal(35.5,r.Max(x=>x.EndTime));
    Equal(0,r[0].StartTime); Equal(0,r[1].StartTime); Equal(21,r[4].StartTime); Equal(23,r[5].StartTime); Equal(21,r[6].StartTime);
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
Console.WriteLine($"{passed} checks passed.");
