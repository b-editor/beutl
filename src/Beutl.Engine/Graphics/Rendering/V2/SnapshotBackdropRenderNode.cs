using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.Graphics.Rendering.V2;

public class SnapshotBackdropRenderNode : RenderNode, IBackdrop
{
    private Bitmap<Bgra8888>? _bitmap;

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        context.IsRenderCacheEnabled = false;
        return
        [
            RenderNodeOperation.CreateLambda(default, canvas =>
            {
                _bitmap?.Dispose();
                _bitmap = canvas.GetRoot().GetBitmap();
            })
        ];
    }

    public void Draw(ImmediateCanvas canvas)
    {
        if (_bitmap != null)
        {
            canvas.DrawBitmap(_bitmap, Brushes.White, null);
        }
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
