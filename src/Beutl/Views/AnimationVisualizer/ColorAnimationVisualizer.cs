using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Animation.Invalidated += OnAnimationInvalidated;
        _tempBitmap = new WriteableBitmap(new PixelSize(BitmapWidth, BitmapHeight), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        RenderBitmap();
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        Animation.Invalidated -= OnAnimationInvalidated;
        _tempBitmap?.Dispose();
        _tempBitmap = null;
    }

    private void OnAnimationInvalidated(object? sender, EventArgs e)
    {
        RenderBitmap();
        InvalidateVisual();
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

public sealed class ColorAnimationSpanVisualizer : AnimationSpanVisualizer<Media.Color>
{
    private const int BitmapWidth = 500;
    private const int BitmapHeight = 25;
    private WriteableBitmap? _tempBitmap;

    static ColorAnimationSpanVisualizer()
    {
        AffectsRender<ColorAnimationSpanVisualizer>(IsPointerOverProperty);
    }

    public ColorAnimationSpanVisualizer(Animation<Media.Color> animation, AnimationSpan<Media.Color> animationSpan)
        : base(animation, animationSpan)
    {
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        Animation.Invalidated += OnAnimationInvalidated;
        AnimationSpan.Invalidated += OnAnimationInvalidated;
        _tempBitmap = new WriteableBitmap(new PixelSize(BitmapWidth, BitmapHeight), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        RenderBitmap();
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        Animation.Invalidated -= OnAnimationInvalidated;
        AnimationSpan.Invalidated -= OnAnimationInvalidated;
        _tempBitmap?.Dispose();
        _tempBitmap = null;
    }

    private void OnAnimationInvalidated(object? sender, EventArgs e)
    {
        RenderBitmap();
        InvalidateVisual();
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

                    Parallel.For(0, BitmapWidth, x =>
                    {
                        float p = x / (float)BitmapWidth;
                        Media.Color color = AnimationSpan.Interpolate(p);
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
            DrawingContext.PushedState state = default;
            if (IsPointerOver)
            {
                state = context.PushOpacity(0.8);
            }

            context.DrawImage(
                _tempBitmap,
                new Rect(_tempBitmap.Size),
                new Rect(Bounds.Size),
                BitmapInterpolationMode.HighQuality);

            state.Dispose();
        }
    }
}
