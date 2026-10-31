using MccDesigner.Models;
namespace MccDesigner.Services;
public sealed class Scheduler
{
    public IReadOnlyList<ScheduledOperation> Calculate(IReadOnlyList<OperationDefinition> operations)
    {
        var result = new List<ScheduledOperation>();
        var resourceEnd = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var cursor = 0d;
        foreach (var op in operations)
        {
            if (string.IsNullOrWhiteSpace(op.Name)) throw new InvalidOperationException("작업 이름은 비워 둘 수 없습니다.");
            if (string.IsNullOrWhiteSpace(op.Resource)) throw new InvalidOperationException($"'{op.Name}' 작업의 장비를 지정하세요.");
            if (!double.IsFinite(op.DurationSeconds) || op.DurationSeconds <= 0)
                throw new InvalidOperationException($"'{op.Name}' 작업 시간은 0보다 커야 합니다.");
            var start = Math.Max(cursor, resourceEnd.GetValueOrDefault(op.Resource));
            var end = start + op.DurationSeconds;
            result.Add(new ScheduledOperation { Id = op.Id, Name = op.Name, Resource = op.Resource,
                StartTime = start, EndTime = end, IsMotion = op.IsMotion,
                StartPosition = op.StartPosition, EndPosition = op.EndPosition, TargetLane = op.TargetLane });
            resourceEnd[op.Resource] = end;
            cursor = end;
        }
        return result;
    }
}
