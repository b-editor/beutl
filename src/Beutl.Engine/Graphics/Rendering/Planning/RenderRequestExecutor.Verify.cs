using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class CompatibilityExecutionState
    {
        private void VerifyCacheHit(
            RenderFragmentReference fragment,
            RenderCacheHitSubstitution hit,
            IReadOnlyList<CompatibilityRenderValue> cachedValues,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            IReadOnlyList<CompatibilityRenderValue> executedValues = ExecuteFragment(
                fragment,
                currentTarget,
                requestedScale);
            AddValueReferences(executedValues);
            try
            {
                CompareVerifiedCacheHit(fragment, hit, cachedValues, executedValues);
            }
            finally
            {
                foreach (CompatibilityRenderValue value in executedValues)
                    ReleaseValueReference(value);
                if (fragment.Kind == RenderFragmentKind.ContributeValues)
                    CompleteFragmentUse(fragment.Inputs.Single());
            }
        }

        private void CompareVerifiedCacheHit(
            RenderFragmentReference fragment,
            RenderCacheHitSubstitution hit,
            IReadOnlyList<CompatibilityRenderValue> cachedValues,
            IReadOnlyList<CompatibilityRenderValue> executedValues)
        {
            if (cachedValues.Count != executedValues.Count)
            {
                throw CreateCacheOutputMismatch(
                    fragment,
                    hit,
                    $"the cached output has {cachedValues.Count} value(s) but a fresh execution published "
                    + $"{executedValues.Count}");
            }

            for (int index = 0; index < cachedValues.Count; index++)
            {
                CompatibilityRenderValue cached = cachedValues[index];
                CompatibilityRenderValue executed = executedValues[index];
                if (cached.Bounds != executed.Bounds
                    || cached.CompleteBounds != executed.CompleteBounds
                    || cached.EffectiveScale != executed.EffectiveScale
                    || cached.DeviceBounds != executed.DeviceBounds
                    || cached.DeviceGridOffset != executed.DeviceGridOffset)
                {
                    throw CreateCacheOutputMismatch(
                        fragment,
                        hit,
                        $"value {index} has cached geometry (bounds {cached.Bounds}, complete "
                        + $"{cached.CompleteBounds}, density {cached.EffectiveScale.Value}, device "
                        + $"{cached.DeviceBounds}, grid {cached.DeviceGridOffset}) but a fresh execution "
                        + $"produced (bounds {executed.Bounds}, complete {executed.CompleteBounds}, density "
                        + $"{executed.EffectiveScale.Value}, device {executed.DeviceBounds}, grid "
                        + $"{executed.DeviceGridOffset})");
                }

                if (RenderCacheVerification.DescribeDifference(cached.Target, executed.Target)
                    is { } difference)
                {
                    throw CreateCacheOutputMismatch(fragment, hit, $"value {index} {difference}");
                }
            }
        }

        private RenderCacheOutputMismatchException CreateCacheOutputMismatch(
            RenderFragmentReference fragment,
            RenderCacheHitSubstitution hit,
            string difference)
        {
            RenderCacheCandidate candidate = _cacheResolution.GetDecision(hit.CandidateId).Candidate;
            RenderFragmentReference producer = ResolveIdentityCarrier(fragment);
            return new RenderCacheOutputMismatchException(
                "A render-cache runtime identity does not capture everything its producer draws with: "
                + $"{difference}. Cached fragment {hit.OriginalProducerId.Value} ({fragment.Kind}) produced by "
                + $"{producer.Kind} declared on node '{candidate.Cache?.NodeType?.FullName ?? "<unreachable>"}', "
                + $"structural key '{Describe(GetStructuralKey(producer))}', "
                + $"runtime identity '{Describe(GetRuntimeIdentity(producer))}'. "
                + "Add every value the producer draws with to its runtime identity.");
        }

        // A cache candidate is often a payload-less wrapper around the fragment carrying the authored identity.
        private static RenderFragmentReference ResolveIdentityCarrier(RenderFragmentReference fragment)
        {
            RenderFragmentReference current = fragment;
            while (GetRuntimeIdentity(current) is null
                   && GetStructuralKey(current) is null
                   && current.Inputs.Length == 1)
            {
                current = current.Inputs[0];
            }

            return current;
        }

        private static object? GetStructuralKey(RenderFragmentReference fragment)
            => fragment.Payload switch
            {
                ShaderRenderFragmentPayload shader => shader.Description.StructuralIdentity,
                GeometryRenderFragmentPayload geometry => geometry.Description.StructuralIdentity,
                OpaqueRenderFragmentPayload opaque => opaque.Description.StructuralKey,
                _ => null,
            };

        private static object? GetRuntimeIdentity(RenderFragmentReference fragment)
            => fragment.Payload switch
            {
                ShaderRenderFragmentPayload shader => shader.RuntimeIdentity,
                GeometryRenderFragmentPayload geometry => geometry.RuntimeIdentity,
                OpaqueRenderFragmentPayload opaque => opaque.Description.RuntimeIdentity?.Key,
                TargetScopeRenderFragmentPayload scope => scope.Description.RuntimeIdentity?.Key,
                TargetCommandRenderFragmentPayload command => command.Description.RuntimeIdentity?.Key,
                _ => null,
            };

        private static string Describe(object? value)
            => value switch
            {
                null => "<none>",
                Type type => type.FullName ?? type.Name,
                _ => value.ToString() ?? value.GetType().Name,
            };
    }
}
