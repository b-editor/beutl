using BeUtl.Media;
using BeUtl.Media.Pixel;

namespace BeUtl.Graphics.Shapes;

public sealed class RoundedRect : Drawable
{
    public static readonly CoreProperty<float> StrokeWidthProperty;
    public static readonly CoreProperty<CornerRadius> CornerRadiusProperty;
    private float _strokeWidth = 4000;
    private CornerRadius _cornerRadius;

    static RoundedRect()
    {
        StrokeWidthProperty = ConfigureProperty<float, RoundedRect>(nameof(StrokeWidth))
            .Accessor(o => o.StrokeWidth, (o, v) => o.StrokeWidth = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(4000)
            .Minimum(0)
            .SerializeName("stroke-width")
            .Register();

        CornerRadiusProperty = ConfigureProperty<CornerRadius, RoundedRect>(nameof(CornerRadius))
            .Accessor(o => o.CornerRadius, (o, v) => o.CornerRadius = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(new CornerRadius())
            .Minimum(new CornerRadius())
            .SerializeName("corner-radius")
            .Register();

        AffectsRender<RoundedRect>(StrokeWidthProperty, CornerRadiusProperty);
    }

    public float StrokeWidth
    {
        get => _strokeWidth;
        set => SetAndRaise(StrokeWidthProperty, ref _strokeWidth, value);
    }

    public CornerRadius CornerRadius
    {
        get => _cornerRadius;
        set => SetAndRaise(CornerRadiusProperty, ref _cornerRadius, value);
    }

    protected override Size MeasureCore(Size availableSize)
    {
        return new Size(Math.Max(Width, 0), Math.Max(Height, 0));
    }

    protected override void OnDraw(ICanvas canvas)
    {
        if (Width > 0 && Height > 0)
        {
            using Bitmap<Bgra8888> bmp = ToBitmapWithoutEffect();
            canvas.DrawBitmap(bmp);
        }
    }

    public Bitmap<Bgra8888> ToBitmapWithoutEffect()
    {
        using var g = new Canvas((int)Width, (int)Height);

        if (Foreground != null)
        {
            g.Foreground = Foreground;
        }
        g.StrokeWidth = StrokeWidth;
        g.DrawRect(new Size(Width, Height));

        Bitmap<Bgra8888> bmp = g.GetBitmap();
        ReplaceCorners(bmp);

        return bmp;
    }

    private void ReplaceCorners(Bitmap<Bgra8888> bitmap)
    {
        CornerRadius cornerRadius = Clamp(CornerRadius, new CornerRadius(0), new CornerRadius(Math.Min(bitmap.Width, bitmap.Height) / 2));

        // topleft
        int topleftI = (int)cornerRadius.TopLeft;
        using (Bitmap<Bgra8888> topleft = Corner(cornerRadius.TopLeft))
        using (Bitmap<Bgra8888> topleft1 = topleft[new PixelRect(0, 0, topleftI, topleftI)])
        {
            bitmap[new PixelRect(0, 0, topleftI, topleftI)] = topleft1;
        }

        // topright
        int toprightI = (int)cornerRadius.TopRight;
        using (Bitmap<Bgra8888> topright = Corner(cornerRadius.TopRight))
        using (Bitmap<Bgra8888> topright1 = topright[new PixelRect(toprightI, 0, toprightI, toprightI)])
        {
            bitmap[new PixelRect(bitmap.Width - toprightI, 0, toprightI, toprightI)] = topright1;
        }

        // bottomright
        int bottomrightI = (int)cornerRadius.BottomRight;
        using (Bitmap<Bgra8888> bottomright = Corner(cornerRadius.BottomRight))
        using (Bitmap<Bgra8888> bottomright1 = bottomright[new PixelRect(bottomrightI, bottomrightI, bottomrightI, bottomrightI)])
        {
            bitmap[new PixelRect(bitmap.Width - bottomrightI, bitmap.Height - bottomrightI, bottomrightI, bottomrightI)] = bottomright1;
        }

        // bottomleft
        int bottomleftI = (int)cornerRadius.BottomLeft;
        using (Bitmap<Bgra8888> bottomleft = Corner(cornerRadius.BottomLeft))
        using (Bitmap<Bgra8888> bottomleft1 = bottomleft[new PixelRect(0, bottomleftI, bottomleftI, bottomleftI)])
        {
            bitmap[new PixelRect(0, bitmap.Height - bottomleftI, bottomleftI, bottomleftI)] = bottomleft1;
        }
    }

    private Bitmap<Bgra8888> Corner(float radius)
    {
        if ((int)radius <= 0)
        {
            return new Bitmap<Bgra8888>(0, 0);
        }
        else
        {
            float size = radius * 2;
            using var c = new Canvas((int)size, (int)size);

            if (Foreground != null)
            {
                c.Foreground = Foreground;
            }
            c.StrokeWidth = StrokeWidth;
            c.DrawCircle(new Size(size, size));

            return c.GetBitmap();
        }
    }

    private static CornerRadius Clamp(CornerRadius value, CornerRadius min, CornerRadius max)
    {
        return new CornerRadius(
            Math.Clamp(value.TopLeft, min.TopLeft, max.TopLeft),
            Math.Clamp(value.TopRight, min.TopRight, max.TopRight),
            Math.Clamp(value.BottomRight, min.BottomRight, max.BottomRight),
            Math.Clamp(value.BottomLeft, min.BottomLeft, max.BottomLeft));
    }
}
