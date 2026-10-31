using System.IO;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MccDesigner.Models;
using MccDesigner.Services;
using MccDesigner.ViewModels;
namespace MccDesigner;
public partial class MainWindow : Window
{
    private readonly MainViewModel vm;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly Stopwatch playbackClock = new();
    private readonly PlaybackSession playback = new();
    private int editGeneration;
    private bool operationEditPending;
    private bool committingEdit;
    public MainWindow()
    {
        InitializeComponent();
        vm = new MainViewModel();
        DataContext = vm;
        TimelineScrollViewer.ScrollChanged += (_, e) => {
            if (e.ViewportWidthChange != 0) DrawTimeline();
        };
        MachineCanvas.SizeChanged += (_, _) => DrawMachine();
        OperationsGrid.BeginningEdit += (_, _) => operationEditPending = true;
        timer.Tick += Timer_Tick;
        vm.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(MainViewModel.ScheduledOperations)) operationEditPending = false;
            if (e.PropertyName is nameof(MainViewModel.ScheduledOperations) or nameof(MainViewModel.Machine) or nameof(MainViewModel.RepeatCount) or nameof(MainViewModel.InfiniteRepeat)) ResetPlayback();
            if (e.PropertyName is nameof(MainViewModel.ScheduledOperations) or nameof(MainViewModel.CurrentTime) or nameof(MainViewModel.Machine)) DrawAll();
        };
        Closed += (_, _) => { timer.Stop(); playbackClock.Stop(); };
        Loaded += (_, _) => DrawAll();
        SizeChanged += (_, _) => DrawAll();
    }
    private void OperationsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (committingEdit || e.EditAction != DataGridEditAction.Commit) return;
        PausePlayback();
        var generation = ++editGeneration;
        Dispatcher.BeginInvoke(new Action(() => {
            if (generation != editGeneration) return;
            committingEdit = true;
            try { if (OperationsGrid.CommitEdit(DataGridEditingUnit.Row, true)) vm.RefreshCalculationAfterEdit(); }
            finally { committingEdit = false; }
        }), DispatcherPriority.Background);
    }
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source != null && source is not Button) source = VisualTreeHelper.GetParent(source);
        if (source is not Button { Command: not null }) return;
        PausePlayback();
        editGeneration++;
        committingEdit = true;
        try
        {
            if (!OperationsGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
                !OperationsGrid.CommitEdit(DataGridEditingUnit.Row, true))
            {
                timer.Stop();
                vm.Status = "숫자 입력 오류를 수정한 뒤 다시 시도하세요.";
                e.Handled = true;
            }
        }
        finally { committingEdit = false; }
    }
    private void OperationsGrid_PreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command == DataGrid.DeleteCommand) e.Handled = true;
    }
    private void Reset_Click(object sender, RoutedEventArgs e) => ResetPlayback();
    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (playback.State == PlaybackState.Running) SamplePlayback();
        timer.Stop(); playbackClock.Stop(); playback.Stop(); SyncPlayback();
    }
    private void ResetPlayback()
    {
        timer.Stop(); playbackClock.Reset(); playback.Reset(); SyncPlayback();
    }
    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (playback.State == PlaybackState.Running) return;
        editGeneration++;
        committingEdit = true;
        try
        {
            RepeatCountInput.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (Validation.GetHasError(RepeatCountInput) ||
                !OperationsGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
                !OperationsGrid.CommitEdit(DataGridEditingUnit.Row, true))
            {
                vm.Status = "작업 입력과 반복 횟수를 확인하세요."; return;
            }
        }
        finally { committingEdit = false; }
        try
        {
            if (operationEditPending && playback.State == PlaybackState.Paused) ResetPlayback();
            if (playback.State == PlaybackState.Paused)
            {
                playback.Resume(); playbackClock.Start();
            }
            else
            {
                vm.RefreshCalculationAfterEdit();
                operationEditPending = false;
                if (vm.TotalDuration <= 0) return;
                playback.Start(vm.ScheduledOperations, vm.Machine, vm.PlaybackOptions);
                playbackClock.Restart();
            }
            SyncPlayback(); timer.Start();
        }
        catch (InvalidOperationException error) { vm.Status = error.Message; }
    }
    private void Pause_Click(object sender, RoutedEventArgs e) => PausePlayback();
    private void PausePlayback()
    {
        if (playback.State == PlaybackState.Running) SamplePlayback();
        timer.Stop(); playbackClock.Stop(); playback.Pause(); SyncPlayback();
    }
    private void Timer_Tick(object? sender, EventArgs e) => SamplePlayback();
    private void SamplePlayback()
    {
        try { playback.Advance(playbackClock.Elapsed.TotalSeconds); }
        catch (InvalidOperationException error) { playback.Stop(); vm.Status = error.Message; }
        if (playback.State != PlaybackState.Running) { timer.Stop(); playbackClock.Stop(); }
        SyncPlayback();
    }
    private void SyncPlayback()
    {
        vm.CurrentTime = playback.CycleTime;
        vm.PlaybackActive = playback.State is PlaybackState.Running or PlaybackState.Paused;
        var state = playback.State switch
        {
            PlaybackState.Running => "재생", PlaybackState.Paused => "일시정지",
            PlaybackState.Stopped => "중지", PlaybackState.Completed => "완료", _ => "대기"
        };
        var target = vm.InfiniteRepeat ? "∞" : vm.RepeatCount.ToString();
        vm.PlaybackProgress = $"{state} · 사이클 {playback.CurrentCycle}/{target} · 완료 {playback.CompletedCycles}회 · 누적 {playback.ElapsedSeconds:0.0} s";
    }
    private void DrawAll() { DrawTimeline(); DrawMachine(); }
    private void DrawTimeline()
    {
        TimelineCanvas.Children.Clear();
        var items = vm.ScheduledOperations; var total = Math.Max(vm.TotalDuration, 1);
        const double left=115, top=25, rowH=38;
        // Size from the viewport, never from the canvas modified by the previous draw.
        // Reading ActualWidth here caused the right margin to accumulate on each frame.
        TimelineCanvas.Width = Math.Max(900, TimelineScrollViewer.ViewportWidth);
        var scale = (TimelineCanvas.Width - left - 25) / total;
        var step = total <= 20 ? 1 : Math.Ceiling(total/20);
        for (double t=0; t<=total; t+=step) { var x=left+t*scale; Line(TimelineCanvas,x,15,x,180,"#E2E8F0",1); Text(TimelineCanvas,$"{t:0.#}s",x-12,2,"#475569",10); }
        string[] resources = [MachineAxes.Stage1, MachineAxes.Stage2, MachineAxes.Head];
        for (int i=0;i<resources.Length;i++)
        {
            var y=top+i*rowH; Text(TimelineCanvas,resources[i],5,y+5,"#0F172A",11); Line(TimelineCanvas,left,y+rowH-2,TimelineCanvas.Width-8,y+rowH-2,"#CBD5E1",1);
            foreach(var op in items.Where(o=>o.RequiredResources.Contains(resources[i])))
            {
                var x=left+op.StartTime*scale; var w=Math.Max(2,op.Duration*scale);
                var primary = op.Resource == resources[i];
                var r=new Rectangle { Width=w, Height=22, RadiusX=4, RadiusY=4, Opacity=primary?1:0.45, Fill=Brush(op.Resource==MachineAxes.Head?"#2563EB":op.Resource=="Stage 1"?"#0D9488":"#D97706"), ToolTip=$"{op.Name}\n{op.StartTime:0.###}–{op.EndTime:0.###} s\n점유: {string.Join(", ", op.RequiredResources)}" };
                Canvas.SetLeft(r,x); Canvas.SetTop(r,y+2); TimelineCanvas.Children.Add(r);
                if(w>45) Text(TimelineCanvas,primary?op.Name:$"{op.Name} (점유)",x+4,y+5,primary?"White":"#0F172A",10,w-8);
            }
        }
        var cx=left+vm.CurrentTime*scale; Line(TimelineCanvas,cx,12,cx,150,"#DC2626",2);
    }
    private void DrawMachine()
    {
        MachineCanvas.Children.Clear();
        var width = Math.Max(1, MachineCanvas.ActualWidth);
        var height = Math.Max(1, MachineCanvas.ActualHeight);
        var settings = vm.Machine;
        var map = new MachineProjection(settings, width, height);
        var state = MachineStateEvaluator.Evaluate(vm.ScheduledOperations, vm.CurrentTime, settings);
        Text(MachineCanvas, "X → (Stage) · Y ↑ (Head) · mm · 고정 갠트리", 6, 2, "#334155", 10);
        foreach (var lane in new[] { MachineAxes.Stage1, MachineAxes.Stage2 })
        {
            var y = settings.ScanY(lane);
            var position = lane == MachineAxes.Stage1 ? state.Stage1X : state.Stage2X;
            Box(settings.StageXMin, settings.StageXMax, y - map.LaneHalfHeight, y + map.LaneHalfHeight,
                lane == MachineAxes.Stage1 ? "#CCFBF1" : "#FEF3C7", .8);
            Line(MachineCanvas, map.X(settings.StageXMin), map.Y(y), map.X(settings.StageXMax), map.Y(y), "#94A3B8", 1);
            Box(position - map.MarkerWidth / 2, position + map.MarkerWidth / 2,
                y - map.LaneHalfHeight * .7, y + map.LaneHalfHeight * .7,
                lane == MachineAxes.Stage1 ? "#0D9488" : "#D97706", 1);
            Text(MachineCanvas, $"{lane}\nY={y:0.###}", 2, map.Y(y) - 10, "#0F172A", 9, 62);
        }
        // All four gantry edges are configured in world mm; none depends on playback.
        Box(settings.GantryXStart, settings.GantryXEnd, settings.GantryYStart, settings.GantryYEnd, "#64748B", .4);
        var scanning = vm.ScheduledOperations.Any(o => o.RequiresScannerHead && vm.CurrentTime >= o.StartTime && vm.CurrentTime < o.EndTime);
        Box(settings.HeadX - settings.HeadWidth / 2, settings.HeadX + settings.HeadWidth / 2,
            state.HeadY - settings.HeadHeight / 2, state.HeadY + settings.HeadHeight / 2, scanning ? "#DC2626" : "#2563EB", 1);
        var axisY = map.Y(Math.Min(settings.GantryYStart, Math.Min(settings.Stage1Y, settings.Stage2Y) - map.LaneHalfHeight)) + 8;
        for (var i = 0; i <= 4; i++)
        {
            var mm = settings.StageXMin + (settings.StageXMax - settings.StageXMin) * i / 4;
            Line(MachineCanvas, map.X(mm), axisY - 3, map.X(mm), axisY + 2, "#64748B", 1);
            Text(MachineCanvas, $"{mm:0.##}", map.X(mm) - 12, axisY + 2, "#475569", 9, 50);
        }
        Text(MachineCanvas, $"S1 X={state.Stage1X:0.###} · S2 X={state.Stage2X:0.###} · Head Y={state.HeadY:0.###} mm", 4, Math.Max(20, height - 16), "#334155", 10, Math.Max(1, width - 8));

        void Box(double x1, double x2, double y1, double y2, string color, double opacity)
        {
            var rectangle = new Rectangle { Width = (x2 - x1) * map.Scale, Height = (y2 - y1) * map.Scale,
                Fill = Brush(color), Stroke = Brush("#64748B"), StrokeThickness = .5, Opacity = opacity,
                ToolTip = $"X: {x1:0.###}–{x2:0.###} mm · Y: {y1:0.###}–{y2:0.###} mm" };
            Canvas.SetLeft(rectangle, map.X(x1)); Canvas.SetTop(rectangle, map.Y(y2)); MachineCanvas.Children.Add(rectangle);
        }
    }
    private static void Line(Canvas c,double x1,double y1,double x2,double y2,string color,double thickness) => c.Children.Add(new System.Windows.Shapes.Line { X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=Brush(color),StrokeThickness=thickness });
    private static void Text(Canvas c,string text,double x,double y,string color,double size,double max=500) { var t=new TextBlock { Text=text,Foreground=Brush(color),FontSize=size,MaxWidth=max,TextTrimming=TextTrimming.CharacterEllipsis }; Canvas.SetLeft(t,x); Canvas.SetTop(t,y); c.Children.Add(t); }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
public static class EditorOptions
{
    public static string[] Resources { get; } = MachineAxes.Resources.ToArray();
}
