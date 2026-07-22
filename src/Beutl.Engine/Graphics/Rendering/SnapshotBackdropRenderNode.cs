using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public class SnapshotBackdropRenderNode : RenderNode, IBackdrop, IBuiltInBackdropCaptureSink
{
    private BackdropCapture? _fallback;

    public override void Process(RenderNodeContext context)
    {
        context.DisableRenderCache();
        context.Publish(context.BuiltInBackdropCapture(this));
    }

    public void Draw(ImmediateCanvas canvas)
    {
        BackdropCapture? fallback = Volatile.Read(ref _fallback);
        if (fallback is not null)
        {
            // Un-scale by the capture's density, not the replay canvas's density.
            if (fallback.Density == 1f)
            {
                canvas.DrawBitmap(fallback.Bitmap, Brushes.Resource.White, null);
            }
            else
            {
                var dest = new Rect(
                    0,
                    0,
                    fallback.Bitmap.Width / fallback.Density,
                    fallback.Bitmap.Height / fallback.Density);
                canvas.DrawBitmapScaled(fallback.Bitmap, dest, Brushes.Resource.White);
            }
        }
    }

    void IBuiltInBackdropCaptureSink.CommitBackdropCapture(Bitmap bitmap, float density)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!float.IsFinite(density) || density <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(density),
                density,
                "Capture density must be positive and finite.");
        }

        var next = new BackdropCapture(bitmap, density);
        BackdropCapture? previous = Interlocked.Exchange(ref _fallback, next);
        previous?.Bitmap.Dispose();
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        BackdropCapture? previous = Interlocked.Exchange(ref _fallback, null);
        previous?.Bitmap.Dispose();
    }

    private sealed record BackdropCapture(Bitmap Bitmap, float Density);
}
