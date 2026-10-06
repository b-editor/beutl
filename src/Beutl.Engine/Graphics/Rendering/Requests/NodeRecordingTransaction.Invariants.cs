namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class NodeRecordingTransaction
{
    private void ValidateRecordedInvariants()
    {
        InvariantScratch scratch = RentScratch();
        try
        {
            HashSet<RenderFragmentReference> reachable = scratch.Reachable;
            HashSet<RenderFragmentReference> fanOutRestricted = scratch.FanOutRestricted;
            foreach (RenderFragmentReference publication in _publications)
                reachable.Add(publication);

            // A fragment's inputs are already owned when it is created and a child's entries are absorbed at
            // its commit, so _fragments is in creation order. Nothing recorded earlier can make a later entry
            // reachable, which is what lets one backward sweep settle reachability and fan-out together.
            bool fanOutViolation = false;
            for (int index = _fragments.Count - 1; index >= 0; index--)
            {
                RenderFragmentReference reference = _fragments[index].Reference;
                if (!reachable.Contains(reference))
                    continue;

                foreach (RenderFragmentReference input in reference.Inputs)
                {
                    reachable.Add(input);

                    // Only a fragment barred from fan-out can fail the check, so the rest never enter the set.
                    if (!input.AllowsFanOut && !fanOutRestricted.Add(input))
                        fanOutViolation = true;
                }
            }

            // The orphan diagnostic keeps precedence over fan-out, so the sweep records rather than throws.
            if (_hasOwnTargetEffectFragment)
                ValidateNoOrphanedTargetEffects(reachable);

            foreach (RenderFragmentReference publication in _publications)
            {
                if (!publication.AllowsFanOut && !fanOutRestricted.Add(publication))
                    fanOutViolation = true;
            }

            if (fanOutViolation)
            {
                throw new InvalidOperationException(
                    "A target-effect render fragment cannot be consumed or published more than once.");
            }
        }
        finally
        {
            ReturnScratch(scratch);
        }
    }

    private static InvariantScratch RentScratch()
    {
        InvariantScratch? scratch = t_scratch;
        if (scratch is null)
        {
            t_scratch = scratch = new InvariantScratch();
        }
        else if (scratch.Rented)
        {
            // The sweep runs no user code, so an overlapping rent is not expected. Private sets keep the
            // sharing an allocation win rather than a correctness assumption.
            return new InvariantScratch { Rented = true };
        }

        scratch.Rented = true;
        return scratch;
    }

    private static void ReturnScratch(InvariantScratch scratch)
    {
        int peak = Math.Max(scratch.Reachable.Count, scratch.FanOutRestricted.Count);
        scratch.Reachable.Clear();
        scratch.FanOutRestricted.Clear();

        // Clear keeps the buckets a warm thread already sized, which is the point of pooling. One outsized
        // commit must not pin that capacity for the life of the thread.
        if (peak > RecycledSetSizeLimit)
        {
            scratch.Reachable.TrimExcess();
            scratch.FanOutRestricted.TrimExcess();
        }

        scratch.Rented = false;
    }

    // Drop is not transitive and a parent never receives handles to a child's internal fragments.
    private void ValidateNoOrphanedTargetEffects(
        HashSet<RenderFragmentReference> reachable)
    {
        foreach (RecordedRenderFragmentEntry entry in _fragments)
        {
            RenderFragmentReference reference = entry.Reference;
            if (!ReferenceEquals(entry.Origin, _origin)
                || !IsTargetEffect(reference.Kind)
                || reachable.Contains(reference)
                || _dropped?.Contains(reference) == true)
            {
                continue;
            }

            throw new InvalidOperationException(
                "A recorded target-effect fragment was neither published nor consumed. "
                + "Publish it, wrap it in a fragment you publish, or call Drop to abandon it "
                + $"deliberately. Fragment kind: {reference.Kind}; recorded by: "
                + $"{entry.Origin.GetType().FullName}.");
        }
    }

    private static bool IsTargetEffect(RenderFragmentKind kind)
        => kind is RenderFragmentKind.TargetCommand
            or RenderFragmentKind.RawTargetCommand
            or RenderFragmentKind.TargetScope
            or RenderFragmentKind.RawTargetScope
            or RenderFragmentKind.TargetLayerScope;

    private sealed class InvariantScratch
    {
        public HashSet<RenderFragmentReference> Reachable { get; } = new(ReferenceEqualityComparer.Instance);

        public HashSet<RenderFragmentReference> FanOutRestricted { get; } =
            new(ReferenceEqualityComparer.Instance);

        public bool Rented { get; set; }
    }
}
