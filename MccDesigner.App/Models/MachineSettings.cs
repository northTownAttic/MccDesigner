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
    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        foreach (var property in NumericProperties)
        {
            var value = (double)property.GetValue(this)!;
            if (!double.IsFinite(value) || Math.Abs(value) > 1e9)
                errors.Add($"{property.Name}: ±1,000,000,000 mm 범위의 유한한 수여야 합니다.");
        }
        if (errors.Count > 0) return errors;
        Range("Stage X 이동", StageXMin, StageXMax); Range("Head Y 이동", HeadYMin, HeadYMax);
        Range("갠트리 X", GantryXStart, GantryXEnd); Range("갠트리 Y", GantryYStart, GantryYEnd);
        Range("Rail 1 X", Rail1XStart, Rail1XEnd); Range("Rail 2 X", Rail2XStart, Rail2XEnd);
        Positive("Rail 폭 Y", RailWidthY); Positive("Stage 1 길이 X", Stage1LengthX); Positive("Stage 1 폭 Y", Stage1WidthY);
        Positive("Stage 2 길이 X", Stage2LengthX); Positive("Stage 2 폭 Y", Stage2WidthY);
        Positive("Head 폭 X", HeadWidth); Positive("Head 높이 Y", HeadHeight);
        Inside("Stage X 이동 범위 / Rail 1", StageXMin, StageXMax, Rail1XStart, Rail1XEnd);
        Inside("Stage X 이동 범위 / Rail 2", StageXMin, StageXMax, Rail2XStart, Rail2XEnd);
        Inside("Stage 1 초기 X", Stage1InitialX, Stage1InitialX, StageXMin, StageXMax);
        Inside("Stage 2 초기 X", Stage2InitialX, Stage2InitialX, StageXMin, StageXMax);
        Inside("1열 Y", Stage1Y, Stage1Y, HeadYMin, HeadYMax);
        Inside("2열 Y", Stage2Y, Stage2Y, HeadYMin, HeadYMax);
        Inside("Head 초기 Y", HeadInitialY, HeadInitialY, HeadYMin, HeadYMax);
        if (Math.Abs(Stage1Y - Stage2Y) < .001) errors.Add("1열과 2열의 Y 위치는 최소 0.001 mm 떨어져 있어야 합니다.");
        if (HeadWidth > 0) Inside("Head X 외곽 / 갠트리", HeadX - HeadWidth / 2, HeadX + HeadWidth / 2, GantryXStart, GantryXEnd);
        if (HeadHeight > 0) Inside("Head Y 이동 외곽 / 갠트리", HeadYMin - HeadHeight / 2, HeadYMax + HeadHeight / 2, GantryYStart, GantryYEnd);
        return errors;

        void Range(string label, double start, double end)
        {
            if (end - start < .001) errors.Add($"{label}: 시작 {start:0.###}, 끝 {end:0.###} mm — 끝은 시작보다 최소 0.001 mm 커야 합니다.");
        }
        void Positive(string label, double value)
        {
            if (value < .001) errors.Add($"{label}: {value:0.###} mm — 최소 0.001 mm여야 합니다.");
        }
        void Inside(string label, double from, double to, double min, double max)
        {
            if (from < min || to > max)
                errors.Add($"{label}: {from:0.###}~{to:0.###} mm가 허용 범위 {min:0.###}~{max:0.###} mm를 벗어났습니다.");
        }
    }
    internal static readonly System.Reflection.PropertyInfo[] NumericProperties = typeof(MachineSettings)
        .GetProperties().Where(p => p.PropertyType == typeof(double)).ToArray();
    public void Validate()
    {
        var errors = GetValidationErrors();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
    }
}
