namespace Beutl.Graphics.Effects;

/// <summary>Identifies an individual input and its optional composition-time override within a continuation.</summary>
public readonly record struct FilterEffectInstance
{
    public FilterEffectInstance(int index, int count, MediaTime? time = null, MediaTime? originTime = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);
        Index = index;
        Count = count;
        Time = time;
        OriginTime = originTime ?? time;
    }

    public int Index { get; }

    public int Count { get; }

    public MediaTime? Time { get; }

    /// <summary>The enclosing frame's clock before any individual time offsets.</summary>
    public MediaTime? OriginTime { get; }
}
