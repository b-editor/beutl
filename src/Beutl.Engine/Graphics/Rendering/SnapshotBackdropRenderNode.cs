using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public class SnapshotBackdropRenderNode : RenderNode, IBackdrop
{
    private Bitmap? _bitmap;
    private float _captureScale = 1f;

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        context.IsRenderCacheEnabled = false;
        return
        [
            RenderNodeOperation.CreateLambda(default, canvas =>
            {
                _bitmap?.Dispose();
                using var renderTarget = RenderTarget.GetRenderTarget(canvas);
                _bitmap = renderTarget.Snapshot();
                // feature 003 (CSM-3): record the scale of the surface we captured (the root canvas carries
                // s_out) so Draw un-scales by it even when replayed on a nested flush canvas (OutputScale == 1).
                _captureScale = canvas.OutputScale;
            })
        ];
    }

    public void Draw(ImmediateCanvas canvas)
    {
        if (_bitmap != null)
        {
            // feature 003 (CSM-3): the snapshot is the device-sized backing surface (ceil(frame × captureScale)).
            // Un-scale by the CAPTURE scale, not the replay canvas's OutputScale: when this backdrop is replayed
            // inside a buffer-flushing FilterEffect, Draw runs on a nested canvas whose OutputScale is 1 under a
            // CreateScale(w) CTM, and keying off that would render the device capture ~s_out× too large. Drawing
            // into its LOGICAL footprint lets the active CTM map it back. captureScale == 1 keeps the bare blit.
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
