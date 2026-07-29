using System.Collections.Immutable;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Derives the backward sampling support of a recorded legacy filter segment from its forward bounds items.
/// </summary>
internal static class LegacyFilterSamplingSupport
{
    private const float ProbeOffset = 37;
    private const float ProbeGrowth = 101;
    private const float ProbeTolerance = 1e-3f;

    /// <summary>
    /// Resolves the margins by which a requested output region must grow to cover every input sample the
    /// segment can read.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the segment is not a pure Skia chain, when a forward map is invalid, or
    /// when the chain is not translation invariant; the caller must then require the complete input.
    /// </returns>
    public static bool TryResolve(ImmutableArray<IFEItem> items, out Thickness support)
    {
        support = default;
        if (items.IsDefaultOrEmpty)
            return false;

        // Only Skia items declare bounds that coincide with their sampling support; a custom item may read
        // outside its declared growth, as InnerShadow does behind an identity declaration.
        foreach (IFEItem item in items)
        {
            if (item is not IFEItem_Skia)
                return false;
        }

        var unit = new Rect(0, 0, 1, 1);
        if (!TryTransform(items, unit, out Rect mapped)
            || !TryTransform(items, unit.Translate(new Vector(ProbeOffset, ProbeOffset)), out Rect translated)
            || !TryTransform(items, unit.Inflate(new Thickness(0, 0, ProbeGrowth, ProbeGrowth)), out Rect grown))
        {
            return false;
        }

        if (!IsClose(translated, mapped.Translate(new Vector(ProbeOffset, ProbeOffset)))
            || !IsClose(grown, mapped.Inflate(new Thickness(0, 0, ProbeGrowth, ProbeGrowth))))
        {
            return false;
        }

        float left = mapped.Right - unit.Right;
        float top = mapped.Bottom - unit.Bottom;
        float right = unit.X - mapped.X;
        float bottom = unit.Y - mapped.Y;
        if (left + right < 0 || top + bottom < 0)
            return false;

        support = new Thickness(left, top, right, bottom);
        return true;
    }

    private static bool TryTransform(ImmutableArray<IFEItem> items, Rect probe, out Rect result)
    {
        result = probe;
        foreach (IFEItem item in items)
        {
            result = item.TransformBounds(result);
            if (!float.IsFinite(result.X)
                || !float.IsFinite(result.Y)
                || !float.IsFinite(result.Width)
                || !float.IsFinite(result.Height)
                || result.Width < 0
                || result.Height < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsClose(Rect actual, Rect expected)
        => IsClose(actual.X, expected.X)
           && IsClose(actual.Y, expected.Y)
           && IsClose(actual.Width, expected.Width)
           && IsClose(actual.Height, expected.Height);

    private static bool IsClose(float actual, float expected)
        => MathF.Abs(actual - expected) <= ProbeTolerance * (1 + MathF.Abs(expected));
}
