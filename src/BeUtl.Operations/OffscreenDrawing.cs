using BeUtl.Graphics;
using BeUtl.Graphics.Transformation;
using BeUtl.Media;
using BeUtl.Media.Pixel;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations;

public sealed class OffscreenDrawing : LayerOperation
{
    public static readonly CoreProperty<PixelSize> BufferSizeProperty;

    static OffscreenDrawing()
    {
        BufferSizeProperty = ConfigureProperty<PixelSize, OffscreenDrawing>(nameof(BufferSize))
            .Accessor(o => o.BufferSize, (o, v) => o.BufferSize = v)
            .OverrideMetadata(new OperationPropertyMetadata<PixelSize>
            {
                SerializeName = "bufferSize",
                PropertyFlags = PropertyFlags.Designable,
                DefaultValue = new PixelSize(-1, -1)
            })
            .Register();
    }

    public PixelSize BufferSize { get; set; }

    protected override void RenderCore(ref OperationRenderArgs args)
    {
        if (args.Result is not Drawable obj) return;

        static Bitmap<Bgra8888> Draw(PixelSize canvasSize, IDrawable drawable)
        {
            using var canvas = new Canvas(canvasSize.Width, canvasSize.Height);
            drawable.Draw(canvas);

            return canvas.GetBitmap();
        }

        using IBitmap absbmp = Draw(BufferSize.Width <= 0 || BufferSize.Height <= 0 ? args.Renderer.Graphics.Size : BufferSize, obj);
        using Bitmap<Bgra8888> srcBmp = absbmp.Convert<Bgra8888>();
        PixelRect bounds = FindRect(srcBmp);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var srcRect = new Rect(0, 0, srcBmp.Width, srcBmp.Height);
        var result = new DrawableBitmap(srcBmp[bounds])
        {
            BlendMode = obj.BlendMode,
            Foreground = obj.Foreground,
        };

        var transgroup = new TransformGroup
        {
            Children =
            {
                new TranslateTransform(bounds.Position.ToPoint(1)),
            }
        };
        if (obj.Transform is Graphics.Transformation.Transform baseTransform)
        {
            transgroup.Children.Add(baseTransform);
        }
        result.Transform = transgroup;

        obj.Dispose();
        obj = result;
        base.RenderCore(ref args);
    }

    private static unsafe PixelRect FindRect(Bitmap<Bgra8888> bitmap)
    {
        int x0 = bitmap.Width;
        int y0 = bitmap.Height;
        int x1 = 0;
        int y1 = 0;

        // 透明でないピクセルを探す
        Parallel.For(0, bitmap.DataSpan.Length, i =>
        {
            if (bitmap.DataSpan[i].A != 0)
            {
                int x = i % bitmap.Width;
                int y = i / bitmap.Width;

                if (x0 > x) x0 = x;
                if (y0 > y) y0 = y;
                if (x1 < x) x1 = x;
                if (y1 < y) y1 = y;
            }
        });

        return new PixelRect(x0, y0, x1 - x0, y1 - y0);
    }
}
