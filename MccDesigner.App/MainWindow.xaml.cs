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
    private MediaProductionSession? production;
    private IReadOnlyList<ScheduledOperation>? renderedTimelineItems;
    private double renderedTimelineWidth, renderedTimelineOrigin, renderedTimelineDuration;
    private Line? timelineCursor;
    private bool draggingTimeline;
    private double playbackElapsedBase;
    private double? previewSeekTime;
    private int editGeneration;
    private bool operationEditPending;
    private bool committingEdit;
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => { if (WindowState == WindowState.Normal) WindowState = WindowState.Maximized; };
        vm = new MainViewModel();
        DataContext = vm;
        TimelineScrollViewer.ScrollChanged += (_, e) => {
            if (e.ViewportWidthChange != 0) DrawTimeline();
        };
        MachineScrollViewer.ScrollChanged += (_, e) => {
            if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) ResizeMachineCanvas();
        };
        MachineCanvas.SizeChanged += (_, _) => DrawMachine();
        OperationsGrid.BeginningEdit += (_, _) => operationEditPending = true;
        timer.Tick += Timer_Tick;
        vm.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(MainViewModel.SimulatorZoom)) ZoomMachineCanvas();
            if (e.PropertyName == nameof(MainViewModel.ScheduledOperations)) operationEditPending = false;
            if (e.PropertyName is nameof(MainViewModel.ScheduledOperations) or nameof(MainViewModel.Machine) or nameof(MainViewModel.RepeatCount) or nameof(MainViewModel.InfiniteRepeat) or nameof(MainViewModel.SupplyEnabled) or nameof(MainViewModel.SupplyInterval1) or nameof(MainViewModel.SupplyInterval2) or nameof(MainViewModel.SupplyBatchSize) or nameof(MainViewModel.SupplyAtStart)) ResetPlayback();
            if (e.PropertyName == nameof(MainViewModel.ProductionFrame) ||
                e.PropertyName is nameof(MainViewModel.ScheduledOperations) or nameof(MainViewModel.Machine) ||
                (e.PropertyName == nameof(MainViewModel.CurrentTime) && !vm.ProductionView)) DrawAll();
        };
        Closed += (_, _) => { timer.Stop(); playbackClock.Stop(); };
        Loaded += (_, _) => { ResizeMachineCanvas(); CenterMachineCanvas(); DrawAll(); };
        SizeChanged += (_, _) => DrawAll();
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
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
        if (source is not Button { Command: MccDesigner.Mvvm.RelayCommand }) return;
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
        if (production != null)
        {
            if (production.State == PlaybackState.Running) SamplePlayback();
            timer.Stop(); playbackClock.Stop(); production.Stop(); SyncPlayback(); return;
        }
        if (playback.State == PlaybackState.Running) SamplePlayback();
        timer.Stop(); playbackClock.Stop(); playback.Stop(); SyncPlayback();
    }
    private void ResetPlayback()
    {
        draggingTimeline = false;
        if (TimelineCanvas.IsMouseCaptured) TimelineCanvas.ReleaseMouseCapture();
        production = null; vm.SetProductionFrame(null);
        previewSeekTime = null; playbackElapsedBase = 0;
        timer.Stop(); playbackClock.Reset(); playback.Reset(); SyncPlayback();
    }
    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (playback.State == PlaybackState.Running || production?.State == PlaybackState.Running) return;
        editGeneration++;
        committingEdit = true;
        try
        {
            SupplyInterval1Input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            SupplyInterval2Input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            SupplyBatchInput.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            RepeatCountInput.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (Validation.GetHasError(SupplyInterval1Input) || Validation.GetHasError(SupplyInterval2Input) || Validation.GetHasError(SupplyBatchInput) || Validation.GetHasError(RepeatCountInput) ||
                !OperationsGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
                !OperationsGrid.CommitEdit(DataGridEditingUnit.Row, true))
            {
                vm.Status = "작업 입력과 반복 횟수를 확인하세요."; return;
            }
        }
        finally { committingEdit = false; }
        try
        {
            if (operationEditPending && (playback.State == PlaybackState.Paused || production?.State == PlaybackState.Paused)) ResetPlayback();
            if (production?.State == PlaybackState.Paused)
            { production.Resume(); playbackClock.Start(); SyncPlayback(); timer.Start(); return; }
            if (playback.State == PlaybackState.Paused)
            {
                playback.Resume(); playbackClock.Start();
            }
            else
            {
                var initialTime = previewSeekTime ?? 0;
                vm.RefreshCalculationAfterEdit();
                operationEditPending = false;
                if (vm.TotalDuration <= 0) return;
                if (vm.SupplyEnabled)
                {
                    production = new MediaProductionSession(vm.Operations.ToList(), vm.Machine, vm.SupplySettings, vm.PlaybackOptions);
                    playbackElapsedBase = 0;
                }
                else
                {
                    playback.Start(vm.ScheduledOperations, vm.Machine, vm.PlaybackOptions, initialTime >= vm.TotalDuration ? 0 : initialTime);
                    playbackElapsedBase = playback.ElapsedSeconds;
                }
                playbackClock.Restart();
            }
            SyncPlayback(); timer.Start();
        }
        catch (InvalidOperationException error) { vm.Status = error.Message; }
    }
    private void Pause_Click(object sender, RoutedEventArgs e) => PausePlayback();
    private void PausePlayback()
    {
        if (production != null)
        {
            if (production.State == PlaybackState.Running) SamplePlayback();
            timer.Stop(); playbackClock.Stop(); production.Pause(); SyncPlayback(); return;
        }
        if (playback.State == PlaybackState.Running) SamplePlayback();
        timer.Stop(); playbackClock.Stop(); playback.Pause(); SyncPlayback();
    }
    private void Timer_Tick(object? sender, EventArgs e) => SamplePlayback();
    private void SamplePlayback()
    {
        if (production != null)
        {
            try { production.Advance(playbackElapsedBase + playbackClock.Elapsed.TotalSeconds); }
            catch (InvalidOperationException error) { production.Stop(); vm.Status = error.Message; }
            if (production.State != PlaybackState.Running) { timer.Stop(); playbackClock.Stop(); }
            SyncPlayback(); return;
        }
        try { playback.Advance(playbackElapsedBase + playbackClock.Elapsed.TotalSeconds); }
        catch (InvalidOperationException error) { playback.Stop(); vm.Status = error.Message; }
        if (playback.State != PlaybackState.Running) { timer.Stop(); playbackClock.Stop(); }
        SyncPlayback();
    }
    private void SyncPlayback()
    {
        if (production != null)
        {
            vm.PlaybackActive = production.State is PlaybackState.Running or PlaybackState.Paused;
            vm.SetProductionFrame(production);
            var productionLabel = production.State switch { PlaybackState.Running => "재생", PlaybackState.Paused => "일시정지", PlaybackState.Stopped => "중지", PlaybackState.Completed => "완료", _ => "대기" };
            vm.PlaybackProgress = $"전처리 연동 · {productionLabel} · S1 완료 {production.CompletedStage1}회 / S2 완료 {production.CompletedStage2}회 · 누적 {production.ElapsedSeconds:0.0} s";
            return;
        }
        if (playback.State == PlaybackState.Ready && previewSeekTime is double preview)
        {
            vm.CurrentTime = preview; vm.PlaybackActive = false;
            vm.PlaybackProgress = $"탐색 · 사이클 1 · {preview:0.###} s · 재생을 누르면 이어집니다.";
            return;
        }
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
    private void Timeline_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (vm.SupplyEnabled) { vm.Status = "전처리 연동 모드는 공급/소비 이력을 따라 진행합니다. 시간선 탐색은 연동 해제 모드에서 사용할 수 있습니다."; return; }
        if (vm.TotalDuration <= 0) return;
        editGeneration++;
        committingEdit = true;
        try
        {
            if (!OperationsGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
                !OperationsGrid.CommitEdit(DataGridEditingUnit.Row, true))
            { vm.Status = "작업 입력 오류를 수정한 뒤 탐색하세요."; return; }
        }
        finally { committingEdit = false; }
        if (operationEditPending) vm.RefreshCalculationAfterEdit();
        if (vm.TotalDuration <= 0) return;
        PausePlayback();
        draggingTimeline = TimelineCanvas.CaptureMouse();
        SeekTimeline(e.GetPosition(TimelineCanvas).X);
        e.Handled = true;
    }
    private void Timeline_MouseMove(object sender, MouseEventArgs e)
    {
        if (draggingTimeline && e.LeftButton == MouseButtonState.Pressed)
            SeekTimeline(e.GetPosition(TimelineCanvas).X);
    }
    private void Timeline_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!draggingTimeline) return;
        SeekTimeline(e.GetPosition(TimelineCanvas).X);
        draggingTimeline = false; TimelineCanvas.ReleaseMouseCapture(); e.Handled = true;
    }
    private void Timeline_LostCapture(object sender, MouseEventArgs e) => draggingTimeline = false;
    private void SeekTimeline(double canvasX)
    {
        var layout = new TimelineLayout(TimelineCanvas.Width, vm.TotalDuration);
        var time = layout.TimeAt(canvasX);
        playbackClock.Reset();
        if (playback.State == PlaybackState.Ready)
        {
            previewSeekTime = time;
            SyncPlayback();
        }
        else
        {
            playback.SeekCycleTime(time);
            playbackElapsedBase = playback.ElapsedSeconds;
            SyncPlayback();
        }
    }
    private void DrawAll() { DrawTimeline(); DrawMachine(); }
    private void DrawTimeline()
    {
        var items = vm.DisplayOperations;
        var origin = vm.TimelineStart;
        var layout = new TimelineLayout(TimelineScrollViewer.ViewportWidth, vm.DisplayDuration);
        var total = layout.DisplayDuration;
        var cursorX = layout.XAt(vm.CurrentTime - origin);
        if (ReferenceEquals(items, renderedTimelineItems) && layout.Width == renderedTimelineWidth &&
            origin == renderedTimelineOrigin && total == renderedTimelineDuration && timelineCursor != null)
        { timelineCursor.X1 = timelineCursor.X2 = cursorX; return; }
        TimelineCanvas.Children.Clear();
        renderedTimelineItems = items; renderedTimelineWidth = layout.Width;
        renderedTimelineOrigin = origin; renderedTimelineDuration = total;
        const double left=TimelineLayout.Left, top=25, rowH=38;
        TimelineCanvas.Width = layout.Width;
        var scale = layout.PixelsPerSecond;
        var step = total <= 20 ? 1 : Math.Ceiling(total/20);
        for (double t=0; t<=total; t+=step) { var x=left+t*scale; Line(TimelineCanvas,x,15,x,180,"#E2E8F0",1); Text(TimelineCanvas,$"{origin+t:0.#}s",x-12,2,"#475569",10); }
        string[] resources = [MachineAxes.Stage1, MachineAxes.Stage2, MachineAxes.Head];
        for (int i=0;i<resources.Length;i++)
        {
            var y=top+i*rowH; Text(TimelineCanvas,resources[i],5,y+5,"#0F172A",11); Line(TimelineCanvas,left,y+rowH-2,TimelineCanvas.Width-8,y+rowH-2,"#CBD5E1",1);
            foreach(var op in items.Where(o=>o.RequiredResources.Contains(resources[i]) && o.EndTime >= origin && o.StartTime <= origin + total))
            {
                var start=Math.Max(origin,op.StartTime); var end=Math.Min(origin+total,op.EndTime);
                var x=left+(start-origin)*scale; var w=Math.Max(2,(end-start)*scale);
                var primary = op.Resource == resources[i];
                var r=new Rectangle { Width=w, Height=22, RadiusX=4, RadiusY=4, Opacity=primary?1:0.45, Fill=Brush(op.Resource==MachineAxes.Head?"#2563EB":op.Resource=="Stage 1"?"#0D9488":"#D97706"), ToolTip=$"{op.Name}\n{op.StartTime:0.###}–{op.EndTime:0.###} s\n점유: {string.Join(", ", op.RequiredResources)}" };
                Canvas.SetLeft(r,x); Canvas.SetTop(r,y+2); TimelineCanvas.Children.Add(r);
                if(w>45) Text(TimelineCanvas,primary?op.Name:$"{op.Name} (점유)",x+4,y+5,primary?"White":"#0F172A",10,w-8);
            }
        }
        timelineCursor = new Line { X1 = cursorX, X2 = cursorX, Y1 = 12, Y2 = 150, Stroke = Brush("#DC2626"), StrokeThickness = 2 };
        TimelineCanvas.Children.Add(timelineCursor);
    }
    private void FitMachine_Click(object sender, RoutedEventArgs e)
    {
        vm.SimulatorZoom = 100;
        ResizeMachineCanvas();
        MachineScrollViewer.ScrollToHorizontalOffset(0); MachineScrollViewer.ScrollToVerticalOffset(0);
    }
    private void ResizeMachineCanvas()
    {
        var width = MachineScrollViewer.ViewportWidth;
        var height = MachineScrollViewer.ViewportHeight;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 1 || height <= 1) return;
        MachineCanvas.Width = width * vm.SimulatorZoom / 100;
        MachineCanvas.Height = height * vm.SimulatorZoom / 100;
    }
    private void CenterMachineCanvas() => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (!double.IsFinite(MachineCanvas.Width) || !double.IsFinite(MachineCanvas.Height)) return;
        MachineScrollViewer.ScrollToHorizontalOffset(Math.Max(0, (MachineCanvas.Width - MachineScrollViewer.ViewportWidth) / 2));
        MachineScrollViewer.ScrollToVerticalOffset(Math.Max(0, (MachineCanvas.Height - MachineScrollViewer.ViewportHeight) / 2));
    }), DispatcherPriority.Loaded);
    private void ZoomMachineCanvas()
    {
        var x = double.IsFinite(MachineCanvas.Width) && MachineCanvas.Width > 0
            ? (MachineScrollViewer.HorizontalOffset + MachineScrollViewer.ViewportWidth / 2) / MachineCanvas.Width : .5;
        var y = double.IsFinite(MachineCanvas.Height) && MachineCanvas.Height > 0
            ? (MachineScrollViewer.VerticalOffset + MachineScrollViewer.ViewportHeight / 2) / MachineCanvas.Height : .5;
        ResizeMachineCanvas();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            MachineScrollViewer.ScrollToHorizontalOffset(Math.Max(0, x * MachineCanvas.Width - MachineScrollViewer.ViewportWidth / 2));
            MachineScrollViewer.ScrollToVerticalOffset(Math.Max(0, y * MachineCanvas.Height - MachineScrollViewer.ViewportHeight / 2));
        }), DispatcherPriority.Loaded);
    }
    private void DrawMachine()
    {
        MachineCanvas.Children.Clear();
        var width = Math.Max(1, MachineCanvas.ActualWidth);
        var height = Math.Max(1, MachineCanvas.ActualHeight);
        var settings = vm.Machine;
        var zoom = vm.SimulatorZoom / 100;
        var map = new MachineProjection(settings, width / zoom, height / zoom, zoom);
        var state = vm.LivePose ?? MachineStateEvaluator.Evaluate(vm.ScheduledOperations, vm.CurrentTime, settings);
        Text(MachineCanvas, "X → (Stage) · Y ↑ (Head) · mm · 고정 갠트리", 6 * zoom, 2 * zoom, "#334155", 10 * zoom);
        foreach (var lane in new[] { MachineAxes.Stage1, MachineAxes.Stage2 })
        {
            var y = settings.ScanY(lane);
            var position = lane == MachineAxes.Stage1 ? state.Stage1X : state.Stage2X;
            var rail = MachineGeometry.Rail(settings, lane);
            var stage = MachineGeometry.Stage(settings, lane, position);
            var railName = lane == MachineAxes.Stage1 ? "Rail 1" : "Rail 2";
            Box(rail.XStart, rail.XEnd, rail.YStart, rail.YEnd, "#CBD5E1", 1, railName);
            Line(MachineCanvas, map.X(rail.XStart), map.Y(y), map.X(rail.XEnd), map.Y(y), "#64748B", 1);
            Box(stage.XStart, stage.XEnd, stage.YStart, stage.YEnd,
                lane == MachineAxes.Stage1 ? "#0D9488" : "#D97706", 1, lane);
            Line(MachineCanvas, map.X(position), map.Y(stage.YStart), map.X(position), map.Y(stage.YEnd), "White", 1);
            Text(MachineCanvas, $"{railName}\nY={y:0.###}", 2 * zoom, map.Y(y) - 10 * zoom, "#0F172A", 9 * zoom, 62 * zoom);
            Text(MachineCanvas, lane, map.X(position) - 24 * zoom, map.Y(stage.YEnd) - 13 * zoom,
                "#0F172A", 10 * zoom, 90 * zoom);
        }
        // All four gantry edges are configured in world mm; none depends on playback.
        Box(settings.GantryXStart, settings.GantryXEnd, settings.GantryYStart, settings.GantryYEnd, "#64748B", .4);
        var scanning = vm.DisplayOperations.Any(o => o.RequiresScannerHead && vm.CurrentTime >= o.StartTime && vm.CurrentTime < o.EndTime);
        Box(settings.HeadX - settings.HeadWidth / 2, settings.HeadX + settings.HeadWidth / 2,
            state.HeadY - settings.HeadHeight / 2, state.HeadY + settings.HeadHeight / 2, scanning ? "#DC2626" : "#2563EB", 1);
        var axisY = map.Y(map.MinY) + 8 * zoom;
        for (var i = 0; i <= 4; i++)
        {
            var mm = settings.StageXMin + (settings.StageXMax - settings.StageXMin) * i / 4;
            Line(MachineCanvas, map.X(mm), axisY - 3 * zoom, map.X(mm), axisY + 2 * zoom, "#64748B", 1);
            Text(MachineCanvas, $"{mm:0.##}", map.X(mm) - 12 * zoom, axisY + 2 * zoom, "#475569", 9 * zoom, 50 * zoom);
        }
        Text(MachineCanvas, $"S1 X={state.Stage1X:0.###} · S2 X={state.Stage2X:0.###} · Head Y={state.HeadY:0.###} mm", 4 * zoom, Math.Max(20 * zoom, height - 16 * zoom), "#334155", 10 * zoom, Math.Max(1, width - 8 * zoom));

        void Box(double x1, double x2, double y1, double y2, string color, double opacity, string name = "")
        {
            var rectangle = new Rectangle { Width = (x2 - x1) * map.Scale, Height = (y2 - y1) * map.Scale,
                Fill = Brush(color), Stroke = Brush("#64748B"), StrokeThickness = .5, Opacity = opacity,
                ToolTip = $"{name}\nX: {x1:0.###}–{x2:0.###} mm · Y: {y1:0.###}–{y2:0.###} mm" };
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
