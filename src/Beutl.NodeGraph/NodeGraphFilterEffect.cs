using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
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
        internal readonly GraphSnapshot _snapshot = new();
        internal GraphModel? _model;
        internal TimeSpan? _lastTime;

        public override FilterEffectRenderNode CreateRenderNode()
        {
            return new NodeGraphFilterEffectRenderNode(this);
        }

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);

            if (obj is NodeGraphFilterEffect filterEffect)
            {
                if (_model != filterEffect.Model.CurrentValue)
                {
                    _model?.TopologyChanged -= OnModelTopologyChanged;
                    _model = filterEffect.Model.CurrentValue;
                    _model?.TopologyChanged += OnModelTopologyChanged;
                    _snapshot.MarkDirty();
                }

                if (_model != null)
                {
                    _lastTime = context.Time;
                    _snapshot.Build(_model, context);
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

        protected override void Dispose(bool disposing)
        {
            _model?.TopologyChanged -= OnModelTopologyChanged;
            _model = null;
            if (disposing) _snapshot.Dispose();
            base.Dispose(disposing);
        }
    }
}
