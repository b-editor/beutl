namespace Beutl.Graphics.Rendering;

/// <summary>Selects which runtime values from one authored target-command input require CPU readback.</summary>
public readonly struct TargetInputReadback : IEquatable<TargetInputReadback>
{
    private readonly TargetInputReadbackKind _kind;
    private readonly IReadOnlyList<int>? _valueIndices;

    private TargetInputReadback(TargetInputReadbackKind kind, IReadOnlyList<int> valueIndices)
    {
        _kind = kind;
        _valueIndices = valueIndices;
    }

    /// <summary>Does not schedule CPU readback for any runtime value from the authored input.</summary>
    public static TargetInputReadback None { get; } = new(
        TargetInputReadbackKind.None,
        Array.AsReadOnly(Array.Empty<int>()));

    /// <summary>Schedules CPU readback for every runtime value produced by the authored input.</summary>
    public static TargetInputReadback All { get; } = new(
        TargetInputReadbackKind.All,
        Array.AsReadOnly(Array.Empty<int>()));

    /// <summary>Gets whether every runtime value produced by the authored input requires CPU readback.</summary>
    public bool ReadsAllValues => _kind == TargetInputReadbackKind.All;

    /// <summary>Gets the sorted local runtime-value indices selected by <see cref="Values"/>.</summary>
    public IReadOnlyList<int> ValueIndices => _valueIndices ?? Array.Empty<int>();

    /// <summary>Selects finite local runtime-value indices from one authored input.</summary>
    public static TargetInputReadback Values(IEnumerable<int> valueIndices)
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

        return new TargetInputReadback(
            TargetInputReadbackKind.Values,
            Array.AsReadOnly(result));
    }

    public bool Equals(TargetInputReadback other)
        => _kind == other._kind && ValueIndices.SequenceEqual(other.ValueIndices);

    public override bool Equals(object? obj)
        => obj is TargetInputReadback other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_kind);
        foreach (int valueIndex in ValueIndices)
            hash.Add(valueIndex);
        return hash.ToHashCode();
    }

    public static bool operator ==(TargetInputReadback left, TargetInputReadback right)
        => left.Equals(right);

    public static bool operator !=(TargetInputReadback left, TargetInputReadback right)
        => !left.Equals(right);

    internal bool RequiresAnyReadback => _kind is TargetInputReadbackKind.All or TargetInputReadbackKind.Values;

    internal int StructuralKind => (int)_kind;

    internal bool RequiresValue(int localIndex)
        => _kind == TargetInputReadbackKind.All
           || (_kind == TargetInputReadbackKind.Values && ValueIndices.BinarySearch(localIndex) >= 0);

    internal void ThrowIfUninitialized(string parameterName)
    {
        if (_kind == TargetInputReadbackKind.Uninitialized)
        {
            throw new ArgumentException(
                "default(TargetInputReadback) is uninitialized; use None, All, or Values.",
                parameterName);
        }
    }

    internal void ValidateRuntimeCount(
        RenderValueCardinality cardinality,
        int valueCount)
    {
        if (_kind != TargetInputReadbackKind.Values)
            return;

        foreach (int valueIndex in ValueIndices)
        {
            bool isImpossible = cardinality.Maximum is { } maximum && valueIndex >= maximum;
            bool isGuaranteedButMissing = valueIndex < cardinality.Minimum && valueIndex >= valueCount;
            if (isImpossible || isGuaranteedButMissing)
            {
                throw new InvalidOperationException(
                    "A target command declared readback for a local input value index that was not produced at runtime.");
            }
        }
    }
}

internal enum TargetInputReadbackKind : byte
{
    Uninitialized,
    None,
    All,
    Values,
}

internal static class TargetInputReadbackIndexExtensions
{
    public static int BinarySearch(this IReadOnlyList<int> values, int value)
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
