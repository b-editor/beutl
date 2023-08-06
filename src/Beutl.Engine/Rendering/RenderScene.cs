using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Rendering;

public sealed class RenderScene : IDisposable
{
    private readonly SortedDictionary<int, RenderLayer> _layer = new();

    public RenderScene(PixelSize size)
    {
        Size = size;
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

    public void Render(Audio.Audio audio)
    {
        audio.Clear();

        foreach (RenderLayer item in _layer.Values)
        {
            item.Render(audio);
        }
    }
}
