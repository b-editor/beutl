using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Styling;

namespace Beutl.Operation;

public sealed class DecorateOperator : StylingOperator
{
    private readonly List<IStyleInstance> _instances = [];

    public new Setter<ITransform?> Transform { get; set; } =
        new(Drawable.TransformProperty, new TransformGroup());

    public Setter<RelativePoint> TransformOrigin { get; set; } =
        new(Drawable.TransformOriginProperty, RelativePoint.Center);

    public Setter<FilterEffect?> FilterEffect { get; set; } =
        new(Drawable.FilterEffectProperty, new FilterEffectGroup());

    public Setter<BlendMode> BlendMode { get; set; } =
        new(Drawable.BlendModeProperty, Graphics.BlendMode.SrcOver);

    public override void Evaluate(OperatorEvaluationContext context)
    {
        if (IsEnabled)
        {
            int j = 0;
            for (int i = 0; i < context.FlowRenderables.Count; i++)
            {
                if (context.FlowRenderables[i] is not Drawable drawable) continue;
                IStyleInstance instance = GetInstance(j, drawable);

                if (instance is { Target: DrawableDecorator decorator })
                {
                    while (drawable.BatchUpdate)
                    {
                        drawable.EndBatchUpdate();
                    }

                    ApplyStyle(instance, context.Clock);
                    context.FlowRenderables[i] = decorator;
                    j++;
                }
                else
                {
                    context.FlowRenderables.RemoveAt(i);
                    i--;
                }
            }
        }
    }

    private void ApplyStyle(IStyleInstance instance, IClock clock)
    {
        instance.Begin();
        instance.Apply(clock);
        instance.End();
    }

    private IStyleInstance GetInstance(int index, Drawable value)
    {
        IStyleInstance? instance;
        if (index < _instances.Count)
        {
            instance = _instances[index];
            if (instance.Target is DrawableDecorator dec)
            {
                dec.Child = value;
            }
        }
        else
        {
            instance = Style.Instance(new DrawableDecorator { Child = value });
            _instances.Add(instance);
        }

        return instance;
    }

    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<DrawableDecorator>();
        style.Setters.AddRange(setters());
        return style;
    }

    public override void Exit()
    {
        base.Exit();
        foreach (IStyleInstance instance in _instances)
        {
            if (instance.Target is DrawableDecorator dec)
            {
                dec.Child = null;
            }
        }
    }
}
