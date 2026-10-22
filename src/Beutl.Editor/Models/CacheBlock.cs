namespace Beutl.Editor.Models;

public sealed class CacheBlock(Rational rate, int start, int length, bool isLocked)
{
    public MediaTime Start { get; } = MediaTime.FromFrames(start, rate);

    public MediaTime Length { get; } = MediaTime.FromFrames(length, rate);

    public int StartFrame { get; } = start;

    public int LengthFrame { get; } = length;

    public bool IsLocked { get; } = isLocked;
}
