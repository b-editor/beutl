using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Operation;

public sealed class GroupOperator() : PublishOperator<DrawableGroup>([
    (Drawable.TransformProperty, () => new TransformGroup()),
    (Drawable.TransformOriginProperty, RelativePoint.Center),
    (Drawable.FilterEffectProperty, () => new FilterEffectGroup()),
    (Drawable.BlendModeProperty, BlendMode.SrcOver)
])
{
    private Element? _element;

    public override void Evaluate(OperatorEvaluationContext context)
    {
        var value = Value;
        if (!IsEnabled) return;

        var items = context.FlowRenderables.OfType<Drawable>().ToArray();
        context.FlowRenderables.Clear();
        value.Children.Replace(items);
        context.AddFlowRenderable(value);

        if (_element == null) return;
        Value.ZIndex = _element.ZIndex;
        Value.TimeRange = new TimeRange(_element.Start, _element.Length);
        Value.ApplyAnimations(_element.Clock);
        Value.IsVisible = _element.IsEnabled;
    }

    public override void Enter()
    {
        base.Enter();
        Value.Children.Clear();
    }

    public override void Exit()
    {
        base.Exit();
        Value.Children.Clear();
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        Value.Children.Clear();
        base.Serialize(context);
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(in args);
        _element = this.FindHierarchicalParent<Element>();
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(in args);
        _element = null;
    }
}
