using MccDesigner.Models;
namespace MccDesigner.Services;

/// <summary>Read-only chart planning is separate from the live production session.</summary>
public sealed record ProductionPlan(IReadOnlyList<ScheduledOperation> FirstCycle,
    IReadOnlyList<ScheduledOperation> Operations, bool Complete, long ThroughRound)
{
    public static ProductionPlan Create(IReadOnlyList<OperationDefinition> operations, MachineSettings machine,
        MediaSupplySettings supply, PlaybackOptions playback)
    {
        playback.Validate();
        var first = new MediaProductionSession(operations, machine, supply, new PlaybackOptions(), true);
        first.RunToCompletion();
        var complete = !playback.Infinite && (long)playback.RepeatCount * operations.Count <= Math.Max(5000, operations.Count);
        if (complete)
        {
            var all = playback.RepeatCount == 1 ? first : new MediaProductionSession(operations, machine, supply, playback, true);
            if (playback.RepeatCount != 1) all.RunToCompletion();
            return new(first.VisibleOperations, all.VisibleOperations, true, playback.RepeatCount);
        }
        var forecast = new MediaProductionSession(operations, machine, supply, playback).Forecast();
        return new(first.VisibleOperations, forecast.Operations, false, forecast.ThroughRound);
    }
}
