using System.Runtime.InteropServices;

using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl;

internal sealed class SceneRenderer :
    //ImmediateRenderer
    DeferredRenderer
{
    private readonly Scene _scene;
    private readonly List<Layer> _layers = new();

    public SceneRenderer(Scene scene, int width, int height)
        : base(width, height)
    {
        _scene = scene;
    }

    public TimeSpan FrameNumber => _scene.CurrentFrame;

    public TimeSpan CurrentTime { get; private set; }

    protected override void RenderGraphicsCore()
    {
        var timeSpan = Clock.CurrentTime;
        CurrentTime = timeSpan;
        SortLayers(timeSpan);
        Span<Layer> layers = CollectionsMarshal.AsSpan(_layers);

        foreach (Layer layer in layers)
        {
            InvokeSourceOperators(layer);
        }

        base.RenderGraphicsCore();
    }

    private void InvokeSourceOperators(Layer layer)
    {
        RenderLayerSpan? span = layer.Span;
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

        span.Value = result;
        span.Value?.ApplyStyling(Clock);
        span.Value?.ApplyAnimations(Clock);

        if (prevResult != null)
        {
            prevResult.IsVisible = layer.IsEnabled;
        }
        span.Value?.EndBatchUpdate();
    }

    // Layersを振り分ける
    private void SortLayers(TimeSpan timeSpan)
    {
        _layers.Clear();
        foreach (Layer? item in _scene.Children)
        {
            bool current = InRange(item, timeSpan);

            if (current)
            {
                _layers.Add(item);
            }
        }
    }

    // itemがtsの範囲内かを確かめます
    private static bool InRange(Layer item, TimeSpan ts)
    {
        return item.Start <= ts && ts < item.Length + item.Start;
    }
}
