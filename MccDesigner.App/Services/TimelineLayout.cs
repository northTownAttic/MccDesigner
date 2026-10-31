namespace MccDesigner.Services;

/// <summary>Shared coordinates for drawing and seeking, in scrollable canvas space.</summary>
public sealed class TimelineLayout
{
    public const double Left = 115;
    public const double Right = 25;
    public double Width { get; }
    public double DisplayDuration { get; }
    public double PixelsPerSecond { get; }
    private readonly double duration;
    public TimelineLayout(double viewportWidth, double totalDuration)
    {
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(totalDuration) || totalDuration < 0)
            throw new ArgumentOutOfRangeException();
        Width = Math.Max(900, viewportWidth); duration = totalDuration;
        DisplayDuration = Math.Max(1, totalDuration);
        PixelsPerSecond = (Width - Left - Right) / DisplayDuration;
    }
    public double TimeAt(double canvasX) => Math.Clamp((canvasX - Left) / PixelsPerSecond, 0, duration);
    public double XAt(double time) => Left + Math.Clamp(time, 0, duration) * PixelsPerSecond;
}
