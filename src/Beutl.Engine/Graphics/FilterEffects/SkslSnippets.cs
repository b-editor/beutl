namespace Beutl.Graphics.Effects;

/// <summary>SkSL functions that more than one built-in effect program concatenates.</summary>
internal static class SkslSnippets
{
    /// <summary>The sRGB transfer function pair the curve and lookup programs apply their tables in.</summary>
    /// <remarks>
    /// Curves and lookup tables are authored against sRGB-encoded values, so the linear working colour is
    /// encoded before they apply and decoded after. Every such program has to agree on the encoding, or one
    /// table maps a colour two ways.
    /// </remarks>
    public const string SrgbTransfer =
        """
        float3 linearToSrgb(float3 c) {
            float3 lo = c * 12.92;
            // mix evaluates both branches, so the unused power branch must also stay in its domain.
            float3 hi = 1.055 * pow(max(c, float3(0.0)), float3(1.0/2.4)) - 0.055;
            return mix(lo, hi, step(float3(0.0031308), c));
        }

        float3 srgbToLinear(float3 c) {
            float3 lo = c / 12.92;
            // Negative extended-range values use lo, but a NaN in the unused branch still poisons mix.
            float3 hi = pow(max((c + 0.055) / 1.055, float3(0.0)), float3(2.4));
            return mix(lo, hi, step(float3(0.04045), c));
        }
        """;
}
