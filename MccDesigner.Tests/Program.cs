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
Console.WriteLine($"{passed} checks passed.");
