namespace Beutl.Graphics.Effects;

/// <summary>Writes the single value produced by an execution-time uniform binder.</summary>
/// <remarks>
/// A binder must call one <c>Set</c> overload exactly once. The writer is valid only during that binder invocation
/// and must not be retained.
/// </remarks>
public sealed class ShaderUniformWriter
{
    private readonly SkslUniformDeclaration _declaration;
    private ShaderUniformValue? _value;
    private bool _active = true;

    internal ShaderUniformWriter(SkslUniformDeclaration declaration)
    {
        _declaration = declaration;
    }

    /// <summary>Sets the binder result from a supported canonical scalar, vector, or matrix value.</summary>
    /// <typeparam name="T">An unmanaged type in the supported canonical uniform allowlist.</typeparam>
    /// <param name="value">The value to validate against the parsed SkSL declaration.</param>
    /// <exception cref="InvalidOperationException">
    /// The writer is inactive, a value was already set, or the value is incompatible with the SkSL declaration.
    /// </exception>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is not a supported canonical uniform type.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An unsigned value cannot be represented by its SkSL type.</exception>
    public void Set<T>(T value)
        where T : unmanaged
    {
        ThrowIfInactive();
        if (_value is not null)
            throw new InvalidOperationException("A shader uniform binder must set its writer exactly once.");
        ShaderCanonicalValue canonical = ShaderCanonicalValue.Create(value);
        canonical.ThrowIfIncompatible(_declaration);
        _value = new ShaderUniformValue(canonical.Values, canonical.Integers, canonical.IsInteger);
    }

    /// <summary>Sets the binder result from a floating-point sequence copied during the call.</summary>
    /// <param name="values">The values to validate and copy; the caller's memory is not retained.</param>
    /// <exception cref="InvalidOperationException">
    /// The writer is inactive, a value was already set, or the sequence is incompatible with the SkSL declaration.
    /// </exception>
    public void Set(ReadOnlySpan<float> values)
    {
        ThrowIfInactive();
        if (_value is not null)
            throw new InvalidOperationException("A shader uniform binder must set its writer exactly once.");
        float[] copy = values.ToArray();
        ShaderCanonicalValue.ThrowIfFloatSequenceIncompatible(copy, _declaration);
        _value = new ShaderUniformValue(copy, null, false);
    }

    internal ShaderUniformValue Complete()
    {
        ThrowIfInactive();
        return _value
               ?? throw new InvalidOperationException("A shader uniform binder must set its writer exactly once.");
    }

    internal void Deactivate() => _active = false;

    private void ThrowIfInactive()
    {
        if (!_active)
            throw new InvalidOperationException("The shader uniform writer is no longer active.");
    }
}
