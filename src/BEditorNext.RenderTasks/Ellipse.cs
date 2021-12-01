using BEditorNext.Graphics;
using BEditorNext.ProjectItems;

using SkiaSharp;

namespace BEditorNext.RenderTasks;

public sealed class Ellipse : RenderTask
{
    public static readonly PropertyDefine<PixelSize> SizeProperty;
    public static readonly PropertyDefine<int> LineWidthProperty;
    public static readonly PropertyDefine<Color> ColorProperty;

    static Ellipse()
    {
        SizeProperty = RegisterProperty<PixelSize, Ellipse>(nameof(Size), (owner, obj) => owner.Size = obj, owner => owner.Size)
            .DefaultValue(new PixelSize(100, 100))
            .EnableAnimation()
            .JsonName("size")
            .Minimum(new PixelSize(0, 0))
            .EnableEditor();

        LineWidthProperty = RegisterProperty<int, Ellipse>(nameof(LineWidth), (owner, obj) => owner.LineWidth = obj, owner => owner.LineWidth)
            .DefaultValue(4000)
            .EnableAnimation()
            .JsonName("lineWidth")
            .Minimum(0)
            .EnableEditor();

        ColorProperty = RegisterProperty<Color, Ellipse>(nameof(Color), (owner, obj) => owner.Color = obj, owner => owner.Color)
            .DefaultValue(Colors.White)
            .EnableAnimation()
            .JsonName("color")
            .EnableEditor();
    }

    public PixelSize Size { get; set; }

    public int LineWidth { get; set; }

    public Color Color { get; set; }

    public override void Execute(in RenderTaskExecuteArgs args)
    {
        var width = Size.Width;
        var height = Size.Height;
        var line = LineWidth;

        if (LineWidth >= Math.Min(width, height) / 2)
            line = Math.Min(width, height) / 2;

        var min = Math.Min(width, height);

        if (line < min) min = line;
        if (min < 0) min = 0;

        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888));
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
            new SKSize((width / 2) - (min / 2), (height / 2) - (min / 2)),
            paint);

        args.List.Add(new RenderableBitmap(bmp.ToBitmap()));
    }
}
