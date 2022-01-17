using System.Runtime.InteropServices;

namespace BeUtl.Rendering;

public interface ILayerScope : IList<IRenderable>
{
    void Append(IRenderable item)
    {
        item.IsVisible = true;
        if (!Contains(item))
        {
            Add(item);
        }
    }

    void Invalidate(IRenderable item)
    {
        item.IsVisible = false;
        if (!Contains(item))
        {
            Add(item);
        }
    }

    Span<IRenderable> AsSpan();
}

public sealed class LayerScope : List<IRenderable>, ILayerScope
{
    public Span<IRenderable> AsSpan()
    {
        return CollectionsMarshal.AsSpan(this);
    }
}
