namespace Beutl.Graphics.Shaders;

/// <summary>Describes one immutable uniform binding declared for a shader.</summary>
/// <remarks>Instances are created through <see cref="ShaderBindingBuilder"/>.</remarks>
internal readonly struct ShaderUniformBinding
{
    private readonly ShaderUniformValue _directValue;
    private readonly Action<ShaderUniformWriter, ShaderExecutionContext>? _bind;

    internal ShaderUniformBinding(
        string name,
        object definitionFingerprint,
        ShaderUniformValue directValue)
    {
        Name = name;
        DefinitionFingerprint = definitionFingerprint;
        _directValue = directValue;
        _bind = null;
    }

    internal ShaderUniformBinding(
        string name,
        object definitionFingerprint,
        Action<ShaderUniformWriter, ShaderExecutionContext> bind)
    {
        Name = name;
        DefinitionFingerprint = definitionFingerprint;
        _directValue = default;
        _bind = bind;
    }

    /// <summary>Gets the non-null SkSL uniform declaration name.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets whether an author-supplied binder produces this uniform's value during execution.
    /// </summary>
    internal bool ReadsExecutionContext => _bind is not null;

    internal object DefinitionFingerprint { get; }

    internal void ValidateDeclaration(SkslUniformDeclaration declaration)
    {
        if (_bind is null)
            _directValue.ThrowIfIncompatible(declaration);
    }

    internal ShaderUniformValue Bind(SkslUniformDeclaration declaration, ShaderExecutionContext? context)
    {
        if (_bind is null)
            return _directValue;

        ArgumentNullException.ThrowIfNull(context);
        var writer = new ShaderUniformWriter(declaration);
        try
        {
            _bind(writer, context);
            return writer.Complete();
        }
        finally
        {
            writer.Deactivate();
        }
    }
}
