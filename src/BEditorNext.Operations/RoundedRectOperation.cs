using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations;

public sealed class RoundedRectOperation : DrawableOperation
{
    public static readonly CoreProperty<float> WidthProperty;
    public static readonly CoreProperty<float> HeightProperty;
    public static readonly CoreProperty<float> StrokeWidthProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<CornerRadius> CornerRadiusProperty;
    private readonly RoundedRect _drawable = new();

    static RoundedRectOperation()
    {
        WidthProperty = ConfigureProperty<float, RoundedRectOperation>(nameof(Width))
            .Accessor(owner => owner.Width, (owner, obj) => owner.Width = obj)
            .DefaultValue(100)
            .Animatable()
            .Header("WidthString")
            .JsonName("width")
            .Minimum(0)
            .EnableEditor()
            .Register();

        HeightProperty = ConfigureProperty<float, RoundedRectOperation>(nameof(Height))
            .Accessor(owner => owner.Height, (owner, obj) => owner.Height = obj)
            .DefaultValue(100)
            .Animatable()
            .Header("HeightString")
            .JsonName("height")
            .Minimum(0)
            .EnableEditor()
            .Register();

        StrokeWidthProperty = ConfigureProperty<float, RoundedRectOperation>(nameof(StrokeWidth))
            .Accessor(owner => owner.StrokeWidth, (owner, obj) => owner.StrokeWidth = obj)
            .DefaultValue(4000)
            .Animatable()
            .Header("StrokeWidthString")
            .JsonName("strokeWidth")
            .Minimum(0)
            .EnableEditor()
            .Register();

        ColorProperty = ConfigureProperty<Color, RoundedRectOperation>(nameof(Color))
            .Accessor(owner => owner.Color, (owner, obj) => owner.Color = obj)
            .DefaultValue(Colors.White)
            .Animatable()
            .Header("ColorString")
            .JsonName("color")
            .EnableEditor()
            .Register();

        CornerRadiusProperty = ConfigureProperty<CornerRadius, RoundedRectOperation>(nameof(CornerRadius))
            .Accessor(owner => owner.CornerRadius, (owner, obj) => owner.CornerRadius = obj)
            .DefaultValue(new CornerRadius(25))
            .Minimum(new CornerRadius(0))
            .Animatable()
            .Header("CornerRadiusString")
            .JsonName("cornerRadius")
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

    public CornerRadius CornerRadius
    {
        get => _drawable.CornerRadius;
        set => _drawable.CornerRadius = value;
    }

    public override Drawable Drawable => _drawable;

    public override void ApplySetters(in OperationRenderArgs args)
    {
        _drawable.Initialize();
        base.ApplySetters(args);
    }
}
