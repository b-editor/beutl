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
        // Only the fallback for a consumer rasterized during processing; the operation below is the
        // capture that lands in the right place in the stream.
        _pendingCanvas = canvas;
        _capturedThisPass = false;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        context.IsRenderCacheEnabled = false;
        // A backdrop that follows a sibling inside the same group has to see what that sibling drew,
        // which the prepass cannot know: it runs before any operation of the tree has rendered.
        return
        [
            RenderNodeOperation.CreateLambda(default, canvas =>
            {
                // A second full-surface readback when the fallback already captured this pass.
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
