using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.Graphics.Shaders;

/// <summary>Describes one immutable child-shader resource binding declared for a shader.</summary>
/// <remarks>Instances are created through <see cref="ShaderBindingBuilder"/>.</remarks>
internal readonly struct ShaderResourceBinding
{
    private readonly Action<ShaderResourceWriter, object, ShaderExecutionContext> _bind;
    private readonly RenderResource _resource;

    internal ShaderResourceBinding(
        string name,
        RenderResource resource,
        ShaderResourceCoordinateSpace coordinateSpace,
        object binderIdentity,
        Action<ShaderResourceWriter, object, ShaderExecutionContext> bind)
    {
        Name = name;
        _resource = resource;
        CoordinateSpace = coordinateSpace;
        BinderIdentity = binderIdentity;
        _bind = bind;
    }

    /// <summary>Gets the non-null SkSL child-shader declaration name.</summary>
    public string Name { get; }

    /// <summary>Gets how coordinates passed to the child shader are interpreted.</summary>
    public ShaderResourceCoordinateSpace CoordinateSpace { get; }

    internal RenderResource Resource => _resource;

    internal object BinderIdentity { get; }

    internal SKShader Bind(ShaderExecutionContext context)
    {
        RenderResource resource = _resource;
        Action<ShaderResourceWriter, object, ShaderExecutionContext> bind = _bind;
        return resource.Registry.UseUntyped(resource, value =>
        {
            var writer = new ShaderResourceWriter();
            bool completed = false;
            try
            {
                bind(writer, value, context);
                SKShader result = writer.Complete();
                completed = true;
                return result;
            }
            finally
            {
                writer.Deactivate();
                if (!completed)
                    writer.DisposePending();
            }
        });
    }
}
