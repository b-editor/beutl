using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

using SkiaSharp;

namespace BEditorNext.Operations;

public sealed class EllipseOperation : RenderOperation
{
    public static readonly PropertyDefine<Size> SizeProperty;
    public static readonly PropertyDefine<float> StrokeWidthProperty;
    public static readonly PropertyDefine<Color> ColorProperty;

    static EllipseOperation()
    {
        SizeProperty = RegisterProperty<Size, EllipseOperation>(nameof(Size), (owner, obj) => owner.Size = obj, owner => owner.Size)
            .DefaultValue(new Size(100, 100))
            .EnableAnimation()
            .Header("SizeString")
            .JsonName("size")
            .Minimum(new Size(0, 0))
            .EnableEditor();

        StrokeWidthProperty = RegisterProperty<float, EllipseOperation>(nameof(StrokeWidth), (owner, obj) => owner.StrokeWidth = obj, owner => owner.StrokeWidth)
            .DefaultValue(4000)
            .EnableAnimation()
            .Header("StrokeWidthString")
            .JsonName("strokeWidth")
            .Minimum(0)
            .EnableEditor();

        ColorProperty = RegisterProperty<Color, EllipseOperation>(nameof(Color), (owner, obj) => owner.Color = obj, owner => owner.Color)
            .DefaultValue(Colors.White)
            .EnableAnimation()
            .Header("ColorString")
            .JsonName("color")
            .EnableEditor();
    }

    public Size Size { get; set; }

    public float StrokeWidth { get; set; }

    public Color Color { get; set; }

    public override void Render(in OperationRenderArgs args)
    {
        float width = Size.Width;
        float height = Size.Height;
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
