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

/// <summary>What one <see cref="RenderNode.Process(RenderNodeContext)"/> call produced, kept for reuse.</summary>
/// <remarks>
/// A snapshot with no <see cref="Fragments"/> records only that the node recorded for this key, which is what
/// lets a node the cache refuses still report a repeat recording to whoever records above it.
/// </remarks>
internal sealed class RenderNodeRecordingSnapshot
{
    public RenderNodeRecordingSnapshot(
        RenderNodeRecordingKey key,
        long[] inputFingerprints,
        ReplayedRenderFragment[]? fragments,
        int[]? publicationSlots,
        int[]? droppedSlots,
        bool disabledRenderCache = false)
    {
        Key = key;
        InputFingerprints = inputFingerprints;
        Fragments = fragments;
        PublicationSlots = publicationSlots;
        DroppedSlots = droppedSlots;
        DisabledRenderCache = disabledRenderCache;
    }

    public RenderNodeRecordingKey Key { get; }

    public long[] InputFingerprints { get; }

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

        if (transaction.RecordedResourceCount != 0
            || transaction.RecordedNestedRequestCount != 0
            || transaction.RecordedBuiltInBackdropBindingCount != 0
            || transaction.AbsorbedRecordingCount != 0)
        {
            return new RenderNodeRecordingSnapshot(key, inputFingerprints, null, null, null);
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
                return new RenderNodeRecordingSnapshot(key, inputFingerprints, null, null, null);

            RenderFragmentReference reference = entry.Reference;
            ImmutableArray<RenderFragmentReference> referenceInputs = reference.Inputs;
            int[] inputSlots = referenceInputs.Length == 0 ? [] : new int[referenceInputs.Length];
            for (int inputIndex = 0; inputIndex < referenceInputs.Length; inputIndex++)
            {
                if (!slots.TryGetValue(referenceInputs[inputIndex], out int slot))
                    return new RenderNodeRecordingSnapshot(key, inputFingerprints, null, null, null);
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
                return new RenderNodeRecordingSnapshot(key, inputFingerprints, null, null, null);
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
                    return new RenderNodeRecordingSnapshot(key, inputFingerprints, null, null, null);
                droppedSlots[write++] = slot;
            }
        }

        return new RenderNodeRecordingSnapshot(
            key,
            inputFingerprints,
            fragments,
            publicationSlots,
            droppedSlots,
            transaction.IsRenderCacheDisabledHere);
    }
}
