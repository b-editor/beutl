namespace Beutl.Graphics.Effects;

/// <summary>
/// Declares whether a fragment shader is proven to write every output pixel, allowing a filter render pass to
/// load a transparently initialized destination or discard its previous contents safely.
/// </summary>
/// <remarks>
/// Only engine-owned built-in shaders may claim <see cref="ProvablyFull"/>, and only after proving that every
/// control-flow path writes the fragment output and that the shader contains no <c>discard</c>. A false claim
/// leaves unwritten pixels unchanged, so a reused pooled target can reveal stale pixels from a previous,
/// unrelated frame; this failure may not reproduce while the pool is cold.
/// </remarks>
internal enum ShaderOutputCoverage : byte
{
    /// <summary>
    /// The shader may leave fragments unwritten, so the destination is transparently initialized and the
    /// render-pass attachment is cleared before drawing. Public or user-authored shaders always use this
    /// conservative contract.
    /// </summary>
    MayLeavePixelsUnwritten,

    /// <summary>
    /// Every control-flow path is proven to write the fragment output and no path uses <c>discard</c>. Only an
    /// audited engine-owned built-in shader may claim this; an incorrect claim exposes stale pooled-target pixels
    /// from an unrelated frame and can remain hidden until a warm-pool reuse.
    /// </summary>
    ProvablyFull,
}
