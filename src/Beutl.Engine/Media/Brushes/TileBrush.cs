﻿using System.ComponentModel.DataAnnotations;

using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

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
            .DefaultValue(AlignmentX.Center)
            .Register();

        AlignmentYProperty = ConfigureProperty<AlignmentY, TileBrush>(nameof(AlignmentY))
            .Accessor(o => o.AlignmentY, (o, v) => o.AlignmentY = v)
            .DefaultValue(AlignmentY.Center)
            .Register();

        DestinationRectProperty = ConfigureProperty<RelativeRect, TileBrush>(nameof(DestinationRect))
            .Accessor(o => o.DestinationRect, (o, v) => o.DestinationRect = v)
            .DefaultValue(RelativeRect.Fill)
            .Register();

        SourceRectProperty = ConfigureProperty<RelativeRect, TileBrush>(nameof(SourceRect))
            .Accessor(o => o.SourceRect, (o, v) => o.SourceRect = v)
            .DefaultValue(RelativeRect.Fill)
            .Register();

        StretchProperty = ConfigureProperty<Stretch, TileBrush>(nameof(Stretch))
            .Accessor(o => o.Stretch, (o, v) => o.Stretch = v)
            .DefaultValue(Stretch.Uniform)
            .Register();

        TileModeProperty = ConfigureProperty<TileMode, TileBrush>(nameof(TileMode))
            .Accessor(o => o.TileMode, (o, v) => o.TileMode = v)
            .DefaultValue(TileMode.None)
            .Register();

        BitmapInterpolationModeProperty = ConfigureProperty<BitmapInterpolationMode, TileBrush>(nameof(BitmapInterpolationMode))
            .Accessor(o => o.BitmapInterpolationMode, (o, v) => o.BitmapInterpolationMode = v)
            .DefaultValue(BitmapInterpolationMode.Default)
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
    [Display(Name = nameof(Strings.AlignmentX), ResourceType = typeof(Strings))]
    public AlignmentX AlignmentX
    {
        get => _alignmentX;
        set => SetAndRaise(AlignmentXProperty, ref _alignmentX, value);
    }

    /// <summary>
    /// Gets or sets the horizontal alignment of a tile in the destination.
    /// </summary>
    [Display(Name = nameof(Strings.AlignmentY), ResourceType = typeof(Strings))]
    public AlignmentY AlignmentY
    {
        get => _alignmentY;
        set => SetAndRaise(AlignmentYProperty, ref _alignmentY, value);
    }

    /// <summary>
    /// Gets or sets the rectangle on the destination in which to paint a tile.
    /// </summary>
    [Display(Name = nameof(Strings.DestinationRect), ResourceType = typeof(Strings))]
    public RelativeRect DestinationRect
    {
        get => _destinationRect;
        set => SetAndRaise(DestinationRectProperty, ref _destinationRect, value);
    }

    /// <summary>
    /// Gets or sets the rectangle of the source image that will be displayed.
    /// </summary>
    [Display(Name = nameof(Strings.SourceRect), ResourceType = typeof(Strings))]
    public RelativeRect SourceRect
    {
        get => _sourceRect;
        set => SetAndRaise(SourceRectProperty, ref _sourceRect, value);
    }

    /// <summary>
    /// Gets or sets a value controlling how the source rectangle will be stretched to fill
    /// the destination rect.
    /// </summary>
    [Display(Name = nameof(Strings.Stretch), ResourceType = typeof(Strings))]
    public Stretch Stretch
    {
        get => _stretch;
        set => SetAndRaise(StretchProperty, ref _stretch, value);
    }

    /// <summary>
    /// Gets or sets the brush's tile mode.
    /// </summary>
    [Display(Name = nameof(Strings.TileMode), ResourceType = typeof(Strings))]
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
    [Display(Name = nameof(Strings.BitmapInterpolationMode), ResourceType = typeof(Strings))]
    public BitmapInterpolationMode BitmapInterpolationMode
    {
        get => _bitmapInterpolationMode;
        set => SetAndRaise(BitmapInterpolationModeProperty, ref _bitmapInterpolationMode, value);
    }
}
