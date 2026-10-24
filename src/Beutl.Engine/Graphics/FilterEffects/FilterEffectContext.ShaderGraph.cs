using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Effects;

public sealed partial class FilterEffectContext
{
    /// <summary>Synchronously records a declarative shader graph and appends it atomically to this effect stream.</summary>
    public void ShaderGraph<T>(T state, Func<ShaderGraphBuilder, T, ShaderGraphOutput> record)
    {
        ThrowIfDisposed();
        ShaderGraph(ShaderGraphBuilder.Build(state, record));
    }

    /// <summary>Appends an immutable graph. Its programs are compiled by the renderer, never during authoring.</summary>
    public void ShaderGraph(ShaderGraphDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        AppendDescription(new FEItem_ShaderGraph(description));
    }
}

internal sealed record FEItem_ShaderGraph(ShaderGraphDescription Description) : IFEItem
{
    public Rect TransformBounds(Rect bounds) => Description.TransformBounds(bounds);
}
