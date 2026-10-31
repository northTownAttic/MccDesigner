using MccDesigner.Models;
namespace MccDesigner.Services;

public sealed record MachineState(double Stage1X, double Stage2X, double HeadY);

/// <summary>Fixed gantry; two independent stage X axes and a shared head Y axis.</summary>
public static class MachineStateEvaluator
{
    public static MachineState Evaluate(IReadOnlyList<ScheduledOperation> operations, double time)
    {
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        return new(PositionAt(operations, MachineAxes.Stage1, time),
            PositionAt(operations, MachineAxes.Stage2, time), PositionAt(operations, MachineAxes.Head, time));
    }

    public static double PositionAt(IReadOnlyList<ScheduledOperation> operations, string resource, double time)
    {
        var position = 0d;
        foreach (var op in operations.Where(o => o.Resource == resource && o.IsMotion).OrderBy(o => o.StartTime))
        {
            if (time < op.StartTime) break;
            if (time < op.EndTime)
            {
                var fraction = Math.Clamp((time - op.StartTime) / op.Duration, 0, 1);
                return op.StartPosition + (op.EndPosition - op.StartPosition) * fraction;
            }
            position = op.EndPosition;
        }
        return position;
    }
}
