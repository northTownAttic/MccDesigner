using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MccDesigner.Models;
using MccDesigner.Services;

namespace MccDesigner.Views;

/// <summary>Preview uses the same world geometry and projection as the simulator, at initial pose.</summary>
public sealed class MachineSettingsPreview : Canvas
{
    public MachineSettings? Settings { get; set; }
    public string? SelectedProperty { get; set; }
    public double Zoom { get; set; } = 1;
    public MachineSettingsPreview()
    {
        Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)); ClipToBounds = true;
        SizeChanged += (_, _) => Render();
    }
    public void Render()
    {
        Children.Clear();
        if (Settings is not { } m || ActualWidth < 10 || ActualHeight < 10) return;
        var map = new MachineProjection(m, ActualWidth / Zoom, ActualHeight / Zoom, Zoom);
        var selected = SelectedProperty ?? "";
        foreach (var stage in MachineAxes.Resources.Take(2))
        {
            var first = stage == MachineAxes.Stage1;
            var rail = MachineGeometry.Rail(m, stage);
            var platform = MachineGeometry.Stage(m, stage, m.InitialPosition(stage));
            Box(rail, "#CBD5E1", 1, first ? "Rail 1" : "Rail 2", selected.StartsWith(first ? "Rail1" : "Rail2") || selected == nameof(MachineSettings.RailWidthY) || selected == (first ? nameof(MachineSettings.Stage1Y) : nameof(MachineSettings.Stage2Y)));
            Box(platform, first ? "#0D9488" : "#D97706", .95, stage,
                selected.StartsWith(first ? "Stage1" : "Stage2") || selected.StartsWith("StageX"));
            Label(stage + $" · X={platform.CenterX:0.###} · {platform.Width:0.###} × {platform.Height:0.###} mm",
                map.X(platform.XStart), map.Y(platform.YEnd) - 18 * Zoom, "#0F172A");
            Label((first ? "Rail 1" : "Rail 2") + $" · Y={m.ScanY(stage):0.###} mm",
                map.X(rail.XStart), map.Y(rail.YStart) + 5 * Zoom, "#475569");
            var travel = new Line { X1 = map.X(m.StageXMin), X2 = map.X(m.StageXMax),
                Y1 = map.Y(m.ScanY(stage)), Y2 = map.Y(m.ScanY(stage)), Stroke = Brushes.White,
                StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false };
            Children.Add(travel);
        }
        Box(new(m.GantryXStart, m.GantryXEnd, m.GantryYStart, m.GantryYEnd), "#64748B", .4,
            "고정 갠트리", selected.StartsWith("Gantry"));
        Label($"Gantry X {m.GantryXStart:0.###}–{m.GantryXEnd:0.###} mm",
            map.X(m.GantryXStart), map.Y(m.GantryYEnd) - 18 * Zoom, "#334155");
        Children.Add(new Line { X1 = map.X(m.HeadX), X2 = map.X(m.HeadX),
            Y1 = map.Y(m.HeadYMin), Y2 = map.Y(m.HeadYMax), Stroke = selected.StartsWith("HeadY") ? Brushes.Magenta : Brushes.RoyalBlue,
            StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false });
        Box(new(m.HeadX - m.HeadWidth / 2, m.HeadX + m.HeadWidth / 2,
            m.HeadInitialY - m.HeadHeight / 2, m.HeadInitialY + m.HeadHeight / 2), "#2563EB", 1,
            "Scanner Head", selected.StartsWith("Head"));
        Label($"Head · X={m.HeadX:0.###}, Y={m.HeadInitialY:0.###} mm",
            map.X(m.HeadX + m.HeadWidth / 2) + 6 * Zoom, map.Y(m.HeadInitialY), "#1D4ED8");
        for (var i = 0; i <= 4; i++)
        {
            var x = m.StageXMin + (m.StageXMax - m.StageXMin) * i / 4;
            Label($"{x:0.##}", map.X(x) - 10 * Zoom, map.Y(map.MinY) + 8 * Zoom, "#475569");
        }
        Label("X → · Y ↑ · mm (초기 위치)", 8 * Zoom, 4 * Zoom, "#334155");

        void Label(string text, double x, double y, string color)
        {
            var label = new TextBlock { Text = text, Foreground = Brush(color), FontSize = 11 * Zoom,
                Background = new SolidColorBrush(Color.FromArgb(210, 248, 250, 252)), IsHitTestVisible = false,
                MaxWidth = Math.Max(1, ActualWidth - 4), TextTrimming = TextTrimming.CharacterEllipsis };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(label, Math.Clamp(x, 2, Math.Max(2, ActualWidth - label.DesiredSize.Width - 2)));
            SetTop(label, Math.Clamp(y, 2, Math.Max(2, ActualHeight - label.DesiredSize.Height - 2)));
            Children.Add(label);
        }
        void Box(MachineRectangle geometry, string color, double opacity, string name, bool highlight)
        {
            var shape = new Rectangle { Width = geometry.Width * map.Scale, Height = geometry.Height * map.Scale,
                Fill = Brush(color), Opacity = opacity, Stroke = highlight ? Brushes.Magenta : Brush("#64748B"),
                StrokeThickness = highlight ? 3 : 1,
                ToolTip = $"{name}\nX {geometry.XStart:0.###}–{geometry.XEnd:0.###} mm\nY {geometry.YStart:0.###}–{geometry.YEnd:0.###} mm\n{geometry.Width:0.###} × {geometry.Height:0.###} mm" };
            SetLeft(shape, map.X(geometry.XStart)); SetTop(shape, map.Y(geometry.YEnd)); Children.Add(shape);
        }
    }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
