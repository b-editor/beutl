namespace Beutl.Graphics.Effects;

internal sealed class SkslBackendBudget : IEquatable<SkslBackendBudget>
{
    private static readonly object s_unlimitedCapability = new();

    public SkslBackendBudget(
        object capabilityClass,
        int maxStages,
        int maxUniformVectors,
        int maxSamplers,
        int maxChildren,
        int maxSourceBytes,
        int maxProgramTokens)
    {
        ArgumentNullException.ThrowIfNull(capabilityClass);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxStages, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxUniformVectors);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSamplers);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChildren, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSourceBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxProgramTokens, 1);

        CapabilityClass = capabilityClass;
        MaxStages = maxStages;
        MaxUniformVectors = maxUniformVectors;
        MaxSamplers = maxSamplers;
        MaxChildren = maxChildren;
        MaxSourceBytes = maxSourceBytes;
        MaxProgramTokens = maxProgramTokens;
    }

    public static SkslBackendBudget Unlimited { get; } = new(
        s_unlimitedCapability,
        int.MaxValue,
        int.MaxValue,
        int.MaxValue,
        int.MaxValue,
        int.MaxValue,
        int.MaxValue);

    public object CapabilityClass { get; }

    public int MaxStages { get; }

    public int MaxUniformVectors { get; }

    public int MaxSamplers { get; }

    public int MaxChildren { get; }

    public int MaxSourceBytes { get; }

    public int MaxProgramTokens { get; }

    public bool Equals(SkslBackendBudget? other)
        => other is not null
           && Equals(CapabilityClass, other.CapabilityClass)
           && MaxStages == other.MaxStages
           && MaxUniformVectors == other.MaxUniformVectors
           && MaxSamplers == other.MaxSamplers
           && MaxChildren == other.MaxChildren
           && MaxSourceBytes == other.MaxSourceBytes
           && MaxProgramTokens == other.MaxProgramTokens;

    public override bool Equals(object? obj) => obj is SkslBackendBudget other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(
            CapabilityClass,
            MaxStages,
            MaxUniformVectors,
            MaxSamplers,
            MaxChildren,
            MaxSourceBytes,
            MaxProgramTokens);
}
