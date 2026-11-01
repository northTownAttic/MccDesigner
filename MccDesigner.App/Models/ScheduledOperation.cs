namespace MccDesigner.Models;
public sealed class ScheduledOperation
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Resource { get; init; } = "";
    public IReadOnlyList<string> RequiredResources { get; init; } = Array.Empty<string>();
    public double StartTime { get; init; }
    public double EndTime { get; init; }
    public double Duration => EndTime - StartTime;
    public bool IsLoading { get; init; }
    public bool IsUnloading { get; init; }
    public bool IsMotion { get; init; }
    public double StartPosition { get; init; }
    public double EndPosition { get; init; }
    public bool RequiresScannerHead { get; init; }
}
