using System.Runtime.CompilerServices;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Operation;

public sealed class DecorateOperator() : PublishOperator<DrawableDecorator>([
    (Drawable.TransformProperty, () => new TransformGroup()),
    (Drawable.TransformOriginProperty, RelativePoint.Center),
    (Drawable.FilterEffectProperty, () => new FilterEffectGroup()),
    (Drawable.BlendModeProperty, BlendMode.SrcOver)
])
{
    private readonly ConditionalWeakTable<Drawable, DrawableDecorator> _bag = [];
    private Element? _element;

    public override void Evaluate(OperatorEvaluationContext context)
    {
        if (!IsEnabled) return;

        Value.ApplyAnimations(context.Clock);
        for (int i = 0; i < context.FlowRenderables.Count; i++)
        {
            if (context.FlowRenderables[i] is not Drawable drawable) continue;
            var decorator = _bag.GetValue(drawable, d => new DrawableDecorator { Child = d });
            decorator.Child = drawable;
            context.FlowRenderables[i] = decorator;

            decorator.Transform = (Value.Transform as IMutableTransform)?.ToImmutable() ?? Value.Transform;
            decorator.TransformOrigin = Value.TransformOrigin;
            decorator.BlendMode = Value.BlendMode;
            if (Value.FilterEffect is null)
            {
                decorator.FilterEffect = null;
            }
            else
            {
                decorator.FilterEffect ??= Value.FilterEffect.CreateDelegatedInstance();
            }


            if (_element == null) continue;
            decorator.ZIndex = _element.ZIndex;
            decorator.TimeRange = new TimeRange(_element.Start, _element.Length);
            decorator.ApplyAnimations(_element.Clock);
            decorator.IsVisible = _element.IsEnabled;
        }
    }

    public override void Exit()
    {
        base.Exit();
        foreach (var entry in _bag)
        {
            entry.Value.Child = null;
        }
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
