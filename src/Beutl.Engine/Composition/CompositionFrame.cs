using System.Collections.Immutable;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Composition;

public readonly record struct CompositionFrame(
    ImmutableArray<EngineObject.Resource> Objects,
    TimeRange Time,
    PixelSize Size)
{
    /// <summary>
    /// Creates a frame with explicit composition-policy provenance. The existing three-argument constructor remains
    /// a preview frame pull and retains its three-value deconstruction contract.
    /// </summary>
    public CompositionFrame(
        ImmutableArray<EngineObject.Resource> objects,
        TimeRange time,
        PixelSize size,
        RenderIntent renderIntent,
        RenderPullPurpose pullPurpose)
        : this(objects, time, size)
    {
        RenderIntent = RenderPolicyValidation.Validate(renderIntent, nameof(renderIntent));
        PullPurpose = RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
    }

    /// <summary>The preview/delivery policy used while composing <see cref="Objects"/>.</summary>
    public RenderIntent RenderIntent { get; }

    /// <summary>The pull purpose used while composing <see cref="Objects"/>.</summary>
    public RenderPullPurpose PullPurpose { get; }
}
