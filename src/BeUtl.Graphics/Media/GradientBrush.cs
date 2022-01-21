namespace BeUtl.Media;

/// <summary>
/// Base class for brushes that draw with a gradient.
/// </summary>
public abstract class GradientBrush : Brush, IGradientBrush
{
    public static readonly CoreProperty<GradientSpreadMethod> SpreadMethodProperty;
    public static readonly CoreProperty<GradientStops> GradientStopsProperty;
    private GradientSpreadMethod _spreadMethod;
    private GradientStops _gradientStops;

    static GradientBrush()
    {
        SpreadMethodProperty = ConfigureProperty<GradientSpreadMethod, GradientBrush>(nameof(SpreadMethod))
            .Accessor(o => o.SpreadMethod, (o, v) => o.SpreadMethod = v)
            .DefaultValue(GradientSpreadMethod.Pad)
            .Register();

        GradientStopsProperty = ConfigureProperty<GradientStops, GradientBrush>(nameof(GradientStops))
            .Accessor(o => o.GradientStops, (o, v) => o.GradientStops = v)
            .Register();

        AffectRender<GradientBrush>(SpreadMethodProperty, GradientStopsProperty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GradientBrush"/> class.
    /// </summary>
    public GradientBrush()
    {
        _gradientStops = new GradientStops();
    }

    /// <inheritdoc/>
    public GradientSpreadMethod SpreadMethod
    {
        get => _spreadMethod;
        set => SetAndRaise(SpreadMethodProperty, ref _spreadMethod, value);
    }

    /// <inheritdoc/>
    public GradientStops GradientStops
    {
        get => _gradientStops;
        set => SetAndRaise(GradientStopsProperty, ref _gradientStops, value);
    }

    /// <inheritdoc/>
    IReadOnlyList<IGradientStop> IGradientBrush.GradientStops => GradientStops;
}
