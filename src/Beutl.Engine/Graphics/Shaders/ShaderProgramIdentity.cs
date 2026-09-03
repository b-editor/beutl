namespace Beutl.Graphics.Shaders;

/// <summary>
/// A program-cache bucket identity. The stable hash is only the bucket selector; equality compares the complete
/// backend, generated source, binding signature, capability class, and relevant backend limits.
/// </summary>
internal sealed class ShaderProgramIdentity : IEquatable<ShaderProgramIdentity>
{
    private readonly object[] _bindings;
    private readonly int _hashCode;

    private ShaderProgramIdentity(
        ShaderProgramBackend backend,
        string source,
        IEnumerable<object> bindings,
        SkslBackendBudget budget)
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
        _hashCode = ComputeStableHashCode(backend, source);
    }

    private ShaderProgramBackend Backend { get; }

    private string Source { get; }

    private SkslBackendBudget Budget { get; }

    internal static ShaderProgramIdentity CreateSksl(
        string source,
        IReadOnlyList<SkslMergedBindingLayout> bindings,
        SkslBackendBudget budget)
        => new(
            ShaderProgramBackend.Sksl,
            source,
            bindings.Cast<object>(),
            budget);

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
           && Backend == other.Backend
           && Source == other.Source
           && Budget.Equals(other.Budget)
           && _bindings.AsSpan().SequenceEqual(other._bindings);

    public override bool Equals(object? obj) => obj is ShaderProgramIdentity other && Equals(other);

    public override int GetHashCode() => _hashCode;

    private static int ComputeStableHashCode(ShaderProgramBackend backend, string source)
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
