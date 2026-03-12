using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// Base class for brushes which display repeating images.
/// </summary>
public abstract partial class TileBrush : Brush
{
    protected TileBrush()
    {
        ScanProperties<TileBrush>();
    }

    /// <summary>
    /// Gets or sets the horizontal alignment of a tile in the destination.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.TileBrush_AlignmentX), ResourceType = typeof(GraphicsStrings))]
    public IProperty<AlignmentX> AlignmentX { get; } = Property.CreateAnimatable(Media.AlignmentX.Center);

    /// <summary>
    /// Gets or sets the horizontal alignment of a tile in the destination.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.TileBrush_AlignmentY), ResourceType = typeof(GraphicsStrings))]
    public IProperty<AlignmentY> AlignmentY { get; } = Property.CreateAnimatable(Media.AlignmentY.Center);

    /// <summary>
    /// Gets or sets the rectangle on the destination in which to paint a tile.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.TileBrush_DestinationRect), ResourceType = typeof(GraphicsStrings))]
    public IProperty<RelativeRect> DestinationRect { get; } = Property.CreateAnimatable(RelativeRect.Fill);

    /// <summary>
    /// Gets or sets the rectangle of the source image that will be displayed.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Source), ResourceType = typeof(GraphicsStrings))]
    public IProperty<RelativeRect> SourceRect { get; } = Property.CreateAnimatable(RelativeRect.Fill);

    /// <summary>
    /// Gets or sets a value controlling how the source rectangle will be stretched to fill
    /// the destination rect.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.TileBrush_Stretch), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Stretch> Stretch { get; } = Property.CreateAnimatable(Media.Stretch.Uniform);

    /// <summary>
    /// Gets or sets the brush's tile mode.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.TileBrush_TileMode), ResourceType = typeof(GraphicsStrings))]
    public IProperty<TileMode> TileMode { get; } = Property.CreateAnimatable(Media.TileMode.None);

    /// <summary>
    /// Gets or sets the bitmap interpolation mode.
    /// </summary>
    /// <value>
    /// The bitmap interpolation mode.
    /// </value>
    [Display(Name = nameof(GraphicsStrings.TileBrush_BitmapInterpolationMode), ResourceType = typeof(GraphicsStrings))]
    public IProperty<BitmapInterpolationMode> BitmapInterpolationMode { get; } = Property.CreateAnimatable(Media.BitmapInterpolationMode.Default);
}
