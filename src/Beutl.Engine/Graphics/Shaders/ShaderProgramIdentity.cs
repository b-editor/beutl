using System.Collections.Immutable;

namespace Beutl.Graphics.Shaders;

/// <summary>
/// A program-cache bucket identity. The stable hash is only the bucket selector; equality compares the complete
/// backend, generated source, binding signature, capability class, and relevant backend limits.
/// </summary>
internal sealed class ShaderProgramIdentity : IEquatable<ShaderProgramIdentity>
{
    private readonly ImmutableArray<SkslMergedBindingLayout> _bindings;
    private readonly int _hashCode;

    private ShaderProgramIdentity(
        ShaderProgramBackend backend,
        string source,
        ImmutableArray<SkslMergedBindingLayout> bindings,
        SkslBackendBudget budget)
    {
        Backend = backend;
        Source = source;
        _bindings = bindings;
        Budget = budget;
        _hashCode = ComputeStableHashCode(backend, source);
    }

    private ShaderProgramBackend Backend { get; }

    internal string Source { get; }

    internal SkslBackendBudget Budget { get; }

    internal ImmutableArray<SkslMergedBindingLayout> Bindings => _bindings;

    internal static ShaderProgramIdentity CreateSksl(
        string source,
        ImmutableArray<SkslMergedBindingLayout> bindings,
        SkslBackendBudget budget)
        => new(
            ShaderProgramBackend.Sksl,
            source,
            bindings,
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
           && _bindings.AsSpan().SequenceEqual(other._bindings.AsSpan());

    public override bool Equals(object? obj) => obj is ShaderProgramIdentity other && Equals(other);

    public override int GetHashCode() => _hashCode;

    private static int ComputeStableHashCode(ShaderProgramBackend backend, string source)
        => unchecked((int)Fnv1a.AppendChars(Fnv1a.Append(Fnv1a.Offset32, (byte)backend), source));
}
