namespace Beutl.Graphics.Effects;

/// <summary>Describes one immutable uniform binding declared for a shader.</summary>
/// <remarks>Instances are created through <see cref="ShaderBindingBuilder"/>.</remarks>
internal sealed class ShaderUniformBinding
{
    private readonly Action<ShaderUniformWriter, ShaderExecutionContext> _bind;
    private readonly Action<SkslUniformDeclaration> _validate;
    internal ShaderUniformBinding(
        string name,
        object definitionFingerprint,
        bool readsExecutionContext,
        Action<ShaderUniformWriter, ShaderExecutionContext> bind,
        Action<SkslUniformDeclaration> validate)
    {
        Name = name;
        DefinitionFingerprint = definitionFingerprint;
        ReadsExecutionContext = readsExecutionContext;
        _bind = bind;
        _validate = validate;
    }

    /// <summary>Gets the non-null SkSL uniform declaration name.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets whether an author-supplied binder produces this uniform's value during execution.
    /// </summary>
    /// <remarks>
    /// Such a binder may derive the value from any <see cref="ShaderExecutionContext"/> property, including
    /// request state the recorded graph does not otherwise carry, so a cache identity covering this stage has
    /// to account for that state. A uniform whose value is fixed while recording reads nothing.
    /// </remarks>
    internal bool ReadsExecutionContext { get; }

    internal object DefinitionFingerprint { get; }

    internal void ValidateDeclaration(SkslUniformDeclaration declaration) => _validate(declaration);

    internal ShaderUniformValue Bind(SkslUniformDeclaration declaration, ShaderExecutionContext context)
    {
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
