namespace Beutl.Graphics.Rendering;

/// <summary>Selects which runtime values from one authored render input require CPU readback.</summary>
public readonly struct RenderInputReadback : IEquatable<RenderInputReadback>
{
    private readonly RenderInputReadbackKind _kind;
    private readonly IReadOnlyList<int>? _valueIndices;

    private RenderInputReadback(RenderInputReadbackKind kind, IReadOnlyList<int> valueIndices)
    {
        _kind = kind;
        _valueIndices = valueIndices;
    }

    /// <summary>Does not schedule CPU readback for any runtime value from the authored input.</summary>
    public static RenderInputReadback None { get; } = new(
        RenderInputReadbackKind.None,
        Array.AsReadOnly(Array.Empty<int>()));

    /// <summary>Schedules CPU readback for every runtime value produced by the authored input.</summary>
    public static RenderInputReadback All { get; } = new(
        RenderInputReadbackKind.All,
        Array.AsReadOnly(Array.Empty<int>()));

    /// <summary>Gets whether every runtime value produced by the authored input requires CPU readback.</summary>
    public bool ReadsAllValues => _kind == RenderInputReadbackKind.All;

    /// <summary>Gets the sorted local runtime-value indices selected by <see cref="Values"/>.</summary>
    public IReadOnlyList<int> ValueIndices => _valueIndices ?? Array.Empty<int>();

    /// <summary>Selects finite local runtime-value indices from one authored input.</summary>
    public static RenderInputReadback Values(IEnumerable<int> valueIndices)
    {
        ArgumentNullException.ThrowIfNull(valueIndices);
        int[] result = valueIndices.ToArray();
        if (result.Length == 0)
            throw new ArgumentException("At least one input value index is required.", nameof(valueIndices));
        if (result.Any(static index => index < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(valueIndices),
                "Input value indices must be non-negative.");
        }

        Array.Sort(result);
        for (int index = 1; index < result.Length; index++)
        {
            if (result[index] == result[index - 1])
                throw new ArgumentException("Input value indices must be unique.", nameof(valueIndices));
        }

        return new RenderInputReadback(
            RenderInputReadbackKind.Values,
            Array.AsReadOnly(result));
    }

    public bool Equals(RenderInputReadback other)
        => _kind == other._kind && ValueIndices.SequenceEqual(other.ValueIndices);

    public override bool Equals(object? obj)
        => obj is RenderInputReadback other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_kind);
        foreach (int valueIndex in ValueIndices)
            hash.Add(valueIndex);
        return hash.ToHashCode();
    }

    public static bool operator ==(RenderInputReadback left, RenderInputReadback right)
        => left.Equals(right);

    public static bool operator !=(RenderInputReadback left, RenderInputReadback right)
        => !left.Equals(right);

    internal bool RequiresAnyReadback => _kind is RenderInputReadbackKind.All or RenderInputReadbackKind.Values;

    internal int StructuralKind => (int)_kind;

    internal bool RequiresValue(int localIndex)
        => _kind == RenderInputReadbackKind.All
           || (_kind == RenderInputReadbackKind.Values && BinarySearch(ValueIndices, localIndex) >= 0);

    internal void ThrowIfUninitialized(string parameterName)
    {
        if (_kind == RenderInputReadbackKind.Uninitialized)
        {
            throw new ArgumentException(
                "default(RenderInputReadback) is uninitialized; use None, All, or Values.",
                parameterName);
        }
    }

    internal void ValidateRuntimeCount(
        RenderValueCardinality cardinality,
        int valueCount)
    {
        if (_kind != RenderInputReadbackKind.Values)
            return;

        foreach (int valueIndex in ValueIndices)
        {
            bool isImpossible = cardinality.Maximum is { } maximum && valueIndex >= maximum;
            bool isGuaranteedButMissing = valueIndex < cardinality.Minimum && valueIndex >= valueCount;
            if (isImpossible || isGuaranteedButMissing)
            {
                throw new InvalidOperationException(
                    "A render operation declared readback for a local input value index that was not produced at runtime.");
            }
        }
    }

    private static int BinarySearch(IReadOnlyList<int> values, int value)
    {
        int lower = 0;
        int upper = values.Count - 1;
        while (lower <= upper)
        {
            int middle = lower + ((upper - lower) / 2);
            int current = values[middle];
            if (current == value)
                return middle;
            if (current < value)
                lower = middle + 1;
            else
                upper = middle - 1;
        }

        return -1;
    }
}

internal enum RenderInputReadbackKind : byte
{
    Uninitialized,
    None,
    All,
    Values,
}
