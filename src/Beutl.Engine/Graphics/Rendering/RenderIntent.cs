namespace Beutl.Graphics.Rendering;

/// <summary>Whether a render may degrade recoverably or must produce delivery-complete output.</summary>
public enum RenderIntent
{
    /// <summary>Interactive preview; recoverable resource failures may drop affected content.</summary>
    Preview,

    /// <summary>Delivery/export; recoverable resource failures fail the render instead of dropping content.</summary>
    Delivery,
}

internal static class RenderIntentResolver
{
    public static RenderIntent Resolve(RenderIntent? intent, float maxWorkingScale)
        => intent ?? (float.IsPositiveInfinity(RenderNodeContext.SanitizeMaxWorkingScale(maxWorkingScale))
            ? RenderIntent.Delivery
            : RenderIntent.Preview);
}
