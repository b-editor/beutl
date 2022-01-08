using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations;

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
            .DefaultValue(100)
            .Animatable()
            .Header("WidthString")
            .JsonName("width")
            .Minimum(0)
            .EnableEditor()
            .Register();

        HeightProperty = ConfigureProperty<float, RectOperation>(nameof(Height))
            .Accessor(o => o.Height, (o, v) => o.Height = v)
            .DefaultValue(100)
            .Animatable()
            .Header("HeightString")
            .JsonName("height")
            .Minimum(0)
            .EnableEditor()
            .Register();

        StrokeWidthProperty = ConfigureProperty<float, RectOperation>(nameof(StrokeWidth))
            .Accessor(o => o.StrokeWidth, (o, v) => o.StrokeWidth = v)
            .DefaultValue(4000)
            .Animatable()
            .Header("StrokeWidthString")
            .JsonName("strokeWidth")
            .Minimum(0)
            .EnableEditor()
            .Register();

        ColorProperty = ConfigureProperty<Color, RectOperation>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .DefaultValue(Colors.White)
            .Animatable()
            .Header("ColorString")
            .JsonName("color")
            .EnableEditor()
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
        set => _drawable.Foreground.TrySetColor(value);
    }

    public override Drawable Drawable => _drawable;

    public override void ApplySetters(in OperationRenderArgs args)
    {
        _drawable.Initialize();
        base.ApplySetters(args);
    }
}
