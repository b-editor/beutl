namespace Beutl.Models;

// NOTE (feature 003, S3): the global working-scale ceiling (FR-037) was an inline magic number duplicated across
// EditViewModel (preview) and OutputViewModel / PlayerViewModel (export / save-frame). Centralizing it here (in
// Beutl.Editor, which the test project references — the Beutl app exe is not) gives the ceiling policy ONE
// definition and makes it unit-testable, instead of three hand-copied `MathF.Max(8f, 4f * s)` expressions that
// could drift. The namespace stays Beutl.Models so existing `using Beutl.Models;` call sites pick it up.

/// <summary>
/// The per-render-request global working-scale ceiling <c>MaxWorkingScale</c> (feature 003, FR-037). Caps the
/// supply-driven working scale <c>w</c> on the high side only; it never pulls <c>w</c> below a proxy's supply and
/// is inert at <c>s_out = 1.0</c> with unit-scale inputs (byte-identity preserved). It bounds the working scale
/// <c>w</c> itself, NOT buffer memory (see the separate per-buffer dimension clamp
/// <c>RenderNodeContext.ClampWorkingScaleToBufferBudget</c>).
/// </summary>
public static class WorkingScaleCeiling
{
    /// <summary>
    /// Preview ceiling: <c>2 × s_out</c>. A tight backstop that bounds interactive (scrub / playback) working
    /// scale and memory. Lower than <see cref="Export"/>, so a scene whose supply density exceeds 2 renders its
    /// resolution-sensitive effects at a different working scale in Full preview than in export (FR-037; there is
    /// no mismatch-warning UI in v1 — the divergence is documented on <see cref="RenderScale.Full"/>).
    /// </summary>
    public static float Preview(float outputScale) => 2f * outputScale;

    /// <summary>
    /// Export / save-frame ceiling: <c>max(8, 4 × s_out)</c>. Generous enough never to clip a legitimate
    /// high-resolution source (so a 4K source still exports at full fidelity, FR-013) yet finite. The constant
    /// floor of 8 keeps a 4K-into-1080 source (supply ≈ 2–4) un-clipped even at <c>s_out = 1</c>, where
    /// <c>4 × s_out = 4</c> alone would be the binding term.
    /// </summary>
    public static float Export(float outputScale) => MathF.Max(8f, 4f * outputScale);
}
