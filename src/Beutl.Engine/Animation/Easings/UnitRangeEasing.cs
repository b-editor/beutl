namespace Beutl.Animation.Easings;

/// <summary>An easing whose output is guaranteed to stay in [0, 1] for progress in [0, 1].</summary>
public abstract class UnitRangeEasing : Easing
{
    public sealed override bool TryGetOutputRange(out float minimum, out float maximum)
    {
        minimum = 0;
        maximum = 1;
        return true;
    }
}
