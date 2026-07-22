namespace Beutl.Graphics.Rendering;

/// <summary>
/// Describes a target-relative pixel access region without resolving a symbolic full target during recording.
/// </summary>
public readonly struct TargetRegion
{
    private readonly TargetRegionKind _kind;
    private readonly Rect _value;

    private TargetRegion(TargetRegionKind kind, Rect value = default)
    {
        _kind = kind;
        _value = value;
    }

    public static TargetRegion Full { get; } = new(TargetRegionKind.Full);

    public static TargetRegion Empty { get; } = new(TargetRegionKind.Empty);

    public static TargetRegion Region(Rect region)
    {
        if (!RenderRectValidation.IsFiniteNonNegative(region))
        {
            throw new ArgumentException(
                "A target region must be finite and have non-negative dimensions.",
                nameof(region));
        }

        return region.Width == 0 || region.Height == 0
            ? Empty
            : new TargetRegion(TargetRegionKind.Region, region);
    }

    internal TargetRegionKind Kind => _kind;

    internal Rect Value
        => _kind == TargetRegionKind.Region
            ? _value
            : throw new InvalidOperationException("Only a finite target region has a Rect value.");

    internal void ThrowIfUninitialized(string parameterName)
    {
        if (_kind == TargetRegionKind.Uninitialized)
        {
            throw new ArgumentException(
                "default(TargetRegion) is uninitialized; use Full, Empty, or Region.",
                parameterName);
        }
    }
}

internal enum TargetRegionKind : byte
{
    Uninitialized,
    Full,
    Empty,
    Region,
}
