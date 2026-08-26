using System.Collections.Immutable;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Everything about a request that a <see cref="RenderNode"/> can read while it records.
/// </summary>
/// <remarks>
/// A recording is only reusable for a request that agrees on every one of these. It carries the whole
/// observable surface of <see cref="RenderRequestOptions"/> rather than the members nodes read today, so a
/// value later exposed to <see cref="RenderNodeContext"/> or <see cref="RenderNodePreparation"/> cannot
/// silently widen what a cached recording depends on. <c>Owner</c> and the request ID are deliberately absent:
/// both are new every request and neither reaches a node.
/// </remarks>
internal readonly record struct RenderNodeRecordingKey(
    RenderIntent Intent,
    RenderRequestPurpose Purpose,
    Rect? TargetDomain,
    Rect? RequestedRegion,
    float OutputScale,
    float MaxWorkingScale,
    bool CacheEnabled,
    RenderCacheRules CacheRules,
    FusionMode FusionMode,
    bool HasSeparateTargetBinding,
    bool TransactionCacheEnabled)
{
    public static RenderNodeRecordingKey Create(
        RenderRequestOptions options,
        bool transactionCacheEnabled)
        => new(
            options.Intent,
            options.Purpose,
            options.TargetDomain,
            options.RequestedRegion,
            options.OutputScale,
            options.MaxWorkingScale,
            options.CachePolicy.IsEnabled,
            options.CachePolicy.Rules,
            options.FusionMode,
            options.TargetBinding is not null,
            transactionCacheEnabled);
}

/// <summary>One fragment of a reusable recording, with its inputs stored as slots rather than references.</summary>
internal readonly struct ReplayedRenderFragment(
    RenderFragmentReference template,
    object origin,
    string role,
    int[] inputSlots)
{
    public RenderFragmentReference Template { get; } = template;

    public object Origin { get; } = origin;

    public string Role { get; } = role;

    /// <summary>
    /// Where each input came from: a non-negative slot indexes an earlier fragment of this recording, a
    /// negative slot <c>s</c> indexes declared input <c>-s - 1</c>.
    /// </summary>
    public int[] InputSlots { get; } = inputSlots;
}

/// <summary>One hit-test answer a recording read, stated over an input slot so it can be read again.</summary>
/// <remarks>
/// Only a read of a declared input is kept: everything else a recording can read a hit test on either has an
/// answer the retained fragments alone fix, in which case there is nothing to re-check, or belongs to a
/// request that has ended, in which case the recording is refused outright.
/// </remarks>
internal readonly record struct ReplayedHitTestRead(
    int InputIndex,
    Point Point,
    bool Concrete,
    bool Result);

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
        ReplayedHitTestRead[] hitTestReads,
        ReplayedRenderFragment[]? fragments,
        int[]? publicationSlots,
        int[]? droppedSlots,
        bool disabledRenderCache = false)
    {
        Key = key;
        InputFingerprints = inputFingerprints;
        HitTestReads = hitTestReads;
        Fragments = fragments;
        PublicationSlots = publicationSlots;
        DroppedSlots = droppedSlots;
        DisabledRenderCache = disabledRenderCache;
    }

    public RenderNodeRecordingKey Key { get; }

    public long[] InputFingerprints { get; }

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

/// <summary>Builds the reusable form of a recording, and reports what cannot take one.</summary>
internal static class RenderNodeRecordingCache
{
    public const string ProcessRole = "RenderNode.Process";

    /// <summary>Describes one node's recording for reuse, or refuses it.</summary>
    /// <remarks>
    /// <para>
    /// Four things make a recording unreusable, and each is a lifetime the recording does not own:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// A <see cref="RenderResource"/> registration lives from its recording's commit to the release that
    /// follows the request. Replaying a fragment that names one would hand the new request a token the
    /// previous request's registry has already discharged.
    /// </item>
    /// <item>
    /// A nested request carries the same problem one level down, and its own recorded graph besides.
    /// </item>
    /// <item>
    /// A built-in backdrop binding names a fragment for the rest of the request family to find, so replaying
    /// it would publish a fragment of a request that has ended.
    /// </item>
    /// <item>
    /// Driving another node - <see cref="RenderNodeContext.RecordNode"/> or
    /// <see cref="RenderNodeContext.RecordSubtree"/> - makes part of this recording that node's, and reusing
    /// it here would skip that node's own <see cref="RenderNode.HasChanges"/>. Counting the calls rather than
    /// the fragments they left is what catches a driven node that records nothing traceable.
    /// </item>
    /// <item>
    /// An input reached from outside this node's own recording belongs to the request that produced it, so
    /// there is nothing for a later request to rebase it onto.
    /// </item>
    /// <item>
    /// A hit test read on a fragment that is neither a declared input nor fixed by this recording answers
    /// for a graph that has ended, so there is no way to ask it again for the request being served.
    /// </item>
    /// </list>
    /// </remarks>
    public static RenderNodeRecordingSnapshot Capture(
        in RenderNodeRecordingKey key,
        RenderNode node,
        IReadOnlyList<RenderFragmentReference> inputs,
        NodeRecordingTransaction transaction)
    {
        long[] inputFingerprints = inputs.Count == 0 ? [] : new long[inputs.Count];
        for (int index = 0; index < inputs.Count; index++)
            inputFingerprints[index] = inputs[index].RecordingFingerprint;

        ReplayedHitTestRead[] hitTestReads = [];
        bool everyHitTestReadCanBeAskedAgain = transaction.RecordedHitTestReads is not { Count: > 0 } reads
            || TryRebaseHitTestReads(reads, inputs, transaction.RecordedFragments, out hitTestReads);

        if (!everyHitTestReadCanBeAskedAgain
            || transaction.RecordedResourceCount != 0
            || transaction.RecordedNestedRequestCount != 0
            || transaction.RecordedBuiltInBackdropBindingCount != 0
            || transaction.AbsorbedRecordingCount != 0)
        {
            return Refuse(in key, inputFingerprints, hitTestReads);
        }

        IReadOnlyList<RecordedRenderFragmentEntry> entries = transaction.RecordedFragments;
        var slots = new Dictionary<RenderFragmentReference, int>(
            entries.Count + inputs.Count,
            ReferenceEqualityComparer.Instance);
        for (int index = 0; index < inputs.Count; index++)
            slots[inputs[index]] = -index - 1;

        var fragments = new ReplayedRenderFragment[entries.Count];
        for (int index = 0; index < entries.Count; index++)
        {
            RecordedRenderFragmentEntry entry = entries[index];
            if (!ReferenceEquals(entry.Origin, node))
                return Refuse(in key, inputFingerprints, hitTestReads);

            RenderFragmentReference reference = entry.Reference;
            ImmutableArray<RenderFragmentReference> referenceInputs = reference.Inputs;
            int[] inputSlots = referenceInputs.Length == 0 ? [] : new int[referenceInputs.Length];
            for (int inputIndex = 0; inputIndex < referenceInputs.Length; inputIndex++)
            {
                if (!slots.TryGetValue(referenceInputs[inputIndex], out int slot))
                    return Refuse(in key, inputFingerprints, hitTestReads);
                inputSlots[inputIndex] = slot;
            }

            // Detached from the fragments it was recorded over: a template that kept them would pin the
            // previous request's whole graph below this node, and its inputs are what replay supplies.
            fragments[index] = new ReplayedRenderFragment(
                reference.CloneForReplay([]),
                entry.Origin,
                entry.Role,
                inputSlots);
            slots[reference] = index;
        }

        IReadOnlyList<RenderFragmentReference> publications = transaction.RecordedPublications;
        int[] publicationSlots = publications.Count == 0 ? [] : new int[publications.Count];
        for (int index = 0; index < publications.Count; index++)
        {
            if (!slots.TryGetValue(publications[index], out int slot))
                return Refuse(in key, inputFingerprints, hitTestReads);
            publicationSlots[index] = slot;
        }

        IReadOnlyCollection<RenderFragmentReference>? dropped = transaction.RecordedDropped;
        int[] droppedSlots = [];
        if (dropped is { Count: > 0 })
        {
            droppedSlots = new int[dropped.Count];
            int write = 0;
            foreach (RenderFragmentReference reference in dropped)
            {
                if (!slots.TryGetValue(reference, out int slot))
                    return Refuse(in key, inputFingerprints, hitTestReads);
                droppedSlots[write++] = slot;
            }
        }

        return new RenderNodeRecordingSnapshot(
            key,
            inputFingerprints,
            hitTestReads,
            fragments,
            publicationSlots,
            droppedSlots,
            transaction.IsRenderCacheDisabledHere);
    }

    private static RenderNodeRecordingSnapshot Refuse(
        in RenderNodeRecordingKey key,
        long[] inputFingerprints,
        ReplayedHitTestRead[] hitTestReads)
        => new(key, inputFingerprints, hitTestReads, null, null, null);

    /// <summary>
    /// States each hit test the recording read over an input slot, or reports one that cannot be asked again.
    /// </summary>
    /// <remarks>
    /// A read of a declared input is kept, and asking it again over the inputs a later request offers is what
    /// decides whether the recording still stands. A read of a fragment this recording made itself is dropped
    /// when nothing in that fragment's input cone leaves the recording: replay rebuilds such a fragment from
    /// the retained templates alone, so it answers what it answered. Anything else - a fragment reached from
    /// outside this recording, or one of its own whose cone reaches a declared input - has an answer that only
    /// a fresh recording can settle.
    /// </remarks>
    private static bool TryRebaseHitTestReads(
        IReadOnlyList<RecordedHitTestRead> reads,
        IReadOnlyList<RenderFragmentReference> inputs,
        IReadOnlyList<RecordedRenderFragmentEntry> entries,
        out ReplayedHitTestRead[] rebased)
    {
        var declaredInputs = new Dictionary<RenderFragmentReference, int>(
            inputs.Count,
            ReferenceEqualityComparer.Instance);
        for (int index = 0; index < inputs.Count; index++)
            declaredInputs[inputs[index]] = index;

        HashSet<RenderFragmentReference>? recorded = null;
        HashSet<RenderFragmentReference>? visited = null;
        var kept = new List<ReplayedHitTestRead>(reads.Count);
        foreach (RecordedHitTestRead read in reads)
        {
            if (declaredInputs.TryGetValue(read.Reference, out int inputIndex))
            {
                kept.Add(new ReplayedHitTestRead(inputIndex, read.Point, read.Concrete, read.Result));
                continue;
            }

            if (recorded is null)
            {
                recorded = new HashSet<RenderFragmentReference>(
                    entries.Count,
                    ReferenceEqualityComparer.Instance);
                foreach (RecordedRenderFragmentEntry entry in entries)
                    recorded.Add(entry.Reference);
                visited = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
            }

            visited!.Clear();
            if (!IsFixedByTheRecording(read.Reference, recorded, declaredInputs, visited))
            {
                rebased = [];
                return false;
            }
        }

        rebased = [.. kept];
        return true;
    }

    private static bool IsFixedByTheRecording(
        RenderFragmentReference reference,
        HashSet<RenderFragmentReference> recorded,
        Dictionary<RenderFragmentReference, int> declaredInputs,
        HashSet<RenderFragmentReference> visited)
    {
        if (declaredInputs.ContainsKey(reference) || !recorded.Contains(reference))
            return false;
        if (!visited.Add(reference))
            return true;

        foreach (RenderFragmentReference input in reference.Inputs)
        {
            if (!IsFixedByTheRecording(input, recorded, declaredInputs, visited))
                return false;
        }

        return true;
    }
}
