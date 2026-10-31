namespace MccDesigner.Models;

public static class MachineAxes
{
    public const string Stage1 = "Stage 1";
    public const string Stage2 = "Stage 2";
    public const string Head = "Scanner Head";
    public static IReadOnlyList<string> Resources { get; } = Array.AsReadOnly(new[] { Stage1, Stage2, Head });
    public static double ScanY(string stage) => stage switch
    {
        Stage1 => 0, Stage2 => 100,
        _ => throw new InvalidOperationException("스캔 장비는 Stage 1 또는 Stage 2여야 합니다.")
    };
}
