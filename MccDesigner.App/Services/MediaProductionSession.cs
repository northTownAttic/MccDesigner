using MccDesigner.Models;
namespace MccDesigner.Services;

/// <summary>Discrete events; stage cycles can overlap. A media queue is a count, not a list of sheets.</summary>
public sealed class MediaProductionSession
{
    private sealed class Job(OperationDefinition definition, int index, long round)
    {
        public OperationDefinition Definition { get; } = definition;
        public int Index { get; } = index;
        public long Round { get; } = round;
        public List<Job> Parents { get; } = new();
        public int State; // 0 pending, 1 running, 2 complete
        public double Start, End;
        public string[] Resources { get; } = definition.RequiresScannerHead ? [definition.Resource, MachineAxes.Head] : [definition.Resource];
    }
    private readonly List<OperationDefinition> template;
    private readonly MachineSettings machine;
    private readonly MediaSupplySettings supply;
    private readonly PlaybackOptions options;
    private readonly Dictionary<Guid, Guid?> predecessors = new();
    private readonly Dictionary<string, Guid[]> stageOrder = new();
    private readonly Guid[] headOrder;
    private readonly List<Job[]> rounds = new();
    private readonly Dictionary<string, Job> stageTail = new();
    private Job? headTail;
    private readonly List<Job> running = new();
    private readonly HashSet<string> busy = new();
    private readonly Dictionary<string, double> position = new();
    private readonly Queue<ScheduledOperation> history = new();
    private readonly bool retainFullPreview;
    private long? roundLimit;
    public long HighestRound => highestRound;
    private long highestRound;
    private readonly Dictionary<Guid, double> latestEnds = new();
    public IReadOnlyDictionary<Guid, double> LatestEndTimes => latestEnds;
    public string LoadingStages => string.Join(", ", running.Where(x => x.Definition.IsLoading).Select(x => x.Definition.Resource));
    private readonly Dictionary<string, long> consumed = new() { [MachineAxes.Stage1] = 0, [MachineAxes.Stage2] = 0 };
    private long completed1, completed2;
    public PlaybackState State { get; private set; } = PlaybackState.Ready;
    public double ElapsedSeconds { get; private set; }
    public long CompletedStage1 => completed1;
    public long CompletedStage2 => completed2;
    public long CompletedCycles => Math.Min(completed1, completed2);
    public long ConsumedMedia => consumed.Values.Sum();
    public long SuppliedMedia => SuppliedFor(MachineAxes.Stage1) + SuppliedFor(MachineAxes.Stage2);
    public long SuppliedFor(string stage) => Produced(stage, ElapsedSeconds);
    public long ConsumedFor(string stage) => consumed[stage];
    public long WaitingFor(string stage) => SuppliedFor(stage) - consumed[stage];
    public long WaitingMedia => WaitingFor(MachineAxes.Stage1) + WaitingFor(MachineAxes.Stage2);
    public int Version { get; private set; }
    public int RetainedJobCount => rounds.Sum(x => x.Length) + history.Count;
    public IReadOnlyList<ScheduledOperation> VisibleOperations => history.Concat(running.Select(Snapshot)).OrderBy(x => x.StartTime).ToArray();
    public MachineState MachineState => new(Sample(MachineAxes.Stage1), Sample(MachineAxes.Stage2), Sample(MachineAxes.Head));

    public MediaProductionSession(IReadOnlyList<OperationDefinition> operations, MachineSettings machine,
        MediaSupplySettings supply, PlaybackOptions options, bool retainFullPreview = false)
    {
        machine.Validate(); supply.Validate(); options.Validate();
        if (retainFullPreview && (options.Infinite || (long)options.RepeatCount * operations.Count > Math.Max(5000, operations.Count))) throw new InvalidOperationException("전체 미리보기 보관 한도를 초과했습니다. 범위 예측을 사용하세요.");
        roundLimit = options.Infinite ? null : options.RepeatCount;
        this.retainFullPreview = retainFullPreview;
        this.machine = machine.Copy(); this.supply = supply.Copy(); this.options = options.Copy();
        var validated = new Scheduler().Calculate(operations, this.machine);
        // Snapshot editable input without retaining UI event subscriptions.
        template = operations.Select(op => new OperationDefinition { Id = op.Id, Name = op.Name, Resource = op.Resource,
            DurationSeconds = op.DurationSeconds, IsMotion = op.IsMotion, StartPosition = op.StartPosition,
            EndPosition = op.EndPosition, WaitForPrevious = op.WaitForPrevious, RequiresScannerHead = op.RequiresScannerHead,
            PredecessorId = op.PredecessorId, IsLoading = op.IsLoading }).ToList();
        var baseline = validated;
        foreach (var stage in MachineAxes.Resources.Take(2))
        {
            var jobs = baseline.Where(x => x.Resource == stage).OrderBy(x => x.StartTime).Select(x => x.Id).ToArray();
            var loads = template.Where(x => x.Resource == stage && x.IsLoading).ToList();
            if (loads.Count != 1 || jobs.Length == 0 || jobs[0] != loads[0].Id)
                throw new InvalidOperationException($"전처리 연동: {stage}의 첫 작업 하나를 ‘미디어 로딩’으로 지정하세요.");
            if (loads[0].HasExplicitPredecessor || (loads[0].WaitForPrevious && template.IndexOf(loads[0]) > 0))
                throw new InvalidOperationException($"'{loads[0].Name}': 미디어 로딩은 선행 작업을 기본으로 두고 ‘이전 완료 대기’를 해제하세요. 공급과 직전 스테이지 사이클 완료를 기다립니다.");
            if (!template.Any(x => x.Resource == stage && x.RequiresScannerHead))
                throw new InvalidOperationException($"전처리 연동: {stage}에 ‘헤드 사용’ 스캔 작업이 필요합니다.");
            if (loads[0].RequiresScannerHead) throw new InvalidOperationException($"'{loads[0].Name}': 미디어 로딩의 ‘헤드 사용’을 해제하세요.");
            stageOrder[stage] = jobs;
        }
        if (template.Any(x => x.IsLoading && x.Resource == MachineAxes.Head))
            throw new InvalidOperationException("미디어 로딩은 스테이지 작업에만 지정할 수 있습니다.");
        if (options.Infinite || options.RepeatCount > 1) PlaybackSession.ValidateCycleClosure(baseline, this.machine);
        for (var i = 0; i < template.Count; i++)
        {
            var op = template[i];
            predecessors[op.Id] = op.HasExplicitPredecessor ? op.PredecessorId : op.WaitForPrevious && i > 0 ? template[i - 1].Id : null;
        }
        headOrder = baseline.Where(x => x.RequiredResources.Contains(MachineAxes.Head)).OrderBy(x => x.StartTime).Select(x => x.Id).ToArray();
        foreach (var resource in MachineAxes.Resources) position[resource] = this.machine.InitialPosition(resource);
        AddRound(1); State = PlaybackState.Running; Dispatch();
    }
    private MediaProductionSession(MediaProductionSession source, long forecastLimit)
    {
        template = source.template; machine = source.machine; supply = source.supply; options = source.options;
        predecessors = source.predecessors; stageOrder = source.stageOrder; headOrder = source.headOrder;
        retainFullPreview = true; roundLimit = forecastLimit;
        highestRound = source.highestRound; completed1 = source.completed1; completed2 = source.completed2;
        ElapsedSeconds = source.ElapsedSeconds; State = PlaybackState.Running; Version = source.Version;
        foreach (var (key, value) in source.consumed) consumed[key] = value;
        foreach (var (key, value) in source.position) position[key] = value;
        foreach (var (key, value) in source.latestEnds) latestEnds[key] = value;
        busy.UnionWith(source.busy);
        foreach (var item in source.history) history.Enqueue(item);
        var copies = new Dictionary<Job, Job>();
        var queue = new Queue<Job>();
        Job Copy(Job original)
        {
            if (copies.TryGetValue(original, out var existing)) return existing;
            var copy = new Job(original.Definition, original.Index, original.Round) { State = original.State, Start = original.Start, End = original.End };
            copies.Add(original, copy); queue.Enqueue(original); return copy;
        }
        foreach (var round in source.rounds) rounds.Add(round.Select(Copy).ToArray());
        running.AddRange(source.running.Select(Copy));
        foreach (var (stage, tail) in source.stageTail) stageTail[stage] = Copy(tail);
        headTail = source.headTail is null ? null : Copy(source.headTail);
        while (queue.TryDequeue(out var original))
            foreach (var parent in original.Parents) copies[original].Parents.Add(Copy(parent));
    }
    /// <summary>Plan a bounded future from a copy; production clocks, queues and counters stay untouched.</summary>
    public ProductionForecast Forecast(int roundsAhead = 2)
    {
        if (roundsAhead < 1 || roundsAhead > 3) throw new ArgumentOutOfRangeException(nameof(roundsAhead));
        var limit = highestRound + roundsAhead;
        if (roundLimit is long final) limit = Math.Min(limit, final);
        var copy = new MediaProductionSession(this, limit);
        copy.RunToCompletion();
        return new(limit, copy.VisibleOperations);
    }
    public void Advance(double time)
    {
        if (State != PlaybackState.Running) return;
        if (!double.IsFinite(time) || time < ElapsedSeconds) throw new ArgumentOutOfRangeException(nameof(time));
        while (State == PlaybackState.Running)
        {
            Dispatch();
            if (rounds.Count == 0 && running.Count == 0) { State = PlaybackState.Completed; break; }
            var next = running.Count == 0 ? double.PositiveInfinity : running.Min(x => x.End);
            foreach (var loader in rounds.SelectMany(x => x).Where(x => x.State == 0 && x.Definition.IsLoading && x.Parents.All(p => p.State == 2)))
                if (WaitingFor(loader.Definition.Resource) == 0) next = Math.Min(next, NextSupply(loader.Definition.Resource));
            if (!double.IsFinite(next)) throw new InvalidOperationException("전처리 시퀀스를 진행할 수 없습니다. 선행 작업과 스테이지 복귀 순서를 확인하세요.");
            if (next > time) { ElapsedSeconds = time; break; }
            if (next <= ElapsedSeconds) throw new InvalidOperationException("전처리 시간 정밀도 범위를 벗어났습니다.");
            ElapsedSeconds = next;
            foreach (var job in running.Where(x => x.End <= next).ToList()) Complete(job);
            rounds.RemoveAll(round => round.All(job => job.State == 2));
            PruneHistory();
        }
    }
    public void RunToCompletion()
    {
        if (roundLimit is null) throw new InvalidOperationException("무한 반복은 일괄 계산할 수 없습니다.");
        Advance(double.MaxValue);
    }
    public void Pause() { if (State == PlaybackState.Running) State = PlaybackState.Paused; }
    public void Resume() { if (State == PlaybackState.Paused) State = PlaybackState.Running; }
    public void Stop() { if (State is PlaybackState.Running or PlaybackState.Paused) State = PlaybackState.Stopped; }

    private long Produced(string stage, double time)
    {
        var batches = SupplyPeriods(stage, time) + (supply.SupplyAtStart ? 1 : 0);
        var count = batches * supply.BatchSize;
        if (count >= long.MaxValue) throw new InvalidOperationException("전처리 공급 수량 범위를 초과했습니다.");
        return (long)count;
    }
    private double SupplyPeriods(string stage, double time)
    {
        var ratio = time / supply.IntervalFor(stage);
        var integer = Math.Round(ratio);
        if (Math.Abs(ratio - integer) <= 2 * Math.Abs(Math.BitIncrement(ratio) - ratio)) ratio = integer;
        return Math.Floor(ratio);
    }
    private double NextSupply(string stage)
    {
        var next = (SupplyPeriods(stage, ElapsedSeconds) + 1) * supply.IntervalFor(stage);
        if (next <= ElapsedSeconds || !double.IsFinite(next)) throw new InvalidOperationException("전처리 공급 시간 정밀도 범위를 초과했습니다.");
        return next;
    }
    private void AddRound(long number)
    {
        if (roundLimit is long limit && number > limit) return;
        var jobs = template.Select((op, index) => new Job(op, index, number)).ToArray();
        var map = jobs.ToDictionary(x => x.Definition.Id);
        foreach (var job in jobs)
            if (!job.Definition.IsLoading && predecessors[job.Definition.Id] is Guid id) job.Parents.Add(map[id]);
        foreach (var (stage, order) in stageOrder)
        {
            var first = map[order[0]];
            if (stageTail.TryGetValue(stage, out var previous)) first.Parents.Add(previous);
            for (var i = 1; i < order.Length; i++) map[order[i]].Parents.Add(map[order[i - 1]]);
            stageTail[stage] = map[order[^1]];
        }
        if (headTail is not null) map[headOrder[0]].Parents.Add(headTail);
        for (var i = 1; i < headOrder.Length; i++) map[headOrder[i]].Parents.Add(map[headOrder[i - 1]]);
        headTail = map[headOrder[^1]];
        rounds.Add(jobs); highestRound = number;
    }
    private void Dispatch()
    {
        var ready = rounds.SelectMany(x => x).Where(x => x.State == 0 && x.Parents.All(p => p.State == 2))
            .OrderBy(x => x.Definition.IsLoading ? 0 : 1)
            .ThenBy(x => x.Definition.IsLoading ? x.Parents.Select(p => p.End).DefaultIfEmpty(0).Max() : 0)
            .ThenBy(x => x.Definition.IsLoading && x.Definition.Resource == MachineAxes.Stage1 ? 0 : 1)
            .ThenBy(x => x.Round).ThenBy(x => x.Index).ToList();
        foreach (var job in ready)
        {
            if (job.Resources.Any(busy.Contains) || (job.Definition.IsLoading && WaitingFor(job.Definition.Resource) == 0)) continue;
            var op = job.Definition;
            if (op.IsMotion && Math.Abs(op.StartPosition - position[op.Resource]) > 1e-8)
                throw new InvalidOperationException($"'{op.Name}' 시작 위치가 {op.Resource}의 현재 위치와 다릅니다.");
            if (op.RequiresScannerHead && Math.Abs(position[MachineAxes.Head] - machine.ScanY(op.Resource)) > 1e-8)
                throw new InvalidOperationException($"'{op.Name}' 실행 전에 헤드를 해당 열로 이동해야 합니다.");
            job.Start = ElapsedSeconds; job.End = ElapsedSeconds + op.DurationSeconds;
            if (!double.IsFinite(job.End) || job.End <= job.Start) throw new InvalidOperationException("생산 동작 시간 범위를 초과했습니다.");
            if (op.IsLoading) consumed[op.Resource]++;
            latestEnds[op.Id] = job.End;
            job.State = 1; job.Parents.Clear(); running.Add(job);
            foreach (var resource in job.Resources) busy.Add(resource);
            Version++;
        }
    }
    private void Complete(Job job)
    {
        job.State = 2; running.Remove(job);
        foreach (var resource in job.Resources) busy.Remove(resource);
        if (job.Definition.IsMotion) position[job.Definition.Resource] = job.Definition.EndPosition;
        history.Enqueue(Snapshot(job)); Version++;
        if (job.Definition.Resource != MachineAxes.Head && job.Definition.Id == stageOrder[job.Definition.Resource][^1])
        {
            if (job.Definition.Resource == MachineAxes.Stage1) completed1++; else completed2++;
            if (job.Round == highestRound) AddRound(highestRound + 1);
        }
    }
    private void PruneHistory()
    {
        if (retainFullPreview) return;
        while (history.Count > 500 || (history.Count > 0 && history.Peek().EndTime < ElapsedSeconds - 180)) history.Dequeue();
    }
    private ScheduledOperation Snapshot(Job job) => new() { Id = job.Definition.Id,
        Name = $"{job.Definition.Name} [{job.Round}회]", Resource = job.Definition.Resource,
        StartTime = job.Start, EndTime = job.End, IsMotion = job.Definition.IsMotion,
        StartPosition = job.Definition.StartPosition, EndPosition = job.Definition.EndPosition,
        RequiredResources = job.Resources, RequiresScannerHead = job.Definition.RequiresScannerHead };
    private double Sample(string resource)
    {
        var job = running.FirstOrDefault(x => x.Definition.Resource == resource && x.Definition.IsMotion);
        if (job is null) return position[resource];
        var fraction = Math.Clamp((ElapsedSeconds - job.Start) / job.Definition.DurationSeconds, 0, 1);
        return job.Definition.StartPosition + (job.Definition.EndPosition - job.Definition.StartPosition) * fraction;
    }
}

public sealed record ProductionForecast(long ThroughRound, IReadOnlyList<ScheduledOperation> Operations);
