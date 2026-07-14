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
public sealed partial class NodeGraphFilterEffect : CustomRenderNodeFilterEffect
{
    public NodeGraphFilterEffect()
    {
        ScanProperties<NodeGraphFilterEffect>();
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

    public new sealed class Resource : CustomRenderNodeFilterEffect.Resource
    {
        private static readonly FilterEffectRenderNodeFactory s_renderNodeFactory =
            FilterEffectRenderNodeFactory.Of<Resource, NodeGraphFilterEffectRenderNode>(
                static resource => new NodeGraphFilterEffectRenderNode(resource));

        public GraphSnapshot Snapshot { get; } = new();

        public GraphModel? Model { get; private set; }

        public TimeSpan? LastTime { get; private set; }

        // Composition flags captured from the build-time context; the render node replays the graph
        // with a fresh context and must restore them. Otherwise graph video inputs always evaluate
        // with PreferProxy=false (wrong in a "prefer proxy" preview) and DisableResourceShare=false
        // (loses reader isolation during an export/full-scale render).
        public bool PreferProxy { get; private set; }

        public ProxyPreset PreferredProxyPreset { get; private set; } = ProxyPreset.Quarter;

        public bool DisableResourceShare { get; private set; }

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => s_renderNodeFactory;

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
                    PreferProxy = context.PreferProxy;
                    PreferredProxyPreset = context.PreferredProxyPreset;
                    DisableResourceShare = context.DisableResourceShare;
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
