using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public class SnapshotBackdropRenderNode : RenderNode, IBackdrop
{
    private Bitmap? _bitmap;
    private float _captureScale = 1f;

    public override void PrepareForProcess(ImmediateCanvas canvas)
    {
        Capture(canvas);
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        context.IsRenderCacheEnabled = false;
        // Capturing again here keeps the backdrop operation-relative: a backdrop that follows a
        // sibling inside the same group has to see what that sibling drew, which PrepareForProcess
        // cannot know because it runs before any operation of the tree has rendered.
        return
        [
            RenderNodeOperation.CreateLambda(default, Capture)
        ];
    }

    private void Capture(ImmediateCanvas canvas)
    {
        _bitmap?.Dispose();
        using var renderTarget = RenderTarget.GetRenderTarget(canvas);
        _bitmap = renderTarget.Snapshot();
        // Record the surface density (not current Density, which PushDeviceSpace resets to 1).
        _captureScale = canvas.SurfaceDensity;
    }

    public void Draw(ImmediateCanvas canvas)
    {
        if (_bitmap != null)
        {
            // Un-scale by the capture's density, not the replay canvas's density.
            if (_captureScale == 1f)
            {
                canvas.DrawBitmap(_bitmap, Brushes.Resource.White, null);
            }
            else
            {
                var dest = new Rect(0, 0, _bitmap.Width / _captureScale, _bitmap.Height / _captureScale);
                canvas.DrawBitmapScaled(_bitmap, dest, Brushes.Resource.White);
            }
        }
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
