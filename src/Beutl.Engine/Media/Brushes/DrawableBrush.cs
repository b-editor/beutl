using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// Paints an area with an <see cref="Drawable"/>.
/// </summary>
[Display(Name = nameof(Strings.Drawable), ResourceType = typeof(Strings))]
public partial class DrawableBrush : TileBrush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DrawableBrush"/> class.
    /// </summary>
    public DrawableBrush()
    {
        ScanProperties<DrawableBrush>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawableBrush"/> class.
    /// </summary>
    /// <param name="drawable">The drawable to draw.</param>
    public DrawableBrush(Drawable drawable) : this()
    {
        Drawable.CurrentValue = drawable;
    }

    /// <summary>
    /// Gets or sets the visual to draw.
    /// </summary>
    [Display(Name = nameof(Strings.Drawable), ResourceType = typeof(Strings))]
    public IProperty<Drawable?> Drawable { get; } = Property.Create<Drawable?>();
}
