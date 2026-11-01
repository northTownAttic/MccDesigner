namespace MccDesigner.Models;

public sealed class MediaSupplySettings
{
    public bool Enabled { get; set; }
    public double IntervalSeconds { get; set; } = 30;
    private double? stage1Interval, stage2Interval;
    public double Stage1IntervalSeconds { get => stage1Interval ?? IntervalSeconds; set => stage1Interval = value; }
    public double Stage2IntervalSeconds { get => stage2Interval ?? IntervalSeconds; set => stage2Interval = value; }
    public double IntervalFor(string stage) => stage == MachineAxes.Stage1 ? Stage1IntervalSeconds : stage == MachineAxes.Stage2 ? Stage2IntervalSeconds : throw new InvalidOperationException("공급 대상 스테이지가 올바르지 않습니다.");
    public int BatchSize { get; set; } = 1;
    public bool SupplyAtStart { get; set; }
    public MediaSupplySettings Copy() => (MediaSupplySettings)MemberwiseClone();
    public void Validate()
    {
        if (new[] { IntervalSeconds, Stage1IntervalSeconds, Stage2IntervalSeconds }.Any(x => !double.IsFinite(x) || x < .001 || x > 1e9))
            throw new InvalidOperationException("각 열의 전처리 공급 주기는 0.001~1,000,000,000초여야 합니다.");
        if (BatchSize < 1 || BatchSize > 1_000_000)
            throw new InvalidOperationException("전처리 1회 공급 매수는 1~1,000,000매여야 합니다.");
    }
}
