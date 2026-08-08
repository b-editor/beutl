namespace Beutl.Graphics.Rendering;

/// <summary>
/// Declares the number of materializable values represented by a recorded render fragment.
/// </summary>
public readonly struct RenderValueCardinality : IEquatable<RenderValueCardinality>
{
    private readonly bool _isInitialized;

    private RenderValueCardinality(int minimum, int? maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
        _isInitialized = true;
    }

    public int Minimum { get; }

    public int? Maximum { get; }

    public static RenderValueCardinality None { get; } = new(0, 0);

    public static RenderValueCardinality Single { get; } = new(1, 1);

    public static RenderValueCardinality ZeroOrOne { get; } = new(0, 1);

    public static RenderValueCardinality Dynamic { get; } = new(0, null);

    public static RenderValueCardinality Exactly(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "The value count cannot be negative.");

        return new RenderValueCardinality(count, count);
    }

    public static RenderValueCardinality Range(int minimum, int? maximum)
    {
        if (minimum < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimum), minimum, "The minimum value count cannot be negative.");
        }

        if (maximum is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximum), maximum, "The maximum value count cannot be negative.");
        }

        if (maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximum), maximum, "The maximum value count cannot be smaller than the minimum.");
        }

        return new RenderValueCardinality(minimum, maximum);
    }

    public bool Equals(RenderValueCardinality other)
        => _isInitialized == other._isInitialized
           && Minimum == other.Minimum
           && Maximum == other.Maximum;

    public override bool Equals(object? obj)
        => obj is RenderValueCardinality other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(_isInitialized, Minimum, Maximum);

    internal bool IsInitialized => _isInitialized;

    internal void ThrowIfUninitialized(string parameterName)
    {
        if (!_isInitialized)
        {
            throw new ArgumentException(
                "default(RenderValueCardinality) is uninitialized; use a named value, Exactly, or Range.",
                parameterName);
        }
    }
}
