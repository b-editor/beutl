using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// Paints an area with an <see cref="Drawable"/>.
/// </summary>
[Display(Name = nameof(GraphicsStrings.Drawable), ResourceType = typeof(GraphicsStrings))]
public partial class DrawableBrush : TileBrush
{
    public DrawableBrush()
    {
        ScanProperties<DrawableBrush>();
    }

    public DrawableBrush(Drawable drawable) : this()
    {
        Drawable.Value = drawable;
    }

    /// <summary>
    /// Gets or sets the visual to draw.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Drawable), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Drawable?> Drawable { get; } = Property.CreateExpression<Drawable?>();
}
