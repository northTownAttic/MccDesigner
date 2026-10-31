using MccDesigner.Models;
namespace MccDesigner.Services;

/// <summary>A shared millimetre-to-pixel scale for X and Y, with positive Y upward.</summary>
public sealed class MachineProjection
{
    public double Scale { get; }
    private readonly double left, bottom, minX, minY;
    public double MinY { get; }
    public MachineProjection(MachineSettings settings, double width, double height, double zoom = 1)
    {
        settings.Validate();
        if (!double.IsFinite(zoom) || zoom <= 0) throw new ArgumentOutOfRangeException(nameof(zoom));
        var maxHalfLength = Math.Max(settings.Stage1LengthX, settings.Stage2LengthX) / 2;
        minX = Math.Min(settings.StageXMin - maxHalfLength,
            Math.Min(settings.GantryXStart, Math.Min(settings.Rail1XStart, settings.Rail2XStart)));
        var maxX = Math.Max(settings.StageXMax + maxHalfLength,
            Math.Max(settings.GantryXEnd, Math.Max(settings.Rail1XEnd, settings.Rail2XEnd)));
        minY = Math.Min(settings.GantryYStart, Math.Min(
            settings.Stage1Y - Math.Max(settings.Stage1WidthY, settings.RailWidthY) / 2,
            settings.Stage2Y - Math.Max(settings.Stage2WidthY, settings.RailWidthY) / 2));
        MinY = minY;
        var maxY = Math.Max(settings.GantryYEnd, Math.Max(
            settings.Stage1Y + Math.Max(settings.Stage1WidthY, settings.RailWidthY) / 2,
            settings.Stage2Y + Math.Max(settings.Stage2WidthY, settings.RailWidthY) / 2));
        var plotWidth = Math.Max(1, width - 90);
        var plotHeight = Math.Max(1, height - 70);
        var baseScale = Math.Min(plotWidth / (maxX - minX), plotHeight / (maxY - minY));
        Scale = baseScale * zoom;
        left = (65 + (plotWidth - (maxX - minX) * baseScale) / 2) * zoom;
        bottom = (25 + plotHeight - (plotHeight - (maxY - minY) * baseScale) / 2) * zoom;
    }
    public double X(double millimetres) => left + (millimetres - minX) * Scale;
    public double Y(double millimetres) => bottom - (millimetres - minY) * Scale;
}
