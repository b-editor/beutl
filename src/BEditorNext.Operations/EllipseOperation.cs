using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

using SkiaSharp;

namespace BEditorNext.Operations;

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
    public static readonly PropertyDefine<float> WidthProperty;
    public static readonly PropertyDefine<float> HeightProperty;
    public static readonly PropertyDefine<float> StrokeWidthProperty;
    public static readonly PropertyDefine<Color> ColorProperty;
    private readonly Ellipse _drawable = new();

    static EllipseOperation()
    {
        WidthProperty = RegisterProperty<float, EllipseOperation>(nameof(Width), (owner, obj) => owner.Width = obj, owner => owner.Width)
            .DefaultValue(100)
            .Animatable()
            .Header("WidthString")
            .JsonName("width")
            .Minimum(0)
            .EnableEditor();

        HeightProperty = RegisterProperty<float, EllipseOperation>(nameof(Height), (owner, obj) => owner.Height = obj, owner => owner.Height)
            .DefaultValue(100)
            .Animatable()
            .Header("HeightString")
            .JsonName("height")
            .Minimum(0)
            .EnableEditor();

        StrokeWidthProperty = RegisterProperty<float, EllipseOperation>(nameof(StrokeWidth), (owner, obj) => owner.StrokeWidth = obj, owner => owner.StrokeWidth)
            .DefaultValue(4000)
            .Animatable()
            .Header("StrokeWidthString")
            .JsonName("strokeWidth")
            .Minimum(0)
            .EnableEditor();

        ColorProperty = RegisterProperty<Color, EllipseOperation>(nameof(Color), (owner, obj) => owner.Color = obj, owner => owner.Color)
            .DefaultValue(Colors.White)
            .Animatable()
            .Header("ColorString")
            .JsonName("color")
            .EnableEditor();
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
