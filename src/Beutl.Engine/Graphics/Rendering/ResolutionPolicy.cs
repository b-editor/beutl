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
}

/// <summary>
/// A per-effect / per-node resolution policy consumed by the working-scale
/// negotiation (feature 003, FR-036). <c>default(ResolutionPolicy)</c> is
/// <see cref="Inherit"/> (the enum's zero value), so an unset policy is the
/// supply-driven default.
/// </summary>
/// <remarks>
/// Built only through <see cref="Inherit"/> / <see cref="ClampToOutput"/> / <see cref="Oversample"/>:
/// the constructor is private and the properties are get-only so a caller — including an out-of-tree
/// plugin — cannot fabricate an invalid <c>Oversample</c> with a non-positive factor (which would
/// silently resolve like <see cref="Inherit"/>). <c>Factor</c> defaults to 0 (the struct-zero) so
/// <c>default(ResolutionPolicy)</c> stays value-equal to <see cref="Inherit"/>.
/// </remarks>
public readonly record struct ResolutionPolicy
{
    private ResolutionPolicy(ResolutionPolicyKind kind, float factor)
    {
        if (kind == ResolutionPolicyKind.Oversample && !(factor > 0f))
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "Oversample factor must be positive.");

        Kind = kind;
        Factor = factor;
    }

    /// <summary>The negotiation strategy.</summary>
    public ResolutionPolicyKind Kind { get; }

    /// <summary>The oversample multiple; only consumed when <see cref="Kind"/> is <see cref="ResolutionPolicyKind.Oversample"/>.</summary>
    public float Factor { get; }

    /// <summary>Run at the input supply density (default).</summary>
    public static readonly ResolutionPolicy Inherit = new(ResolutionPolicyKind.Inherit, 0f);

    /// <summary>Clamp the working scale to the output scale (perf opt-out).</summary>
    public static readonly ResolutionPolicy ClampToOutput = new(ResolutionPolicyKind.ClampToOutput, 0f);

    /// <summary>
    /// Force at least <paramref name="factor"/> times the output scale (quality opt-in). <paramref name="factor"/>
    /// MUST be positive — a zero/negative factor would make Oversample resolve identically to <see cref="Inherit"/>
    /// (supply density), so it is rejected here rather than silently degrading.
    /// </summary>
    public static ResolutionPolicy Oversample(float factor) => new(ResolutionPolicyKind.Oversample, factor);
}
