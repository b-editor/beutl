using BeUtl.Graphics;

namespace BeUtl.Media;

/// <summary>
/// Base class for brushes which display repeating images.
/// </summary>
public abstract class TileBrush : Brush, ITileBrush
{
    public static readonly CoreProperty<AlignmentX> AlignmentXProperty;
    public static readonly CoreProperty<AlignmentY> AlignmentYProperty;
    public static readonly CoreProperty<RelativeRect> DestinationRectProperty;
    public static readonly CoreProperty<RelativeRect> SourceRectProperty;
    public static readonly CoreProperty<Stretch> StretchProperty;
    public static readonly CoreProperty<TileMode> TileModeProperty;
    public static readonly CoreProperty<BitmapInterpolationMode> BitmapInterpolationModeProperty;
    private AlignmentX _alignmentX = AlignmentX.Center;
    private AlignmentY _alignmentY = AlignmentY.Center;
    private RelativeRect _destinationRect = RelativeRect.Fill;
    private RelativeRect _sourceRect = RelativeRect.Fill;
    private Stretch _stretch = Stretch.Uniform;
    private TileMode _tileMode;
    private BitmapInterpolationMode _bitmapInterpolationMode = BitmapInterpolationMode.Default;

    static TileBrush()
    {
        AlignmentXProperty = ConfigureProperty<AlignmentX, TileBrush>(nameof(AlignmentX))
            .Accessor(o => o.AlignmentX, (o, v) => o.AlignmentX = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(AlignmentX.Center)
            .SerializeName("alignment-x")
            .Register();

        AlignmentYProperty = ConfigureProperty<AlignmentY, TileBrush>(nameof(AlignmentY))
            .Accessor(o => o.AlignmentY, (o, v) => o.AlignmentY = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(AlignmentY.Center)
            .SerializeName("alignment-y")
            .Register();

        DestinationRectProperty = ConfigureProperty<RelativeRect, TileBrush>(nameof(DestinationRect))
            .Accessor(o => o.DestinationRect, (o, v) => o.DestinationRect = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(RelativeRect.Fill)
            .SerializeName("destination-rect")
            .Register();

        SourceRectProperty = ConfigureProperty<RelativeRect, TileBrush>(nameof(SourceRect))
            .Accessor(o => o.SourceRect, (o, v) => o.SourceRect = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(RelativeRect.Fill)
            .SerializeName("source-rect")
            .Register();

        StretchProperty = ConfigureProperty<Stretch, TileBrush>(nameof(Stretch))
            .Accessor(o => o.Stretch, (o, v) => o.Stretch = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(Stretch.Uniform)
            .SerializeName("stretch")
            .Register();

        TileModeProperty = ConfigureProperty<TileMode, TileBrush>(nameof(TileMode))
            .Accessor(o => o.TileMode, (o, v) => o.TileMode = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(TileMode.None)
            .SerializeName("tile-mode")
            .Register();

        BitmapInterpolationModeProperty = ConfigureProperty<BitmapInterpolationMode, TileBrush>(nameof(BitmapInterpolationMode))
            .Accessor(o => o.BitmapInterpolationMode, (o, v) => o.BitmapInterpolationMode = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(BitmapInterpolationMode.Default)
            .SerializeName("interpolation-mode")
            .Register();

        AffectsRender<TileBrush>(
            AlignmentXProperty, AlignmentYProperty,
            DestinationRectProperty, SourceRectProperty,
            StretchProperty,
            TileModeProperty,
            BitmapInterpolationModeProperty);
    }

    /// <summary>
    /// Gets or sets the horizontal alignment of a tile in the destination.
    /// </summary>
    public AlignmentX AlignmentX
    {
        get => _alignmentX;
        set => SetAndRaise(AlignmentXProperty, ref _alignmentX, value);
    }

    /// <summary>
    /// Gets or sets the horizontal alignment of a tile in the destination.
    /// </summary>
    public AlignmentY AlignmentY
    {
        get => _alignmentY;
        set => SetAndRaise(AlignmentYProperty, ref _alignmentY, value);
    }

    /// <summary>
    /// Gets or sets the rectangle on the destination in which to paint a tile.
    /// </summary>
    public RelativeRect DestinationRect
    {
        get => _destinationRect;
        set => SetAndRaise(DestinationRectProperty, ref _destinationRect, value);
    }

    /// <summary>
    /// Gets or sets the rectangle of the source image that will be displayed.
    /// </summary>
    public RelativeRect SourceRect
    {
        get => _sourceRect;
        set => SetAndRaise(SourceRectProperty, ref _sourceRect, value);
    }

    /// <summary>
    /// Gets or sets a value controlling how the source rectangle will be stretched to fill
    /// the destination rect.
    /// </summary>
    public Stretch Stretch
    {
        get => _stretch;
        set => SetAndRaise(StretchProperty, ref _stretch, value);
    }

    /// <summary>
    /// Gets or sets the brush's tile mode.
    /// </summary>
    public TileMode TileMode
    {
        get => _tileMode;
        set => SetAndRaise(TileModeProperty, ref _tileMode, value);
    }

    /// <summary>
    /// Gets or sets the bitmap interpolation mode.
    /// </summary>
    /// <value>
    /// The bitmap interpolation mode.
    /// </value>
    public BitmapInterpolationMode BitmapInterpolationMode
    {
        get => _bitmapInterpolationMode;
        set => SetAndRaise(BitmapInterpolationModeProperty, ref _bitmapInterpolationMode, value);
    }
}
