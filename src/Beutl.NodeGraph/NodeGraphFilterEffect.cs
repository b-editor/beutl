using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media.Proxy;
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
        Model.Value = new GraphModel();
    }

    public IExpressionProperty<GraphModel?> Model { get; } = Property.CreateExpression<GraphModel?>();

    public override Resource ToResource(CompositionContext context)
    {
        bool versionBumped = false;
        var resource = new Resource();
        resource.Reconcile(this, context, ref versionBumped);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        public GraphSnapshot Snapshot { get; } = new();

        public GraphModel? Model { get; private set; }

        // Set by each Reconcile that leaves a Model, so it is valid whenever Model is not null.
        public MediaTime LastTime { get; private set; }

        // Composition flags captured from the build-time context; the render node replays the graph
        // with a fresh context and must restore them. Otherwise graph video inputs always evaluate
        // with PreferProxy=false (wrong in a "prefer proxy" preview) and DisableResourceShare=false
        // (loses reader isolation during an export/full-scale render).
        public bool PreferProxy { get; private set; }

        public ProxyPreset PreferredProxyPreset { get; private set; } = ProxyPreset.Quarter;

        public bool DisableResourceShare { get; private set; }

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

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            base.Reconcile(obj, context, ref versionBumped);

            if (obj is NodeGraphFilterEffect filterEffect)
            {
                if (Model != filterEffect.Model.Value)
                {
                    Model?.TopologyChanged -= OnModelTopologyChanged;
                    Model = filterEffect.Model.Value;
                    Model?.TopologyChanged += OnModelTopologyChanged;
                    Snapshot.MarkDirty();
                }

                if (Model != null)
                {
                    LastTime = context.Time;
                    PreferProxy = context.PreferProxy;
                    PreferredProxyPreset = context.PreferredProxyPreset;
                    DisableResourceShare = context.DisableResourceShare;
                    Snapshot.Build(Model, context);
                    Version++;
                    versionBumped = true;
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

        protected override void ApplyTo(FilterEffectContext context)
        {
            // FilterEffectContext records an effect only where it shares a node with others: after a
            // continuation-controlling effect in the same group, or inside another effect's recording.
            throw new NotSupportedException(
                $"{nameof(NodeGraphFilterEffect)} renders only through its own render node, so it cannot follow "
                + "an effect that controls its continuation in the same group, or be applied by another effect. "
                + "Move it before that effect.");
        }
    }
}
