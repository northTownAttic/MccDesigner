namespace MccDesigner.Models;
public sealed class OperationDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New operation";
    public string Resource { get; set; } = "Stage 1";
    public double DurationSeconds { get; set; } = 1.0;
    // True preserves the sequential behavior of projects saved by earlier versions.
    public bool WaitForPrevious { get; set; } = true;
    // A scanning stage moves on X while exclusively reserving the stationary head.
    public bool RequiresScannerHead { get; set; }
    public bool IsMotion { get; set; }
    public double StartPosition { get; set; }
    public double EndPosition { get; set; } = 100;
}
