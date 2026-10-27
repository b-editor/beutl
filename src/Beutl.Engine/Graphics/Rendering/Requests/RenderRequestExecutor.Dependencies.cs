using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private readonly HashSet<object> _activeDependencies = [];
    private readonly Dictionary<(object Identity, Rect Domain, float Scale), NestedRenderTargetBinding> _dependencies = [];
    private int _dynamicRootAcquisitions;

    private RenderTargetImageLease ExecuteDependency(
        CompiledRenderRequest parent, RenderNode root, object identity, Rect domain, float scale,
        ImmediateCanvas fallbackDestination, ProgramCache<CachedSkRuntimeEffect> programCache,
        ProgramCache<GLSLFilterPipeline> spirvProgramCache,
        ProgramCache<ShaderGraphPipeline> graphProgramCache,
        ICollection<RenderRequestExecutionState> frames, ICollection<Exception> cleanupFailures)
    {
        RenderDescriptionValidation.ThrowUnlessFiniteNonEmpty(domain, nameof(domain));
        if (_activeDependencies.Count >= 32 || !_activeDependencies.Add(identity))
            throw new InvalidOperationException($"Circular render dependency: {identity}.");
        try
        {
            var key = (identity, domain, scale);
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
                if (dropped) _targets.MarkContentDropped();
                RenderTargetImageLease image = binding.AcquireImage();
                _dependencies.Add(key, binding);
                return image;
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
}
