using MccDesigner.Models;
namespace MccDesigner.Services;
public sealed class Scheduler
{
    public IReadOnlyList<ScheduledOperation> Calculate(IReadOnlyList<OperationDefinition> operations)
    {
        var result = new List<ScheduledOperation>();
        var resourceEnd = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var cursor = 0d;
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
            var start = Math.Max(cursor, resourceEnd.GetValueOrDefault(op.Resource));
            var end = start + op.DurationSeconds;
            if (!double.IsFinite(end)) throw new InvalidOperationException("전체 작업 시간이 너무 큽니다.");
            result.Add(new ScheduledOperation { Id = op.Id, Name = op.Name, Resource = op.Resource,
                StartTime = start, EndTime = end, IsMotion = op.IsMotion,
                StartPosition = op.StartPosition, EndPosition = op.EndPosition, TargetLane = op.TargetLane });
            resourceEnd[op.Resource] = end;
            cursor = end;
        }
        return result;
    }
}
