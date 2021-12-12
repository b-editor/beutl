using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

using SkiaSharp;

namespace BEditorNext.Operations;

public sealed class EllipseOperation : RenderOperation
{
    public static readonly PropertyDefine<float> WidthProperty;
    public static readonly PropertyDefine<float> HeightProperty;
    public static readonly PropertyDefine<float> StrokeWidthProperty;
    public static readonly PropertyDefine<Color> ColorProperty;

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

    public float Width { get; set; }
    
    public float Height { get; set; }

    public float StrokeWidth { get; set; }

    public Color Color { get; set; }

    public override void Render(in OperationRenderArgs args)
    {
        float width = Width;
        float height = Height;
        float stroke = StrokeWidth;

        if (StrokeWidth >= MathF.Min(width, height) / 2)
            stroke = MathF.Min(width, height) / 2;

        float min = MathF.Min(width, height);

        if (stroke < min) min = stroke;
        if (min < 0) min = 0;

        using var bmp = new SKBitmap(new SKImageInfo((int)MathF.Round(width), (int)MathF.Round(height), SKColorType.Bgra8888));
        using var canvas = new SKCanvas(bmp);

        using var paint = new SKPaint
        {
            Color = Color.ToSkia(),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = min,
        };

        canvas.DrawOval(
            new SKPoint(width / 2, height / 2),
            new SKSize(width / 2 - min / 2, height / 2 - min / 2),
            paint);

        args.List.Add(new RenderableBitmap(bmp.ToBitmap()));
    }
}
