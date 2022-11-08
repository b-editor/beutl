using Beutl.Graphics;
using Beutl.Media.Immutable;

namespace Beutl.Media;

/// <summary>
/// Paints an area with an <see cref="IDrawable"/>.
/// </summary>
public class DrawableBrush : TileBrush, IDrawableBrush
{
    public static readonly CoreProperty<IDrawable?> DrawableProperty;
    private IDrawable? _drawable;

    static DrawableBrush()
    {
        DrawableProperty = ConfigureProperty<IDrawable?, DrawableBrush>(nameof(Drawable))
            .Accessor(o => o.Drawable, (o, v) => o.Drawable = v)
            .PropertyFlags(PropertyFlags.All & ~PropertyFlags.Animatable)
            .SerializeName("drawable")
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
    public DrawableBrush(IDrawable drawable)
    {
        Drawable = drawable;
    }

    /// <summary>
    /// Gets or sets the visual to draw.
    /// </summary>
    public IDrawable? Drawable
    {
        get => _drawable;
        set => SetAndRaise(DrawableProperty, ref _drawable, value);
    }

    /// <inheritdoc/>
    public override IBrush ToImmutable()
    {
        return new ImmutableDrawableBrush(this);
    }
}
