namespace Beutl.Graphics.Effects;

public sealed class HighContrast : FilterEffect
{
    public static readonly CoreProperty<bool> GrayscaleProperty;
    public static readonly CoreProperty<HighContrastInvertStyle> InvertStyleProperty;
    public static readonly CoreProperty<float> ContrastProperty;
    private bool _grayscale;
    private HighContrastInvertStyle _invertStyle;
    private float _contrast;

    static HighContrast()
    {
        GrayscaleProperty = ConfigureProperty<bool, HighContrast>(nameof(Grayscale))
            .Accessor(o => o.Grayscale, (o, v) => o.Grayscale = v)
            .Register();

        InvertStyleProperty = ConfigureProperty<HighContrastInvertStyle, HighContrast>(nameof(InvertStyle))
            .Accessor(o => o.InvertStyle, (o, v) => o.InvertStyle = v)
            .Register();

        ContrastProperty = ConfigureProperty<float, HighContrast>(nameof(Contrast))
                .Accessor(o => o.Contrast, (o, v) => o.Contrast = v)
            .Register();

        AffectsRender<HighContrast>(GrayscaleProperty, InvertStyleProperty, ContrastProperty);
    }

    public bool Grayscale
    {
        get => _grayscale;
        set => SetAndRaise(GrayscaleProperty, ref _grayscale, value);
    }

    public HighContrastInvertStyle InvertStyle
    {
        get => _invertStyle;
        set => SetAndRaise(InvertStyleProperty, ref _invertStyle, value);
    }

    public float Contrast
    {
        get => _contrast;
        set => SetAndRaise(ContrastProperty, ref _contrast, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.HighContrast(Grayscale, InvertStyle, Contrast);
    }
}
