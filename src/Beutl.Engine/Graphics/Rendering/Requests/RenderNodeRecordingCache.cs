using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

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

/// <summary>Builds the reusable form of a recording, and reports what cannot take one.</summary>
internal static class RenderNodeRecordingCache
{
    public const string ProcessRole = "RenderNode.Process";

    /// <summary>Captures one node recording for reuse, or returns a refused snapshot.</summary>
    /// <remarks>
    /// Recordings that retain request-scoped resources, nested requests, backdrop bindings, driven-node work,
    /// external fragments, or non-replayable hit-test reads are refused.
    /// </remarks>
    public static RenderNodeRecordingSnapshot Capture(
        in RenderNodeRecordingKey key,
        RenderNode node,
        IReadOnlyList<RenderFragmentReference> inputs,
        NodeRecordingTransaction transaction)
    {
        long[] inputFingerprints = inputs.Count == 0 ? [] : new long[inputs.Count];
        RenderFragmentRecordingIdentity[] inputIdentities =
            inputs.Count == 0 ? [] : new RenderFragmentRecordingIdentity[inputs.Count];
        for (int index = 0; index < inputs.Count; index++)
        {
            inputFingerprints[index] = inputs[index].RecordingFingerprint;
            inputIdentities[index] = inputs[index].RecordingIdentity;
        }

        ReplayedHitTestRead[] hitTestReads = [];
        bool everyHitTestReadCanBeAskedAgain = transaction.RecordedHitTestReads is not { Count: > 0 } reads
            || TryRebaseHitTestReads(reads, inputs, transaction.RecordedFragments, out hitTestReads);

        if (!everyHitTestReadCanBeAskedAgain
            || transaction.RecordedResourceCount != 0
            || transaction.RecordedNestedRequestCount != 0
            || transaction.RecordedBuiltInBackdropBindingCount != 0
            || transaction.AbsorbedRecordingCount != 0)
        {
            return Refuse(in key, inputFingerprints, inputIdentities, hitTestReads);
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
                return Refuse(in key, inputFingerprints, inputIdentities, hitTestReads);

            RenderFragmentReference reference = entry.Reference;
            ImmutableArray<RenderFragmentReference> referenceInputs = reference.Inputs;
            int[] inputSlots = referenceInputs.Length == 0 ? [] : new int[referenceInputs.Length];
            for (int inputIndex = 0; inputIndex < referenceInputs.Length; inputIndex++)
            {
                if (!slots.TryGetValue(referenceInputs[inputIndex], out int slot))
                    return Refuse(in key, inputFingerprints, inputIdentities, hitTestReads);
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
                return Refuse(in key, inputFingerprints, inputIdentities, hitTestReads);
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
                    return Refuse(in key, inputFingerprints, inputIdentities, hitTestReads);
                droppedSlots[write++] = slot;
            }
        }

        return new RenderNodeRecordingSnapshot(
            key,
            inputFingerprints,
            inputIdentities,
            hitTestReads,
            fragments,
            publicationSlots,
            droppedSlots,
            transaction.IsRenderCacheDisabledHere);
    }

    private static RenderNodeRecordingSnapshot Refuse(
        in RenderNodeRecordingKey key,
        long[] inputFingerprints,
        RenderFragmentRecordingIdentity[] inputIdentities,
        ReplayedHitTestRead[] hitTestReads)
        => new(key, inputFingerprints, inputIdentities, hitTestReads, null, null, null);

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
