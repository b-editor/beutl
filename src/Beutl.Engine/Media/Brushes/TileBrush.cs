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
    [Display(Name = nameof(Strings.AlignmentX), ResourceType = typeof(Strings))]
    public IProperty<AlignmentX> AlignmentX { get; } = Property.CreateAnimatable(Media.AlignmentX.Center);

    /// <summary>
    /// Gets or sets the horizontal alignment of a tile in the destination.
    /// </summary>
    [Display(Name = nameof(Strings.AlignmentY), ResourceType = typeof(Strings))]
    public IProperty<AlignmentY> AlignmentY { get; } = Property.CreateAnimatable(Media.AlignmentY.Center);

    /// <summary>
    /// Gets or sets the rectangle on the destination in which to paint a tile.
    /// </summary>
    [Display(Name = nameof(Strings.DestinationRect), ResourceType = typeof(Strings))]
    public IProperty<RelativeRect> DestinationRect { get; } = Property.CreateAnimatable(RelativeRect.Fill);

    /// <summary>
    /// Gets or sets the rectangle of the source image that will be displayed.
    /// </summary>
    [Display(Name = nameof(Strings.SourceRect), ResourceType = typeof(Strings))]
    public IProperty<RelativeRect> SourceRect { get; } = Property.CreateAnimatable(RelativeRect.Fill);

    /// <summary>
    /// Gets or sets a value controlling how the source rectangle will be stretched to fill
    /// the destination rect.
    /// </summary>
    [Display(Name = nameof(Strings.Stretch), ResourceType = typeof(Strings))]
    public IProperty<Stretch> Stretch { get; } = Property.CreateAnimatable(Media.Stretch.Uniform);

    /// <summary>
    /// Gets or sets the brush's tile mode.
    /// </summary>
    [Display(Name = nameof(Strings.TileMode), ResourceType = typeof(Strings))]
    public IProperty<TileMode> TileMode { get; } = Property.CreateAnimatable(Media.TileMode.None);

    /// <summary>
    /// Gets or sets the bitmap interpolation mode.
    /// </summary>
    /// <value>
    /// The bitmap interpolation mode.
    /// </value>
    [Display(Name = nameof(Strings.BitmapInterpolationMode), ResourceType = typeof(Strings))]
    public IProperty<BitmapInterpolationMode> BitmapInterpolationMode { get; } = Property.CreateAnimatable(Media.BitmapInterpolationMode.Default);
}
