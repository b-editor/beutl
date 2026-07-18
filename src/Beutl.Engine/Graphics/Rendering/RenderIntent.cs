namespace Beutl.Graphics.Rendering;

/// <summary>Whether a render may degrade recoverably or must produce delivery-complete output.</summary>
public enum RenderIntent
{
    /// <summary>Interactive preview; recoverable resource failures may drop affected content.</summary>
    Preview,

    /// <summary>Delivery/export; recoverable resource failures fail the render instead of dropping content.</summary>
    Delivery,
}

/// <summary>The purpose of a render-tree pull and whether it may mutate retained frame-render state.</summary>
public enum RenderPullPurpose
{
    /// <summary>A normal frame render that may update retained frame caches.</summary>
    Frame,

    /// <summary>Measurement, hit-testing, thumbnails, or other work that must preserve frame caches.</summary>
    Auxiliary,
}

internal static class RenderPolicyValidation
{
    public static RenderIntent Validate(RenderIntent value, string paramName)
        => value is RenderIntent.Preview or RenderIntent.Delivery
            ? value
            : throw new ArgumentOutOfRangeException(paramName, value, "Unknown render intent.");

    public static RenderPullPurpose Validate(RenderPullPurpose value, string paramName)
        => value is RenderPullPurpose.Frame or RenderPullPurpose.Auxiliary
            ? value
            : throw new ArgumentOutOfRangeException(paramName, value, "Unknown render pull purpose.");
}
