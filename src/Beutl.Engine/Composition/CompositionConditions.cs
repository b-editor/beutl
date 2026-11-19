using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
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

    public FontManager? Fonts { get; init; }

    public DecoderRegistry? Decoders { get; init; }

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

    /// <summary>Objects whose resources this evaluation reconciles as disabled, as if they were turned off.</summary>
    /// <remarks>
    /// An editor reads the colors an effect receives from a frame rendered without it, so that what the effect does
    /// cannot move what it is aimed at. Code that reads an object's <c>IsEnabled</c> property itself, rather than its
    /// resource, does not see this.
    /// </remarks>
    internal IReadOnlySet<EngineObject>? DisabledObjects { get; init; }

    internal bool Disables(EngineObject obj) => DisabledObjects is { Count: > 0 } objects && objects.Contains(obj);

    /// <summary>Filter effects that leave out their per-pixel color stages in this evaluation.</summary>
    /// <remarks>
    /// An editor reads the colors an effect receives from a frame where the effects after it change no colors, while
    /// those that move pixels still put them where the preview shows them. A shader stage that reads only the current
    /// pixel and a Skia color filter are color stages; everything else such an effect records stays. The effects it
    /// applies inside its own recording leave out theirs too, but a group that draws its children as effects of their
    /// own needs them in the set as well.
    /// </remarks>
    internal IReadOnlySet<EngineObject>? ColorStagesSkipped { get; init; }

    internal bool SkipsColorStages(EngineObject obj)
        => ColorStagesSkipped is { Count: > 0 } objects && objects.Contains(obj);
}
