using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using MccDesigner.Models;
using MccDesigner.Mvvm;
using MccDesigner.Services;
namespace MccDesigner.ViewModels;
public sealed class MainViewModel : ObservableObject
{
    private readonly Scheduler _scheduler = new();
    private OperationDefinition? _selectedOperation;
    private IReadOnlyList<ScheduledOperation> _scheduled = Array.Empty<ScheduledOperation>();
    private string _status = "작업을 편집한 뒤 MCC 계산을 누르세요.";
    private double _currentTime;
    private double _simulatorZoom = 200;
    public double SimulatorZoom { get => _simulatorZoom; set => SetProperty(ref _simulatorZoom, double.IsFinite(value) ? Math.Clamp(value, 100, 400) : 200); }
    private bool _supplyEnabled = true;
    private double _supplyInterval1 = 30;
    private double _supplyInterval2 = 30;
    private int _supplyBatchSize = 1;
    private bool _supplyAtStart;
    public bool SupplyEnabled { get => _supplyEnabled; set { if (SetProperty(ref _supplyEnabled, value)) { RefreshPredecessorChoices(); OnPropertyChanged(nameof(TimelineHint)); } } }
    public double SupplyInterval1 { get => _supplyInterval1; set => SetProperty(ref _supplyInterval1, value); }
    public double SupplyInterval2 { get => _supplyInterval2; set => SetProperty(ref _supplyInterval2, value); }
    public int SupplyBatchSize { get => _supplyBatchSize; set => SetProperty(ref _supplyBatchSize, value); }
    public bool SupplyAtStart { get => _supplyAtStart; set => SetProperty(ref _supplyAtStart, value); }
    public MediaSupplySettings SupplySettings => new() { Enabled = SupplyEnabled, IntervalSeconds = SupplyInterval1, Stage1IntervalSeconds = SupplyInterval1, Stage2IntervalSeconds = SupplyInterval2, BatchSize = SupplyBatchSize, SupplyAtStart = SupplyAtStart };
    public bool ProductionView { get; private set; }
    public double TimelineStart { get; private set; }
    private IReadOnlyList<ScheduledOperation> plannedOperations = Array.Empty<ScheduledOperation>();
    private bool completeProductionPlan;
    public string TimelineHint => !SupplyEnabled ? "클릭·드래그: 시간 탐색 (일시정지) · 연한 막대: 스캔 중 헤드 점유" : completeProductionPlan ? "전체 예정 작업 표시 · 빨간 선: 현재 시간 · 연한 막대: 스캔 중 헤드 점유" : "무한·대량 반복: 일정 구간의 예정 작업을 미리 표시 · 빨간 선: 현재 시간";
    private long forecastThroughRound;
    private long lastForecastHighest;
    public double DisplayDuration => SupplyEnabled && plannedOperations.Count > 0
        ? Math.Max(1, plannedOperations.Max(x => x.EndTime) - TimelineStart) : TotalDuration;
    public IReadOnlyList<ScheduledOperation> DisplayOperations => SupplyEnabled && plannedOperations.Count > 0 ? plannedOperations : ScheduledOperations;
    public MachineState? LivePose { get; private set; }
    private string _supplyStatus = "전처리 공급 대기 · 첫 공급 30초";
    private bool _hasWaitingMedia;
    public string SupplyStatus { get => _supplyStatus; set => SetProperty(ref _supplyStatus, value); }
    public bool HasWaitingMedia { get => _hasWaitingMedia; set => SetProperty(ref _hasWaitingMedia, value); }
    private MediaProductionSession? displayedSession;
    private int displayedVersion = -1;
    public void SetProductionFrame(MediaProductionSession? session)
    {
        ProductionView = session != null;
        var changed = !ReferenceEquals(session, displayedSession) || session?.Version != displayedVersion;
        if (session != null && !completeProductionPlan &&
            (!ReferenceEquals(session, displayedSession) || (session.HighestRound >= forecastThroughRound - 1 && session.HighestRound != lastForecastHighest)))
        {
            var future = session.Forecast(); plannedOperations = future.Operations;
            forecastThroughRound = future.ThroughRound; lastForecastHighest = session.HighestRound;
            TimelineStart = Math.Max(0, session.ElapsedSeconds - 120);
        }
        displayedSession = session; displayedVersion = session?.Version ?? -1;
        LivePose = session?.MachineState;
        if (session is null || completeProductionPlan) TimelineStart = 0;
        if (session != null)
        {
            HasWaitingMedia = session.WaitingMedia > 0;
            SupplyStatus = $"1열 전처리 · 공급 {session.SuppliedFor(MachineAxes.Stage1)} / 전달 {session.ConsumedFor(MachineAxes.Stage1)} / 대기 {session.WaitingFor(MachineAxes.Stage1)}매\n" +
                $"2열 전처리 · 공급 {session.SuppliedFor(MachineAxes.Stage2)} / 전달 {session.ConsumedFor(MachineAxes.Stage2)} / 대기 {session.WaitingFor(MachineAxes.Stage2)}매" +
                (session.LoadingStages.Length > 0 ? $" · {session.LoadingStages} 로딩 중" : "");
            if (changed)
            {
                var times = ScheduledOperations.ToDictionary(x => x.Id, x => x.EndTime);
                foreach (var (id, end) in session.LatestEndTimes) times[id] = end;
                OperationEndTimes = times; OnPropertyChanged(nameof(OperationEndTimes));
            }
            CurrentTime = session.ElapsedSeconds;
        }
        else
        {
            HasWaitingMedia = false;
            SupplyStatus = SupplyEnabled ? $"열별 독립 공급 · 1열 {SupplyInterval1:0.###}초 / 2열 {SupplyInterval2:0.###}초 · 열당 {SupplyBatchSize}매" + (SupplyAtStart ? " · 시작 즉시 첫 공급" : "") : "전처리 연동 해제";
            OperationEndTimes = ScheduledOperations.ToDictionary(x => x.Id, x => x.EndTime);
            OnPropertyChanged(nameof(OperationEndTimes));
        }
        OnPropertyChanged(nameof(ProductionFrame));
    }
    public int ProductionFrame => 0;
    private int _repeatCount = 1;
    private bool _infiniteRepeat;
    private bool _playbackActive;
    private string _playbackProgress = "대기 · 완료 0회 · 누적 0.0 s";
    public int RepeatCount { get => _repeatCount; set => SetProperty(ref _repeatCount, value); }
    public bool InfiniteRepeat { get => _infiniteRepeat; set => SetProperty(ref _infiniteRepeat, value); }
    public bool PlaybackActive { get => _playbackActive; set { if (SetProperty(ref _playbackActive, value)) OnPropertyChanged(nameof(CanEditPlayback)); } }
    public bool CanEditPlayback => !PlaybackActive;
    public string PlaybackProgress { get => _playbackProgress; set => SetProperty(ref _playbackProgress, value); }
    public PlaybackOptions PlaybackOptions => new() { RepeatCount = RepeatCount, Infinite = InfiniteRepeat };
    public MachineSettings Machine { get; private set; } = new();
    public string AxisHint => $"위치 단위: mm · Stage=X / Head=Y · 1열 Y={Machine.Stage1Y:0.###} / 2열 Y={Machine.Stage2Y:0.###}";
    public RelayCommand EditMachineCommand { get; }
    public IReadOnlyDictionary<Guid, string> DependencyLabels { get; private set; } = new Dictionary<Guid, string>();
    public ObservableCollection<DependencyChoice> PredecessorChoices { get; } = [new(Guid.Empty, "기본 (이전 완료 대기 설정)")];
    public ObservableCollection<OperationDefinition> Operations { get; } = new();
    public IReadOnlyDictionary<Guid, double> OperationEndTimes { get; private set; } = new Dictionary<Guid, double>();
    public IReadOnlyList<ScheduledOperation> ScheduledOperations
    {
        get => _scheduled;
        private set
        {
            if (!SetProperty(ref _scheduled, value)) return;
            OperationEndTimes = value.ToDictionary(operation => operation.Id, operation => operation.EndTime);
            OnPropertyChanged(nameof(OperationEndTimes));
            OnPropertyChanged(nameof(TotalDuration));
        }
    }
    public OperationDefinition? SelectedOperation { get => _selectedOperation; set { SetProperty(ref _selectedOperation, value); RefreshSelectionCommands(); } }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public double CurrentTime { get => _currentTime; set { if (SetProperty(ref _currentTime, value)) OnPropertyChanged(nameof(CurrentTimeLabel)); } }
    public string CurrentTimeLabel => $"재생 시간: {CurrentTime:0.0} s";
    public double TotalDuration => ScheduledOperations.Count == 0 ? 0 : ScheduledOperations.Max(x => x.EndTime);
    public int OperationCount => Operations.Count;
    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand CalculateCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand LoadCommand { get; }
    public RelayCommand ResetTimeCommand { get; }
    public MainViewModel()
    {
        EditMachineCommand = new(EditMachine);
        AddCommand = new(AddOperation);
        RemoveCommand = new(RemoveOperation, () => SelectedOperation != null);
        MoveUpCommand = new(() => MoveSelected(-1), () => SelectedOperation != null && Operations.IndexOf(SelectedOperation) > 0);
        MoveDownCommand = new(() => MoveSelected(1), () => SelectedOperation != null && Operations.IndexOf(SelectedOperation) < Operations.Count - 1);
        CalculateCommand = new(Calculate);
        SaveCommand = new(SaveProject);
        LoadCommand = new(LoadProject);
        ResetTimeCommand = new(() => CurrentTime = 0);
        Operations.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(OperationCount)); RefreshSelectionCommands(); };
        foreach (var operation in DemoProject.Create()) Operations.Add(operation);
        Calculate();
    }
    private void EditMachine()
    {
        var dialog = new Views.MachineSettingsWindow(Machine) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;
        Machine = dialog.Settings;
        OnPropertyChanged(nameof(Machine)); OnPropertyChanged(nameof(AxisHint));
        Calculate();
    }
    private void AddOperation()
    {
        var op = new OperationDefinition { Name = $"Operation {Operations.Count + 1}", Resource = "Stage 1", DurationSeconds = 1.0 };
        Operations.Add(op); SelectedOperation = op; Calculate();
    }
    private void RemoveOperation()
    {
        if (SelectedOperation is null) return;
        Operations.Remove(SelectedOperation); SelectedOperation = null; Calculate();
    }
    private void RefreshSelectionCommands()
    {
        RemoveCommand.RaiseCanExecuteChanged();
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
    }
    private void MoveSelected(int offset)
    {
        if (SelectedOperation is null) return;
        var selected = SelectedOperation;
        var index = Operations.IndexOf(selected);
        var destination = index + offset;
        if (index < 0 || destination < 0 || destination >= Operations.Count) return;
        Operations.Move(index, destination);
        SelectedOperation = selected;
        Calculate();
    }
    private void RefreshPredecessorChoices(IReadOnlyList<OperationDefinition>? source = null)
    {
        source ??= Operations.ToList();
        var labels = new Dictionary<Guid, string> { [Guid.Empty] = "기본 (이전 완료 대기 설정)" };
        for (var i = 0; i < source.Count; i++)
            if (source[i].Id != Guid.Empty) labels.TryAdd(source[i].Id, $"{i + 1}. {source[i].Name}");
        foreach (var op in source.Where(op => op.HasExplicitPredecessor))
            labels.TryAdd(op.PredecessorId, $"누락된 작업 ({op.PredecessorId.ToString()[..8]})");
        var summaries = new Dictionary<Guid, string>();
        for (var i = 0; i < source.Count; i++)
        {
            var op = source[i];
            summaries[op.Id] = op.HasExplicitPredecessor ? labels[op.PredecessorId]
                : op.WaitForPrevious && i > 0 ? $"이전 → {i}. {source[i - 1].Name}"
                : SupplyEnabled && op.IsLoading ? "미디어 공급 + 스테이지 준비"
                : "없음 (자원만 대기)";
        }
        DependencyLabels = summaries; OnPropertyChanged(nameof(DependencyLabels));
        // Retain choice instances and dangling references so UI refresh cannot silently erase a dependency.
        foreach (var choice in PredecessorChoices.ToList())
            if (!labels.ContainsKey(choice.Id)) PredecessorChoices.Remove(choice);
        foreach (var (id, label) in labels)
        {
            var choice = PredecessorChoices.FirstOrDefault(x => x.Id == id);
            if (choice is null) PredecessorChoices.Add(new(id, label));
            else choice.Label = label;
        }
        var orderedIds = labels.Keys.ToList();
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var current = PredecessorChoices.IndexOf(PredecessorChoices.First(x => x.Id == orderedIds[i]));
            if (current != i) PredecessorChoices.Move(current, i);
        }
    }
    public void RefreshCalculationAfterEdit() { OnPropertyChanged(nameof(OperationCount)); Calculate(); }
    private void ApplyProductionPlan(ProductionPlan plan)
    {
        plannedOperations = plan.Operations; completeProductionPlan = plan.Complete;
        forecastThroughRound = plan.ThroughRound; lastForecastHighest = 0; TimelineStart = 0;
        OnPropertyChanged(nameof(TimelineHint));
        ScheduledOperations = plan.FirstCycle;
    }
    private void Calculate()
    {
        RefreshPredecessorChoices();
        try
        {
            SupplySettings.Validate();
            if (SupplyEnabled) ApplyProductionPlan(ProductionPlan.Create(Operations.ToList(), Machine, SupplySettings, PlaybackOptions));
            else { plannedOperations = Array.Empty<ScheduledOperation>(); ScheduledOperations = _scheduler.Calculate(Operations.ToList(), Machine); }
            CurrentTime = 0; Status = $"계산 완료: {ScheduledOperations.Count}개 작업, 첫 사이클 {TotalDuration:0.###}초 · 예정 작업 미리 표시";
        }
        catch (Exception ex) { plannedOperations = Array.Empty<ScheduledOperation>(); ScheduledOperations = Array.Empty<ScheduledOperation>(); CurrentTime = 0; Status = $"계산 오류: {ex.Message}"; }
    }
    private void SaveProject()
    {
        var dlg = new SaveFileDialog { Filter = "MCC 프로젝트 (*.json)|*.json", FileName = "MccProject.json" };
        if (dlg.ShowDialog() != true) return;
        try { File.WriteAllText(dlg.FileName, ProjectSerializer.Serialize(Operations.ToList(), Machine, PlaybackOptions, SupplySettings)); Status = $"저장 완료: {dlg.FileName}"; }
        catch (Exception ex) { Status = $"저장 실패: {ex.Message}"; MessageBox.Show(Status); }
    }
    private void LoadProject()
    {
        var dlg = new OpenFileDialog { Filter = "MCC 프로젝트 (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        try {
            var loaded = ProjectSerializer.DeserializeProject(File.ReadAllText(dlg.FileName));
            var plan = loaded.Supply.Enabled ? ProductionPlan.Create(loaded.Operations, loaded.Machine, loaded.Supply, loaded.Playback) : null;
            var calculated = plan?.FirstCycle ?? _scheduler.Calculate(loaded.Operations, loaded.Machine);
            SupplyEnabled = loaded.Supply.Enabled; SupplyInterval1 = loaded.Supply.Stage1IntervalSeconds; SupplyInterval2 = loaded.Supply.Stage2IntervalSeconds; SupplyBatchSize = loaded.Supply.BatchSize; SupplyAtStart = loaded.Supply.SupplyAtStart;
            RepeatCount = loaded.Playback.RepeatCount; InfiniteRepeat = loaded.Playback.Infinite;
            Machine = loaded.Machine; OnPropertyChanged(nameof(Machine)); OnPropertyChanged(nameof(AxisHint));
            RefreshPredecessorChoices(loaded.Operations);
            Operations.Clear(); foreach (var op in loaded.Operations) Operations.Add(op);
            SelectedOperation = null;
            if (plan != null) ApplyProductionPlan(plan);
            else { plannedOperations = Array.Empty<ScheduledOperation>(); ScheduledOperations = calculated; }
            CurrentTime = 0; Status = $"불러오기 완료: {dlg.FileName}" + (loaded.ImportedLegacyUnits ? " · 이전 좌표값을 변경 없이 1단위=1 mm로 읽었습니다. 설비 설정을 확인하세요." : "");
        } catch (Exception ex) { Status = $"불러오기 실패: {ex.Message}"; MessageBox.Show(Status); }
    }
}
