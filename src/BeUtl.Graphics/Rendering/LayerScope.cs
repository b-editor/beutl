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

    T? First<T>()
        where T : IRenderable
    {
        foreach (IRenderable? item in AsSpan())
        {
            if (item is T typed)
            {
                return typed;
            }
        }

        return default;
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
