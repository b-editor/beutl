using System.Runtime.InteropServices;

using BeUtl.Graphics;
using BeUtl.ProjectSystem;
using BeUtl.Rendering;
using BeUtl.Threading;

namespace BeUtl;

internal sealed class SceneRenderer : DeferredRenderer
{
    private readonly Scene _scene;
    private readonly List<Layer> _begin = new();
    private readonly List<Layer> _end = new();
    private readonly List<Layer> _layers = new();
    private TimeSpan _recentTime = TimeSpan.MinValue;

    public SceneRenderer(Scene scene, int width, int height)
        : base(width, height)
    {
        _scene = scene;
    }

    public TimeSpan FrameNumber => _scene.CurrentFrame;

    public override IRenderer.RenderResult Render()
    {
        //if (_recentTime == TimeSpan.MinValue)
        //{
        //    _recentTime = FrameNumber;
        //}

        Dispatcher.VerifyAccess();
        IRenderer.RenderResult result;
        if (!IsRendering)
        {
            TimeSpan timeSpan = _scene.CurrentFrame;
            DevideLayers(timeSpan);
            Span<Layer> layers = CollectionsMarshal.AsSpan(_layers);
            Span<Layer> begin = CollectionsMarshal.AsSpan(_begin);
            Span<Layer> end = CollectionsMarshal.AsSpan(_end);

            foreach (Layer item in begin)
            {
                foreach (LayerOperation item2 in item.Operations)
                {
                    item2.BeginningRender(item.Scope);
                }
            }

            foreach (Layer layer in layers)
            {
                var args = new OperationRenderArgs(timeSpan, this, layer.Scope);
                foreach (LayerOperation item in layer.Operations)
                {
                    item.ApplySetters(args);
                }
            }

            foreach (Layer item in end)
            {
                foreach (LayerOperation item2 in item.Operations)
                {
                    item2.EndingRender(item.Scope);
                }

                Span<IRenderable> span = item.Scope.AsSpan();
                foreach (IRenderable item2 in span)
                {
                    if (item2 is Drawable d)
                        AddDirtyRect(d.Bounds);
                }
            }

            result = base.Render();
            _recentTime = timeSpan;
        }
        else
        {
            result = new IRenderer.RenderResult(Graphics.GetBitmap());
        }

        return result;
    }

    // Layersを振り分ける
    private void DevideLayers(TimeSpan timeSpan)
    {
        _begin.Clear();
        _end.Clear();
        _layers.Clear();
        foreach (Layer? item in _scene.Layers)
        {
            bool recent = InRange(item, _recentTime);
            bool current = InRange(item, timeSpan);

            if (current)
            {
                _layers.Add(item);
            }

            if (!recent && current)
            {
                // _recentTimeの範囲外でcurrntTimeの範囲内
                _begin.Add(item);
            }
            else if (recent && !current)
            {
                // _recentTimeの範囲内でcurrntTimeの範囲外
                _end.Add(item);
            }
        }
    }

    // itemがtsの範囲内かを確かめます
    private static bool InRange(Layer item, TimeSpan ts)
    {
        return item.Start <= ts && ts < item.Length + item.Start;
    }

    //private static int ToFrameNumber(TimeSpan tp, int rate)
    //{
    //    return (int)(tp.TotalSeconds * rate);
    //}

    //private static int TicksPerFrame(int rate)
    //{
    //    return 10000000 / rate;
    //}

    //private static TimeSpan ToTimeSpan(int f, int rate)
    //{
    //    return TimeSpan.FromSeconds(f / (double)rate);
    //}
}
