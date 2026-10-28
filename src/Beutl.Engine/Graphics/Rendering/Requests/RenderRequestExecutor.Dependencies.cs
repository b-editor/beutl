using System.Runtime.CompilerServices;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private readonly HashSet<object> _activeDependencies = [];
    private readonly Dictionary<DependencyKey, NestedRenderTargetBinding> _dependencies = [];
    private int _dynamicRootAcquisitions;

    private RenderTargetImageLease ExecuteDependency(
        CompiledRenderRequest parent, RenderNode root, object? owner, object identity, Rect domain, float scale,
        ImmediateCanvas fallbackDestination, ProgramCache<CachedSkRuntimeEffect> programCache,
        ProgramCache<GLSLFilterPipeline> spirvProgramCache,
        ProgramCache<ShaderGraphPipeline> graphProgramCache,
        ICollection<RenderRequestExecutionState> frames, ICollection<Exception> cleanupFailures)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        RenderDescriptionValidation.ThrowUnlessFiniteNonEmpty(domain, nameof(domain));
        if (_activeDependencies.Count >= 32 || !_activeDependencies.Add(identity))
            throw new InvalidOperationException($"Circular render dependency: {identity}.");
        try
        {
            var key = new DependencyKey(owner, identity, domain, scale);
            if (_dependencies.TryGetValue(key, out NestedRenderTargetBinding? ready))
                return ready.AcquireImage();

            var binding = new NestedRenderTargetBinding();
            RenderRequestOptions options = parent.Request.Options.CreateNestedAtScale(binding, scale, domain, domain);
            var request = new RenderRequest(options, parent.Request);
            // A dynamic dependency participates in family resource cleanup and the same allocation policy.
            var resource = options.Owner.ResourceRegistry.RegisterOwned(binding);
            options.Owner.ResourceRegistry.Commit(resource);
            CompiledRenderRequest? compiled = null;
            try
            {
                RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
                compiled = new RenderRequestCompiler().Compile(request, graph,
                    SkslBackendBudgetResolver.Resolve(_targets.ExternalTarget?.RawValue.Context?.Backend));
                parent.AddDynamicDependency(compiled);
                bool dropped = false;
                ExecuteNested(compiled, fallbackDestination, programCache, spirvProgramCache, graphProgramCache,
                    frames, cleanupFailures, ref _dynamicRootAcquisitions, ref dropped, requireTarget: true);
                if (dropped)
                {
                    _targets.MarkContentDropped();
                    if (!binding.IsReady)
                        throw new PreviewAllocationDropException();
                }
                RenderTargetImageLease image = binding.AcquireImage();
                if (!dropped) _dependencies.Add(key, binding);
                return image;
            }
            catch (PreviewAllocationDropException)
            {
                // Replay abandons this contribution and continues the preview's remaining roots.
                throw;
            }
            catch (Exception ex)
            {
                Exception failure = ex is FamilyExecutionException family ? family.Failure.SourceException : ex;
                options.Owner.RecordPrimaryFailure(failure);
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                throw;
            }
            finally
            {
                if (compiled is null) request.Dispose();
            }
        }
        finally { _activeDependencies.Remove(identity); }
    }

    // Recorded effect items are records, so the owner compares by reference: two effects with equal data and
    // an equal identity may still render different content. Cycles are still detected by identity alone.
    private readonly struct DependencyKey(object? owner, object identity, Rect domain, float scale)
        : IEquatable<DependencyKey>
    {
        private readonly object? _owner = owner;
        private readonly object _identity = identity;
        private readonly Rect _domain = domain;
        private readonly float _scale = scale;

        public bool Equals(DependencyKey other) => ReferenceEquals(_owner, other._owner)
            && _identity.Equals(other._identity) && _domain == other._domain && _scale.Equals(other._scale);

        public override bool Equals(object? obj) => obj is DependencyKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(RuntimeHelpers.GetHashCode(_owner), _identity, _domain, _scale);
    }
}
