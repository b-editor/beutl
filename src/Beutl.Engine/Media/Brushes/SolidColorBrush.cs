using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// Fills an area with a solid color.
/// </summary>
[Display(Name = nameof(Strings.Brush_Solid), ResourceType = typeof(Strings))]
public partial class SolidColorBrush : Brush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SolidColorBrush"/> class.
    /// </summary>
    public SolidColorBrush()
    {
        ScanProperties<SolidColorBrush>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SolidColorBrush"/> class.
    /// </summary>
    /// <param name="color">The color to use.</param>
    /// <param name="opacity">The opacity of the brush.</param>
    public SolidColorBrush(Color color, float opacity = 100) : this()
    {
        Color.CurrentValue = color;
        Opacity.CurrentValue = opacity;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SolidColorBrush"/> class.
    /// </summary>
    /// <param name="color">The color to use.</param>
    public SolidColorBrush(uint color)
        : this(Media.Color.FromUInt32(color))
    {
    }

    /// <summary>
    /// Gets or sets the color of the brush.
    /// </summary>
    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public IProperty<Color> Color { get; } = Property.CreateAnimatable<Color>();
}
