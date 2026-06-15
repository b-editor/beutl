using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Helpers;

/// <summary>
/// Size math for the export supersampling pre-validation (feature 003, US4). The export pipeline renders
/// the root surface at <c>FrameSize × max(1, factor)</c> (see <c>OutputViewModel.StartEncode</c>); a GPU
/// texture larger than <see cref="RenderNodeContext.MaxBufferDimension"/> on either axis cannot be
/// allocated, so encoding is blocked up-front rather than failing mid-export. Dependency-free (no
/// ViewModel state) so it is unit-testable as a linked source file in <c>Beutl.UnitTests</c>.
/// </summary>
public static class ExportSupersampling
{
    /// <summary>
    /// The root-surface size the export pipeline allocates for <paramref name="frameSize"/> at
    /// <paramref name="factor"/>. Factors below 1 clamp to 1, mirroring
    /// <c>renderScale = max(1, SupersampleFactor)</c> in the encode path. <see cref="long"/> so an
    /// extreme frame size × factor cannot overflow.
    /// </summary>
    public static (long Width, long Height) GetRenderSize(PixelSize frameSize, int factor)
    {
        long f = Math.Max(1, factor);
        return (frameSize.Width * f, frameSize.Height * f);
    }

    /// <summary>
    /// Whether the supersampled root surface fits the per-axis device-buffer limit
    /// (<see cref="RenderNodeContext.MaxBufferDimension"/> by default) on both axes.
    /// </summary>
    public static bool FitsBufferLimit(
        PixelSize frameSize, int factor, int maxDimension = RenderNodeContext.MaxBufferDimension)
    {
        (long width, long height) = GetRenderSize(frameSize, factor);
        return width <= maxDimension && height <= maxDimension;
    }
}
