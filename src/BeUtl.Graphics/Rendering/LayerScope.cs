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
}

public class LayerScope : List<IRenderable>, ILayerScope
{
}
