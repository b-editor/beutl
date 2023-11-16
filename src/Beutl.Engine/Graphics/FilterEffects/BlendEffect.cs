using Beutl.Animation;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

public sealed class BlendEffect : FilterEffect
{
    public static readonly CoreProperty<IBrush?> BrushProperty;
    public static readonly CoreProperty<BlendMode> BlendModeProperty;
    private IBrush? _brush;
    private BlendMode _blendMode = BlendMode.SrcIn;

    static BlendEffect()
    {
        BrushProperty = ConfigureProperty<IBrush?, BlendEffect>(nameof(Brush))
            .Accessor(o => o.Brush, (o, v) => o.Brush = v)
            .Register();

        BlendModeProperty = ConfigureProperty<BlendMode, BlendEffect>(nameof(BlendMode))
            .Accessor(o => o.BlendMode, (o, v) => o.BlendMode = v)
            .DefaultValue(BlendMode.SrcIn)
            .Register();

        AffectsRender<BlendEffect>(BrushProperty, BlendModeProperty);
    }

    public BlendEffect()
    {
        Brush = new SolidColorBrush(Colors.White);
    }

    public IBrush? Brush
    {
        get => _brush;
        set => SetAndRaise(BrushProperty, ref _brush, value);
    }

    public BlendMode BlendMode
    {
        get => _blendMode;
        set => SetAndRaise(BlendModeProperty, ref _blendMode, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.BlendMode(Brush, BlendMode);
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Brush as IAnimatable)?.ApplyAnimations(clock);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.Contains("Color"))
        {
            Color color = context.GetValue<Color>("Color");
            Brush = new SolidColorBrush(color);
        }
    }
}
