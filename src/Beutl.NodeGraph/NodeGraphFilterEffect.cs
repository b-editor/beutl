using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;

namespace Beutl.NodeGraph;

[Display(Name = nameof(GraphicsStrings.NodeGraphFilterEffect), ResourceType = typeof(GraphicsStrings))]
[SuppressResourceClassGeneration]
public sealed partial class NodeGraphFilterEffect : FilterEffect
{
    public NodeGraphFilterEffect()
    {
        ScanProperties<NodeGraphFilterEffect>();
        Model.CurrentValue = new GraphModel();
    }

    public IProperty<GraphModel?> Model { get; } = Property.Create<GraphModel?>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        throw new NotSupportedException(
            $"{nameof(NodeGraphFilterEffect)} does not support {nameof(ApplyTo)}. " +
            "Use the resource/render-node pipeline (via ToResource and CreateRenderNode) instead.");
    }

    public override Resource ToResource(CompositionContext context)
    {
        bool updateOnly = false;
        var resource = new Resource();
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        public GraphSnapshot Snapshot { get; } = new();

        public GraphModel? Model { get; private set; }

        public TimeSpan? LastTime { get; private set; }

        public override FilterEffectRenderNode CreateRenderNode()
        {
            return new NodeGraphFilterEffectRenderNode(this);
        }

        public override PushedState Push(GraphicsContext2D context)
        {
            return context.PushNode(
                this,
                resource => new NodeGraphFilterEffectRenderNode(resource),
                (node, resource) => node.Update(resource));
        }

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);

            if (obj is NodeGraphFilterEffect filterEffect)
            {
                if (Model != filterEffect.Model.CurrentValue)
                {
                    Model?.TopologyChanged -= OnModelTopologyChanged;
                    Model = filterEffect.Model.CurrentValue;
                    Model?.TopologyChanged += OnModelTopologyChanged;
                    Snapshot.MarkDirty();
                }

                if (Model != null)
                {
                    LastTime = context.Time;
                    Snapshot.Build(Model, context);
                    Version++;
                    updateOnly = true;
                }
            }
            else
            {
                Model?.TopologyChanged -= OnModelTopologyChanged;
                Snapshot.MarkDirty();
            }
        }

        private void OnModelTopologyChanged(object? sender, EventArgs e)
        {
            Snapshot.MarkDirty();
        }

        protected override void Dispose(bool disposing)
        {
            Model?.TopologyChanged -= OnModelTopologyChanged;
            Model = null;
            if (disposing) Snapshot.Dispose();
            base.Dispose(disposing);
        }
    }
}
