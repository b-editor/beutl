using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

internal readonly record struct StructuralPlanCacheStatistics(
    long Hits,
    long Misses,
    long Compilations,
    long Replacements,
    int RetainedPlans);

/// <summary>
/// Retains the last structural request family for a renderer. Each stable depth-first family slot keeps
/// one candidate; hashes only select that candidate and the complete structural identity must still compare
/// equal before a plan is rebound to a new request.
/// </summary>
internal sealed class StructuralPlanCache : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Entry> _entries = [];
    private long _hits;
    private long _misses;
    private long _compilations;
    private long _replacements;
    private bool _disposed;

    public StructuralPlanCacheStatistics Statistics
    {
        get
        {
            lock (_gate)
            {
                return new StructuralPlanCacheStatistics(
                    _hits,
                    _misses,
                    _compilations,
                    _replacements,
                    _entries.Count);
            }
        }
    }

    public ExecutionIslandPlan GetOrCompile(
        StructuralPlanIdentity identity,
        RecordedRenderGraph graph,
        Func<ExecutionIslandPlan> compile,
        int? bucketHashOverride = null,
        int familySlot = 0)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(compile);
        ArgumentOutOfRangeException.ThrowIfNegative(familySlot);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int bucketHash = bucketHashOverride ?? identity.GetHashCode();
            if (_entries.TryGetValue(familySlot, out Entry? entry)
                && entry.BucketHash == bucketHash
                && entry.Identity.Equals(identity))
            {
                _hits++;
                return entry.Template.Bind(graph);
            }

            _misses++;
            ExecutionIslandPlan compiled = compile();
            StructuralExecutionPlanTemplate template = StructuralExecutionPlanTemplate.Create(compiled, graph);
            if (entry is not null)
                _replacements++;
            _entries[familySlot] = new Entry(bucketHash, identity, template);
            _compilations++;
            return compiled;
        }
    }

    public void RetainFamilySlots(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            List<int>? staleSlots = null;
            foreach (int slot in _entries.Keys)
            {
                if (slot >= count)
                    (staleSlots ??= []).Add(slot);
            }

            if (staleSlots is null)
                return;

            foreach (int slot in staleSlots)
                _entries.Remove(slot);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _entries.Clear();
        }
    }

    private sealed record Entry(
        int BucketHash,
        StructuralPlanIdentity Identity,
        StructuralExecutionPlanTemplate Template);
}

/// <summary>
/// Complete parameter-independent identity for one recorded request graph.
/// </summary>
internal sealed class StructuralPlanIdentity : IEquatable<StructuralPlanIdentity>
{
    private readonly RenderRequestPlanIdentity _request;
    private readonly SkslBackendBudget _shaderBudget;
    private readonly StructuralFragmentIdentity[] _fragments;
    private readonly int[] _publicationRoots;
    private readonly StructuralCacheBoundaryIdentity[] _cacheBoundaries;
    private readonly StructuralPlanIdentity[] _nestedRequests;

    private StructuralPlanIdentity(
        RenderRequestPlanIdentity request,
        SkslBackendBudget shaderBudget,
        StructuralFragmentIdentity[] fragments,
        int[] publicationRoots,
        StructuralCacheBoundaryIdentity[] cacheBoundaries,
        StructuralPlanIdentity[] nestedRequests)
    {
        _request = request;
        _shaderBudget = shaderBudget;
        _fragments = fragments;
        _publicationRoots = publicationRoots;
        _cacheBoundaries = cacheBoundaries;
        _nestedRequests = nestedRequests;
    }

    public static StructuralPlanIdentity Create(
        RenderRequestPlanIdentity request,
        RecordedRenderGraph graph,
        SkslBackendBudget shaderBudget,
        RenderCacheResolution? cacheResolution = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(shaderBudget);

        RenderFragmentReference[] references = new RenderFragmentReference[graph.Fragments.Length];
        var indexes = new Dictionary<RenderFragmentReference, int>(
            graph.Fragments.Length,
            ReferenceEqualityComparer.Instance);
        for (int index = 0; index < graph.Fragments.Length; index++)
        {
            RecordedRenderFragment recorded = graph.Fragments[index];
            if (recorded.Id.RequestId != graph.RequestId || recorded.Id.Value != index + 1L)
                throw new InvalidOperationException("A recorded fragment has a non-canonical graph ID.");
            if (recorded.Payload is not RenderFragmentReference reference || reference.Id != recorded.Id)
            {
                throw new InvalidOperationException(
                    "A recorded fragment is missing its canonical semantic reference.");
            }

            references[index] = reference;
            indexes.Add(reference, index);
        }

        var fragments = new StructuralFragmentIdentity[references.Length];
        for (int index = 0; index < references.Length; index++)
            fragments[index] = StructuralFragmentIdentity.Create(references[index], indexes);

        ImmutableArray<RenderFragmentId> roots = graph.PublicationRoots;
        int[] publicationRoots = roots.Length == 0 ? [] : new int[roots.Length];
        for (int index = 0; index < roots.Length; index++)
            publicationRoots[index] = GetFragmentIndex(roots[index], graph);

        StructuralCacheBoundaryIdentity[] cacheBoundaries = cacheResolution is null
            ? CreateBypassBoundaries(graph)
            : CreateResolvedBoundaries(cacheResolution, graph);

        ImmutableArray<RecordedNestedRenderRequest> nested = graph.NestedRequests;
        StructuralPlanIdentity[] nestedRequests =
            nested.Length == 0 ? [] : new StructuralPlanIdentity[nested.Length];
        for (int index = 0; index < nested.Length; index++)
        {
            nestedRequests[index] = Create(
                nested[index].Request.Options.PlanIdentity,
                nested[index].Graph,
                shaderBudget);
        }

        return new StructuralPlanIdentity(
            request,
            shaderBudget,
            fragments,
            publicationRoots,
            cacheBoundaries,
            nestedRequests);
    }

    public bool Equals(StructuralPlanIdentity? other)
        => other is not null
           && _request.Equals(other._request)
           && _shaderBudget.Equals(other._shaderBudget)
           && _fragments.AsSpan().SequenceEqual(other._fragments)
           && _publicationRoots.AsSpan().SequenceEqual(other._publicationRoots)
           && _cacheBoundaries.AsSpan().SequenceEqual(other._cacheBoundaries)
           && _nestedRequests.AsSpan().SequenceEqual(other._nestedRequests);

    public override bool Equals(object? obj)
        => obj is StructuralPlanIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_request);
        hash.Add(_shaderBudget);
        foreach (StructuralFragmentIdentity fragment in _fragments)
            hash.Add(fragment);
        foreach (int root in _publicationRoots)
            hash.Add(root);
        foreach (StructuralCacheBoundaryIdentity boundary in _cacheBoundaries)
            hash.Add(boundary);
        foreach (StructuralPlanIdentity nested in _nestedRequests)
            hash.Add(nested);
        return hash.ToHashCode();
    }

    private static StructuralCacheBoundaryIdentity[] CreateBypassBoundaries(RecordedRenderGraph graph)
    {
        ImmutableArray<RenderCacheCandidate> candidates = graph.CacheCandidates;
        if (candidates.Length == 0)
            return [];

        var boundaries = new StructuralCacheBoundaryIdentity[candidates.Length];
        for (int index = 0; index < candidates.Length; index++)
        {
            boundaries[index] = new StructuralCacheBoundaryIdentity(
                GetFragmentIndex(candidates[index].FragmentId, graph),
                RenderCacheResolutionKind.Bypass);
        }

        return boundaries;
    }

    private static StructuralCacheBoundaryIdentity[] CreateResolvedBoundaries(
        RenderCacheResolution cacheResolution,
        RecordedRenderGraph graph)
    {
        ImmutableArray<RenderCacheDecision> decisions = cacheResolution.Decisions;
        int retained = 0;
        for (int index = 0; index < decisions.Length; index++)
        {
            if (IsBoundary(decisions[index].Kind))
                retained++;
        }

        if (retained == 0)
            return [];

        var boundaries = new StructuralCacheBoundaryIdentity[retained];
        int write = 0;
        for (int index = 0; index < decisions.Length; index++)
        {
            RenderCacheDecision decision = decisions[index];
            if (!IsBoundary(decision.Kind))
                continue;

            boundaries[write++] = new StructuralCacheBoundaryIdentity(
                GetFragmentIndex(decision.Candidate.FragmentId, graph),
                decision.Kind);
        }

        return boundaries;

        static bool IsBoundary(RenderCacheResolutionKind kind)
            => kind is RenderCacheResolutionKind.Hit or RenderCacheResolutionKind.MissCapture;
    }

    private static int GetFragmentIndex(RenderFragmentId id, RecordedRenderGraph graph)
    {
        if (id.RequestId != graph.RequestId || id.Value <= 0 || id.Value > graph.Fragments.Length)
            throw new InvalidOperationException("A structural-plan fragment ID does not belong to its graph.");
        return checked((int)id.Value - 1);
    }
}

internal readonly record struct StructuralCacheBoundaryIdentity(
    int FragmentIndex,
    RenderCacheResolutionKind Kind);

internal sealed class StructuralFragmentIdentity : IEquatable<StructuralFragmentIdentity>
{
    private readonly RenderFragmentKind _kind;
    private readonly RenderValueCardinality _cardinality;
    private readonly bool _contributesValuesToTarget;
    private readonly bool _canBeUsedAsValueInput;
    private readonly bool _hasTargetEffects;
    private readonly bool _potentiallyWritesTarget;
    private readonly bool _hasOpaqueExternalWork;
    private readonly int[] _inputs;
    private readonly Component[] _components;

    private StructuralFragmentIdentity(
        RenderFragmentReference reference,
        int[] inputs,
        Component[] components)
    {
        _kind = reference.Kind;
        _cardinality = reference.ValueCardinality;
        _contributesValuesToTarget = reference.ContributesValuesToTarget;
        _canBeUsedAsValueInput = reference.CanBeUsedAsValueInput;
        _hasTargetEffects = reference.HasTargetEffects;
        _potentiallyWritesTarget = reference.PotentiallyWritesTarget;
        _hasOpaqueExternalWork = reference.HasOpaqueExternalWork;
        _inputs = inputs;
        _components = components;
    }

    public static StructuralFragmentIdentity Create(
        RenderFragmentReference reference,
        IReadOnlyDictionary<RenderFragmentReference, int> indexes)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(indexes);
        int[] inputs = reference.Inputs.Length == 0 ? [] : new int[reference.Inputs.Length];
        for (int index = 0; index < reference.Inputs.Length; index++)
        {
            if (!indexes.TryGetValue(reference.Inputs[index], out inputs[index]))
            {
                throw new InvalidOperationException(
                    "A structural-plan input is not part of the recorded graph.");
            }
        }

        ComponentBuilder components = ComponentBuilder.Rent();
        if (reference.Kind is RenderFragmentKind.Shader or RenderFragmentKind.Opacity
            && reference.Inputs.Length == 1)
        {
            components.AddBoolean(ExecutionIslandPlanner.HasCompatibleMergeScale(
                reference.Inputs[0],
                reference));
            if (reference.Kind == RenderFragmentKind.Opacity)
            {
                components.AddBoolean(ExecutionIslandPlanner.HasCompatibleOpacityFusionMetadata(
                    reference.Inputs[0],
                    reference));
            }
        }
        AddPayloadComponents(reference, ref components);
        return new StructuralFragmentIdentity(reference, inputs, components.Build());
    }

    public bool Equals(StructuralFragmentIdentity? other)
        => other is not null
           && _kind == other._kind
           && _cardinality.Equals(other._cardinality)
           && _contributesValuesToTarget == other._contributesValuesToTarget
           && _canBeUsedAsValueInput == other._canBeUsedAsValueInput
           && _hasTargetEffects == other._hasTargetEffects
           && _potentiallyWritesTarget == other._potentiallyWritesTarget
           && _hasOpaqueExternalWork == other._hasOpaqueExternalWork
           && _inputs.AsSpan().SequenceEqual(other._inputs)
           && _components.AsSpan().SequenceEqual(other._components);

    public override bool Equals(object? obj)
        => obj is StructuralFragmentIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_kind);
        hash.Add(_cardinality);
        hash.Add(_contributesValuesToTarget);
        hash.Add(_canBeUsedAsValueInput);
        hash.Add(_hasTargetEffects);
        hash.Add(_potentiallyWritesTarget);
        hash.Add(_hasOpaqueExternalWork);
        foreach (int input in _inputs)
            hash.Add(input);
        foreach (Component component in _components)
            hash.Add(component);
        return hash.ToHashCode();
    }

    private static void AddPayloadComponents(
        RenderFragmentReference reference,
        ref ComponentBuilder components)
    {
        switch (reference.Payload)
        {
            case null:
                return;
            case OpacityRenderFragmentPayload opacity:
                components.AddReference(opacity.FusionDescription.StructuralIdentity);
                components.AddBoolean(opacity.Opacity is >= 0 and <= 1);
                return;
            case BlendRenderFragmentPayload:
                return;
            case OpacityMaskRenderFragmentPayload mask:
                AddResourceType(mask.Mask, ref components);
                return;
            case ShaderRenderFragmentPayload shader:
                components.AddReference(shader.Description.StructuralIdentity);
                AddWorkingScalePolicy(shader.WorkingScalePolicy, ref components);
                return;
            case GeometryRenderFragmentPayload geometry:
                components.AddReference(geometry.Description.StructuralIdentity);
                AddWorkingScalePolicy(geometry.WorkingScalePolicy, ref components);
                return;
            case LayerRenderFragmentPayload layer:
                components.AddBoolean(layer.Domain.HasValue);
                components.AddBoolean(layer.DomainIsQueryFootprint);
                return;
            case TargetLayerScopeRenderFragmentPayload targetLayer:
                components.AddBoolean(targetLayer.Region.Kind != TargetRegionKind.Empty);
                return;
            case OpaqueRenderFragmentPayload opaque:
                AddOpaqueStructuralIdentity(opaque, ref components);
                components.AddReference(opaque.Description.Bounds.StructuralIdentity);
                components.AddReference(opaque.Description.HitTest.StructuralIdentity);
                components.AddReference(opaque.Description.Scale.StructuralIdentity);
                components.AddReference(opaque.Description.InputDemand.StructuralIdentity);
                AddInputReadbacks(opaque.InputReadbacks, ref components);
                AddResourceTypes(opaque.Description.Resources, ref components);
                return;
            case FilterEffectSegmentRenderFragmentPayload effectItem:
                AddWorkingScalePolicy(effectItem.WorkingScalePolicy, ref components);
                components.AddInt32(effectItem.StreamInputCount);
                // Whether the segment holds an imperative callback decides why its island ends, and the
                // boundary reason is part of the plan being cached. Two segments that agree on everything
                // else would otherwise share a plan whose classification contradicts one of their graphs.
                components.AddBoolean(effectItem.HasImperativeItem);
                return;
            case MaterializedInputRenderFragmentPayload input:
                components.AddReference(input.Description.HitTest.StructuralIdentity);
                return;
            case TargetCaptureRenderFragmentPayload capture:
                AddTargetCaptureComponents(capture.Description, ref components);
                return;
            case BuiltInBackdropCaptureRenderFragmentPayload capture:
                AddTargetCaptureComponents(capture.Description, ref components);
                return;
            case TargetScopeRenderFragmentPayload scope:
                AddTargetScopeComponents(scope.Description, ref components);
                return;
            case RawTargetScopeRenderFragmentPayload scope:
                components.AddReference(scope.Description.DefinitionFingerprint);
                components.AddReference(scope.Description.Bounds.StructuralIdentity);
                components.AddReference(scope.Description.HitTest.StructuralIdentity);
                components.AddReference(scope.Description.Scale.StructuralIdentity);
                AddResourceTypes(scope.Description.Resources, ref components);
                return;
            case RawTargetCommandRenderFragmentPayload command:
                components.AddReference(command.Description.DefinitionFingerprint);
                components.AddReference(command.Description.HitTest.StructuralIdentity);
                AddResourceTypes(command.Description.Resources, ref components);
                return;
            case TargetCommandRenderFragmentPayload command:
                components.AddReference(command.Description.DefinitionFingerprint);
                components.AddEnum(command.Description.Access);
                AddInputReadbacks(command.InputReadbacks, ref components);
                components.AddReference(command.Description.HitTest.StructuralIdentity);
                AddResourceTypes(command.Description.Resources, ref components);
                return;
            default:
                throw new InvalidOperationException(
                    $"Render fragment kind '{reference.Kind}' has an unrecognized structural payload.");
        }
    }

    // Mirrors the members OpaqueRenderDescription.GetStructuralIdentity composes, one component each, so that
    // the identity carries them without the record struct that call would allocate on every frame.
    private static void AddOpaqueStructuralIdentity(
        OpaqueRenderFragmentPayload opaque,
        ref ComponentBuilder components)
    {
        OpaqueRenderDescription description = opaque.Description;
        components.AddEnum(opaque.Topology);
        components.AddReference(description.DefinitionFingerprint);
        components.AddEnum(description.DeviceGridSensitivity);
        components.AddEnum(description.BackendBoundary);
        components.AddBoolean(description.HasDirectReplayMaterializationContract);
        components.AddBoolean(description.DirectReplayAtExactIntegerReduction);
        components.AddBoolean(description.SupportsDirectDstOut);
    }

    private static void AddInputReadbacks(
        IReadOnlyList<RenderInputReadback> readbacks,
        ref ComponentBuilder components)
    {
        components.AddInt32(readbacks.Count);
        for (int index = 0; index < readbacks.Count; index++)
        {
            RenderInputReadback readback = readbacks[index];
            components.AddInt32(readback.StructuralKind);
            IReadOnlyList<int> valueIndices = readback.ValueIndices;
            components.AddInt32(valueIndices.Count);
            for (int valueIndex = 0; valueIndex < valueIndices.Count; valueIndex++)
                components.AddInt32(valueIndices[valueIndex]);
        }
    }

    private static void AddTargetCaptureComponents(
        TargetCaptureDescription description,
        ref ComponentBuilder components)
    {
        components.AddReference(description.HitTest.StructuralIdentity);
        components.AddReference(description.Scale.StructuralIdentity);
    }

    private static void AddWorkingScalePolicy(
        FilterEffectWorkingScalePolicy? policy,
        ref ComponentBuilder components)
    {
        components.AddBoolean(policy.HasValue);
        if (policy is { } value)
            components.AddReference(value.StructuralIdentity);
    }

    private static void AddTargetScopeComponents(
        TargetScopeDescription description,
        ref ComponentBuilder components)
    {
        components.AddReference(description.DefinitionFingerprint);
        components.AddReference(description.Bounds.StructuralIdentity);
        components.AddReference(description.HitTest.StructuralIdentity);
        components.AddReference(description.Scale.StructuralIdentity);
        components.AddBoolean(description.IsValueReplayMap);
        components.AddEnum(description.TransformSpace);
        components.AddBoolean(description.BuiltInBackdropCapturesBackingTarget);
        AddResourceTypes(description.Resources, ref components);
    }

    private static void AddResourceType(RenderResource resource, ref ComponentBuilder components)
    {
        components.AddInt32(1);
        components.AddReference(resource.GetType());
    }

    private static void AddResourceTypes(
        IReadOnlyList<RenderResource> resources,
        ref ComponentBuilder components)
    {
        components.AddInt32(resources.Count);
        for (int index = 0; index < resources.Count; index++)
            components.AddReference(resources[index].GetType());
    }

    private static void AddResourceTypes(
        IReadOnlyList<RenderResourceBinding> resources,
        ref ComponentBuilder components)
    {
        components.AddInt32(resources.Count);
        for (int index = 0; index < resources.Count; index++)
            components.AddReference(resources[index].Slot.ValueType);
    }

    /// <summary>
    /// One component of a fragment identity. A value component keeps its payload off the heap and carries the
    /// type it came from, so <see langword="false"/>, <c>0</c> and a zero-valued enum stay as distinct from
    /// one another as separate boxes of them are.
    /// </summary>
    private readonly struct Component : IEquatable<Component>
    {
        private readonly object? _reference;
        private readonly long _value;
        private readonly bool _isValue;

        private Component(object? reference, long value, bool isValue)
        {
            _reference = reference;
            _value = value;
            _isValue = isValue;
        }

        public static Component FromReference(object? value) => new(value, 0L, isValue: false);

        public static Component FromBoolean(bool value) => new(typeof(bool), value ? 1L : 0L, isValue: true);

        public static Component FromInt32(int value) => new(typeof(int), value, isValue: true);

        public static Component FromEnum<TEnum>(TEnum value)
            where TEnum : unmanaged, Enum
            => new(typeof(TEnum), ToInt64(value), isValue: true);

        public bool Equals(Component other)
            => _isValue == other._isValue
               && _value == other._value
               && Equals(_reference, other._reference);

        public override bool Equals(object? obj) => obj is Component other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_isValue, _reference, _value);

        // The read must match the enum's storage width: a wider one would take in bytes that are not part
        // of the value. Reading each width unsigned still maps distinct members of a type to distinct longs.
        private static long ToInt64<TEnum>(TEnum value)
            where TEnum : unmanaged, Enum
            => Unsafe.SizeOf<TEnum>() switch
            {
                1 => Unsafe.As<TEnum, byte>(ref value),
                2 => Unsafe.As<TEnum, ushort>(ref value),
                4 => Unsafe.As<TEnum, uint>(ref value),
                _ => Unsafe.As<TEnum, long>(ref value),
            };
    }

    /// <summary>
    /// Collects one fragment's components into a scratch buffer reused across fragments, then hands back a
    /// right-sized copy. The buffer is detached for the duration of a build, so a nested build takes its own.
    /// </summary>
    private struct ComponentBuilder
    {
        private const int InitialCapacity = 16;

        [ThreadStatic]
        private static Component[]? s_scratch;

        private Component[] _buffer;
        private int _count;

        private ComponentBuilder(Component[] buffer)
        {
            _buffer = buffer;
            _count = 0;
        }

        public static ComponentBuilder Rent()
        {
            Component[]? scratch = s_scratch;
            s_scratch = null;
            return new ComponentBuilder(scratch ?? new Component[InitialCapacity]);
        }

        public void AddReference(object? value) => Add(Component.FromReference(value));

        public void AddBoolean(bool value) => Add(Component.FromBoolean(value));

        public void AddInt32(int value) => Add(Component.FromInt32(value));

        public void AddEnum<TEnum>(TEnum value)
            where TEnum : unmanaged, Enum
            => Add(Component.FromEnum(value));

        public Component[] Build()
        {
            Component[] components = _count == 0 ? [] : _buffer.AsSpan(0, _count).ToArray();
            s_scratch = _buffer;
            return components;
        }

        private void Add(Component component)
        {
            if (_count == _buffer.Length)
                Array.Resize(ref _buffer, _buffer.Length * 2);
            _buffer[_count++] = component;
        }
    }
}

internal sealed class StructuralExecutionPlanTemplate
{
    private readonly int _fragmentCount;
    private readonly IslandTemplate[] _islands;
    private readonly BoundaryTemplate[] _boundaries;

    private StructuralExecutionPlanTemplate(
        int fragmentCount,
        IslandTemplate[] islands,
        BoundaryTemplate[] boundaries)
    {
        _fragmentCount = fragmentCount;
        _islands = islands;
        _boundaries = boundaries;
    }

    public static StructuralExecutionPlanTemplate Create(
        ExecutionIslandPlan plan,
        RecordedRenderGraph graph)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(graph);
        ImmutableArray<ExecutionIsland> planIslands = plan.Islands;
        IslandTemplate[] islands = planIslands.Length == 0 ? [] : new IslandTemplate[planIslands.Length];
        for (int index = 0; index < planIslands.Length; index++)
            islands[index] = IslandTemplate.Create(planIslands[index], graph);

        ImmutableArray<ExecutionIslandBoundary> planBoundaries = plan.Boundaries;
        BoundaryTemplate[] boundaries =
            planBoundaries.Length == 0 ? [] : new BoundaryTemplate[planBoundaries.Length];
        for (int index = 0; index < planBoundaries.Length; index++)
            boundaries[index] = BoundaryTemplate.Create(planBoundaries[index], graph);

        return new StructuralExecutionPlanTemplate(graph.Fragments.Length, islands, boundaries);
    }

    public ExecutionIslandPlan Bind(RecordedRenderGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (graph.Fragments.Length != _fragmentCount)
        {
            throw new InvalidOperationException(
                "A cached structural plan cannot bind to a graph with a different fragment count.");
        }

        RenderFragmentReference[] references =
            _fragmentCount == 0 ? [] : new RenderFragmentReference[_fragmentCount];
        for (int index = 0; index < _fragmentCount; index++)
        {
            references[index] = graph.Fragments[index].Payload as RenderFragmentReference
                ?? throw new InvalidOperationException(
                    "A cached structural plan requires executable semantic fragment references.");
        }

        ExecutionIsland[] islands = _islands.Length == 0 ? [] : new ExecutionIsland[_islands.Length];
        for (int index = 0; index < _islands.Length; index++)
            islands[index] = _islands[index].Bind(graph, references);

        ExecutionIslandBoundary[] boundaries =
            _boundaries.Length == 0 ? [] : new ExecutionIslandBoundary[_boundaries.Length];
        for (int index = 0; index < _boundaries.Length; index++)
            boundaries[index] = _boundaries[index].Bind(graph);

        return new ExecutionIslandPlan(
            ImmutableCollectionsMarshal.AsImmutableArray(islands),
            ImmutableCollectionsMarshal.AsImmutableArray(boundaries));
    }

    private sealed record IslandTemplate(
        int Id,
        ExecutionIslandKind Kind,
        int[] Fragments,
        bool PlansGpuPass,
        ShaderRunTemplate? ShaderRun)
    {
        public static IslandTemplate Create(
            ExecutionIsland island,
            RecordedRenderGraph graph)
        {
            ImmutableArray<RenderFragmentId> islandFragments = island.Fragments;
            int[] fragments = islandFragments.Length == 0 ? [] : new int[islandFragments.Length];
            for (int index = 0; index < islandFragments.Length; index++)
                fragments[index] = GetFragmentIndex(islandFragments[index], graph);

            return new IslandTemplate(
                island.Id.Value,
                island.Kind,
                fragments,
                island.PlansGpuPass,
                island.ShaderRun is { } run ? ShaderRunTemplate.Create(run, graph) : null);
        }

        public ExecutionIsland Bind(
            RecordedRenderGraph graph,
            RenderFragmentReference[] references)
        {
            RenderFragmentId[] fragmentIds =
                Fragments.Length == 0 ? [] : new RenderFragmentId[Fragments.Length];
            for (int index = 0; index < Fragments.Length; index++)
                fragmentIds[index] = graph.Fragments[Fragments[index]].Id;

            return new ExecutionIsland(
                new ExecutionIslandId(Id),
                Kind,
                ImmutableCollectionsMarshal.AsImmutableArray(fragmentIds),
                PlansGpuPass,
                ShaderRun?.Bind(graph, references));
        }
    }

    private sealed record ShaderRunTemplate(
        int Id,
        int Input,
        int Output,
        StageTemplate[] Stages,
        SkslMergedProgram Program,
        ShaderRunCoverageSource CoverageSource)
    {
        public static ShaderRunTemplate Create(
            CompiledShaderRun run,
            RecordedRenderGraph graph)
        {
            ImmutableArray<CompiledShaderStage> runStages = run.Stages;
            StageTemplate[] stages = runStages.Length == 0 ? [] : new StageTemplate[runStages.Length];
            for (int index = 0; index < runStages.Length; index++)
                stages[index] = StageTemplate.Create(runStages[index], graph);

            return new ShaderRunTemplate(
                run.Id.Value,
                GetFragmentIndex(GetId(run.Input), graph),
                GetFragmentIndex(GetId(run.Output), graph),
                stages,
                run.Program,
                run.CoverageSource);
        }

        public CompiledShaderRun Bind(
            RecordedRenderGraph graph,
            RenderFragmentReference[] references)
        {
            CompiledShaderStage[] stages =
                Stages.Length == 0 ? [] : new CompiledShaderStage[Stages.Length];
            for (int index = 0; index < Stages.Length; index++)
                stages[index] = Stages[index].Bind(graph, references);

            return new CompiledShaderRun(
                new CompiledShaderRunId(Id),
                references[Input],
                references[Output],
                ImmutableCollectionsMarshal.AsImmutableArray(stages),
                Program,
                CoverageSource);
        }
    }

    private sealed record StageTemplate(
        int Fragment,
        RenderFragmentKind Kind,
        SkslCoverageBehavior CoverageBehavior,
        int ProgramStageIndex)
    {
        public static StageTemplate Create(
            CompiledShaderStage stage,
            RecordedRenderGraph graph)
            => new(
                GetFragmentIndex(stage.FragmentId, graph),
                stage.Kind,
                stage.CoverageBehavior,
                stage.ProgramStageIndex);

        public CompiledShaderStage Bind(
            RecordedRenderGraph graph,
            RenderFragmentReference[] references)
        {
            RenderFragmentReference reference = references[Fragment];
            if (reference.Kind != Kind)
                throw new InvalidOperationException("A cached Shader stage changed semantic kind.");
            ShaderDescription description = Kind switch
            {
                RenderFragmentKind.Shader
                    => ((ShaderRenderFragmentPayload)reference.Payload!).Description,
                RenderFragmentKind.Opacity
                    => ((OpacityRenderFragmentPayload)reference.Payload!).FusionDescription,
                _ => throw new InvalidOperationException("A cached Shader run contains a non-Shader stage."),
            };
            return new CompiledShaderStage(
                graph.Fragments[Fragment].Id,
                reference,
                Kind,
                description,
                CoverageBehavior,
                ProgramStageIndex);
        }
    }

    private sealed record BoundaryTemplate(
        int? Before,
        int? After,
        ExecutionIslandBoundaryReason Reason,
        ImmutableArray<SkslBackendLimit> BackendLimits)
    {
        public static BoundaryTemplate Create(
            ExecutionIslandBoundary boundary,
            RecordedRenderGraph graph)
            => new(
                boundary.BeforeFragmentId is { } before ? GetFragmentIndex(before, graph) : null,
                boundary.AfterFragmentId is { } after ? GetFragmentIndex(after, graph) : null,
                boundary.Reason,
                boundary.BackendLimits);

        public ExecutionIslandBoundary Bind(RecordedRenderGraph graph)
            => new(
                Before is { } before ? graph.Fragments[before].Id : null,
                After is { } after ? graph.Fragments[after].Id : null,
                Reason,
                BackendLimits);
    }

    private static RenderFragmentId GetId(RenderFragmentReference reference)
        => reference.Id
           ?? throw new InvalidOperationException("A cached plan fragment has not been committed.");

    private static int GetFragmentIndex(RenderFragmentId id, RecordedRenderGraph graph)
    {
        if (id.RequestId != graph.RequestId || id.Value <= 0 || id.Value > graph.Fragments.Length)
            throw new InvalidOperationException("A cached plan fragment ID does not belong to its graph.");
        return checked((int)id.Value - 1);
    }
}
