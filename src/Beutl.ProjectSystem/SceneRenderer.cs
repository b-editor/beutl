using System.Runtime.InteropServices;

using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl;

internal sealed class SceneRenderer :
    ImmediateRenderer
//DeferredRenderer
{
    private readonly Scene _scene;
    private readonly List<Layer> _begin = new();
    private readonly List<Layer> _end = new();
    private readonly List<Layer> _layers = new();
    private readonly List<Renderable> _unhandleds = new();
    private readonly List<Renderable> _sharedList = new();
    private TimeSpan _recentTime = TimeSpan.MinValue;

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
        Span<Layer> begin = CollectionsMarshal.AsSpan(_begin);
        Span<Layer> end = CollectionsMarshal.AsSpan(_end);
        _unhandleds.Clear();

        foreach (Layer item in end)
        {
            ExitSourceOperators(item);
        }

        foreach (Layer item in begin)
        {
            EnterSourceOperators(item);
        }

        foreach (Layer layer in layers)
        {
            EvaluateLayer(layer);
        }

        base.RenderGraphicsCore();
        _recentTime = timeSpan;
    }

    private static void EnterSourceOperators(Layer layer)
    {
        foreach (SourceOperator item in layer.Operators.GetMarshal().Value)
        {
            item.Enter();
        }
    }

    private static void ExitSourceOperators(Layer layer)
    {
        foreach (SourceOperator item in layer.Operators.GetMarshal().Value)
        {
            item.Exit();
        }
    }

    private void EvaluateLayer(Layer layer)
    {
        void Detach(IList<Renderable> renderables)
        {
            foreach (Renderable item in renderables)
            {
                if ((item as ILogicalElement).LogicalParent is RenderLayerSpan span
                    && layer.Span != span)
                {
                    span.Value.Remove(item);
                }
            }
        }

        void DefaultHandler(IList<Renderable> renderables)
        {
            RenderLayerSpan span = layer.Span;
            Detach(renderables);

            span.Value.Replace(renderables);

            foreach (Renderable item in span.Value.GetMarshal().Value)
            {
                item.ApplyStyling(Clock);
                item.ApplyAnimations(Clock);
                item.IsVisible = layer.IsEnabled;
                while (!item.EndBatchUpdate())
                {
                }
            }

            renderables.Clear();
        }

        void EvaluateSourceOperators(List<Renderable> unhandleds)
        {
            foreach (SourceOperator? item in layer.Operators.GetMarshal().Value)
            {
                if (item is ISourceTransformer selector)
                {
                    selector.Transform(unhandleds, Clock);
                }
                else if (item is ISourcePublisher source)
                {
                    if (source.Publish(Clock) is Renderable renderable)
                    {
                        unhandleds.Add(renderable);
                        // Todo: Publish内でBeginBatchUpdateするようにする
                        renderable.BeginBatchUpdate();
                    }
                }
                else if (item is ISourceFilter { IsEnabled: true } filter)
                {
                    if (filter.Scope == SourceFilterScope.Local)
                    {
                        unhandleds = filter.Filter(unhandleds, Clock);
                    }
                    else
                    {
                        unhandleds = filter.Filter(unhandleds, Clock);
                        _unhandleds.Clear();
                        _unhandleds.AddRange(unhandleds);
                    }
                }

                if (item is ISourceHandler handler)
                {
                    handler.Handle(unhandleds, Clock);
                    // 差分を取ってEndBatchUpdateする
                }
            }
        }

        if (layer.AllowOutflow)
        {
            EvaluateSourceOperators(_unhandleds);
        }
        else
        {
            EvaluateSourceOperators(_sharedList);

            DefaultHandler(_sharedList);
        }

        //span.Value = result;
        //span.Value?.ApplyStyling(Clock);
        //span.Value?.ApplyAnimations(Clock);

        //if (prevResult != null)
        //{
        //    prevResult.IsVisible = layer.IsEnabled;
        //}
        //span.Value?.EndBatchUpdate();
    }

    // Layersを振り分ける
    private void SortLayers(TimeSpan timeSpan)
    {
        _begin.Clear();
        _end.Clear();
        _layers.Clear();
        // Todo: 'public Layers Children'はソート済みにしたい
        foreach (Layer? item in _scene.Children)
        {
            bool recent = InRange(item, _recentTime);
            bool current = InRange(item, timeSpan);

            if (current)
            {
                _layers.OrderedAdd(item, x => x.ZIndex);
            }

            if (!recent && current)
            {
                // _recentTimeの範囲外でcurrntTimeの範囲内
                _begin.OrderedAdd(item, x => x.ZIndex);
            }
            else if (recent && !current)
            {
                // _recentTimeの範囲内でcurrntTimeの範囲外
                _end.OrderedAdd(item, x => x.ZIndex);
            }
        }
    }


    // itemがtsの範囲内かを確かめます
    private static bool InRange(Layer item, TimeSpan ts)
    {
        return item.Start <= ts && ts < item.Length + item.Start;
    }
}
