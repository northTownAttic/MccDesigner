using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MccDesigner.Models;
using MccDesigner.ViewModels;
namespace MccDesigner;
public partial class MainWindow : Window
{
    private readonly MainViewModel vm;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private DateTime lastTick;
    public MainWindow()
    {
        InitializeComponent();
        vm = new MainViewModel();
        DataContext = new BindingProxy(vm);
        timer.Tick += Timer_Tick;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(MainViewModel.ScheduledOperations) or nameof(MainViewModel.CurrentTime)) DrawAll(); };
        Loaded += (_, _) => DrawAll();
        SizeChanged += (_, _) => DrawAll();
    }
    private void OperationsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => { vm.RefreshCalculationAfterEdit(); DrawAll(); }), DispatcherPriority.Background);
    }
    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (vm.TotalDuration <= 0) return;
        if (vm.CurrentTime >= vm.TotalDuration) vm.CurrentTime = 0;
        lastTick = DateTime.UtcNow; timer.Start();
    }
    private void Pause_Click(object sender, RoutedEventArgs e) => timer.Stop();
    private void Timer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow; vm.CurrentTime += (now-lastTick).TotalSeconds; lastTick = now;
        if (vm.CurrentTime >= vm.TotalDuration) { vm.CurrentTime = vm.TotalDuration; timer.Stop(); }
        DrawMachine();
    }
    private void DrawAll() { DrawTimeline(); DrawMachine(); }
    private void DrawTimeline()
    {
        TimelineCanvas.Children.Clear();
        var items = vm.ScheduledOperations; var total = Math.Max(vm.TotalDuration, 1);
        const double left=115, top=25, rowH=38;
        var scale = Math.Max(5, (TimelineCanvas.ActualWidth > 200 ? TimelineCanvas.ActualWidth : 900)-left-20)/total;
        TimelineCanvas.Width = Math.Max(900, left + total*scale + 25);
        var step = total <= 20 ? 1 : Math.Ceiling(total/20);
        for (double t=0; t<=total; t+=step) { var x=left+t*scale; Line(TimelineCanvas,x,15,x,180,"#E2E8F0",1); Text(TimelineCanvas,$"{t:0.#}s",x-12,2,"#475569",10); }
        string[] resources = ["Stage 1","Stage 2","Gantry / Head"];
        for (int i=0;i<resources.Length;i++)
        {
            var y=top+i*rowH; Text(TimelineCanvas,resources[i],5,y+5,"#0F172A",11); Line(TimelineCanvas,left,y+rowH-2,TimelineCanvas.Width-8,y+rowH-2,"#CBD5E1",1);
            foreach(var op in items.Where(o=>o.Resource==resources[i]))
            {
                var x=left+op.StartTime*scale; var w=Math.Max(2,op.Duration*scale);
                var r=new Rectangle { Width=w, Height=22, RadiusX=4, RadiusY=4, Fill=Brush(op.Resource=="Gantry / Head"?"#2563EB":op.Resource=="Stage 1"?"#0D9488":"#D97706"), ToolTip=$"{op.Name}\n{op.StartTime:0.###}–{op.EndTime:0.###} s" };
                Canvas.SetLeft(r,x); Canvas.SetTop(r,y+2); TimelineCanvas.Children.Add(r);
                if(w>45) Text(TimelineCanvas,op.Name,x+4,y+5,"White",10,w-8);
            }
        }
        var cx=left+vm.CurrentTime*scale; Line(TimelineCanvas,cx,12,cx,150,"#DC2626",2);
    }
    private void DrawMachine()
    {
        MachineCanvas.Children.Clear(); var width=Math.Max(600,MachineCanvas.ActualWidth); double left=85,right=width-35;
        Text(MachineCanvas,"X →",8,8,"#334155",12); Text(MachineCanvas,"STAGE 2",left,8,"#0F172A",12); Text(MachineCanvas,"STAGE 1",left,63,"#0F172A",12);
        Stage(MachineCanvas,left,right,43,"#FEF3C7"); Stage(MachineCanvas,left,right,98,"#CCFBF1");
        Line(MachineCanvas,left,67,right,67,"#94A3B8",2); Line(MachineCanvas,left,122,right,122,"#94A3B8",2);
        var op=vm.ScheduledOperations.FirstOrDefault(o=>vm.CurrentTime>=o.StartTime && vm.CurrentTime<=o.EndTime && o.IsMotion);
        var pos=op is null?0:Position(op,vm.CurrentTime); var x=left+Math.Clamp(pos/100,0,1)*Math.Max(0,right-left-34); var y=op?.TargetLane=="Stage 2"?43:98;
        var bridge=new Rectangle { Width=34, Height=102, Fill=Brush("#64748B"), RadiusX=4, RadiusY=4, Opacity=.95 };
        Canvas.SetLeft(bridge,x); Canvas.SetTop(bridge,30); MachineCanvas.Children.Add(bridge);
        var head=new Rectangle { Width=24,Height=24,Fill=Brush("#2563EB"),Stroke=Brush("White"),StrokeThickness=2,RadiusX=3,RadiusY=3 };
        Canvas.SetLeft(head,x+5); Canvas.SetTop(head,y+8); MachineCanvas.Children.Add(head);
        Text(MachineCanvas,op?.Name??"대기",8,128,"#334155",12); Text(MachineCanvas,$"X 위치: {pos:0.0} / 100 · 대상: {op?.TargetLane??"Stage 1"}",Math.Max(180,width-300),128,"#334155",12);
    }
    private static double Position(ScheduledOperation op,double t) { var p=Math.Clamp((t-op.StartTime)/Math.Max(op.Duration,.0001),0,1); return op.StartPosition+(op.EndPosition-op.StartPosition)*p; }
    private static void Stage(Canvas c,double x1,double x2,double y,string color) { var r=new Rectangle { Width=Math.Max(10,x2-x1),Height=48,Fill=Brush(color),Stroke=Brush("#94A3B8"),StrokeThickness=1,RadiusX=4,RadiusY=4 }; Canvas.SetLeft(r,x1); Canvas.SetTop(r,y); c.Children.Add(r); }
    private static void Line(Canvas c,double x1,double y1,double x2,double y2,string color,double thickness) => c.Children.Add(new System.Windows.Shapes.Line { X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=Brush(color),StrokeThickness=thickness });
    private static void Text(Canvas c,string text,double x,double y,string color,double size,double max=500) { var t=new TextBlock { Text=text,Foreground=Brush(color),FontSize=size,MaxWidth=max,TextTrimming=TextTrimming.CharacterEllipsis }; Canvas.SetLeft(t,x); Canvas.SetTop(t,y); c.Children.Add(t); }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
public sealed class BindingProxy : INotifyPropertyChanged
{
    private readonly MainViewModel vm;
    public BindingProxy(MainViewModel vm) { this.vm=vm; vm.PropertyChanged += (_,e)=> { PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(e.PropertyName)); if(e.PropertyName==nameof(MainViewModel.ScheduledOperations)) PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(TotalDuration))); }; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public System.Collections.ObjectModel.ObservableCollection<OperationDefinition> Operations=>vm.Operations;
    public OperationDefinition? SelectedOperation { get=>vm.SelectedOperation; set=>vm.SelectedOperation=value; }
    public IReadOnlyList<ScheduledOperation> ScheduledOperations=>vm.ScheduledOperations;
    public string Status=>vm.Status; public string CurrentTimeLabel=>vm.CurrentTimeLabel; public double CurrentTime=>vm.CurrentTime; public double TotalDuration=>vm.TotalDuration;
    public System.Windows.Input.ICommand AddCommand=>vm.AddCommand; public System.Windows.Input.ICommand RemoveCommand=>vm.RemoveCommand; public System.Windows.Input.ICommand CalculateCommand=>vm.CalculateCommand; public System.Windows.Input.ICommand SaveCommand=>vm.SaveCommand; public System.Windows.Input.ICommand LoadCommand=>vm.LoadCommand; public System.Windows.Input.ICommand ResetTimeCommand=>vm.ResetTimeCommand;
    public string[] Resources { get; }=["Stage 1","Stage 2","Gantry / Head"]; public string[] Lanes { get; }=["Stage 1","Stage 2"];
}
