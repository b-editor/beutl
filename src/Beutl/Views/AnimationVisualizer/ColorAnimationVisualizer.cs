using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;

using Beutl.Animation;

namespace Beutl.Views.AnimationVisualizer;

public sealed class ColorAnimationVisualizer : AnimationVisualizer<Media.Color>
{
    private const int BitmapWidth = 500;
    private const int BitmapHeight = 25;
    private WriteableBitmap? _tempBitmap;

    public ColorAnimationVisualizer(Animation<Media.Color> animation)
        : base(animation)
    {
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Animation.Invalidated += OnAnimationInvalidated;
        _tempBitmap = new WriteableBitmap(new PixelSize(BitmapWidth, BitmapHeight), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        RenderBitmap();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Animation.Invalidated -= OnAnimationInvalidated;
        _tempBitmap?.Dispose();
        _tempBitmap = null;
    }

    private void OnAnimationInvalidated(object? sender, EventArgs e)
    {
        RenderBitmap();
    }

    private void RenderBitmap()
    {
        if (_tempBitmap != null)
        {
            using (ILockedFramebuffer lok = _tempBitmap.Lock())
            {
                unsafe
                {
                    var pixels = (uint*)(void*)lok.Address;
                    var duration = CalculateDuration();

                    Parallel.For(0, BitmapWidth, x =>
                    {
                        float p = x / (float)BitmapWidth;
                        Media.Color color = Animation.Interpolate(duration * p);
                        for (int y = 0; y < BitmapHeight; y++)
                        {
                            int pos = (y * BitmapWidth) + x;
                            pixels[pos] = color.ToUint32();
                        }
                    });
                }
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_tempBitmap != null)
        {
            context.DrawImage(
                _tempBitmap,
                new Rect(_tempBitmap.Size),
                new Rect(Bounds.Size),
                BitmapInterpolationMode.HighQuality);
        }
    }
}
