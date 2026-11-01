namespace MccDesigner.Models;

public sealed class MediaSupplySettings
{
    public bool Enabled { get; set; }
    public double IntervalSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 1;
    public bool SupplyAtStart { get; set; }
    public MediaSupplySettings Copy() => (MediaSupplySettings)MemberwiseClone();
    public void Validate()
    {
        if (!double.IsFinite(IntervalSeconds) || IntervalSeconds < .001 || IntervalSeconds > 1e9)
            throw new InvalidOperationException("전처리 공급 주기는 0.001~1,000,000,000초여야 합니다.");
        if (BatchSize < 1 || BatchSize > 1_000_000)
            throw new InvalidOperationException("전처리 1회 공급 매수는 1~1,000,000매여야 합니다.");
    }
}
