using Beutl.Collections.Pooled;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl.Operation;

public sealed class TakeAfterOperator : SourceOperator
{
    public static readonly CoreProperty<int> CountProperty;
    private int _count;
    private Element? _element;

    static TakeAfterOperator()
    {
        CountProperty = ConfigureProperty<int, TakeAfterOperator>(nameof(Count))
            .Accessor(o => o.Count, (o, v) => o.Count = v)
            .Register();
    }

    public TakeAfterOperator()
    {
        Properties.Add(new CorePropertyImpl<int>(CountProperty, this));
    }

    // SourceOperationがこの個数を取得して、Rendererに返す。
    // Rendererはこの個数分をスキップする
    public int Count
    {
        get => _count;
        set
        {
            if (SetAndRaise(CountProperty, ref _count, value))
            {
                RaiseInvalidated(new Media.RenderInvalidatedEventArgs(this, nameof(Count)));
            }
        }
    }

    public override void Evaluate(OperatorEvaluationContext context)
    {
        if (_element != null
            && context.Renderer is SceneRenderer renderer)
        {
            // 野暮ったい、
            using var flow = new PooledList<Renderable>();

            int start = _element.ZIndex + 1;
            int end = _element.ZIndex + _count;

            // elementsはソートされている
            List<Element> elements = renderer.GraphicsEvaluator.CurrentElements;
            for (int i = 0; i < elements.Count; i++)
            {
                Element item = elements[i];
                if (item.ZIndex >= start && item.ZIndex <= end)
                {
                    elements.RemoveAt(i);
                    i--;

                    using (PooledList<Renderable> result = item.Evaluate(context.Target, context.Clock.GlobalClock, context.Renderer))
                    {
                        flow.AddRange(result.Span);
                    }
                }
                else if (item.ZIndex > end)
                {
                    break;
                }
            }

            foreach (Renderable item in flow.Span)
            {
                context.AddFlowRenderable(item);
            }
        }
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        _element = this.FindHierarchicalParent<Element>();
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        _element = null;
    }
}
