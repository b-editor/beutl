using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Media.Immutable;

namespace Beutl.Media;

/// <summary>
/// Paints an area with an <see cref="Drawable"/>.
/// </summary>
public class DrawableBrush : TileBrush, IDrawableBrush
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

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        Drawable?.ApplyAnimations(clock);
    }
}
