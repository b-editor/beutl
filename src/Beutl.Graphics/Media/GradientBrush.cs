using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// Base class for brushes that draw with a gradient.
/// </summary>
public abstract class GradientBrush : Brush, IGradientBrush
{
    public static readonly CoreProperty<GradientSpreadMethod> SpreadMethodProperty;
    public static readonly CoreProperty<GradientStops> GradientStopsProperty;
    private readonly GradientStops _gradientStops;
    private GradientSpreadMethod _spreadMethod;

    static GradientBrush()
    {
        SpreadMethodProperty = ConfigureProperty<GradientSpreadMethod, GradientBrush>(nameof(SpreadMethod))
            .Accessor(o => o.SpreadMethod, (o, v) => o.SpreadMethod = v)
            .Display(Strings.SpreadMethod)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(GradientSpreadMethod.Pad)
            .SerializeName("spread-method")
            .Register();

        GradientStopsProperty = ConfigureProperty<GradientStops, GradientBrush>(nameof(GradientStops))
            .Accessor(o => o.GradientStops, (o, v) => o.GradientStops = v)
            .Display(Strings.GradientStops)
            .PropertyFlags(PropertyFlags.All)
            .Register();

        AffectsRender<GradientBrush>(SpreadMethodProperty, GradientStopsProperty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GradientBrush"/> class.
    /// </summary>
    public GradientBrush()
    {
        _gradientStops = new GradientStops();
        _gradientStops.Invalidated += (_, e) => RaiseInvalidated(e);
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
        set => _gradientStops.Replace(value);
    }

    /// <inheritdoc/>
    IReadOnlyList<IGradientStop> IGradientBrush.GradientStops => GradientStops;
}
