using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media.Source;

namespace Beutl.Media;

/// <summary>
/// Paints an area with an <see cref="ImageSource"/>.
/// </summary>
public partial class ImageBrush : TileBrush
{
    public ImageBrush()
    {
        ScanProperties<ImageBrush>();
    }

    public ImageBrush(ImageSource source) : this()
    {
        Source.CurrentValue = source;
    }

    /// <summary>
    /// Gets or sets the image to draw.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Source), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<ImageSource?> Source { get; } = Property.Create<ImageSource?>();
}
