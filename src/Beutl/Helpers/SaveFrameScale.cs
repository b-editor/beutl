using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Helpers;

/// <summary>
/// Pure size math for the save-frame scale-choice dialog (feature 003, US4 follow-up). When the user
/// saves the current frame (or a selected element) as an image, they pick an output-resolution
/// multiplier <c>s</c>; the frame is rendered at full fidelity onto a <c>ceil(FrameSize × s)</c> surface
/// (see <c>PlayerViewModel.DrawFrameAtScale</c>) and the snapshot is saved as-is. A surface larger than
/// <see cref="RenderNodeContext.MaxBufferDimension"/> on either axis cannot be allocated, so the dialog
/// disables Save up-front instead of failing mid-render with a generic error.
/// Kept dependency-free (no ViewModel state) so it is unit-testable; compiled into
/// <c>Beutl.UnitTests</c> as a linked source file.
/// </summary>
public static class SaveFrameScale
{
    // A sane floor so a degenerate (zero / negative) multiplier can never size the surface 0×0.
    private const float MinScale = 1f / 64f;

    /// <summary>The selectable output-resolution multipliers offered by the save dialog.</summary>
    public static IReadOnlyList<float> Factors { get; } = [0.5f, 1f, 2f, 4f];

    /// <summary>
    /// The surface size the save path allocates for <paramref name="frameSize"/> at <paramref name="scale"/>:
    /// <c>ceil(FrameSize × scale)</c> per axis, mirroring <c>Renderer.DeviceSize</c> and
    /// <c>RenderNodeProcessor.RasterizeAndConcat</c>. Non-positive scales clamp to <see cref="MinScale"/>.
    /// Computed in <see cref="double"/> and returned as <see cref="long"/> so an extreme frame size × scale
    /// neither loses precision nor overflows.
    /// </summary>
    public static (long Width, long Height) GetRenderSize(PixelSize frameSize, float scale)
    {
        double s = MathF.Max(scale, MinScale);
        return ((long)Math.Ceiling(frameSize.Width * s), (long)Math.Ceiling(frameSize.Height * s));
    }

    /// <summary>
    /// Whether the scaled surface fits the per-axis device-buffer limit
    /// (<see cref="RenderNodeContext.MaxBufferDimension"/> by default) on both axes.
    /// </summary>
    public static bool FitsBufferLimit(
        PixelSize frameSize, float scale, int maxDimension = RenderNodeContext.MaxBufferDimension)
    {
        (long width, long height) = GetRenderSize(frameSize, scale);
        return width <= maxDimension && height <= maxDimension;
    }
}
