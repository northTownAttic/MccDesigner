namespace MccDesigner.Models;
public sealed class OperationDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New operation";
    public string Resource { get; set; } = "Stage 1";
    public double DurationSeconds { get; set; } = 1.0;
    // True preserves the sequential behavior of projects saved by earlier versions.
    public bool WaitForPrevious { get; set; } = true;
    // Gantry scans occupy both the shared head and the selected stage.
    // Pure gantry moves may clear this flag to leave the stage available.
    public bool OccupiesTargetLane { get; set; } = true;
    public bool IsMotion { get; set; }
    public double StartPosition { get; set; }
    public double EndPosition { get; set; } = 100;
    public string TargetLane { get; set; } = "Stage 1";
}
