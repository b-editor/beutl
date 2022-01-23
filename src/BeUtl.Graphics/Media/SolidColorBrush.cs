using BeUtl.Media.Immutable;

namespace BeUtl.Media;

/// <summary>
/// Fills an area with a solid color.
/// </summary>
public class SolidColorBrush : Brush, ISolidColorBrush
{
    public static readonly CoreProperty<Color> ColorProperty;
    private Color _color;

    static SolidColorBrush()
    {
        ColorProperty = ConfigureProperty<Color, SolidColorBrush>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .Register();

        AffectsRender<SolidColorBrush>(ColorProperty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SolidColorBrush"/> class.
    /// </summary>
    public SolidColorBrush()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SolidColorBrush"/> class.
    /// </summary>
    /// <param name="color">The color to use.</param>
    /// <param name="opacity">The opacity of the brush.</param>
    public SolidColorBrush(Color color, float opacity = 1)
    {
        Color = color;
        Opacity = opacity;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SolidColorBrush"/> class.
    /// </summary>
    /// <param name="color">The color to use.</param>
    public SolidColorBrush(uint color)
        : this(Color.FromUInt32(color))
    {
    }

    /// <summary>
    /// Gets or sets the color of the brush.
    /// </summary>
    public Color Color
    {
        get => _color;
        set => SetAndRaise(ColorProperty, ref _color, value);
    }

    /// <summary>
    /// Returns a string representation of the brush.
    /// </summary>
    /// <returns>A string representation of the brush.</returns>
    public override string ToString()
    {
        return Color.ToString();
    }

    /// <inheritdoc/>
    public override IBrush ToImmutable()
    {
        return new ImmutableSolidColorBrush(Color, Opacity);
    }
}
