using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

public class SnapshotBackdropNode() : DrawNode(default), IBackdrop, ISupportRenderCache
{
    private Bitmap<Bgra8888>? _bitmap;

    public override bool HitTest(Point point) => false;

    public override void Render(ImmediateCanvas canvas)
    {
        _bitmap?.Dispose();
        _bitmap = canvas.GetRoot().GetBitmap();
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

    void ISupportRenderCache.Accepts(RenderCache cache)
    {
        cache.ReportRenderCount(0);
    }

    void ISupportRenderCache.CreateCache(IImmediateCanvasFactory factory, RenderCache cache, RenderCacheContext context)
    {
    }

    void ISupportRenderCache.RenderWithCache(ImmediateCanvas canvas, RenderCache cache)
    {
    }
}
