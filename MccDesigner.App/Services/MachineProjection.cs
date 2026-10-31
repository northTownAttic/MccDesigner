using MccDesigner.Models;
namespace MccDesigner.Services;

/// <summary>A shared millimetre-to-pixel scale for X and Y, with positive Y upward.</summary>
public sealed class MachineProjection
{
    public double Scale { get; }
    private readonly double left, bottom, minX, minY;
    public double LaneHalfHeight { get; }
    public double MarkerWidth { get; }
    public MachineProjection(MachineSettings settings, double width, double height)
    {
        settings.Validate();
        LaneHalfHeight = Math.Min(10, Math.Abs(settings.Stage2Y - settings.Stage1Y) * .15);
        MarkerWidth = (settings.StageXMax - settings.StageXMin) * .08;
        minX = Math.Min(settings.StageXMin - MarkerWidth / 2, settings.GantryXStart);
        var maxX = Math.Max(settings.StageXMax + MarkerWidth / 2, settings.GantryXEnd);
        minY = Math.Min(settings.GantryYStart, Math.Min(settings.Stage1Y, settings.Stage2Y) - LaneHalfHeight);
        var maxY = Math.Max(settings.GantryYEnd, Math.Max(settings.Stage1Y, settings.Stage2Y) + LaneHalfHeight);
        var plotWidth = Math.Max(1, width - 90);
        var plotHeight = Math.Max(1, height - 70);
        Scale = Math.Min(plotWidth / (maxX - minX), plotHeight / (maxY - minY));
        left = 65 + (plotWidth - (maxX - minX) * Scale) / 2;
        bottom = 25 + plotHeight - (plotHeight - (maxY - minY) * Scale) / 2;
    }
    public double X(double millimetres) => left + (millimetres - minX) * Scale;
    public double Y(double millimetres) => bottom - (millimetres - minY) * Scale;
}
