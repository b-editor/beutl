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
                // feature 003 (CSM-3): record the captured surface's scale so Draw can un-scale by it even when
                // replayed on a nested flush canvas. Use SurfaceDensity, NOT the current Density which a
                // PushDeviceSpace block lowers to 1 — a snapshot grabs the whole surface, always at surface density.
                _captureScale = canvas.SurfaceDensity;
            })
        ];
    }

    public void Draw(ImmediateCanvas canvas)
    {
        if (_bitmap != null)
        {
            // feature 003 (CSM-3): the snapshot is the device-sized backing surface (ceil(frame × captureScale)).
            // Un-scale by the CAPTURE scale, not the replay canvas's density: replayed inside a buffer-flushing
            // FilterEffect, Draw runs on a nested canvas at a different density (its working scale w), and keying
            // off that would size the capture wrong. Drawing into its LOGICAL footprint lets the active CTM map it
            // back. captureScale == 1 keeps the bare blit.
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
