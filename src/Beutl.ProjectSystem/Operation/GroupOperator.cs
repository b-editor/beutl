using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Rendering;
using Beutl.Styling;

namespace Beutl.Operation;

public sealed class GroupOperator : StyledSourcePublisher
{
    public Setter<ITransform?> Transform { get; set; } = new(Drawable.TransformProperty, new TransformGroup());

    public Setter<RelativePoint> TransformOrigin { get; set; } = new(Drawable.TransformOriginProperty, RelativePoint.Center);

    public Setter<FilterEffect?> FilterEffect { get; set; } = new(Drawable.FilterEffectProperty, new FilterEffectGroup());

    public Setter<BlendMode> BlendMode { get; set; } = new Setter<BlendMode>(Drawable.BlendModeProperty, Graphics.BlendMode.SrcOver);

    private Renderable? PublishCore(IList<Renderable> value, IClock clock)
    {
        DrawableGroup? renderable = Instance?.Target as DrawableGroup;

        if (!ReferenceEquals(Style, Instance?.Source) || Instance?.Target == null)
        {
            renderable = Activator.CreateInstance(Style.TargetType) as DrawableGroup;
            if (renderable is ICoreObject coreObj)
            {
                Instance?.Dispose();
                Instance = Style.Instance(coreObj);
            }
            else
            {
                renderable = null;
            }
        }

        OnBeforeApplying();
        if (Instance != null && IsEnabled)
        {
            Instance.Begin();
            Instance.Apply(clock);
            Instance.End();

            Drawable[] items = value.OfType<Drawable>().ToArray();
            foreach (Drawable item in items)
            {
                while (item.BatchUpdate)
                {
                    item.EndBatchUpdate();
                }
            }
            renderable!.Children.Replace(items);
        }

        OnAfterApplying();

        return IsEnabled ? renderable : null;
    }

    public override void Evaluate(OperatorEvaluationContext context)
    {
        if (PublishCore(context.FlowRenderables, context.Clock) is Renderable renderable)
        {
            context.FlowRenderables.Clear();
            context.AddFlowRenderable(renderable);
        }
    }

    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<DrawableGroup>();
        style.Setters.AddRange(setters());
        return style;
    }
}
