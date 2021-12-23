using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

using SkiaSharp;

namespace BEditorNext.Operations;

public sealed class RectOperation : RenderOperation
{
    public static readonly PropertyDefine<float> WidthProperty;
    public static readonly PropertyDefine<float> HeightProperty;
    public static readonly PropertyDefine<float> StrokeWidthProperty;
    public static readonly PropertyDefine<Color> ColorProperty;

    static RectOperation()
    {
        WidthProperty = RegisterProperty<float, RectOperation>(nameof(Width), (owner, obj) => owner.Width = obj, owner => owner.Width)
            .DefaultValue(100)
            .Animatable()
            .Header("WidthString")
            .JsonName("width")
            .Minimum(0)
            .EnableEditor();
        
        HeightProperty = RegisterProperty<float, RectOperation>(nameof(Height), (owner, obj) => owner.Height = obj, owner => owner.Height)
            .DefaultValue(100)
            .Animatable()
            .Header("HeightString")
            .JsonName("height")
            .Minimum(0)
            .EnableEditor();

        StrokeWidthProperty = RegisterProperty<float, RectOperation>(nameof(StrokeWidth), (owner, obj) => owner.StrokeWidth = obj, owner => owner.StrokeWidth)
            .DefaultValue(4000)
            .Animatable()
            .Header("StrokeWidthString")
            .JsonName("strokeWidth")
            .Minimum(0)
            .EnableEditor();

        ColorProperty = RegisterProperty<Color, RectOperation>(nameof(Color), (owner, obj) => owner.Color = obj, owner => owner.Color)
            .DefaultValue(Colors.White)
            .Animatable()
            .Header("ColorString")
            .JsonName("color")
            .EnableEditor();
    }

    public float Width { get; set; }
    
    public float Height { get; set; }

    public float StrokeWidth { get; set; }

    public Color Color { get; set; }

    public override void Render(in OperationRenderArgs args)
    {
        float width = Width;
        float height = Height;
        float stroke = StrokeWidth;

        using var bmp = new SKBitmap(new SKImageInfo((int)MathF.Round(width), (int)MathF.Round(height), SKColorType.Bgra8888));
        using var canvas = new SKCanvas(bmp);

        using var paint = new SKPaint
        {
            Color = Color.ToSkia(),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke,
        };

        canvas.DrawRect(
            0, 0,
            width, height,
            paint);

        args.List.Add(new RenderableBitmap(bmp.ToBitmap()));
    }
}
