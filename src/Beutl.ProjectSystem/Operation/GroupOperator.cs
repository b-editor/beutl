using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Operation;

public sealed class GroupOperator : PublishOperator<DrawableGroup>
{
    private Element? _element;

    protected override void FillProperties()
    {
        AddProperty(Value.Transform, new TransformGroup());
        AddProperty(Value.TransformOrigin, RelativePoint.Center);
        AddProperty(Value.FilterEffect, new FilterEffectGroup());
        AddProperty(Value.BlendMode, BlendMode.SrcOver);
    }

    public override void Evaluate(OperatorEvaluationContext context)
    {
        var value = Value;
        if (!IsEnabled) return;

        var items = context.FlowRenderables.OfType<Drawable>().ToArray();
        context.FlowRenderables.Clear();
        value.Children.Replace(items);
        context.AddFlowRenderable(value);

        if (_element == null) return;

        // TODO: 毎フレーム更新するのではなく、変更があったときだけ更新するようにする
        Value.IsTimeAnchor = true;
        Value.ZIndex = _element.ZIndex;
        Value.TimeRange = new TimeRange(_element.Start, _element.Length);
        Value.IsEnabled = _element.IsEnabled;
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
