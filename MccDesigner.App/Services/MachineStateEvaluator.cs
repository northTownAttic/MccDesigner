using MccDesigner.Models;
namespace MccDesigner.Services;

public sealed record MachineState(double Position, string TargetLane, string? ActiveOperationName);

/// <summary>Samples the same schedule used by the timeline; retains the last pose while idle.</summary>
public static class MachineStateEvaluator
{
    public static MachineState Evaluate(IReadOnlyList<ScheduledOperation> operations, double time)
    {
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        var headOperations = operations.Where(o => o.Resource == "Gantry / Head").OrderBy(o => o.StartTime).ToList();
        var position = headOperations.Count == 0 ? 0 : headOperations[0].StartPosition;
        var lane = headOperations.Count == 0 ? "Stage 1" : headOperations[0].TargetLane;
        foreach (var op in headOperations)
        {
            if (time < op.StartTime) break;
            lane = op.TargetLane;
            if (time < op.EndTime)
            {
                if (!op.IsMotion) return new(position, lane, op.Name);
                var fraction = op.Duration <= 0 ? 1 : Math.Clamp((time - op.StartTime) / op.Duration, 0, 1);
                return new(op.StartPosition + (op.EndPosition - op.StartPosition) * fraction, lane, op.Name);
            }
            if (op.IsMotion) position = op.EndPosition;
        }
        return new(position, lane, null);
    }
}
