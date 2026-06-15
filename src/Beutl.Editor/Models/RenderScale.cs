using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Models;

// NOTE (feature 003): lives in Beutl.Editor (not the app exe) so the pure s_out mapping is unit-testable;
// namespace stays Beutl.Models so existing `using Beutl.Models;` call sites are unaffected.

/// <summary>
/// Preview render-quality selector (feature 003, US4). Per edit view, non-persisted. Resolves to
/// the renderer's output scale <c>s_out</c>, always clamped to <c>(0, 1]</c> — preview never upscales.
/// </summary>
public enum RenderScale
{
    /// <summary>
    /// Full resolution: <c>s_out = 1.0</c>. NOT identical to export — preview caps working scale at
    /// <c>2 × s_out</c> while export has no ceiling (FR-037), so a scene whose supply density exceeds 2 runs
    /// resolution-sensitive effects at a different working scale than the exported result.
    /// </summary>
    Full,

    /// <summary>Half resolution: <c>s_out = 0.5</c>.</summary>
    Half,

    /// <summary>Quarter resolution: <c>s_out = 0.25</c>.</summary>
    Quarter,

    /// <summary>Fit the frame into the on-screen previewer (capped at full).</summary>
    FitToPreviewer,
}

public static class RenderScaleExtensions
{
    // A sane floor so a degenerate previewer size can never produce s_out = 0.
    private const float MinScale = 1f / 64f;

    /// <summary>
    /// Resolves the output scale <c>s_out</c> for this quality level. <paramref name="frameSize"/> is
    /// the scene's logical frame size; <paramref name="previewSize"/> is the on-screen previewer size.
    /// The result is always clamped to <c>[MinScale, 1]</c> (preview never upscales beyond full).
    /// </summary>
    public static float ToFloat(this RenderScale scale, PixelSize frameSize, Size previewSize)
    {
        float s = scale switch
        {
            RenderScale.Full => 1f,
            RenderScale.Half => 0.5f,
            RenderScale.Quarter => 0.25f,
            RenderScale.FitToPreviewer => FitScale(frameSize, previewSize),
            _ => 1f,
        };

        return Math.Clamp(s, MinScale, 1f);
    }

    /// <summary>
    /// Resolves the output scale <c>s_out</c> for a render request. Fixed levels pass through
    /// <see cref="ToFloat"/>; <see cref="RenderScale.FitToPreviewer"/> snaps to 0.05 steps to bound
    /// renderer-rebuild churn during edge drags, then is re-floored to <c>MinScale</c> so the snap can never
    /// reach 0 (which would size the surface 0×0 and throw on renderer construction). Always in <c>[MinScale, 1]</c>.
    /// </summary>
    public static float ResolveOutputScale(this RenderScale scale, PixelSize frameSize, Size previewSize)
    {
        float s = scale.ToFloat(frameSize, previewSize);
        return scale == RenderScale.FitToPreviewer ? MathF.Max(MathF.Round(s * 20f) / 20f, MinScale) : s;
    }

    private static float FitScale(PixelSize frameSize, Size previewSize)
    {
        if (frameSize.Width <= 0 || frameSize.Height <= 0
            || previewSize.Width <= 0 || previewSize.Height <= 0)
        {
            return 1f;
        }

        float sx = (float)(previewSize.Width / frameSize.Width);
        float sy = (float)(previewSize.Height / frameSize.Height);
        return MathF.Min(sx, sy);
    }
}
