using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Models;

/// <summary>
/// Preview render-quality selector. Resolves to the renderer output scale, clamped to (0, 1].
/// </summary>
public enum RenderScale
{
    /// <summary>Full resolution: output scale 1.0.</summary>
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
    private const float MinScale = 1f / 64f;

    /// <summary>Resolves the output scale for this quality level, clamped to [MinScale, 1].</summary>
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

    /// <summary>Resolves the output scale, snapping FitToPreviewer to 0.05 steps to reduce rebuild churn.</summary>
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
