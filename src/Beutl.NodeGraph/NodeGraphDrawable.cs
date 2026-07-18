using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;

namespace Beutl.NodeGraph;

[Display(Name = nameof(GraphicsStrings.NodeGraphDrawable), ResourceType = typeof(GraphicsStrings))]
[SuppressResourceClassGeneration]
public sealed partial class NodeGraphDrawable : Drawable
{
    public NodeGraphDrawable()
    {
        ScanProperties<NodeGraphDrawable>();
        HideProperties(Transform, AlignmentX, AlignmentY, TransformOrigin, FilterEffect, BlendMode, Opacity);
        Model.CurrentValue = new GraphModel();
    }

    public IProperty<GraphModel?> Model { get; } = Property.Create<GraphModel?>();

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        try
        {
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }
        catch
        {
            try
            {
                resource.Dispose();
            }
            catch
            {
                // Preserve the acquisition failure while reclaiming the partially evaluated graph snapshot.
            }

            throw;
        }
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => availableSize;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        foreach (var output in r.OutputRenderNode)
        {
            context.DrawNode(
                output,
                n => new ReferencesChildRenderNode(n),
                (refNode, n) => refNode.Update(n));
        }
    }

    public new sealed class Resource : Drawable.Resource
    {
        private readonly GraphSnapshot _snapshot = new();
        private GraphModel? _model;
        private GeneratedResourceCleanupContext? _resourceCleanupContext;
        private IReadOnlyList<RenderNode> _outputRenderNodes = Array.Empty<RenderNode>();

        public IReadOnlyList<RenderNode> OutputRenderNode => ReadGeneratedResourceState(ref _outputRenderNodes);

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            var typed = (NodeGraphDrawable)obj;
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation(typed);
            base.Update(obj, context, ref updateOnly);
            if (obj is NodeGraphDrawable drawable)
            {
                if (_model != drawable.Model.CurrentValue)
                {
                    _model?.TopologyChanged -= OnModelTopologyChanged;
                    _model = drawable.Model.CurrentValue;
                    _model?.TopologyChanged += OnModelTopologyChanged;
                    _snapshot.MarkDirty();
                }

                if (_model != null)
                {
                    _snapshot.Build(_model, context);
                    _snapshot.Evaluate(CompositionTarget.Graphics, context);

                    _outputRenderNodes = PullOutputValue(_model);

                    Version++;
                    updateOnly = true;
                }
                else
                {
                    _outputRenderNodes = Array.Empty<RenderNode>();
                }
            }
            else
            {
                _model?.TopologyChanged -= OnModelTopologyChanged;
                _snapshot.MarkDirty();
                _outputRenderNodes = Array.Empty<RenderNode>();
            }
        }

        private void OnModelTopologyChanged(object? sender, EventArgs e)
        {
            _snapshot.MarkDirty();
        }

        private IReadOnlyList<RenderNode> PullOutputValue(GraphModel model)
        {
            var outputs = new List<RenderNode>();
            foreach (var node in model.Nodes)
            {
                if (node is OutputNode outputNode)
                {
                    int slotIndex = _snapshot.FindSlotIndex(outputNode);
                    if (slotIndex < 0) continue;

                    var resource = _snapshot.GetResource(slotIndex);
                    if (resource == null) continue;

                    if (!resource.ItemIndexMap.TryGetValue(outputNode.InputPort, out int itemIndex))
                        continue;

                    IItemValue? itemValue = _snapshot.GetItemValue(slotIndex, itemIndex);
                    if (itemValue?.GetBoxed() is RenderNode renderNode)
                    {
                        outputs.Add(renderNode);
                    }
                }
            }

            return outputs.Count == 0
                ? Array.Empty<RenderNode>()
                : Array.AsReadOnly(outputs.ToArray());
        }

        protected override void PrepareGeneratedResourceCleanupCore(
            bool disposing,
            GeneratedResourceCleanupContext context)
        {
            if (disposing)
            {
                _resourceCleanupContext = context;
                _snapshot.ReserveResources(context.Reserve);
            }

            base.PrepareGeneratedResourceCleanupCore(disposing, context);
        }

        protected override void RollbackGeneratedResourceCleanupCore()
        {
            _resourceCleanupContext = null;
            _snapshot.RollbackCleanupReservation();
            base.RollbackGeneratedResourceCleanupCore();
        }

        protected override void Dispose(bool disposing)
        {
            GraphModel? model = _model;
            _model = null;
            _outputRenderNodes = Array.Empty<RenderNode>();
            Exception? failure = null;
            if (model != null)
            {
                NodeGraphDisposal.Capture(
                    () => model.TopologyChanged -= OnModelTopologyChanged,
                    ref failure);
            }

            if (disposing)
            {
                GeneratedResourceCleanupContext? context = _resourceCleanupContext;
                _resourceCleanupContext = null;
                if (context == null)
                {
                    failure = new InvalidOperationException("The graph resources were not reserved before cleanup.");
                    _snapshot.RollbackCleanupReservation();
                    _ = _snapshot.DetachAndDisposeWithoutReservation();
                }
                else
                {
                    NodeGraphDisposal.Capture(
                        () => _snapshot.DisposeAfterResourcesReserved(context.DisposeOwned, context.Capture),
                        ref failure);
                }
            }
            NodeGraphDisposal.Capture(() => base.Dispose(disposing), ref failure);
            NodeGraphDisposal.ThrowIfFailed(failure);
        }
    }
}
