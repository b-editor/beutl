using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media.Immutable;

namespace Beutl.Media;

/// <summary>
/// Fills an area with a solid color.
/// </summary>
public class SolidColorBrush : Brush, ISolidColorBrush, IEquatable<ISolidColorBrush?>
{
    public static readonly CoreProperty<Color> ColorProperty;
    private Color _color;

    static SolidColorBrush()
    {
        ColorProperty = ConfigureProperty<Color, SolidColorBrush>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .Register();

        TransformOriginProperty.OverrideMetadata<SolidColorBrush>(
            new CorePropertyMetadata<RelativePoint>(attributes: new BrowsableAttribute(false)));

        TransformProperty.OverrideMetadata<SolidColorBrush>(
            new CorePropertyMetadata<ITransform>(attributes: new BrowsableAttribute(false)));

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
    public SolidColorBrush(Color color, float opacity = 100)
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
    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
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

    public override bool Equals(object? obj)
    {
        return Equals(obj as ISolidColorBrush);
    }

    public bool Equals(ISolidColorBrush? other)
    {
        return other is not null
            && Color.Equals(other.Color)
            && Opacity == other.Opacity
            && EqualityComparer<ITransform?>.Default.Equals(Transform, other.Transform)
            && TransformOrigin.Equals(other.TransformOrigin);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Color, Opacity, Transform, TransformOrigin);
    }
}
