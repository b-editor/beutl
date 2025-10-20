using Beutl.Animation;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class RenderScene : IDisposable
{
    private readonly SortedDictionary<int, RenderLayer> _layer = [];
    internal readonly RenderNodeCacheContext _cacheContext;

    public RenderScene(PixelSize size)
    {
        Size = size;
        _cacheContext = new RenderNodeCacheContext(this);
    }

    public RenderLayer this[int index]
    {
        get
        {
            if (!_layer.TryGetValue(index, out RenderLayer? value))
            {
                value = new RenderLayer(this);
                _layer.Add(index, value);
            }

            return value;
        }
    }

    public PixelSize Size { get; }

    public void Clear()
    {
        foreach ((int _, RenderLayer value) in _layer)
        {
            value.Clear();
        }
    }

    public void Dispose()
    {
        foreach (RenderLayer item in _layer.Values)
        {
            item.Dispose();
        }
        _layer.Clear();
    }

    public void Render(ImmediateCanvas canvas)
    {
        using (canvas.Push())
        {
            canvas.Clear();

            foreach (RenderLayer item in _layer.Values)
            {
                item.Render(canvas);
            }
        }
    }

    public Drawable? HitTest(Point point)
    {
        foreach (int key in _layer.Keys.Reverse())
        {
            if (_layer[key].HitTest(point) is { } drawable)
            {
                return drawable;
            }
        }

        return null;
    }

    internal void ClearCache()
    {
        foreach (RenderLayer layer in _layer.Values)
        {
            layer.ClearCache();
        }
    }
}
