using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class NestedRenderTargetImage
{
    private readonly RenderExecutionSessionToken _token;
    private readonly SKImage _image;

    public NestedRenderTargetImage(
        RenderExecutionSessionToken token,
        SKImage image,
        Rect logicalBounds,
        float density,
        PixelRect deviceBounds)
    {
        _token = token;
        _image = image;
        LogicalBounds = logicalBounds;
        Density = density;
        DeviceBounds = deviceBounds;
    }

    public Rect LogicalBounds
    {
        get { _token.ThrowIfInactive(); return field; }
    }

    public float Density
    {
        get { _token.ThrowIfInactive(); return field; }
    }

    public PixelRect DeviceBounds
    {
        get { _token.ThrowIfInactive(); return field; }
    }

    public Rect RasterBounds
    {
        get
        {
            _token.ThrowIfInactive();
            return DeviceBounds.ToRect(Density);
        }
    }

    public void Draw(ImmediateCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        _token.VerifyActiveCanvas(canvas);
        canvas.DrawImageScaled(_image, RasterBounds);
    }
}
