using MccDesigner.Models;
namespace MccDesigner.Services;
public sealed class Scheduler
{
    public IReadOnlyList<ScheduledOperation> Calculate(IReadOnlyList<OperationDefinition> operations, MachineSettings? settings = null)
    {
        settings ??= new MachineSettings();
        settings.Validate();

        var ids = new HashSet<Guid>();
        foreach (var op in operations)
        {
            if (op is null) throw new InvalidOperationException("작업 데이터가 올바르지 않습니다.");
            if (op.Id == Guid.Empty || !ids.Add(op.Id)) throw new InvalidOperationException("작업 ID가 비어 있거나 중복됩니다.");
            if (!MachineAxes.Resources.Contains(op.Resource)) throw new InvalidOperationException("지원하지 않는 장비입니다.");
            if (!double.IsFinite(op.StartPosition) || !double.IsFinite(op.EndPosition) ||
                (op.IsMotion && (op.StartPosition < (op.Resource == MachineAxes.Head ? settings.HeadYMin : settings.StageXMin) ||
                op.StartPosition > (op.Resource == MachineAxes.Head ? settings.HeadYMax : settings.StageXMax) ||
                op.EndPosition < (op.Resource == MachineAxes.Head ? settings.HeadYMin : settings.StageXMin) ||
                op.EndPosition > (op.Resource == MachineAxes.Head ? settings.HeadYMax : settings.StageXMax))))
                throw new InvalidOperationException("축 위치(mm)는 설비 설정의 해당 X/Y 이동 범위 안이어야 합니다.");
            if (op.Resource == MachineAxes.Head && op.RequiresScannerHead)
                throw new InvalidOperationException("헤드 사용은 스테이지의 스캔 작업에만 선택하세요.");
            if (string.IsNullOrWhiteSpace(op.Name)) throw new InvalidOperationException("작업 이름은 비워 둘 수 없습니다.");
            if (string.IsNullOrWhiteSpace(op.Resource)) throw new InvalidOperationException($"'{op.Name}' 작업의 장비를 지정하세요.");
            if (!double.IsFinite(op.DurationSeconds) || op.DurationSeconds <= 0)
                throw new InvalidOperationException($"'{op.Name}' 작업 시간은 0보다 커야 합니다.");
        }
        var indices = operations.Select((op, index) => (op.Id, index)).ToDictionary(x => x.Id, x => x.index);
        var parents = new int?[operations.Count];
        var children = Enumerable.Range(0, operations.Count).Select(_ => new List<int>()).ToArray();
        var indegree = new int[operations.Count];
        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            if (op.HasExplicitPredecessor)
            {
                if (!indices.TryGetValue(op.PredecessorId, out var parent))
                    throw new InvalidOperationException($"'{op.Name}'의 선행 작업을 찾을 수 없습니다. 삭제된 작업인지 확인하세요.");
                if (parent == i) throw new InvalidOperationException($"'{op.Name}'은 자기 자신을 선행 작업으로 지정할 수 없습니다.");
                parents[i] = parent;
            }
            else if (op.WaitForPrevious && i > 0) parents[i] = i - 1;
            if (parents[i] is int predecessor) { children[predecessor].Add(i); indegree[i] = 1; }
        }
        var ready = new PriorityQueue<int, int>();
        for (var i = 0; i < operations.Count; i++) if (indegree[i] == 0) ready.Enqueue(i, i);
        var scheduled = new ScheduledOperation[operations.Count];
        var reservations = new Dictionary<string, List<(double Start, double End)>>();
        var completed = 0;
        while (ready.TryDequeue(out var index, out _))
        {
            var op = operations[index];
            string[] resources = op.RequiresScannerHead ? [op.Resource, MachineAxes.Head] : [op.Resource];
            var earliest = parents[index] is int parent ? scheduled[parent].EndTime : 0;
            var start = FindAvailableStart(reservations, resources, earliest, op.DurationSeconds);
            var end = start + op.DurationSeconds;
            if (!double.IsFinite(end) || end <= start) throw new InvalidOperationException("작업 시간이 너무 크거나 시간 정밀도 범위를 벗어났습니다.");
            scheduled[index] = new ScheduledOperation { Id = op.Id, Name = op.Name, Resource = op.Resource,
                RequiredResources = Array.AsReadOnly(resources), StartTime = start, EndTime = end, IsMotion = op.IsMotion,
                StartPosition = op.StartPosition, EndPosition = op.EndPosition, RequiresScannerHead = op.RequiresScannerHead };
            foreach (var resource in resources)
            {
                if (!reservations.TryGetValue(resource, out var intervals)) reservations[resource] = intervals = new();
                intervals.Add((start, end));
            }
            completed++;
            foreach (var child in children[index]) if (--indegree[child] == 0) ready.Enqueue(child, child);
        }
        if (completed != operations.Count)
            throw new InvalidOperationException("선행 작업이 순환 의존성을 만듭니다: " +
                string.Join(", ", operations.Where((_, index) => indegree[index] > 0).Select(op => op.Name)));
        AxisScheduleValidator.Validate(scheduled, settings);
        return scheduled;
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
