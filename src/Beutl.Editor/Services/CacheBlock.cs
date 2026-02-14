namespace Beutl.Editor.Services;

public sealed class CacheBlock(int rate, int start, int length, bool isLocked)
{
    public TimeSpan Start { get; } = TimeSpanExtensions.ToTimeSpan(start, rate);

    public TimeSpan Length { get; } = TimeSpanExtensions.ToTimeSpan(length, rate);

    public int StartFrame { get; } = start;

    public int LengthFrame { get; } = length;

    public bool IsLocked { get; } = isLocked;
}
