namespace Beutl.Graphics.Rendering;

/// <summary>
/// How a buffer-allocating boundary derives its working scale from its inputs'
/// effective scales and the render request's output scale (feature 003).
/// </summary>
public enum ResolutionPolicyKind
{
    /// <summary>
    /// Default. Run at the input supply density: a 0.5 proxy stays 0.5 (no
    /// upsample), a 2.0 source stays 2.0 (no downsample). The output scale is
    /// not a ceiling.
    /// </summary>
    Inherit,

    /// <summary>Performance opt-out: clamp the working scale down to the output scale.</summary>
    ClampToOutput,

    /// <summary>
    /// Quality opt-in: force at least <see cref="ResolutionPolicy.Factor"/> times
    /// the output scale, even from a lower-density input (SSAA on demand).
    /// </summary>
    Oversample,

    /// <summary>
    /// Quality: keep a high source's density through the effect and floor any
    /// ancestor <see cref="ClampToOutput"/>.
    /// </summary>
    PreserveSource,
}

/// <summary>
/// A per-effect / per-node resolution policy consumed by the working-scale
/// negotiation (feature 003, FR-036). <c>default(ResolutionPolicy)</c> is
/// <see cref="Inherit"/> (the enum's zero value), so an unset policy is the
/// supply-driven default.
/// </summary>
public readonly record struct ResolutionPolicy(ResolutionPolicyKind Kind, float Factor = 0f)
{
    // Factor defaults to 0 (the struct-zero) so that default(ResolutionPolicy) is value-equal to
    // Inherit. Only Oversample consumes Factor, and it always supplies one via Oversample(factor).
    /// <summary>Run at the input supply density (default).</summary>
    public static readonly ResolutionPolicy Inherit = new(ResolutionPolicyKind.Inherit);

    /// <summary>Clamp the working scale to the output scale (perf opt-out).</summary>
    public static readonly ResolutionPolicy ClampToOutput = new(ResolutionPolicyKind.ClampToOutput);

    /// <summary>Keep a high source's density (quality).</summary>
    public static readonly ResolutionPolicy PreserveSource = new(ResolutionPolicyKind.PreserveSource);

    /// <summary>Force at least <paramref name="factor"/> times the output scale (quality opt-in).</summary>
    public static ResolutionPolicy Oversample(float factor) => new(ResolutionPolicyKind.Oversample, factor);
}
