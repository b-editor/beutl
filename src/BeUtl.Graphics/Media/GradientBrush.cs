namespace BeUtl.Media;

/// <summary>
/// Base class for brushes that draw with a gradient.
/// </summary>
public abstract class GradientBrush : Brush, IGradientBrush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GradientBrush"/> class.
    /// </summary>
    public GradientBrush()
    {
        GradientStops = new GradientStops();
    }

    /// <inheritdoc/>
    public GradientSpreadMethod SpreadMethod { get; set; }

    /// <inheritdoc/>
    public GradientStops GradientStops { get; set; }

    /// <inheritdoc/>
    IReadOnlyList<IGradientStop> IGradientBrush.GradientStops => GradientStops;
}
