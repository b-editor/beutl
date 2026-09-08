namespace Beutl.Graphics.Rendering.Cache;

internal sealed class RenderNodeCachedOutput
{
    public RenderNodeCachedOutput(IReadOnlyList<RenderNodeCachedValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = values;
    }

    public IReadOnlyList<RenderNodeCachedValue> Values { get; }
}
