using System.Runtime.InteropServices;

using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.Media.TextFormatting;
using BeUtl.ProjectSystem;
using BeUtl.Rendering;
using BeUtl.Styling;
using BeUtl.Threading;

namespace BeUtl;

internal sealed class SceneRenderer : ImmediateRenderer/*DeferredRenderer*/
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

    public TimeSpan CurrentTime { get; private set; }

    protected override void RenderCore(TimeSpan timeSpan)
    {
        CurrentTime = timeSpan;
        SortLayers(timeSpan);
        Span<Layer> layers = CollectionsMarshal.AsSpan(_layers);
        Span<Layer> begin = CollectionsMarshal.AsSpan(_begin);
        Span<Layer> end = CollectionsMarshal.AsSpan(_end);

        foreach (Layer item in begin)
        {
            item.Node.Value?.Invalidate();
        }

        foreach (Layer layer in layers)
        {
            LayerNode? node = layer.Node;
            var args = new OperationRenderArgs(this)
            {
                Result = node.Value
            };
            Renderable? prevResult = args.Result;
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

            node.Value = args.Result;
            node.Value?.ApplyStyling(Clock);

            if (prevResult != null)
            {
                prevResult.IsVisible = layer.IsEnabled;
            }
            node.Value?.EndBatchUpdate();
        }

        foreach (Layer item in end)
        {
            if (item.Node.Value is { } renderable)
            {
                renderable.IsVisible = false;

                if (renderable is Drawable d)
                    AddDirtyRect(d.Bounds);
            }
        }

        base.RenderCore(timeSpan);
        _recentTime = timeSpan;
    }

    // Layersを振り分ける
    private void SortLayers(TimeSpan timeSpan)
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
}
