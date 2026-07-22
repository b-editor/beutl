using System.Collections.Immutable;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

internal readonly record struct StructuralPlanCacheStatistics(
    long Hits,
    long Misses,
    long Compilations,
    long Replacements,
    int RetainedPlans);

/// <summary>
/// Retains the last structural request plan for a renderer. Hashes only select the candidate bucket;
/// the complete structural identity must still compare equal before a plan is rebound to a new request.
/// </summary>
internal sealed class StructuralPlanCache : IDisposable
{
    private readonly object _gate = new();
    private Entry? _entry;
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
                    _entry is null ? 0 : 1);
            }
        }
    }

    public ExecutionIslandPlan GetOrCompile(
        StructuralPlanIdentity identity,
        RecordedRenderGraph graph,
        Func<ExecutionIslandPlan> compile,
        int? bucketHashOverride = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(compile);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int bucketHash = bucketHashOverride ?? identity.GetHashCode();
            if (_entry is { } entry
                && entry.BucketHash == bucketHash
                && entry.Identity.Equals(identity))
            {
                _hits++;
                return entry.Template.Bind(graph);
            }

            _misses++;
            ExecutionIslandPlan compiled = compile();
            StructuralExecutionPlanTemplate template = StructuralExecutionPlanTemplate.Create(compiled, graph);
            if (_entry is not null)
                _replacements++;
            _entry = new Entry(bucketHash, identity, template);
            _compilations++;
            return compiled;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _entry = null;
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

        int[] publicationRoots = graph.PublicationRoots
            .Select(id => GetFragmentIndex(id, graph))
            .ToArray();
        StructuralCacheBoundaryIdentity[] cacheBoundaries = cacheResolution is null
            ? graph.CacheCandidates
                .Select(candidate => new StructuralCacheBoundaryIdentity(
                    GetFragmentIndex(candidate.FragmentId, graph),
                    RenderCacheResolutionKind.Bypass))
                .ToArray()
            : cacheResolution.Decisions
                .Where(static decision => decision.Kind is RenderCacheResolutionKind.Hit
                    or RenderCacheResolutionKind.MissCapture)
                .Select(decision => new StructuralCacheBoundaryIdentity(
                    GetFragmentIndex(decision.Candidate.FragmentId, graph),
                    decision.Kind))
                .ToArray();
        StructuralPlanIdentity[] nestedRequests = graph.NestedRequests
            .Select(nested => Create(
                nested.Request.Options.PlanIdentity,
                nested.Graph,
                shaderBudget))
            .ToArray();

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
    private readonly object[] _components;

    private StructuralFragmentIdentity(
        RenderFragmentReference reference,
        int[] inputs,
        object[] components)
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
        int[] inputs = new int[reference.Inputs.Length];
        for (int index = 0; index < reference.Inputs.Length; index++)
        {
            if (!indexes.TryGetValue(reference.Inputs[index], out inputs[index]))
            {
                throw new InvalidOperationException(
                    "A structural-plan input is not part of the recorded graph.");
            }
        }

        var components = new List<object>();
        if (reference.Kind is RenderFragmentKind.Shader or RenderFragmentKind.Opacity
            && reference.Inputs.Length == 1)
        {
            components.Add(ExecutionIslandPlanner.HasCompatibleMergeScale(
                reference.Inputs[0],
                reference));
        }
        AddPayloadComponents(reference, components);
        return new StructuralFragmentIdentity(reference, inputs, components.ToArray());
    }

    public bool Equals(StructuralFragmentIdentity? other)
    {
        if (other is null
            || _kind != other._kind
            || !_cardinality.Equals(other._cardinality)
            || _contributesValuesToTarget != other._contributesValuesToTarget
            || _canBeUsedAsValueInput != other._canBeUsedAsValueInput
            || _hasTargetEffects != other._hasTargetEffects
            || _potentiallyWritesTarget != other._potentiallyWritesTarget
            || _hasOpaqueExternalWork != other._hasOpaqueExternalWork
            || !_inputs.AsSpan().SequenceEqual(other._inputs)
            || _components.Length != other._components.Length)
        {
            return false;
        }

        for (int index = 0; index < _components.Length; index++)
        {
            if (!Equals(_components[index], other._components[index]))
                return false;
        }
        return true;
    }

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
        foreach (object component in _components)
            hash.Add(component);
        return hash.ToHashCode();
    }

    private static void AddPayloadComponents(
        RenderFragmentReference reference,
        ICollection<object> components)
    {
        switch (reference.Payload)
        {
            case null:
                return;
            case OpacityRenderFragmentPayload opacity:
                components.Add(opacity.FusionDescription.StructuralIdentity);
                components.Add(opacity.Opacity is >= 0 and <= 1);
                return;
            case BlendRenderFragmentPayload:
                return;
            case OpacityMaskRenderFragmentPayload mask:
                components.Add(mask.Mask.Kind);
                components.Add(mask.Mask.DependencyIndex);
                components.Add(mask.IsRawFallback);
                AddResourceTypes(mask.Resources, components);
                return;
            case ShaderRenderFragmentPayload shader:
                components.Add(shader.Description.StructuralIdentity);
                AddWorkingScalePolicy(shader.WorkingScalePolicy, components);
                return;
            case GeometryRenderFragmentPayload geometry:
                components.Add(geometry.Description.StructuralIdentity);
                AddWorkingScalePolicy(geometry.WorkingScalePolicy, components);
                return;
            case LayerRenderFragmentPayload layer:
                components.Add(layer.Domain.HasValue);
                return;
            case TargetLayerScopeRenderFragmentPayload targetLayer:
                components.Add(targetLayer.Region.Kind != TargetRegionKind.Empty);
                return;
            case OpaqueRenderFragmentPayload opaque:
                components.Add(opaque.Description.GetStructuralIdentity(opaque.Topology));
                components.Add(opaque.Description.Bounds.StructuralIdentity);
                components.Add(opaque.Description.HitTest.StructuralIdentity);
                components.Add(opaque.Description.Scale.StructuralIdentity);
                components.Add(opaque.Description.RequiresReadback);
                AddResourceTypes(opaque.Description.Resources, components);
                return;
            case LegacyFilterEffectRenderFragmentPayload legacy:
                AddWorkingScalePolicy(legacy.WorkingScalePolicy, components);
                return;
            case MaterializedInputRenderFragmentPayload input:
                components.Add(input.Description.HitTest.StructuralIdentity);
                return;
            case TargetCaptureRenderFragmentPayload capture:
                AddTargetCaptureComponents(capture.Description, components);
                return;
            case BuiltInBackdropCaptureRenderFragmentPayload capture:
                AddTargetCaptureComponents(capture.Description, components);
                return;
            case TargetScopeRenderFragmentPayload scope:
                AddTargetScopeComponents(scope.Description, components);
                return;
            case RawTargetScopeRenderFragmentPayload scope:
                components.Add(scope.Description.StructuralKey);
                components.Add(scope.Description.Bounds.StructuralIdentity);
                components.Add(scope.Description.HitTest.StructuralIdentity);
                components.Add(scope.Description.Scale.StructuralIdentity);
                AddResourceTypes(scope.Description.Resources, components);
                return;
            case RawTargetCommandRenderFragmentPayload command:
                components.Add(command.Description.StructuralKey);
                components.Add(command.Description.HitTest.StructuralIdentity);
                AddResourceTypes(command.Description.Resources, components);
                return;
            case TargetCommandRenderFragmentPayload command:
                components.Add(command.Description.StructuralKey);
                components.Add(command.Description.Access);
                components.Add(command.Description.RequiresInputReadback);
                components.Add(command.Description.HitTest.StructuralIdentity);
                AddResourceTypes(command.Description.Resources, components);
                return;
            default:
                throw new InvalidOperationException(
                    $"Render fragment kind '{reference.Kind}' has an unrecognized structural payload.");
        }
    }

    private static void AddTargetCaptureComponents(
        TargetCaptureDescription description,
        ICollection<object> components)
    {
        components.Add(description.HitTest.StructuralIdentity);
        components.Add(description.Scale.StructuralIdentity);
    }

    private static void AddWorkingScalePolicy(
        FilterEffectWorkingScalePolicy? policy,
        ICollection<object> components)
    {
        components.Add(policy.HasValue);
        if (policy is { } value)
            components.Add(value.StructuralIdentity);
    }

    private static void AddTargetScopeComponents(
        TargetScopeDescription description,
        ICollection<object> components)
    {
        components.Add(description.StructuralKey);
        components.Add(description.Bounds.StructuralIdentity);
        components.Add(description.HitTest.StructuralIdentity);
        components.Add(description.Scale.StructuralIdentity);
        components.Add(description.IsValueReplayMap);
        AddResourceTypes(description.Resources, components);
    }

    private static void AddResourceTypes(
        IReadOnlyList<RenderResource> resources,
        ICollection<object> components)
    {
        components.Add(resources.Count);
        foreach (RenderResource resource in resources)
            components.Add(resource.GetType());
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
        IslandTemplate[] islands = plan.Islands
            .Select(island => IslandTemplate.Create(island, graph))
            .ToArray();
        BoundaryTemplate[] boundaries = plan.Boundaries
            .Select(boundary => BoundaryTemplate.Create(boundary, graph))
            .ToArray();
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

        RenderFragmentReference[] references = graph.Fragments
            .Select(static fragment => fragment.Payload as RenderFragmentReference
                ?? throw new InvalidOperationException(
                    "A cached structural plan requires executable semantic fragment references."))
            .ToArray();
        ImmutableArray<ExecutionIsland> islands =
            [.. _islands.Select(template => template.Bind(graph, references))];
        ImmutableArray<ExecutionIslandBoundary> boundaries =
            [.. _boundaries.Select(template => template.Bind(graph))];
        return new ExecutionIslandPlan(islands, boundaries);
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
            => new(
                island.Id.Value,
                island.Kind,
                island.Fragments.Select(id => GetFragmentIndex(id, graph)).ToArray(),
                island.PlansGpuPass,
                island.ShaderRun is { } run ? ShaderRunTemplate.Create(run, graph) : null);

        public ExecutionIsland Bind(
            RecordedRenderGraph graph,
            RenderFragmentReference[] references)
            => new(
                new ExecutionIslandId(Id),
                Kind,
                [.. Fragments.Select(index => graph.Fragments[index].Id)],
                PlansGpuPass,
                ShaderRun?.Bind(graph, references));
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
            => new(
                run.Id.Value,
                GetFragmentIndex(GetId(run.Input), graph),
                GetFragmentIndex(GetId(run.Output), graph),
                run.Stages.Select(stage => StageTemplate.Create(stage, graph)).ToArray(),
                run.Program,
                run.CoverageSource);

        public CompiledShaderRun Bind(
            RecordedRenderGraph graph,
            RenderFragmentReference[] references)
            => new(
                new CompiledShaderRunId(Id),
                references[Input],
                references[Output],
                [.. Stages.Select(stage => stage.Bind(graph, references))],
                Program,
                CoverageSource);
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
