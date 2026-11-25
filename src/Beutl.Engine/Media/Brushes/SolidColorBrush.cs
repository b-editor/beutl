using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// Fills an area with a solid color.
/// </summary>
[Display(Name = nameof(GraphicsStrings.SolidColorBrush), ResourceType = typeof(GraphicsStrings))]
public partial class SolidColorBrush : Brush
{
    public SolidColorBrush()
    {
        ScanProperties<SolidColorBrush>();
    }

    public SolidColorBrush(Color color, float opacity = 100) : this()
    {
        Color.Value = color;
        Opacity.Value = opacity;
    }

    public SolidColorBrush(uint color)
        : this(Media.Color.FromUInt32(color))
    {
    }

    /// <summary>
    /// Gets or sets the color of the brush.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> Color { get; } = Property.CreateAnimatable(Colors.White);

    public partial class Resource
    {
        public override bool TrySampleColor(Rect bounds, Point point, out Color color)
            => BrushConstructor.TrySamplePaintColor(this, bounds, point, out color);
    }
}
