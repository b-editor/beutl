using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderCacheResolver
{
    private sealed class ResolverIndex
    {
        private CandidateTopology? _topology;

        public ResolverIndex(RecordedRenderGraph graph)
        {
            Graph = graph;
            var deviceGridReferences = ResolveDeviceGridReferences(graph.Fragments);
            DeviceGridAffectedReferences = deviceGridReferences.Affected;
            TransformDependentReferences = deviceGridReferences.TransformDependent;
        }

        public RecordedRenderGraph Graph { get; }

        public HashSet<RenderFragmentReference> DeviceGridAffectedReferences { get; }

        public HashSet<RenderFragmentReference> TransformDependentReferences { get; }

        public CandidateTopology GetTopology()
            => _topology ??= BuildCandidateTopology(Graph);

        private static (
            HashSet<RenderFragmentReference> Affected,
            HashSet<RenderFragmentReference> TransformDependent) ResolveDeviceGridReferences(
                ImmutableArray<RenderFragmentReference> references)
        {
            var consumers = new Dictionary<RenderFragmentReference, List<RenderFragmentReference>>(
                ReferenceEqualityComparer.Instance);
            foreach (RenderFragmentReference reference in references)
                consumers.Add(reference, []);
            foreach (RenderFragmentReference reference in references)
            {
                foreach (RenderFragmentReference input in reference.Inputs)
                    consumers[input].Add(reference);
            }

            RenderFragmentReference[] phaseUnsafeMaskScopes =
            [
                .. references.Where(IsPhaseUnsafeMaskScope),
            ];
            RenderFragmentReference[] sensitive =
            [
                .. references.Where(IsDeviceGridSensitive),
                .. phaseUnsafeMaskScopes,
            ];
            HashSet<RenderFragmentReference> affected = ExpandConnectedReferences(
                sensitive,
                consumers);
            var transformRootList = new List<RenderFragmentReference>();
            for (int index = 0; index < sensitive.Length; index++)
            {
                if (HasGridRemappingAncestor(sensitive[index], consumers))
                    transformRootList.Add(sensitive[index]);
            }

            RenderFragmentReference[] transformRoots = [.. transformRootList];
            HashSet<RenderFragmentReference> transformDependent = ExpandConnectedReferences(
                transformRoots,
                consumers);
            transformDependent.UnionWith(ExpandConnectedReferences(
                phaseUnsafeMaskScopes,
                consumers));
            return (affected, transformDependent);
        }

        private static HashSet<RenderFragmentReference> ExpandConnectedReferences(
            IEnumerable<RenderFragmentReference> roots,
            IReadOnlyDictionary<RenderFragmentReference, List<RenderFragmentReference>> consumers)
        {
            var result = new HashSet<RenderFragmentReference>(
                ReferenceEqualityComparer.Instance);
            var pending = new Stack<RenderFragmentReference>(roots);
            while (pending.TryPop(out RenderFragmentReference? current))
            {
                if (!result.Add(current))
                    continue;
                foreach (RenderFragmentReference input in current.Inputs)
                    pending.Push(input);
            }

            var visitedAncestors = new HashSet<RenderFragmentReference>(
                ReferenceEqualityComparer.Instance);
            pending = new Stack<RenderFragmentReference>(roots);
            while (pending.TryPop(out RenderFragmentReference? current))
            {
                if (!visitedAncestors.Add(current))
                    continue;
                result.Add(current);
                foreach (RenderFragmentReference consumer in consumers[current])
                    pending.Push(consumer);
            }

            return result;
        }

        private static bool IsDeviceGridSensitive(RenderFragmentReference reference)
        {
            if (reference.Kind is RenderFragmentKind.FilterEffectSegment
                or RenderFragmentKind.Shader
                or RenderFragmentKind.Geometry)
            {
                return true;
            }

            if (reference.Payload is OpaqueRenderFragmentPayload opaque)
            {
                bool declaresPhaseDependence = opaque.Description.DeviceGridSensitivity
                                               == RenderDeviceGridSensitivity.PhaseDependent;
                bool isDrawableBrushHost = reference.Kind == RenderFragmentKind.OpaqueCombine
                                           && opaque.Description.HasDirectReplayMaterializationContract;
                if (declaresPhaseDependence || isDrawableBrushHost)
                    return true;
            }

            if (reference.Payload is TargetScopeRenderFragmentPayload scope
                && scope.Description.DeviceGridSensitivity == RenderDeviceGridSensitivity.PhaseDependent)
            {
                return true;
            }

            return false;
        }

        private static bool IsPhaseUnsafeMaskScope(RenderFragmentReference reference)
        {
            if (reference.Kind != RenderFragmentKind.TargetLayerScope)
                return false;

            var visited = new HashSet<RenderFragmentReference>(
                ReferenceEqualityComparer.Instance);
            var pending = new Stack<RenderFragmentReference>();
            pending.Push(reference);
            while (pending.TryPop(out RenderFragmentReference? current))
            {
                if (!visited.Add(current))
                    continue;
                if (current.Kind == RenderFragmentKind.Blend
                    && ((BlendRenderFragmentPayload)current.Payload!).BlendMode
                    is BlendMode.DstIn or BlendMode.SrcIn or BlendMode.DstATop)
                {
                    return true;
                }

                foreach (RenderFragmentReference input in current.Inputs)
                    pending.Push(input);
            }

            return false;
        }

        private static bool HasGridRemappingAncestor(
            RenderFragmentReference reference,
            IReadOnlyDictionary<RenderFragmentReference, List<RenderFragmentReference>> consumers)
        {
            var visited = new HashSet<RenderFragmentReference>(
                ReferenceEqualityComparer.Instance);
            var pending = new Stack<RenderFragmentReference>(consumers[reference]);
            while (pending.TryPop(out RenderFragmentReference? current))
            {
                if (!visited.Add(current))
                    continue;
                if (RenderFragmentDeviceGrid.ResolveMapping(current)
                    == RenderDeviceGridMapping.Remapped)
                {
                    return true;
                }

                foreach (RenderFragmentReference consumer in consumers[current])
                    pending.Push(consumer);
            }

            return false;
        }
    }
}
