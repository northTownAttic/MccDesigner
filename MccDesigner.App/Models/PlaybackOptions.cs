namespace MccDesigner.Models;

public sealed class PlaybackOptions
{
    public int RepeatCount { get; set; } = 1;
    public bool Infinite { get; set; }
    public PlaybackOptions Copy() => (PlaybackOptions)MemberwiseClone();
    public void Validate()
    {
        if (RepeatCount < 1 || RepeatCount > 1_000_000)
            throw new InvalidOperationException("반복 횟수는 1~1,000,000 사이의 정수여야 합니다.");
    }
}
