using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public class SnapshotBackdropRenderNode : RenderNode, IBackdrop
{
    private Bitmap? _bitmap;
    private float _captureScale = 1f;
    private ImmediateCanvas? _pendingCanvas;
    private bool _capturedThisPass;

    public override void PrepareForProcess(ImmediateCanvas canvas)
    {
        // Remembered, not captured: Snapshot reads back the whole surface, and the capture operation
        // below is the one that lands in the right place in the stream. This canvas is only used
        // when a consumer is rasterized during processing and asks for the picture first.
        _pendingCanvas = canvas;
        _capturedThisPass = false;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        context.IsRenderCacheEnabled = false;
        // Capturing again here keeps the backdrop operation-relative: a backdrop that follows a
        // sibling inside the same group has to see what that sibling drew, which PrepareForProcess
        // cannot know because it runs before any operation of the tree has rendered.
        return
        [
            RenderNodeOperation.CreateLambda(default, canvas =>
            {
                // Already captured if a consumer rasterized during processing asked for the picture
                // first; capturing again would be a second full-surface readback for that frame.
                if (!_capturedThisPass)
                {
                    Capture(canvas);
                }
            })
        ];
    }

    private void Capture(ImmediateCanvas canvas)
    {
        _capturedThisPass = true;
        _bitmap?.Dispose();
        using var renderTarget = RenderTarget.GetRenderTarget(canvas);
        _bitmap = renderTarget.Snapshot();
        // Record the surface density (not current Density, which PushDeviceSpace resets to 1).
        _captureScale = canvas.SurfaceDensity;
    }

    public void Draw(ImmediateCanvas canvas)
    {
        if (!_capturedThisPass && _pendingCanvas != null)
        {
            // A filter effect rasterizes its input while the tree is being processed, so the capture
            // operation has not run yet. Fall back to the canvas the pass is compositing onto.
            Capture(_pendingCanvas);
        }

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
        _pendingCanvas = null;
    }
}
