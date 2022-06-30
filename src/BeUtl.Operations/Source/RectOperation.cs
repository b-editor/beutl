using BeUtl.Graphics;
using BeUtl.Graphics.Shapes;
using BeUtl.Media;

namespace BeUtl.Operations.Source;

public sealed class RectOperation : DrawableOperation
{
    public static readonly CoreProperty<float> WidthProperty;
    public static readonly CoreProperty<float> HeightProperty;
    public static readonly CoreProperty<float> StrokeWidthProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    private readonly Rectangle _drawable = new();

    static RectOperation()
    {
        WidthProperty = ConfigureProperty<float, RectOperation>(nameof(Width))
            .Accessor(o => o.Width, (o, v) => o.Width = v)
            .OverrideMetadata(DefaultMetadatas.Width)
            .Register();

        HeightProperty = ConfigureProperty<float, RectOperation>(nameof(Height))
            .Accessor(o => o.Height, (o, v) => o.Height = v)
            .OverrideMetadata(DefaultMetadatas.Height)
            .Register();

        StrokeWidthProperty = ConfigureProperty<float, RectOperation>(nameof(StrokeWidth))
            .Accessor(o => o.StrokeWidth, (o, v) => o.StrokeWidth = v)
            .OverrideMetadata(DefaultMetadatas.StrokeWidth)
            .Register();

        ColorProperty = ConfigureProperty<Color, RectOperation>(nameof(Color))
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
        get => _drawable.Foreground?.TryGetColorOrDefault(Colors.Transparent) ?? Colors.Transparent;
        set
        {
            if (_drawable.Foreground is SolidColorBrush solidColorBrush)
            {
                solidColorBrush.Color = value;
            }
            else
            {
                _drawable.Foreground = new SolidColorBrush(value);
            }
        }
    }

    public override Drawable Drawable => _drawable;
}
