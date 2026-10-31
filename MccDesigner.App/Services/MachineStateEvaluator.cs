using MccDesigner.Models;
namespace MccDesigner.Services;

public sealed record MachineState(double Position, string TargetLane, string? ActiveOperationName);

/// <summary>Samples the same schedule used by the timeline; retains the last pose while idle.</summary>
public static class MachineStateEvaluator
{
    public static MachineState Evaluate(IReadOnlyList<ScheduledOperation> operations, double time)
    {
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        var motions = operations.Where(o => o.IsMotion).OrderBy(o => o.StartTime).ToList();
        var position = motions.Count == 0 ? 0 : motions[0].StartPosition;
        var lane = motions.Count == 0 ? "Stage 1" : motions[0].TargetLane;
        foreach (var op in motions)
        {
            if (time < op.StartTime) break;
            lane = op.TargetLane;
            if (time < op.EndTime)
            {
                var fraction = op.Duration <= 0 ? 1 : Math.Clamp((time - op.StartTime) / op.Duration, 0, 1);
                return new(op.StartPosition + (op.EndPosition - op.StartPosition) * fraction, lane, op.Name);
            }
            position = op.EndPosition;
        }
        return new(position, lane, null);
    }
}
