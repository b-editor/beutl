using System.ComponentModel.DataAnnotations;
using Beutl.Collections.Pooled;
using Beutl.Engine;
using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Operation;

[Display(Name = nameof(Strings.Portal), ResourceType = typeof(Strings))]
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
        Properties.Add(new CorePropertyAdapter<int>(CountProperty, this));
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
                RaiseEdited(this, EventArgs.Empty);
            }
        }
    }

    public override void Evaluate(OperatorEvaluationContext context)
    {
        if (_element == null) return;

        // elementsはソートされている
        var elements = context.Target == EvaluationTarget.Graphics && context.Renderer is SceneRenderer renderer
            ? renderer.CurrentElements
            : context.Target == EvaluationTarget.Audio && context.Composer is SceneComposer composer
                ? composer.CurrentElements
                : null;

        if (elements == null) return;

        using var flow = new PooledList<EngineObject>();

        int start = _element.ZIndex + 1;
        int end = _element.ZIndex + _count;

        for (int i = 0; i < elements.Count; i++)
        {
            Element item = elements[i];
            if (item.ZIndex >= start && item.ZIndex <= end)
            {
                elements.RemoveAt(i);
                i--;

                using (PooledList<EngineObject> result = item.Evaluate(context.Target, context.Renderer, context.Composer))
                {
                    flow.AddRange(result.Span);
                }
            }
            else if (item.ZIndex > end)
            {
                break;
            }
        }

        foreach (EngineObject item in flow.Span)
        {
            context.AddFlowRenderable(item);
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
