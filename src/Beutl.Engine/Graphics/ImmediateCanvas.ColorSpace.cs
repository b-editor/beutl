using SkiaSharp;

namespace Beutl.Graphics;

public partial class ImmediateCanvas
{
    private void DrawTransferredImage(SKImage image, SKRect source, SKRect destination, SKSamplingOptions sampling)
    {
        float sx = destination.Width / source.Width;
        float sy = destination.Height / source.Height;
        var matrix = SKMatrix.CreateScaleTranslation(sx, sy,
            destination.Left - source.Left * sx, destination.Top - source.Top * sy);
        // The destination rectangle supplies edge coverage, just like DrawImage. Decal sampling
        // would fade at the texture border as well and apply AA twice to a rotated opaque image.
        using SKShader? shader = ColorTransferShader.TryCreate(image, WorkingColorSpace,
            SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, matrix);
        if (shader is null)
        {
            Canvas.DrawImage(image, source, destination, sampling, _sharedFillPaint);
            return;
        }

        _sharedFillPaint.Shader = shader;
        try
        {
            Canvas.DrawRect(destination, _sharedFillPaint);
        }
        finally
        {
            _sharedFillPaint.Shader = null;
        }
    }
}
