namespace MccDesigner.Models;

public static class MachineAxes
{
    public const string Stage1 = "Stage 1";
    public const string Stage2 = "Stage 2";
    public const string Head = "Scanner Head";
    public static IReadOnlyList<string> Resources { get; } = Array.AsReadOnly(new[] { Stage1, Stage2, Head });
}
