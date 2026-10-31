using MccDesigner.Models;
namespace MccDesigner.Services;
public sealed class Scheduler
{
    public IReadOnlyList<ScheduledOperation> Calculate(IReadOnlyList<OperationDefinition> operations)
    {
        var result = new List<ScheduledOperation>();
        var reservations = new Dictionary<string, List<(double Start, double End)>>();
        var previousEnd = 0d;
        var ids = new HashSet<Guid>();
        foreach (var op in operations)
        {
            if (op is null) throw new InvalidOperationException("작업 데이터가 올바르지 않습니다.");
            if (op.Id == Guid.Empty || !ids.Add(op.Id)) throw new InvalidOperationException("작업 ID가 비어 있거나 중복됩니다.");
            if (op.Resource is not ("Stage 1" or "Stage 2" or "Gantry / Head")) throw new InvalidOperationException("지원하지 않는 장비입니다.");
            if (op.TargetLane is not ("Stage 1" or "Stage 2")) throw new InvalidOperationException("대상 열을 선택하세요.");
            if (!double.IsFinite(op.StartPosition) || !double.IsFinite(op.EndPosition) ||
                op.StartPosition < 0 || op.StartPosition > 100 || op.EndPosition < 0 || op.EndPosition > 100)
                throw new InvalidOperationException("X 위치는 0~100 범위의 유한한 수여야 합니다.");
            if (op.IsMotion && op.Resource != "Gantry / Head") throw new InvalidOperationException("현재 MVP의 이동 동작은 Gantry / Head에 지정하세요.");
            if (string.IsNullOrWhiteSpace(op.Name)) throw new InvalidOperationException("작업 이름은 비워 둘 수 없습니다.");
            if (string.IsNullOrWhiteSpace(op.Resource)) throw new InvalidOperationException($"'{op.Name}' 작업의 장비를 지정하세요.");
            if (!double.IsFinite(op.DurationSeconds) || op.DurationSeconds <= 0)
                throw new InvalidOperationException($"'{op.Name}' 작업 시간은 0보다 커야 합니다.");
            string[] resources = op.Resource == "Gantry / Head" && op.OccupiesTargetLane
                ? [op.Resource, op.TargetLane] : [op.Resource];
            var start = FindAvailableStart(reservations, resources,
                op.WaitForPrevious ? previousEnd : 0, op.DurationSeconds);
            var end = start + op.DurationSeconds;
            if (!double.IsFinite(end) || end <= start) throw new InvalidOperationException("작업 시간이 너무 크거나 시간 정밀도 범위를 벗어났습니다.");
            result.Add(new ScheduledOperation { Id = op.Id, Name = op.Name, Resource = op.Resource,
                RequiredResources = Array.AsReadOnly(resources),
                StartTime = start, EndTime = end, IsMotion = op.IsMotion,
                StartPosition = op.StartPosition, EndPosition = op.EndPosition, TargetLane = op.TargetLane });
            foreach (var resource in resources)
            {
                if (!reservations.TryGetValue(resource, out var intervals))
                    reservations[resource] = intervals = new();
                intervals.Add((start, end));
            }
            previousEnd = end;
        }
        return result;
    }

    // List order defines priority. Independent work can fill earlier idle gaps;
    // all required resources must be free for the complete half-open interval.
    private static double FindAvailableStart(
        Dictionary<string, List<(double Start, double End)>> reservations,
        IReadOnlyList<string> resources, double earliest, double duration)
    {
        var start = earliest;
        while (true)
        {
            var end = start + duration;
            if (!double.IsFinite(end) || end <= start)
                throw new InvalidOperationException("작업 시간이 너무 크거나 시간 정밀도 범위를 벗어났습니다.");
            var next = start;
            foreach (var resource in resources)
                if (reservations.TryGetValue(resource, out var intervals))
                    foreach (var interval in intervals)
                        if (start < interval.End && end > interval.Start)
                            next = Math.Max(next, interval.End);
            if (next == start) return start;
            start = next;
        }
    }
}
