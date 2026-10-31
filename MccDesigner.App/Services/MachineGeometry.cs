using MccDesigner.Models;
namespace MccDesigner.Services;

public sealed record MachineRectangle(double XStart, double XEnd, double YStart, double YEnd)
{
    public double Width => XEnd - XStart;
    public double Height => YEnd - YStart;
    public double CenterX => (XStart + XEnd) / 2;
}

/// <summary>Fixed rails and moving stage bodies. Stage axis position is its centre in world mm.</summary>
public static class MachineGeometry
{
    public static MachineRectangle Rail(MachineSettings machine, string stage)
    {
        var y = machine.ScanY(stage);
        return stage == MachineAxes.Stage1
            ? new(machine.Rail1XStart, machine.Rail1XEnd, y - machine.RailWidthY / 2, y + machine.RailWidthY / 2)
            : new(machine.Rail2XStart, machine.Rail2XEnd, y - machine.RailWidthY / 2, y + machine.RailWidthY / 2);
    }
    public static MachineRectangle Stage(MachineSettings machine, string stage, double centerX)
    {
        var y = machine.ScanY(stage);
        var length = stage == MachineAxes.Stage1 ? machine.Stage1LengthX : machine.Stage2LengthX;
        var width = stage == MachineAxes.Stage1 ? machine.Stage1WidthY : machine.Stage2WidthY;
        return new(centerX - length / 2, centerX + length / 2, y - width / 2, y + width / 2);
    }
}
