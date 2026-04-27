using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BtlBitmap = Beutl.Media.Bitmap;

namespace Beutl.Editor.Components.ColorScopesTab.Views.Scopes;

/// <summary>
/// Base for scopes that render an output image preserving the source aspect ratio,
/// without axis labels (used by False Color and Zebra exposure visualizers).
/// </summary>
public abstract class ImageOverlayScopeBase : HdrScopeControlBase
{
    protected ImageOverlayScopeBase()
    {
        AxisMargin = 0;
    }

    protected sealed override string[]? VerticalAxisLabels => null;

    protected sealed override string[]? HorizontalAxisLabels => null;

    protected abstract WriteableBitmap? RenderImage(BtlBitmap source, WriteableBitmap? existing);

    protected sealed override WriteableBitmap? RenderScope(
        BtlBitmap sourceBitmap,
        int targetWidth,
        int targetHeight,
        WriteableBitmap? existingBitmap)
    {
        // Render at the source's native resolution; the View applies Stretch.Uniform on draw.
        return RenderImage(sourceBitmap, existingBitmap);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;

        var bgBrush = BackgroundBrush;
        if (bgBrush != null)
        {
            context.FillRectangle(bgBrush, new Rect(0, 0, bounds.Width, bounds.Height));
        }

        var bitmap = RenderedBitmap;
        if (bitmap != null && bounds.Width > 0 && bounds.Height > 0)
        {
            var pixelSize = bitmap.PixelSize;
            if (pixelSize.Width > 0 && pixelSize.Height > 0)
            {
                var sourceSize = new Size(pixelSize.Width, pixelSize.Height);
                double scale = Math.Min(bounds.Width / sourceSize.Width, bounds.Height / sourceSize.Height);
                if (scale > 0)
                {
                    var scaledSize = sourceSize * scale;
                    var destRect = new Rect(bounds.Size).CenterRect(new Rect(scaledSize));
                    using (context.PushRenderOptions(new RenderOptions
                    {
                        BitmapInterpolationMode = BitmapInterpolationMode.HighQuality
                    }))
                    {
                        context.DrawImage(bitmap, destRect);
                    }
                }
            }
        }

        RenderHdrOverlayText(context);
    }
}
