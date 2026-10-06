using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class NodeRecordingTransaction
{
    /// <summary>Records the fragments of <paramref name="snapshot"/> again, over the current inputs.</summary>
    /// <remarks>
    /// The caller has established that a fresh <see cref="RenderNode.Process(RenderNodeContext)"/> would
    /// record exactly this. Every fragment is recreated rather than reused: a recorded fragment carries the
    /// graph identity of the request that committed it, and metadata resolution writes resolved bounds into
    /// it, so one instance cannot belong to two requests.
    /// </remarks>
    internal void ReplayRecording(
        RenderNodeRecordingSnapshot snapshot,
        IReadOnlyList<RenderFragmentReference> inputs)
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(snapshot);
        ReplayedRenderFragment[] fragments = snapshot.Fragments
            ?? throw new InvalidOperationException("The recording snapshot cannot be replayed.");

        _cacheDisabled |= snapshot.DisabledRenderCache;
        if (fragments.Length == 0)
        {
            ReplaySlots(snapshot, [], inputs);
            return;
        }

        // Pure scratch - nothing reads it once this method returns - which is what lets it come from a pool.
        // Rent hands each caller a buffer of its own, so a replay nested inside another cannot take the one
        // its caller is still indexing.
        ArrayPool<RenderFragmentReference> pool = ReplayScratchPool;
        RenderFragmentReference[] rented = pool.Rent(fragments.Length);
        try
        {
            // Rent may hand back a longer buffer; slicing keeps an out-of-range slot a bounds failure rather
            // than a read of whatever the previous renter left past the end.
            Span<RenderFragmentReference> replayed = rented.AsSpan(0, fragments.Length);
            _fragments.EnsureCapacity(_fragments.Count + fragments.Length);
            for (int index = 0; index < fragments.Length; index++)
            {
                ReplayedRenderFragment fragment = fragments[index];
                int[] slots = fragment.InputSlots;
                ImmutableArray<RenderFragmentReference> fragmentInputs;
                if (slots.Length == 0)
                {
                    fragmentInputs = [];
                }
                else
                {
                    // Retained as the clone's Inputs, so it gets an array of its own rather than the scratch.
                    var resolved = new RenderFragmentReference[slots.Length];
                    for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                        resolved[slotIndex] = ResolveSlot(slots[slotIndex], replayed, inputs);
                    fragmentInputs = ImmutableCollectionsMarshal.AsImmutableArray(resolved);
                }

                RenderFragmentReference reference = fragment.Template.CloneForReplay(fragmentInputs);
                replayed[index] = reference;
                OwnedReferences.Add(reference);
                _fragments.Add(new RecordedRenderFragmentEntry(reference, fragment.Origin, fragment.Role));
                _hasOwnTargetEffectFragment |= IsTargetEffect(reference.Kind);
            }

            ReplaySlots(snapshot, replayed, inputs);
        }
        finally
        {
            pool.Return(rented, clearArray: true);
        }
    }

    private void ReplaySlots(
        RenderNodeRecordingSnapshot snapshot,
        ReadOnlySpan<RenderFragmentReference> replayed,
        IReadOnlyList<RenderFragmentReference> inputs)
    {
        foreach (int slot in snapshot.DroppedSlots ?? [])
        {
            (_dropped ??= new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance))
                .Add(ResolveSlot(slot, replayed, inputs));
        }

        int[] publicationSlots = snapshot.PublicationSlots ?? [];
        _publications.EnsureCapacity(_publications.Count + publicationSlots.Length);
        foreach (int slot in publicationSlots)
            _publications.Add(ResolveSlot(slot, replayed, inputs));
    }

    private static RenderFragmentReference ResolveSlot(
        int slot,
        ReadOnlySpan<RenderFragmentReference> replayed,
        IReadOnlyList<RenderFragmentReference> inputs)
        => slot >= 0 ? replayed[slot] : inputs[-slot - 1];
}
