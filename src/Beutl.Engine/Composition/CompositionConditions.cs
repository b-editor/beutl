using Beutl.Graphics;
using Beutl.Media.Proxy;

namespace Beutl.Composition;

/// <summary>The output conditions an evaluation inherits as one immutable value.</summary>
/// <remarks>
/// Hosts copy them with a single assignment, so a newly added condition reaches every nested evaluation,
/// time sample and query without another hand-written copy.
/// </remarks>
public sealed record CompositionConditions
{
    /// <summary>The frame rate an evaluation without an output rate uses, such as a thumbnail or template preview.</summary>
    public static Rational DefaultFrameRate { get; } = new(30, 1);

    public static CompositionConditions Default { get; } = new();

    /// <summary>The effective output frame rate, including an export's rate override.</summary>
    public Rational FrameRate { get; init; } = DefaultFrameRate;

    public CancellationToken CancellationToken { get; init; }

    /// <summary>The only decode-selection signal consumers read.</summary>
    public bool PreferProxy { get; init; }

    /// <summary>The resolve-time density cap passed to <see cref="ProxyResolver.Resolve(Uri, ProxyPreset)"/>.</summary>
    public ProxyPreset PreferredProxyPreset { get; init; } = ProxyPreset.Quarter;

    /// <summary>The finite logical composition domain available to auxiliary render-node evaluation.</summary>
    /// <remarks>
    /// Scene composition supplies its frame rectangle. A null value means the caller has no finite target domain;
    /// self-bounded render graphs remain valid, while root <c>TargetRegion.Full</c> access requires a value.
    /// </remarks>
    public Rect? TargetDomain { get; init; }

    /// <summary>Optional scene queries. Detached evaluations need not provide these services.</summary>
    public ICompositionEvaluationServices? EvaluationServices { get; init; }
}
