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
    /// Export / save-frame ceiling: <b>none</b> (<see cref="float.PositiveInfinity"/>). Export is the delivery
    /// render, so the working scale follows the true supply density — a deliberately-authored high-density
    /// source (e.g. a 4096-px logo shrunk into a small box, supply ≈ 16) exports at full fidelity, honouring
    /// FR-013/FR-037's "never clip a legitimate high-resolution source". The earlier finite <c>max(8, 4 × s_out)</c>
    /// was a quality clip masquerading as an OOM backstop: it discarded detail from any source denser than 8
    /// on the export render, far below any allocation limit. Allocatability is instead guaranteed per-buffer by
    /// <see cref="Beutl.Graphics.Rendering.RenderNodeContext.ClampWorkingScaleToBufferBudget"/> (the 16384-px
    /// GPU axis limit, a real physical bound), which uses each buffer's own bounds so a small dense element is
    /// never clipped while a genuinely over-large buffer still is. A request-scoped aggregate byte/area budget
    /// (the complete OOM fix, and the bound on a degenerate tiny-element-from-a-huge-source density) remains a
    /// documented follow-up; until it lands, <see cref="Preview"/> keeps interactive renders bounded and export
    /// trades peak memory for fidelity by design.
    /// </summary>
    public static float Export(float outputScale) => float.PositiveInfinity;
}
