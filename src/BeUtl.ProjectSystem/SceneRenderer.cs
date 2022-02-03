using System.Runtime.InteropServices;

using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.Media.TextFormatting;
using BeUtl.ProjectSystem;
using BeUtl.Rendering;
using BeUtl.Styling;
using BeUtl.Threading;

namespace BeUtl;

internal sealed class SceneRenderer : ImmediateRenderer/*DeferredRenderer*/, IClock
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
        Clock = this;
    }

    public TimeSpan FrameNumber => _scene.CurrentFrame;

    public TimeSpan CurrentTime { get; private set; }

    protected override void RenderCore()
    {
        TimeSpan timeSpan = _scene.CurrentFrame;
        CurrentTime = timeSpan;
        DevideLayers(timeSpan);
        Span<Layer> layers = CollectionsMarshal.AsSpan(_layers);
        Span<Layer> begin = CollectionsMarshal.AsSpan(_begin);
        Span<Layer> end = CollectionsMarshal.AsSpan(_end);

        foreach (Layer item in begin)
        {
            item.Renderable?.Invalidate();
        }

        foreach (Layer layer in layers)
        {
            if (!layer.IsEnabled)
            {
                layer.Renderable = null;
            }
            else
            {
                var args = new OperationRenderArgs(this)
                {
                    Result = layer.Renderable
                };
                var prevResult = args.Result;
                prevResult?.BeginBatchUpdate();
                foreach (LayerOperation? item in layer.Children.AsSpan())
                {
                    item.Render(ref args);
                    if (prevResult != args.Result)
                    {
                        // Resultが変更された
                        prevResult?.EndBatchUpdate();
                        args.Result?.BeginBatchUpdate();
                        prevResult = args.Result;
                    }
                }

                layer.Renderable = args.Result;
                layer.Renderable?.ApplyStyling(Clock);

                layer.Renderable?.EndBatchUpdate();
            }
        }

        foreach (Layer item in end)
        {
            if (item.Renderable != null)
            {
                item.Renderable.IsVisible = false;

                if (item.Renderable is Drawable d)
                    AddDirtyRect(d.Bounds);
            }
        }

        base.RenderCore();
        _recentTime = timeSpan;
    }

    // Layersを振り分ける
    private void DevideLayers(TimeSpan timeSpan)
    {
        _begin.Clear();
        _end.Clear();
        _layers.Clear();
        foreach (Layer? item in _scene.Children)
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
