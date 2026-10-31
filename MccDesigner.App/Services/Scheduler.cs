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
            if (!MachineAxes.Resources.Contains(op.Resource)) throw new InvalidOperationException("지원하지 않는 장비입니다.");
            if (!double.IsFinite(op.StartPosition) || !double.IsFinite(op.EndPosition) ||
                op.StartPosition < 0 || op.StartPosition > 100 || op.EndPosition < 0 || op.EndPosition > 100)
                throw new InvalidOperationException("축 위치는 0~100 범위의 유한한 수여야 합니다.");
            if (op.Resource == MachineAxes.Head && op.RequiresScannerHead)
                throw new InvalidOperationException("헤드 사용은 스테이지의 스캔 작업에만 선택하세요.");
            if (string.IsNullOrWhiteSpace(op.Name)) throw new InvalidOperationException("작업 이름은 비워 둘 수 없습니다.");
            if (string.IsNullOrWhiteSpace(op.Resource)) throw new InvalidOperationException($"'{op.Name}' 작업의 장비를 지정하세요.");
            if (!double.IsFinite(op.DurationSeconds) || op.DurationSeconds <= 0)
                throw new InvalidOperationException($"'{op.Name}' 작업 시간은 0보다 커야 합니다.");
            string[] resources = op.RequiresScannerHead
                ? [op.Resource, MachineAxes.Head] : [op.Resource];
            var start = FindAvailableStart(reservations, resources,
                op.WaitForPrevious ? previousEnd : 0, op.DurationSeconds);
            var end = start + op.DurationSeconds;
            if (!double.IsFinite(end) || end <= start) throw new InvalidOperationException("작업 시간이 너무 크거나 시간 정밀도 범위를 벗어났습니다.");
            result.Add(new ScheduledOperation { Id = op.Id, Name = op.Name, Resource = op.Resource,
                RequiredResources = Array.AsReadOnly(resources),
                StartTime = start, EndTime = end, IsMotion = op.IsMotion,
                StartPosition = op.StartPosition, EndPosition = op.EndPosition, RequiresScannerHead = op.RequiresScannerHead });
            foreach (var resource in resources)
            {
                if (!reservations.TryGetValue(resource, out var intervals))
                    reservations[resource] = intervals = new();
                intervals.Add((start, end));
            }
            previousEnd = end;
        }
        AxisScheduleValidator.Validate(result);
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
