namespace Beutl.Graphics.Rendering;

internal static class RenderRectValidation
{
    public static bool IsFiniteNonNegative(Rect value)
        => float.IsFinite(value.X)
           && float.IsFinite(value.Y)
           && float.IsFinite(value.Width)
           && float.IsFinite(value.Height)
           && value.Width >= 0
           && value.Height >= 0;

    public static void ThrowIfInvalidInput(Rect value, string parameterName)
    {
        if (!IsFiniteNonNegative(value))
        {
            throw new ArgumentException(
                "Bounds must be finite and have non-negative dimensions.",
                parameterName);
        }
    }

    public static void ThrowIfInvalidResult(Rect value, string message)
    {
        if (!IsFiniteNonNegative(value))
            throw new InvalidOperationException(message);
    }
}
