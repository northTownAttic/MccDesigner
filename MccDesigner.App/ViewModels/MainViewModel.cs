using System.IO;
using System.Collections.ObjectModel;
using System.Text.Json;
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
    public ObservableCollection<OperationDefinition> Operations { get; } = new();
    public IReadOnlyList<ScheduledOperation> ScheduledOperations { get => _scheduled; private set { if (SetProperty(ref _scheduled, value)) { OnPropertyChanged(nameof(TotalDuration)); } } }
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
        AddCommand = new(AddOperation);
        RemoveCommand = new(RemoveOperation, () => SelectedOperation != null);
        MoveUpCommand = new(() => MoveSelected(-1), () => SelectedOperation != null && Operations.IndexOf(SelectedOperation) > 0);
        MoveDownCommand = new(() => MoveSelected(1), () => SelectedOperation != null && Operations.IndexOf(SelectedOperation) < Operations.Count - 1);
        CalculateCommand = new(Calculate);
        SaveCommand = new(SaveProject);
        LoadCommand = new(LoadProject);
        ResetTimeCommand = new(() => CurrentTime = 0);
        Operations.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(OperationCount)); RefreshSelectionCommands(); };
        Operations.Add(new() { Name = "Stage 1 Loading", Resource = "Stage 1", DurationSeconds = 5.0 });
        Operations.Add(new() { Name = "Stage 1 Vision", Resource = "Stage 1", DurationSeconds = 3.5 });
        Operations.Add(new() { Name = "Scan Stage 1", Resource = "Gantry / Head", DurationSeconds = 12.5, IsMotion = true, StartPosition = 0, EndPosition = 100, TargetLane = "Stage 1" });
        Operations.Add(new() { Name = "Gantry Return", Resource = "Gantry / Head", DurationSeconds = 3.5, IsMotion = true, StartPosition = 100, EndPosition = 0, TargetLane = "Stage 1", OccupiesTargetLane = false });
        Operations.Add(new() { Name = "Stage 2 Loading", Resource = "Stage 2", DurationSeconds = 6.25, WaitForPrevious = false });
        Operations.Add(new() { Name = "Scan Stage 2", Resource = "Gantry / Head", DurationSeconds = 12.5, IsMotion = true, StartPosition = 0, EndPosition = 100, TargetLane = "Stage 2" });
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
    public void RefreshCalculationAfterEdit() { OnPropertyChanged(nameof(OperationCount)); Calculate(); }
    private void Calculate()
    {
        try { ScheduledOperations = _scheduler.Calculate(Operations.ToList()); CurrentTime = 0; Status = $"계산 완료: {ScheduledOperations.Count}개 작업, 총 {TotalDuration:0.###}초 (순차 합계 {Operations.Sum(x => x.DurationSeconds):0.###}초)"; }
        catch (Exception ex) { ScheduledOperations = Array.Empty<ScheduledOperation>(); CurrentTime = 0; Status = $"계산 오류: {ex.Message}"; }
    }
    private void SaveProject()
    {
        var dlg = new SaveFileDialog { Filter = "MCC 프로젝트 (*.json)|*.json", FileName = "MccProject.json" };
        if (dlg.ShowDialog() != true) return;
        try { _scheduler.Calculate(Operations.ToList()); File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(Operations.ToList(), new JsonSerializerOptions { WriteIndented = true })); Status = $"저장 완료: {dlg.FileName}"; }
        catch (Exception ex) { Status = $"저장 실패: {ex.Message}"; MessageBox.Show(Status); }
    }
    private void LoadProject()
    {
        var dlg = new OpenFileDialog { Filter = "MCC 프로젝트 (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        try {
            var loaded = JsonSerializer.Deserialize<List<OperationDefinition>>(File.ReadAllText(dlg.FileName)) ?? throw new InvalidOperationException("작업 데이터가 없습니다.");
            var calculated = _scheduler.Calculate(loaded);
            Operations.Clear(); foreach (var op in loaded) Operations.Add(op);
            SelectedOperation = null; ScheduledOperations = calculated; CurrentTime = 0; Status = $"불러오기 완료: {dlg.FileName}";
        } catch (Exception ex) { Status = $"불러오기 실패: {ex.Message}"; MessageBox.Show(Status); }
    }
}
