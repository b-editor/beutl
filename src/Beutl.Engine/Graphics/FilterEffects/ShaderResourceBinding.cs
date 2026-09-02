using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>Describes one immutable child-shader resource binding declared for a shader.</summary>
/// <remarks>Instances are created through <see cref="ShaderBindingBuilder"/>.</remarks>
internal sealed class ShaderResourceBinding
{
    private readonly Action<ShaderResourceWriter, object, ShaderExecutionContext> _bind;
    private readonly Func<Action<object>, bool> _useResource;

    internal ShaderResourceBinding(
        string name,
        RenderResource resource,
        ShaderResourceCoordinateSpace coordinateSpace,
        object definitionFingerprint,
        Action<ShaderResourceWriter, object, ShaderExecutionContext> bind,
        Func<Action<object>, bool> useResource)
    {
        Name = name;
        Resource = resource;
        CoordinateSpace = coordinateSpace;
        DefinitionFingerprint = definitionFingerprint;
        _bind = bind;
        _useResource = useResource;
    }

    /// <summary>Gets the non-null SkSL child-shader declaration name.</summary>
    public string Name { get; }

    /// <summary>Gets how coordinates passed to the child shader are interpreted.</summary>
    public ShaderResourceCoordinateSpace CoordinateSpace { get; }

    /// <summary>Gets the request-scoped resource token used by the execution-time binder.</summary>
    /// <remarks>
    /// The token scopes access to the raw resource without changing whether the request or the caller owns it.
    /// </remarks>
    public RenderResource Resource { get; }

    internal object DefinitionFingerprint { get; }

    internal SKShader Bind(ShaderExecutionContext context)
    {
        SKShader? result = null;
        bool invoked = _useResource(value =>
        {
            var writer = new ShaderResourceWriter();
            bool completed = false;
            try
            {
                _bind(writer, value, context);
                result = writer.Complete();
                completed = true;
            }
            finally
            {
                writer.Deactivate();
                if (!completed)
                    writer.DisposePending();
            }
        });
        if (!invoked || result is null)
            throw new InvalidOperationException($"Shader resource binder '{Name}' did not produce a shader.");
        return result;
    }
}
