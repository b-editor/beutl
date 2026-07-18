using System.Runtime.ExceptionServices;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes;

public sealed partial class GeometryShapeNode : GraphNode
{
    public GeometryShapeNode()
    {
        Output = AddOutput<GeometryRenderNode?>("Output");
        Geometry = AddInput<Geometry?>("Geometry");
        Fill = AddInput<Brush?>("Fill");
        Pen = AddInput<Pen?>("Pen");
    }

    public OutputPort<GeometryRenderNode?> Output { get; }

    public InputPort<Geometry?> Geometry { get; }

    public InputPort<Brush?> Fill { get; }

    public InputPort<Pen?> Pen { get; }

    public partial class Resource
    {
        private GeometryRenderNode? _cachedOutput;
        private Geometry.Resource? _geometryResource;
        private Brush.Resource? _fillResource;
        private Pen.Resource? _penResource;
        private readonly List<EngineObject.Resource> _pendingRollbackResources = [];

        protected override void UpdateCore(GraphCompositionContext context)
        {
            DrainPendingRollbackResources();

            var geometry = Geometry;

            if (geometry == null)
            {
                ClearOutput();
                return;
            }

            var fill = Fill;
            var pen = Pen;
            bool geometryChanged = _geometryResource?.GetOriginal() != geometry;
            bool fillChanged = _fillResource?.GetOriginal() != fill;
            bool penChanged = _penResource?.GetOriginal() != pen;
            Geometry.Resource? replacementGeometry = null;
            Brush.Resource? replacementFill = null;
            Pen.Resource? replacementPen = null;
            try
            {
                if (geometryChanged)
                    replacementGeometry = geometry.ToResource(context);
                if (fillChanged)
                    replacementFill = fill?.ToResource(context);
                if (penChanged)
                    replacementPen = pen?.ToResource(context);
            }
            catch
            {
                RollbackUnpublishedResources(replacementGeometry, replacementFill, replacementPen);
                throw;
            }

            var retired = new List<EngineObject.Resource>(3);
            if (geometryChanged && _geometryResource != null)
                retired.Add(_geometryResource);
            if (fillChanged && _fillResource != null)
                retired.Add(_fillResource);
            if (penChanged && _penResource != null)
                retired.Add(_penResource);

            ExceptionDispatchInfo? cleanupFailure;
            try
            {
                cleanupFailure = RetireOwnedResourceGraphs(retired);
            }
            catch
            {
                RollbackUnpublishedResources(replacementGeometry, replacementFill, replacementPen);
                throw;
            }

            if (geometryChanged)
                _geometryResource = replacementGeometry!;
            if (fillChanged)
                _fillResource = replacementFill;
            if (penChanged)
                _penResource = replacementPen;

            if (!geometryChanged)
            {
                bool updateOnly = false;
                _geometryResource!.Update(geometry, context, ref updateOnly);
            }
            if (!fillChanged && fill != null)
            {
                bool updateOnly = false;
                _fillResource!.Update(fill, context, ref updateOnly);
            }
            if (!penChanged && pen != null)
            {
                bool updateOnly = false;
                _penResource!.Update(pen, context, ref updateOnly);
            }

            Geometry.Resource geometryResource = _geometryResource!;
            if (_cachedOutput == null || _cachedOutput.IsDisposed)
                _cachedOutput = new GeometryRenderNode(geometryResource, _fillResource, _penResource);
            else
                _cachedOutput.Update(geometryResource, _fillResource, _penResource);

            Output = _cachedOutput;
            cleanupFailure?.Throw();
        }

        private void ClearOutput()
        {
            var retired = new List<EngineObject.Resource>(3);
            if (_geometryResource != null)
                retired.Add(_geometryResource);
            if (_fillResource != null)
                retired.Add(_fillResource);
            if (_penResource != null)
                retired.Add(_penResource);

            ExceptionDispatchInfo? failure = RetireOwnedResourceGraphs(retired);
            _geometryResource = null;
            _fillResource = null;
            _penResource = null;
            GeometryRenderNode? output = _cachedOutput;
            _cachedOutput = null;
            Output = null;
            try
            {
                output?.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ExceptionDispatchInfo.Capture(ex);
            }

            failure?.Throw();
        }

        private void RollbackUnpublishedResources(params EngineObject.Resource?[] resources)
        {
            foreach (EngineObject.Resource? resource in resources)
            {
                if (resource != null
                    && !_pendingRollbackResources.Any(pending => ReferenceEquals(pending, resource)))
                {
                    _pendingRollbackResources.Add(resource);
                }
            }

            if (_pendingRollbackResources.Count == 0)
                return;

            try
            {
                _ = RetireOwnedResourceGraphs(_pendingRollbackResources);
                _pendingRollbackResources.Clear();
            }
            catch
            {
                // Preserve the operation failure. The unpublished resources remain owned by this Resource and are
                // retried before the next update or as part of graph-wide cleanup.
            }
        }

        private void DrainPendingRollbackResources()
        {
            if (_pendingRollbackResources.Count == 0)
                return;

            ExceptionDispatchInfo? failure = RetireOwnedResourceGraphs(_pendingRollbackResources);
            _pendingRollbackResources.Clear();
            failure?.Throw();
        }

        partial void PrepareResourceDispose(bool disposing, GeneratedResourceCleanupContext context)
        {
            if (!disposing)
                return;

            context.Reserve(_fillResource);
            context.Reserve(_penResource);
            context.Reserve(_geometryResource);
            foreach (EngineObject.Resource resource in _pendingRollbackResources)
                context.Reserve(resource);
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            GeometryRenderNode? cachedOutput = _cachedOutput;
            _cachedOutput = null;
            _geometryResource = null;
            _fillResource = null;
            _penResource = null;
            _pendingRollbackResources.Clear();
            Output = null;

            Exception? failure = null;
            DisposeOwnedResources(ref failure, cachedOutput);
            ThrowIfCleanupFailed(failure);
        }
    }
}
