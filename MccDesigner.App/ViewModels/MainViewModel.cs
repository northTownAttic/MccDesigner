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
    private void Calculate()
    {
        RefreshPredecessorChoices();
        try { ScheduledOperations = _scheduler.Calculate(Operations.ToList(), Machine); CurrentTime = 0; Status = $"계산 완료: {ScheduledOperations.Count}개 작업, 총 {TotalDuration:0.###}초 (순차 합계 {Operations.Sum(x => x.DurationSeconds):0.###}초)"; }
        catch (Exception ex) { ScheduledOperations = Array.Empty<ScheduledOperation>(); CurrentTime = 0; Status = $"계산 오류: {ex.Message}"; }
    }
    private void SaveProject()
    {
        var dlg = new SaveFileDialog { Filter = "MCC 프로젝트 (*.json)|*.json", FileName = "MccProject.json" };
        if (dlg.ShowDialog() != true) return;
        try { File.WriteAllText(dlg.FileName, ProjectSerializer.Serialize(Operations.ToList(), Machine, PlaybackOptions)); Status = $"저장 완료: {dlg.FileName}"; }
        catch (Exception ex) { Status = $"저장 실패: {ex.Message}"; MessageBox.Show(Status); }
    }
    private void LoadProject()
    {
        var dlg = new OpenFileDialog { Filter = "MCC 프로젝트 (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        try {
            var loaded = ProjectSerializer.DeserializeProject(File.ReadAllText(dlg.FileName));
            var calculated = _scheduler.Calculate(loaded.Operations, loaded.Machine);
            RepeatCount = loaded.Playback.RepeatCount; InfiniteRepeat = loaded.Playback.Infinite;
            Machine = loaded.Machine; OnPropertyChanged(nameof(Machine)); OnPropertyChanged(nameof(AxisHint));
            RefreshPredecessorChoices(loaded.Operations);
            Operations.Clear(); foreach (var op in loaded.Operations) Operations.Add(op);
            SelectedOperation = null; ScheduledOperations = calculated; CurrentTime = 0; Status = $"불러오기 완료: {dlg.FileName}" + (loaded.ImportedLegacyUnits ? " · 이전 좌표값을 변경 없이 1단위=1 mm로 읽었습니다. 설비 설정을 확인하세요." : "");
        } catch (Exception ex) { Status = $"불러오기 실패: {ex.Message}"; MessageBox.Show(Status); }
    }
}
