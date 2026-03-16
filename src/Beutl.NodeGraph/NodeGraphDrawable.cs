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
        bool updateOnly = false;
        var resource = new Resource();
        resource.Update(this, context, ref updateOnly);
        return resource;
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

        public List<RenderNode> OutputRenderNode { get; private set; } = [];

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            OutputRenderNode.Clear();
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

                    PullOutputValue(_model);

                    Version++;
                    updateOnly = true;
                }
            }
            else
            {
                _model?.TopologyChanged -= OnModelTopologyChanged;
                _snapshot.MarkDirty();
            }
        }

        private void OnModelTopologyChanged(object? sender, EventArgs e)
        {
            _snapshot.MarkDirty();
        }

        private void PullOutputValue(GraphModel model)
        {
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
                        OutputRenderNode.Add(renderNode);
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            _model?.TopologyChanged -= OnModelTopologyChanged;
            _model = null;
            if (disposing) _snapshot.Dispose();
            base.Dispose(disposing);
        }
    }
}
