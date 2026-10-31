using MccDesigner.Models;
namespace MccDesigner.Services;

/// <summary>A shared millimetre-to-pixel scale for X and Y, with positive Y upward.</summary>
public sealed class MachineProjection
{
    public double Scale { get; }
    private readonly double left, bottom, minX, minY;
    public double MinY { get; }
    public MachineProjection(MachineSettings settings, double width, double height, double zoom = 1)
    : this(settings, width, height, zoom, false) { }

    public static MachineProjection ForPreview(MachineSettings settings, double width, double height, double zoom = 1)
        => new(settings, width, height, zoom, true);

    private MachineProjection(MachineSettings settings, double width, double height, double zoom, bool preview)
    {
        if (!preview) settings.Validate();
        else if (MachineSettings.NumericProperties.Any(p => !double.IsFinite((double)p.GetValue(settings)!) || Math.Abs((double)p.GetValue(settings)!) > 1e9))
            throw new InvalidOperationException("미리보기 좌표에 표시할 수 없는 수치가 있습니다.");
        if (!double.IsFinite(zoom) || zoom <= 0) throw new ArgumentOutOfRangeException(nameof(zoom));
        var halfLength = Math.Max(Math.Abs(settings.Stage1LengthX), Math.Abs(settings.Stage2LengthX)) / 2;
        double[] xs = [Math.Min(settings.StageXMin, settings.StageXMax) - halfLength,
            Math.Max(settings.StageXMin, settings.StageXMax) + halfLength,
            settings.Rail1XStart, settings.Rail1XEnd, settings.Rail2XStart, settings.Rail2XEnd,
            settings.GantryXStart, settings.GantryXEnd,
            settings.HeadX - Math.Abs(settings.HeadWidth) / 2, settings.HeadX + Math.Abs(settings.HeadWidth) / 2,
            settings.Stage1InitialX - Math.Abs(settings.Stage1LengthX) / 2, settings.Stage1InitialX + Math.Abs(settings.Stage1LengthX) / 2,
            settings.Stage2InitialX - Math.Abs(settings.Stage2LengthX) / 2, settings.Stage2InitialX + Math.Abs(settings.Stage2LengthX) / 2];
        var halfStage1 = Math.Max(Math.Abs(settings.Stage1WidthY), Math.Abs(settings.RailWidthY)) / 2;
        var halfStage2 = Math.Max(Math.Abs(settings.Stage2WidthY), Math.Abs(settings.RailWidthY)) / 2;
        var halfHead = Math.Abs(settings.HeadHeight) / 2;
        double[] ys = [settings.GantryYStart, settings.GantryYEnd,
            settings.HeadYMin - halfHead, settings.HeadYMin + halfHead, settings.HeadYMax - halfHead, settings.HeadYMax + halfHead,
            settings.HeadInitialY - halfHead, settings.HeadInitialY + halfHead,
            settings.Stage1Y - halfStage1, settings.Stage1Y + halfStage1, settings.Stage2Y - halfStage2, settings.Stage2Y + halfStage2];
        minX = xs.Min(); var maxX = xs.Max();
        minY = ys.Min(); var maxY = ys.Max();
        // A zero-size draft still needs a finite canvas transform and a visible location marker.
        if (maxX - minX < .001) { minX -= .5; maxX += .5; }
        if (maxY - minY < .001) { minY -= .5; maxY += .5; }
        MinY = minY;
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
