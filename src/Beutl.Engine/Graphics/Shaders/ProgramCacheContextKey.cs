namespace Beutl.Graphics.Shaders;

/// <summary>
/// Identifies the backend lifetime and compile contract in which a merged shader program is valid.
/// </summary>
internal sealed class ProgramCacheContextKey : IEquatable<ProgramCacheContextKey>
{
    public ProgramCacheContextKey(
        object deviceIdentity,
        object contextIdentity,
        object backendCapabilityClass,
        string colorAlphaFormatContract,
        object compileOptionsIdentity)
    {
        DeviceIdentity = deviceIdentity ?? throw new ArgumentNullException(nameof(deviceIdentity));
        ContextIdentity = contextIdentity ?? throw new ArgumentNullException(nameof(contextIdentity));
        BackendCapabilityClass = backendCapabilityClass
            ?? throw new ArgumentNullException(nameof(backendCapabilityClass));
        ColorAlphaFormatContract = colorAlphaFormatContract
            ?? throw new ArgumentNullException(nameof(colorAlphaFormatContract));
        CompileOptionsIdentity = compileOptionsIdentity
            ?? throw new ArgumentNullException(nameof(compileOptionsIdentity));
    }

    public object DeviceIdentity { get; }

    public object ContextIdentity { get; }

    public object BackendCapabilityClass { get; }

    public string ColorAlphaFormatContract { get; }

    public object CompileOptionsIdentity { get; }

    public bool Equals(ProgramCacheContextKey? other)
        => other is not null
           && Equals(DeviceIdentity, other.DeviceIdentity)
           && Equals(ContextIdentity, other.ContextIdentity)
           && Equals(BackendCapabilityClass, other.BackendCapabilityClass)
           && string.Equals(
               ColorAlphaFormatContract,
               other.ColorAlphaFormatContract,
               StringComparison.Ordinal)
           && Equals(CompileOptionsIdentity, other.CompileOptionsIdentity);

    public override bool Equals(object? obj)
        => obj is ProgramCacheContextKey other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(
            DeviceIdentity,
            ContextIdentity,
            BackendCapabilityClass,
            ColorAlphaFormatContract,
            CompileOptionsIdentity);
}
