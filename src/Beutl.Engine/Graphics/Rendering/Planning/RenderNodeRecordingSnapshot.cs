namespace Beutl.Graphics.Rendering;

/// <summary>What one <see cref="RenderNode.Process(RenderNodeContext)"/> call produced, kept for reuse.</summary>
/// <remarks>
/// A snapshot with no <see cref="Fragments"/> records only that the node recorded for this key and over which
/// input digests, which is all a node the cache refuses can offer - it must record again either way.
/// </remarks>
internal sealed class RenderNodeRecordingSnapshot
{
    public RenderNodeRecordingSnapshot(
        RenderNodeRecordingKey key,
        long[] inputFingerprints,
        RenderFragmentRecordingIdentity[] inputIdentities,
        ReplayedHitTestRead[] hitTestReads,
        ReplayedRenderFragment[]? fragments,
        int[]? publicationSlots,
        int[]? droppedSlots,
        bool disabledRenderCache = false)
    {
        Key = key;
        InputFingerprints = inputFingerprints;
        InputIdentities = inputIdentities;
        HitTestReads = hitTestReads;
        Fragments = fragments;
        PublicationSlots = publicationSlots;
        DroppedSlots = droppedSlots;
        DisabledRenderCache = disabledRenderCache;
    }

    public RenderNodeRecordingKey Key { get; }

    public long[] InputFingerprints { get; }

    /// <summary>The exact structure of each input, for settling a fingerprint match.</summary>
    public RenderFragmentRecordingIdentity[] InputIdentities { get; }

    /// <summary>The hit-test answers the recording branched on, which the fingerprints cannot report.</summary>
    public ReplayedHitTestRead[] HitTestReads { get; }

    public ReplayedRenderFragment[]? Fragments { get; }

    public int[]? PublicationSlots { get; }

    public int[]? DroppedSlots { get; }

    /// <summary>Whether the recording opted its own transaction out of persistent render caching.</summary>
    public bool DisabledRenderCache { get; }

    /// <summary>
    /// The shape this recording had, for the cross-check to compare a fresh recording against.
    /// </summary>
    /// <remarks>
    /// Captured only while <see cref="RenderRecordingCrossCheck"/> is on. Describing a recording allocates,
    /// and the render path must not pay for a diagnostic it is not running.
    /// </remarks>
    public RecordedNodeShape? Shape { get; set; }

    public bool IsReplayable => Fragments is not null;

    /// <summary>Whether this recording can stand in for one made now over <paramref name="inputs"/>.</summary>
    public bool Matches(in RenderNodeRecordingKey key, IReadOnlyList<RenderFragmentReference> inputs)
    {
        if (!Key.Equals(key) || InputFingerprints.Length != inputs.Count)
            return false;

        for (int index = 0; index < InputFingerprints.Length; index++)
        {
            if (InputFingerprints[index] != inputs[index].RecordingFingerprint)
                return false;
        }

        // The digests agree, which is a reject that did not fire rather than a verdict: FR-033 requires the
        // comparison to hold under a collision, and one fingerprint stands for a whole input cone.
        for (int index = 0; index < InputIdentities.Length; index++)
        {
            if (!InputIdentities[index].Matches(inputs[index]))
                return false;
        }

        foreach (ReplayedHitTestRead read in HitTestReads)
        {
            RenderFragmentReference input = inputs[read.InputIndex];
            if (input.HasConcreteRecordingMetadata != read.Concrete)
                return false;
            if (read.Concrete && input.HitTest(read.Point) != read.Result)
                return false;
        }

        return true;
    }
}
