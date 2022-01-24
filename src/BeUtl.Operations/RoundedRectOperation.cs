using BeUtl.Graphics;
using BeUtl.Media;

namespace BeUtl.Operations;

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
            .OverrideMetadata(DefaultMetadatas.Width)
            .Register();

        HeightProperty = ConfigureProperty<float, RoundedRectOperation>(nameof(Height))
            .Accessor(owner => owner.Height, (owner, obj) => owner.Height = obj)
            .OverrideMetadata(DefaultMetadatas.Height)
            .Register();

        StrokeWidthProperty = ConfigureProperty<float, RoundedRectOperation>(nameof(StrokeWidth))
            .Accessor(owner => owner.StrokeWidth, (owner, obj) => owner.StrokeWidth = obj)
            .OverrideMetadata(DefaultMetadatas.StrokeWidth)
            .Register();

        ColorProperty = ConfigureProperty<Color, RoundedRectOperation>(nameof(Color))
            .Accessor(owner => owner.Color, (owner, obj) => owner.Color = obj)
            .OverrideMetadata(DefaultMetadatas.Color)
            .Register();

        CornerRadiusProperty = ConfigureProperty<CornerRadius, RoundedRectOperation>(nameof(CornerRadius))
            .Accessor(owner => owner.CornerRadius, (owner, obj) => owner.CornerRadius = obj)
            .OverrideMetadata(DefaultMetadatas.CornerRadius)
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
}
