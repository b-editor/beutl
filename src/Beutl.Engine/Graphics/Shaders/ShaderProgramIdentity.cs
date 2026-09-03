namespace Beutl.Graphics.Shaders;

/// <summary>
/// A program-cache bucket identity. The stable hash is only the bucket selector; equality compares the complete
/// backend, generated source, binding signature, capability class, and relevant backend limits.
/// </summary>
internal sealed class ShaderProgramIdentity : IEquatable<ShaderProgramIdentity>
{
    private readonly object[] _bindings;

    private ShaderProgramIdentity(
        ShaderProgramBackend backend,
        string source,
        IEnumerable<object> bindings,
        SkslBackendBudget budget,
        int? bucketHashOverride = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(budget);
        if (!Enum.IsDefined(backend))
            throw new ArgumentOutOfRangeException(nameof(backend));

        Backend = backend;
        Source = source;
        _bindings = bindings.ToArray();
        Budget = budget;
        BucketHash = bucketHashOverride ?? ComputeStableBucketHash(backend, source);
    }

    public int BucketHash { get; }

    private ShaderProgramBackend Backend { get; }

    private string Source { get; }

    private SkslBackendBudget Budget { get; }

    internal static ShaderProgramIdentity CreateSksl(
        string source,
        IReadOnlyList<SkslMergedBindingLayout> bindings,
        SkslBackendBudget budget,
        int? bucketHashOverride = null)
        => new(
            ShaderProgramBackend.Sksl,
            source,
            bindings.Cast<object>(),
            budget,
            bucketHashOverride);

    internal static ShaderProgramIdentity CreateStandaloneSksl(
        string source,
        SkslBackendBudget budget)
        => CreateSksl(source, [], budget);

    internal static ShaderProgramIdentity CreateSpirv(string source)
        => new(
            ShaderProgramBackend.Spirv,
            source,
            [],
            SkslBackendBudgetResolver.SpirvVulkan);

    public bool Equals(ShaderProgramIdentity? other)
        => other is not null
           && BucketHash == other.BucketHash
           && Backend == other.Backend
           && Source == other.Source
           && Budget.Equals(other.Budget)
           && _bindings.AsSpan().SequenceEqual(other._bindings);

    public override bool Equals(object? obj) => obj is ShaderProgramIdentity other && Equals(other);

    public override int GetHashCode() => BucketHash;

    private static int ComputeStableBucketHash(ShaderProgramBackend backend, string source)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        uint hash = (offset ^ (byte)backend) * prime;
        foreach (char value in source)
        {
            hash ^= value;
            hash *= prime;
        }
        return unchecked((int)hash);
    }
}
