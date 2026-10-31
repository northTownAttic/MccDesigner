using MccDesigner.Models;
using MccDesigner.Services;

var passed = 0;
void Check(string name, Action test) { test(); Console.WriteLine($"PASS {name}"); passed++; }
void Equal(double expected, double actual) { if (Math.Abs(expected - actual) > 1e-9) throw new Exception($"Expected {expected}, got {actual}"); }
void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected validation rejection"); }
OperationDefinition Move(double duration = 2.5) => new() { Name = "Move", Resource = "Gantry / Head", IsMotion = true, DurationSeconds = duration, StartPosition = 20, EndPosition = 80, TargetLane = "Stage 2" };
var scheduler = new Scheduler();
Check("decimal sequential timing", () => { var r = scheduler.Calculate([new() { DurationSeconds = 3.5 }, Move()]); Equal(3.5, r[1].StartTime); Equal(6, r[1].EndTime); });
Check("empty schedule", () => Equal(0, scheduler.Calculate([]).Count));
foreach (var invalid in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
    Check($"reject duration {invalid}", () => Reject(() => scheduler.Calculate([Move(invalid)])));
Check("reject invalid position", () => { var op = Move(); op.EndPosition = 101; Reject(() => scheduler.Calculate([op])); });
Check("reject unknown lane", () => { var op = Move(); op.TargetLane = "Other"; Reject(() => scheduler.Calculate([op])); });
Check("reject unknown resource", () => Reject(() => scheduler.Calculate([new() { Resource = "Other" }])));
Check("reject stage motion", () => { var op = Move(); op.Resource = "Stage 1"; Reject(() => scheduler.Calculate([op])); });
Check("reject duplicate IDs", () => { var op = Move(); Reject(() => scheduler.Calculate([op, op])); });
Check("reject null operation", () => Reject(() => scheduler.Calculate([null!])));
Check("reject time overflow", () => Reject(() => scheduler.Calculate([Move(double.MaxValue), Move(double.MaxValue)])));
var schedule = scheduler.Calculate([new() { DurationSeconds = 1 }, Move(2), new() { DurationSeconds = 3 }]);
Check("initial pose before motion", () => Equal(20, MachineStateEvaluator.Evaluate(schedule, 0).Position));
Check("midpoint interpolation", () => Equal(50, MachineStateEvaluator.Evaluate(schedule, 2).Position));
Check("retain pose at motion end", () => Equal(80, MachineStateEvaluator.Evaluate(schedule, 3).Position));
Check("retain pose while waiting", () => { var state = MachineStateEvaluator.Evaluate(schedule, 4); Equal(80, state.Position); if (state.TargetLane != "Stage 2" || state.ActiveOperationName != null) throw new Exception("Incorrect idle state"); });
Check("retain pose after completion", () => Equal(80, MachineStateEvaluator.Evaluate(schedule, 10).Position));
Check("empty machine pose", () => Equal(0, MachineStateEvaluator.Evaluate([], 0).Position));
Check("parallel stages start together", () => {
    var r = scheduler.Calculate([new() { DurationSeconds = 8 }, new() { Resource = "Stage 2", DurationSeconds = 6.25, WaitForPrevious = false }]);
    Equal(0, r[0].StartTime); Equal(0, r[1].StartTime); Equal(8, r.Max(x => x.EndTime));
});
Check("independent work on same stage remains serialized", () => {
    var r = scheduler.Calculate([new() { DurationSeconds = 8 }, new() { WaitForPrevious = false, DurationSeconds = 2 }]);
    Equal(8, r[1].StartTime);
});
Check("shared head cannot scan both lanes at once", () => {
    var a = Move(12); a.TargetLane = "Stage 1";
    var b = Move(10); b.WaitForPrevious = false;
    var r = scheduler.Calculate([a, b]); Equal(12, r[1].StartTime);
});
Check("scan reserves target stage", () => {
    var scan = Move(12);
    var r = scheduler.Calculate([scan, new() { Resource = "Stage 2", WaitForPrevious = false, DurationSeconds = 3 }]);
    Equal(12, r[1].StartTime);
    if (!r[0].RequiredResources.SequenceEqual(new[] { "Gantry / Head", "Stage 2" })) throw new Exception("Missing shared reservation");
});
Check("scan waits for target stage", () => {
    var scan = Move(12); scan.WaitForPrevious = false;
    var r = scheduler.Calculate([new() { Resource = "Stage 2", DurationSeconds = 8 }, scan]); Equal(8, r[1].StartTime);
});
Check("pure head move leaves stage free", () => {
    var move = Move(12); move.OccupiesTargetLane = false;
    var r = scheduler.Calculate([move, new() { Resource = "Stage 2", WaitForPrevious = false, DurationSeconds = 3 }]); Equal(0, r[1].StartTime);
});
Check("independent work fills idle resource gap", () => {
    var move = Move(2);
    var r = scheduler.Calculate([new() { DurationSeconds = 8 }, move, new() { Resource = "Stage 2", DurationSeconds = 3, WaitForPrevious = false }]);
    Equal(8, r[1].StartTime); Equal(0, r[2].StartTime);
});
Check("find common free interval across both resources", () => {
    var first = Move(2); var next = Move(7); next.WaitForPrevious = false;
    var r = scheduler.Calculate([new() { DurationSeconds = 10 }, first,
        new() { Resource = "Stage 2", DurationSeconds = 5, WaitForPrevious = false }, next]);
    Equal(10, r[1].StartTime); Equal(0, r[2].StartTime); Equal(12, r[3].StartTime);
});
Check("previous wait refers to previous row not latest end", () => {
    var r = scheduler.Calculate([new() { DurationSeconds = 20 },
        new() { Resource = "Stage 2", DurationSeconds = 3, WaitForPrevious = false },
        new() { Resource = "Stage 2", DurationSeconds = 2 }]);
    Equal(3, r[2].StartTime); Equal(20, r.Max(x => x.EndTime));
});
Check("legacy JSON remains sequential", () => {
    var ops = System.Text.Json.JsonSerializer.Deserialize<List<OperationDefinition>>("""[{"Name":"A","DurationSeconds":8},{"Name":"B","Resource":"Stage 2","DurationSeconds":4}]""")!;
    var r = scheduler.Calculate(ops); Equal(8, r[1].StartTime); Equal(12, r[1].EndTime);
});
Check("parallel settings survive JSON roundtrip", () => {
    var move = Move(); move.WaitForPrevious = false; move.OccupiesTargetLane = false;
    var copy = System.Text.Json.JsonSerializer.Deserialize<OperationDefinition>(System.Text.Json.JsonSerializer.Serialize(move))!;
    if (copy.WaitForPrevious || copy.OccupiesTargetLane || copy.Id != move.Id) throw new Exception("Settings lost");
});
Check("reject unrepresentable positive time increment", () => Reject(() => scheduler.Calculate([Move(1e20), Move(1)])));
Check("stationary head operation retains previous position", () => {
    var r = scheduler.Calculate([Move(2), new() { Resource = "Gantry / Head", TargetLane = "Stage 2", Name = "Hold", DurationSeconds = 3 }]);
    var state = MachineStateEvaluator.Evaluate(r, 3); Equal(80, state.Position);
    if (state.ActiveOperationName != "Hold") throw new Exception("Head activity missing");
});
Check("demo cycle 43.25 seconds reduces to 37", () => {
    var scan1 = Move(12.5); scan1.TargetLane = "Stage 1";
    var returning = Move(3.5); returning.TargetLane = "Stage 1"; returning.OccupiesTargetLane = false;
    var r = scheduler.Calculate([new() { DurationSeconds = 5 }, new() { DurationSeconds = 3.5 }, scan1, returning,
        new() { Resource = "Stage 2", DurationSeconds = 6.25, WaitForPrevious = false }, Move(12.5)]);
    Equal(0, r[4].StartTime); Equal(37, r.Max(x => x.EndTime));
    foreach (var resource in new[] { "Stage 1", "Stage 2", "Gantry / Head" })
    {
        var intervals = r.Where(x => x.RequiredResources.Contains(resource)).OrderBy(x => x.StartTime).ToList();
        for (var i = 1; i < intervals.Count; i++)
            if (intervals[i].StartTime < intervals[i - 1].EndTime) throw new Exception($"Resource overlap: {resource}");
    }
});
Console.WriteLine($"{passed} checks passed.");
