using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Rendering;
using Beutl.Styling;

namespace Beutl.Operation;

public sealed class DecorateOperator : SourceStyler
{
    public new Setter<ITransform?> Transform { get; set; } = new(Drawable.TransformProperty, new TransformGroup());

    public Setter<RelativePoint> TransformOrigin { get; set; } = new(Drawable.TransformOriginProperty, RelativePoint.Center);

    public Setter<FilterEffect?> FilterEffect { get; set; } = new(Drawable.FilterEffectProperty, new FilterEffectGroup());

    public Setter<BlendMode> BlendMode { get; set; } = new Setter<BlendMode>(Drawable.BlendModeProperty, Graphics.BlendMode.SrcOver);

    public override void Evaluate(OperatorEvaluationContext context)
    {
        TransformCore(context.FlowRenderables, context.Clock);
    }

    private void TransformCore(IList<Renderable> value, IClock clock)
    {
        if (IsEnabled)
        {
            for (int i = 0; i < value.Count; i++)
            {
                Renderable renderable = value[i];
                IStyleInstance? instance = GetInstance(value[i]);

                if (instance is { Target: DrawableDecorator decorator })
                {
                    while (renderable.BatchUpdate)
                    {
                        renderable.EndBatchUpdate();
                    }

                    ApplyStyle(instance, renderable, clock);
                    value[i] = decorator;
                }
                else
                {
                    value.RemoveAt(i);
                    i--;
                }
            }
        }
    }

    protected override IStyleInstance? GetInstance(Renderable value)
    {
        if (Table.TryGetValue(value, out IStyleInstance? styleInstance))
        {
            return styleInstance;
        }
        else
        {
            if (value is Drawable drawable)
            {
                IStyleInstance instance = Style.Instance(new DrawableDecorator
                {
                    Child = drawable
                });
                Table.AddOrUpdate(value, instance);
                return instance;
            }
            else
            {
                return null;
            }
        }
    }

    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<DrawableDecorator>();
        style.Setters.AddRange(setters());
        return style;
    }
}
