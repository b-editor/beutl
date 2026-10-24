using Avalonia;
using PixelSize = Beutl.Media.PixelSize;

namespace Beutl.Views;

// Coordinates are image-local: GetPosition(image) already removes the preview's pan and zoom.
// The image and overlays share the preview's uniform fit.
internal sealed class PlayerViewportTransform
{
    private PlayerViewportTransform(Size previewSize, PixelSize frameSize, double scale)
    {
        PreviewSize = previewSize;
        FrameSize = frameSize;
        FrameScale = scale;
        FrameToPreview = Matrix.CreateScale(scale, scale);
        PreviewToFrame = Matrix.CreateScale(1 / scale, 1 / scale);
    }

    public Size PreviewSize { get; }

    public PixelSize FrameSize { get; }

    public double FrameScale { get; }

    public Matrix FrameToPreview { get; }

    public Matrix PreviewToFrame { get; }

    public Point ToFrame(Point point) => PreviewToFrame.Transform(point);

    public Point ToPreview(Point point) => FrameToPreview.Transform(point);

    public static PlayerViewportTransform? Create(Size previewSize, PixelSize frameSize)
    {
        if (!(previewSize.Width > 0) || !(previewSize.Height > 0)
            || !double.IsFinite(previewSize.Width) || !double.IsFinite(previewSize.Height)
            || frameSize.Width <= 0 || frameSize.Height <= 0)
            return null;

        double scale = previewSize.Width / frameSize.Width;
        return scale > 0 && double.IsFinite(1 / scale)
            ? new PlayerViewportTransform(previewSize, frameSize, scale)
            : null;
    }
}
