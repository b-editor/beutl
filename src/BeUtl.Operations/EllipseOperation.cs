using BeUtl.Graphics;
using BeUtl.Media;

namespace BeUtl.Operations;

public static class BrushExtensions
{
    public static Color TryGetColorOrDefault(this IBrush brush, Color defaultValue)
    {
        if (brush is ISolidColorBrush solidBrush)
        {
            return solidBrush.Color;
        }
        else
        {
            return defaultValue;
        }
    }

    public static bool TrySetColor(this IBrush brush, Color value)
    {
        if (brush is SolidColorBrush solidBrush)
        {
            solidBrush.Color = value;
            return true;
        }
        else
        {
            return false;
        }
    }
}

public sealed class EllipseOperation : DrawableOperation
{
    public static readonly CoreProperty<float> WidthProperty;
    public static readonly CoreProperty<float> HeightProperty;
    public static readonly CoreProperty<float> StrokeWidthProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    private readonly Ellipse _drawable = new();

    static EllipseOperation()
    {
        WidthProperty = ConfigureProperty<float, EllipseOperation>(nameof(Width))
            .Accessor(o => o.Width, (o, v) => o.Width = v)
            .OverrideMetadata(DefaultMetadatas.Width)
            .Register();

        HeightProperty = ConfigureProperty<float, EllipseOperation>(nameof(Height))
            .Accessor(o => o.Height, (o, v) => o.Height = v)
            .OverrideMetadata(DefaultMetadatas.Height)
            .Register();

        StrokeWidthProperty = ConfigureProperty<float, EllipseOperation>(nameof(StrokeWidth))
            .Accessor(o => o.StrokeWidth, (o, v) => o.StrokeWidth = v)
            .OverrideMetadata(DefaultMetadatas.StrokeWidth)
            .Register();

        ColorProperty = ConfigureProperty<Color, EllipseOperation>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .OverrideMetadata(DefaultMetadatas.Color)
            .Register();
    }

    public float Width
    {
        get => _drawable.Width;
        set => _drawable.Width = value;
    }

    public float Height
    {
        get => _drawable.Height;
        set => _drawable.Height = value;
    }

    public float StrokeWidth
    {
        get => _drawable.StrokeWidth;
        set => _drawable.StrokeWidth = value;
    }

    public Color Color
    {
        get => _drawable.Foreground.TryGetColorOrDefault(Colors.Transparent);
        set => _drawable.Foreground = value.ToImmutableBrush();
    }

    public override Drawable Drawable => _drawable;
}
