using MccDesigner.Models;
namespace MccDesigner.Services;

public enum PlaybackState { Ready, Running, Paused, Stopped, Completed }

/// <summary>One completed schedule is repeated without overlap. Time comes from a monotonic clock.</summary>
public sealed class PlaybackSession
{
    private PlaybackOptions options = new();
    private double duration;
    public PlaybackState State { get; private set; }
    public double ElapsedSeconds { get; private set; }
    public double CycleTime { get; private set; }
    public long CompletedCycles { get; private set; }
    public long CurrentCycle { get; private set; } = 1;

    public void Start(IReadOnlyList<ScheduledOperation> schedule, MachineSettings machine, PlaybackOptions requested)
    {
        requested.Validate();
        var cycleDuration = schedule.Count == 0 ? 0 : schedule.Max(x => x.EndTime);
        if (!double.IsFinite(cycleDuration) || cycleDuration <= 0)
            throw new InvalidOperationException("계산된 작업이 없습니다. 작업을 확인하고 MCC를 계산하세요.");
        if (requested.Infinite || requested.RepeatCount > 1) ValidateCycleClosure(schedule, machine);
        if (!requested.Infinite && !double.IsFinite(cycleDuration * requested.RepeatCount))
            throw new InvalidOperationException("전체 반복 시간이 너무 큽니다.");
        options = requested.Copy(); duration = cycleDuration;
        Reset(); State = PlaybackState.Running;
    }
    public static void ValidateCycleClosure(IReadOnlyList<ScheduledOperation> schedule, MachineSettings machine)
    {
        machine.Validate();
        var endTime = schedule.Count == 0 ? 0 : schedule.Max(x => x.EndTime);
        var errors = new List<string>();
        foreach (var resource in MachineAxes.Resources)
        {
            var final = MachineStateEvaluator.PositionAt(schedule, resource, endTime, machine);
            var initial = machine.InitialPosition(resource);
            if (Math.Abs(final - initial) > 1e-8)
                errors.Add($"{resource}: 종료 {final:0.###} → 초기 {initial:0.###} mm");
        }
        if (errors.Count > 0)
            throw new InvalidOperationException("연속 반복 전에 복귀 작업이 필요합니다. " + string.Join(" / ", errors));
    }
    public void Advance(double elapsedSeconds)
    {
        if (State != PlaybackState.Running) return;
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < ElapsedSeconds)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (!options.Infinite && elapsedSeconds >= duration * options.RepeatCount)
        {
            ElapsedSeconds = duration * options.RepeatCount; CycleTime = duration;
            CompletedCycles = options.RepeatCount; CurrentCycle = options.RepeatCount;
            State = PlaybackState.Completed; return;
        }
        var completed = Math.Floor(elapsedSeconds / duration);
        if (completed >= long.MaxValue - 1d) throw new InvalidOperationException("반복 카운터 범위를 초과했습니다.");
        ElapsedSeconds = elapsedSeconds; CompletedCycles = (long)completed;
        CurrentCycle = CompletedCycles + 1; CycleTime = elapsedSeconds % duration;
    }
    public void Pause() { if (State == PlaybackState.Running) State = PlaybackState.Paused; }
    public void Resume() { if (State == PlaybackState.Paused) State = PlaybackState.Running; }
    public void Stop() { if (State is PlaybackState.Running or PlaybackState.Paused) State = PlaybackState.Stopped; }
    public void Reset()
    {
        State = PlaybackState.Ready; ElapsedSeconds = 0; CycleTime = 0;
        CompletedCycles = 0; CurrentCycle = 1;
    }
}
