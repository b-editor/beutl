using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Helpers;

/// <summary>
/// Pure size math for the save-frame scale-choice dialog (feature 003, US4 follow-up). The user picks an
/// output-resolution multiplier <c>s</c> and the frame renders onto a <c>ceil(FrameSize × s)</c> surface
/// (see <c>PlayerViewModel.DrawFrameAtScale</c>). A surface larger than
/// <see cref="RenderNodeContext.MaxBufferDimension"/> on either axis cannot be allocated, so the dialog
/// disables Save up-front instead of failing mid-render. Dependency-free so it is unit-testable; linked
/// into <c>Beutl.UnitTests</c>.
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
    /// Computed in <see cref="double"/> and returned as <see cref="long"/> so an extreme size neither loses
    /// precision nor overflows.
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

    /// <summary>
    /// Whether <paramref name="frameSize"/> at <paramref name="scale"/> yields a non-empty surface (at least
    /// 1 px on each axis). A 0-area source — e.g. a selected element that renders nothing — sizes the surface
    /// <c>0×0</c>, which <c>RenderTarget.Create</c> rejects (throws mid-render), so the caller must not offer
    /// Save for it. Independent of <see cref="FitsBufferLimit"/> (the too-large guard).
    /// </summary>
    public static bool ProducesRenderableSurface(PixelSize frameSize, float scale)
    {
        (long width, long height) = GetRenderSize(frameSize, scale);
        return width >= 1 && height >= 1;
    }
}
