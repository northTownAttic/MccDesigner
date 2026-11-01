using MccDesigner.Models;
namespace MccDesigner.Services;

public static class DemoProject
{
    public static List<OperationDefinition> Create() =>
    [
        new() { Name = "Stage 2 Loading", Resource = MachineAxes.Stage2, DurationSeconds = 6.25, IsLoading = true, WaitForPrevious = false },
        new() { Name = "Stage 1 Loading", Resource = MachineAxes.Stage1, DurationSeconds = 5, IsLoading = true, WaitForPrevious = false },
        new() { Name = "Stage 1 Vision", Resource = MachineAxes.Stage1, DurationSeconds = 3.5 },
        new() { Name = "Stage 1 X Scan", Resource = MachineAxes.Stage1, DurationSeconds = 12.5,
            IsMotion = true, RequiresScannerHead = true, StartPosition = 0, EndPosition = 100 },
        new() { Name = "Head Y: 1 → 2", Resource = MachineAxes.Head, DurationSeconds = 2,
            IsMotion = true, StartPosition = 0, EndPosition = 100 },
        new() { Name = "Stage 2 X Scan", Resource = MachineAxes.Stage2, DurationSeconds = 12.5,
            IsMotion = true, RequiresScannerHead = true, StartPosition = 0, EndPosition = 100 },
        new() { Name = "Head Y: 2 → 1", Resource = MachineAxes.Head, DurationSeconds = 2,
            IsMotion = true, StartPosition = 100, EndPosition = 0 },
        new() { Name = "Stage 1 X Return", Resource = MachineAxes.Stage1, DurationSeconds = 3.5,
            IsMotion = true, StartPosition = 100, EndPosition = 0, WaitForPrevious = false },
        new() { Name = "Stage 2 X Return", Resource = MachineAxes.Stage2, DurationSeconds = 3.5,
            IsMotion = true, StartPosition = 100, EndPosition = 0 }
    ];
}
