using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public class SnapshotBackdropRenderNode : RenderNode, IBackdrop
{
    private Bitmap? _bitmap;

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
            })
        ];
    }

    public void Draw(ImmediateCanvas canvas)
    {
        if (_bitmap != null)
        {
            // feature 003: the snapshot is the device-sized backing surface (ceil(frame × s_out)); at
            // s_out != 1 draw it into its LOGICAL footprint so the active root CTM maps it back 1:1
            // instead of double-scaling. s_out == 1 keeps the bare pixel-extent blit (byte-identical).
            if (canvas.OutputScale == 1f)
            {
                canvas.DrawBitmap(_bitmap, Brushes.Resource.White, null);
            }
            else
            {
                var dest = new Rect(0, 0, _bitmap.Width / canvas.OutputScale, _bitmap.Height / canvas.OutputScale);
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
