using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Media.Immutable;

namespace Beutl.Media;

/// <summary>
/// Paints an area with an <see cref="Drawable"/>.
/// </summary>
public class DrawableBrush : TileBrush, IDrawableBrush, IEquatable<IDrawableBrush?>
{
    public static readonly CoreProperty<Drawable?> DrawableProperty;
    private Drawable? _drawable;

    static DrawableBrush()
    {
        DrawableProperty = ConfigureProperty<Drawable?, DrawableBrush>(nameof(Drawable))
            .Accessor(o => o.Drawable, (o, v) => o.Drawable = v)
            .Register();

        AffectsRender<DrawableBrush>(DrawableProperty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawableBrush"/> class.
    /// </summary>
    public DrawableBrush()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawableBrush"/> class.
    /// </summary>
    /// <param name="drawable">The drawable to draw.</param>
    public DrawableBrush(Drawable drawable)
    {
        Drawable = drawable;
    }

    /// <summary>
    /// Gets or sets the visual to draw.
    /// </summary>
    public Drawable? Drawable
    {
        get => _drawable;
        set => SetAndRaise(DrawableProperty, ref _drawable, value);
    }

    int? IDrawableBrush.Version => Drawable?.Version;

    /// <inheritdoc/>
    public override IBrush ToImmutable()
    {
        return new ImmutableDrawableBrush(this);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as IDrawableBrush);
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        Drawable?.ApplyAnimations(clock);
    }

    public bool Equals(IDrawableBrush? other)
    {
        return other is not null
            && AlignmentX == other.AlignmentX
            && AlignmentY == other.AlignmentY
            && DestinationRect.Equals(other.DestinationRect)
            && Opacity == other.Opacity
            && EqualityComparer<ITransform?>.Default.Equals(Transform, other.Transform)
            && TransformOrigin.Equals(other.TransformOrigin)
            && SourceRect.Equals(other.SourceRect)
            && Stretch == other.Stretch
            && TileMode == other.TileMode
            && BitmapInterpolationMode == other.BitmapInterpolationMode
            && ReferenceEquals(Drawable, other.Drawable)
            && Drawable?.Version == other.Version;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(AlignmentX);
        hash.Add(AlignmentY);
        hash.Add(DestinationRect);
        hash.Add(Opacity);
        hash.Add(Transform);
        hash.Add(TransformOrigin);
        hash.Add(SourceRect);
        hash.Add(Stretch);
        hash.Add(TileMode);
        hash.Add(BitmapInterpolationMode);
        hash.Add(Drawable);
        return hash.ToHashCode();
    }
}
