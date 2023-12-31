using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Rendering;

public sealed class RenderScene(PixelSize size) : IDisposable
{
    private readonly SortedDictionary<int, RenderLayer> _layer = [];

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

    public PixelSize Size { get; } = size;

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
}
