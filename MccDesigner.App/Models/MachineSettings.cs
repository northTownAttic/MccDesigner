namespace MccDesigner.Models;

/// <summary>All coordinates and dimensions are millimetres. Gantry and head X are fixed.</summary>
public sealed class MachineSettings
{
    public double StageXMin { get; set; } = 0;
    public double StageXMax { get; set; } = 100;
    // Missing fields in older projects inherit the original travel interval.
    private double? rail1XStart, rail1XEnd, rail2XStart, rail2XEnd;
    public double Rail1XStart { get => rail1XStart ?? StageXMin; set => rail1XStart = value; }
    public double Rail1XEnd { get => rail1XEnd ?? StageXMax; set => rail1XEnd = value; }
    public double Rail2XStart { get => rail2XStart ?? StageXMin; set => rail2XStart = value; }
    public double Rail2XEnd { get => rail2XEnd ?? StageXMax; set => rail2XEnd = value; }
    public double RailWidthY { get; set; } = 4;
    public double Stage1LengthX { get; set; } = 20;
    public double Stage1WidthY { get; set; } = 20;
    public double Stage2LengthX { get; set; } = 20;
    public double Stage2WidthY { get; set; } = 20;
    public double Stage1InitialX { get; set; }
    public double Stage2InitialX { get; set; }
    public double HeadYMin { get; set; } = 0;
    public double HeadYMax { get; set; } = 100;
    public double Stage1Y { get; set; } = 0;
    public double Stage2Y { get; set; } = 100;
    public double HeadInitialY { get; set; }
    public double GantryXStart { get; set; } = 20;
    public double GantryXEnd { get; set; } = 60;
    public double GantryYStart { get; set; } = -15;
    public double GantryYEnd { get; set; } = 115;
    public double HeadX { get; set; } = 40;
    public double HeadWidth { get; set; } = 8;
    public double HeadHeight { get; set; } = 12;

    public MachineSettings Copy() => (MachineSettings)MemberwiseClone();
    public double InitialPosition(string resource) => resource switch
    {
        MachineAxes.Stage1 => Stage1InitialX, MachineAxes.Stage2 => Stage2InitialX,
        MachineAxes.Head => HeadInitialY, _ => throw new InvalidOperationException("지원하지 않는 장비입니다.")
    };
    public double ScanY(string stage) => stage switch
    {
        MachineAxes.Stage1 => Stage1Y, MachineAxes.Stage2 => Stage2Y,
        _ => throw new InvalidOperationException("스캔 장비는 Stage 1 또는 Stage 2여야 합니다.")
    };
    public void Validate()
    {
        double[] numbers = [StageXMin, StageXMax, Stage1InitialX, Stage2InitialX, HeadYMin, HeadYMax,
            Rail1XStart, Rail1XEnd, Rail2XStart, Rail2XEnd, RailWidthY, Stage1LengthX, Stage1WidthY, Stage2LengthX, Stage2WidthY,
            Stage1Y, Stage2Y, HeadInitialY, GantryXStart, GantryXEnd, GantryYStart, GantryYEnd, HeadX, HeadWidth, HeadHeight];
        if (numbers.Any(x => !double.IsFinite(x) || Math.Abs(x) > 1e9))
            throw new InvalidOperationException("설비 치수는 ±1,000,000,000 mm 범위의 유한한 수여야 합니다.");
        if (StageXMax - StageXMin < 0.001 || HeadYMax - HeadYMin < 0.001 ||
            GantryXEnd - GantryXStart < 0.001 || GantryYEnd - GantryYStart < 0.001)
            throw new InvalidOperationException("각 이동/갠트리 범위는 시작보다 끝이 최소 0.001 mm 커야 합니다.");
        if (Rail1XEnd - Rail1XStart < .001 || Rail2XEnd - Rail2XStart < .001 ||
            RailWidthY < .001 || Stage1LengthX < .001 || Stage1WidthY < .001 || Stage2LengthX < .001 || Stage2WidthY < .001)
            throw new InvalidOperationException("레일 길이·폭과 스테이지 길이·폭은 최소 0.001 mm여야 합니다.");
        if (StageXMin < Math.Max(Rail1XStart, Rail2XStart) || StageXMax > Math.Min(Rail1XEnd, Rail2XEnd))
            throw new InvalidOperationException("스테이지 중심의 X 이동 범위가 Rail 1과 Rail 2의 X 범위 안에 있어야 합니다.");
        if (Stage1InitialX < StageXMin || Stage1InitialX > StageXMax || Stage2InitialX < StageXMin || Stage2InitialX > StageXMax)
            throw new InvalidOperationException("스테이지 초기 X 위치는 X 이동 범위 안이어야 합니다.");
        if (Stage1Y < HeadYMin || Stage1Y > HeadYMax || Stage2Y < HeadYMin || Stage2Y > HeadYMax ||
            HeadInitialY < HeadYMin || HeadInitialY > HeadYMax || Math.Abs(Stage1Y - Stage2Y) < 0.001)
            throw new InvalidOperationException("열 Y 위치는 서로 달라야 하며 열/헤드 초기 위치는 Y 이동 범위 안이어야 합니다.");
        if (HeadWidth < 0.001 || HeadHeight < 0.001 || HeadX - HeadWidth / 2 < GantryXStart || HeadX + HeadWidth / 2 > GantryXEnd ||
            HeadYMin - HeadHeight / 2 < GantryYStart || HeadYMax + HeadHeight / 2 > GantryYEnd)
            throw new InvalidOperationException("헤드 크기는 양수여야 하며 헤드의 X 위치와 Y 이동 범위 전체가 갠트리 외곽 안이어야 합니다.");
    }
}
