using System.Text.Json.Nodes;

using BeUtl.Styling;

namespace BeUtl.Media;

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
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(GradientSpreadMethod.Pad)
            .SerializeName("spread-method")
            .Register();

        GradientStopsProperty = ConfigureProperty<GradientStops, GradientBrush>(nameof(GradientStops))
            .Accessor(o => o.GradientStops, (o, v) => o.GradientStops = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .Register();

        AffectsRender<GradientBrush>(SpreadMethodProperty, GradientStopsProperty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GradientBrush"/> class.
    /// </summary>
    public GradientBrush()
    {
        _gradientStops = new GradientStops();
        _gradientStops.Attached += item =>
        {
            (item as ILogicalElement)?.NotifyAttachedToLogicalTree(new(this));
            (item as IStylingElement)?.NotifyAttachedToStylingTree(new(this));
        };
        _gradientStops.Detached += item =>
        {
            (item as ILogicalElement)?.NotifyDetachedFromLogicalTree(new(this));
            (item as IStylingElement)?.NotifyDetachedFromStylingTree(new(this));
        };

        _gradientStops.Invalidated += (_, _) => RaiseInvalidated();
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
