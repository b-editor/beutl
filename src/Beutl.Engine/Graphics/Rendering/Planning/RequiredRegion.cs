namespace Beutl.Graphics.Rendering;

internal readonly record struct RequiredRegion
{
    private readonly RequiredRegionKind _kind;
    private readonly Rect _value;

    private RequiredRegion(RequiredRegionKind kind, Rect value = default)
    {
        _kind = kind;
        _value = value;
    }

    public static RequiredRegion Empty { get; } = new(RequiredRegionKind.Empty);

    public static RequiredRegion Full { get; } = new(RequiredRegionKind.Full);

    public static RequiredRegion Region(Rect value)
    {
        RenderRectValidation.ThrowIfInvalidResult(
            value,
            "A required region must be finite and have non-negative dimensions.");
        return value.Width == 0 || value.Height == 0
            ? Empty
            : new RequiredRegion(RequiredRegionKind.Region, value);
    }

    public bool IsEmpty => _kind == RequiredRegionKind.Empty;

    public bool IsFull => _kind == RequiredRegionKind.Full;

    public Rect Value
        => _kind == RequiredRegionKind.Region
            ? _value
            : throw new InvalidOperationException("Only a finite required region has a Rect value.");

    public RequiredRegion Union(RequiredRegion other)
    {
        ThrowIfUninitialized();
        other.ThrowIfUninitialized();
        if (IsFull || other.IsFull)
            return Full;
        if (IsEmpty)
            return other;
        if (other.IsEmpty)
            return this;
        return Region(_value.Union(other._value));
    }

    public RequiredRegion Intersect(Rect bounds)
    {
        ThrowIfUninitialized();
        RenderRectValidation.ThrowIfInvalidInput(bounds, nameof(bounds));
        if (IsEmpty)
            return Empty;
        if (IsFull)
            return bounds.Width == 0 || bounds.Height == 0 ? Empty : Region(bounds);
        return Region(_value.Intersect(bounds));
    }

    public Rect Resolve(Rect fullBounds)
    {
        ThrowIfUninitialized();
        RenderRectValidation.ThrowIfInvalidInput(fullBounds, nameof(fullBounds));
        return _kind switch
        {
            RequiredRegionKind.Empty => Rect.Empty,
            RequiredRegionKind.Full => fullBounds,
            RequiredRegionKind.Region => _value,
            _ => throw new InvalidOperationException("The required region is uninitialized."),
        };
    }

    private void ThrowIfUninitialized()
    {
        if (_kind == RequiredRegionKind.Uninitialized)
        {
            throw new InvalidOperationException(
                "default(RequiredRegion) is uninitialized; use Empty, Full, or Region.");
        }
    }
}
