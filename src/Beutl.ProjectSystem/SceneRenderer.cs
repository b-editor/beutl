using System.Runtime.InteropServices;

using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Media.TextFormatting;
using Beutl.ProjectSystem;
using Beutl.Rendering;
using Beutl.Operation;
using Beutl.Styling;
using Beutl.Threading;

namespace Beutl;

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

    protected override void RenderGraphicsCore(TimeSpan timeSpan)
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
            Render_StreamOperators(layer);
        }

        foreach (Layer item in end)
        {
            if (item.Node.Value is { } renderable)
            {
                renderable.IsVisible = false;

                if (renderable is Drawable d)
                    (this as IRenderer).AddDirtyRect(d.Bounds);
            }
        }

        base.RenderGraphicsCore(timeSpan);
        _recentTime = timeSpan;
    }

    private void Render_StreamOperators(Layer layer)
    {
        LayerNode? node = layer.Node;
        Renderable? prevResult = null;
        prevResult?.BeginBatchUpdate();

        Renderable? result = prevResult;
        foreach (SourceOperator? item in layer.Operators.GetMarshal().Value)
        {
            if (item is ISourceTransformer selector)
            {
                result = selector.Transform(prevResult, Clock) as Renderable;
            }
            else if (item is ISourcePublisher source)
            {
                result = source.Publish(Clock) as Renderable;
            }

            if (prevResult != result)
            {
                // Resultが変更された
                prevResult?.EndBatchUpdate();
                result?.BeginBatchUpdate();
                prevResult = result;
            }
        }

        node.Value = result;
        node.Value?.ApplyStyling(Clock);
        node.Value?.ApplyAnimations(Clock);

        if (prevResult != null)
        {
            prevResult.IsVisible = layer.IsEnabled;
        }
        node.Value?.EndBatchUpdate();
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
