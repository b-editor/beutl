using System.Runtime.CompilerServices;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private readonly HashSet<OwnedIdentity> _activeDependencies = [];
    private readonly Dictionary<(OwnedIdentity Identity, Rect Domain, float Scale), NestedRenderTargetBinding> _dependencies = [];
    private int _dynamicRootAcquisitions;

    private RenderTargetImageLease ExecuteDependency(
        CompiledRenderRequest parent, RenderNode root, object? owner, object identity, Rect domain, float scale,
        ImmediateCanvas fallbackDestination, RenderProgramCaches programCaches,
        ICollection<RenderRequestExecutionState> frames, ICollection<Exception> cleanupFailures)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        RenderDescriptionValidation.ThrowUnlessFiniteNonEmpty(domain, nameof(domain));
        // Recursion that records a new effect item at every level is bounded by depth instead.
        var active = new OwnedIdentity(owner, identity);
        if (_activeDependencies.Count >= 32 || !_activeDependencies.Add(active))
            throw new InvalidOperationException($"Circular render dependency: {identity}.");
        try
        {
            var key = (active, domain, scale);
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
                ExecuteNested(compiled, fallbackDestination, programCaches,
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
        finally { _activeDependencies.Remove(active); }
    }

    // Recorded effect items are records, so the owner compares by reference: two effects with equal data and
    // an equal identity are different dependencies, and an inner effect reusing an outer one's identity is no cycle.
    private readonly struct OwnedIdentity(object? owner, object identity) : IEquatable<OwnedIdentity>
    {
        private readonly object? _owner = owner;
        private readonly object _identity = identity;

        public bool Equals(OwnedIdentity other) => ReferenceEquals(_owner, other._owner) && _identity.Equals(other._identity);

        public override bool Equals(object? obj) => obj is OwnedIdentity other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(_owner), _identity);
    }
}
