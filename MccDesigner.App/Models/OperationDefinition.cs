namespace MccDesigner.Models;
public sealed class OperationDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New operation";
    public string Resource { get; set; } = "Stage 1";
    public double DurationSeconds { get; set; } = 1.0;
    public bool IsMotion { get; set; }
    public double StartPosition { get; set; }
    public double EndPosition { get; set; } = 100;
    public string TargetLane { get; set; } = "Stage 1";
}
