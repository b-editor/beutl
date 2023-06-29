using Beutl.Animation;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.Graphics.Filters;

public sealed class BrushImageFilter : ImageFilter
{
    public static readonly CoreProperty<BlendMode> BlendModeProperty;
    public static readonly CoreProperty<IBrush?> BrushProperty;
    private BlendMode _blendMode = BlendMode.SrcOver;
    private IBrush? _brush = null;

    static BrushImageFilter()
    {
        BlendModeProperty = ConfigureProperty<BlendMode, BrushImageFilter>(o => o.BlendMode)
            .DefaultValue(BlendMode.SrcOver)
            .Register();

        BrushProperty = ConfigureProperty<IBrush?, BrushImageFilter>(o => o.Brush)
            .DefaultValue(null)
            .Register();

        AffectsRender<BrushImageFilter>(BlendModeProperty, BrushProperty);
    }

    public BlendMode BlendMode
    {
        get => _blendMode;
        set => SetAndRaise(BlendModeProperty, ref _blendMode, value);
    }

    public IBrush? Brush
    {
        get => _brush;
        set => SetAndRaise(BrushProperty, ref _brush, value);
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Brush as Animatable)?.ApplyAnimations(clock);
    }

    protected internal override SKImageFilter? ToSKImageFilter(Rect bounds)
    {
        if (Brush != null)
        {
            var paint = new SKPaint();
            Canvas.ConfigurePaint(paint, bounds.Size, Brush, BlendMode);
            return SKImageFilter.CreatePaint(paint);
        }
        else
        {
            return null;
        }
    }
}
