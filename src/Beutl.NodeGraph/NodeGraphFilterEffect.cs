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

    public new sealed class Resource : CustomRenderNodeFilterEffect.Resource
    {
        private static readonly FilterEffectRenderNodeFactory s_renderNodeFactory =
            FilterEffectRenderNodeFactory.Of<Resource, NodeGraphFilterEffectRenderNode>(
                static resource => new NodeGraphFilterEffectRenderNode(resource));

        private readonly GraphSnapshot _snapshot = new();
        private GeneratedResourceCleanupContext? _resourceCleanupContext;
        private EvaluationState _evaluationState;

        public Resource()
        {
            _evaluationState = new EvaluationState(
                _snapshot,
                null,
                null,
                false,
                ProxyPreset.Quarter,
                false);
        }

        public GraphModel? Model => ReadEvaluationState().Model;

        public TimeSpan? LastTime => ReadEvaluationState().LastTime;

        // Composition flags captured from the build-time context; the render node replays the graph
        // with a fresh context and must restore them. Otherwise graph video inputs always evaluate
        // with PreferProxy=false (wrong in a "prefer proxy" preview) and DisableResourceShare=false
        // (loses reader isolation during an export/full-scale render).
        public bool PreferProxy => ReadEvaluationState().PreferProxy;

        public ProxyPreset PreferredProxyPreset => ReadEvaluationState().PreferredProxyPreset;

        public bool DisableResourceShare => ReadEvaluationState().DisableResourceShare;

        private EvaluationState ReadEvaluationState()
            => ReadGeneratedResourceState(ref _evaluationState);

        internal TResult UseEvaluationState<TResult>(Func<EvaluationState, TResult> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation();
            return action(_evaluationState);
        }

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => s_renderNodeFactory;

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            var typed = (NodeGraphFilterEffect)obj;
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation(typed);
            base.Update(obj, context, ref updateOnly);

            if (obj is NodeGraphFilterEffect filterEffect)
            {
                EvaluationState previous = _evaluationState;
                GraphModel? model = filterEffect.Model.CurrentValue;
                if (previous.Model != model)
                {
                    previous.Model?.TopologyChanged -= OnModelTopologyChanged;
                    model?.TopologyChanged += OnModelTopologyChanged;
                    previous.Snapshot.MarkDirty();
                }

                if (model != null)
                {
                    try
                    {
                        previous.Snapshot.Build(model, context);
                    }
                    catch
                    {
                        _evaluationState = previous with { Model = model, LastTime = null };
                        throw;
                    }

                    _evaluationState = new EvaluationState(
                        previous.Snapshot,
                        model,
                        context.Time,
                        context.PreferProxy,
                        context.PreferredProxyPreset,
                        context.DisableResourceShare);
                    Version++;
                    updateOnly = true;
                }
                else
                {
                    _evaluationState = previous with { Model = null, LastTime = null };
                }
            }
            else
            {
                EvaluationState previous = _evaluationState;
                previous.Model?.TopologyChanged -= OnModelTopologyChanged;
                previous.Snapshot.MarkDirty();
                _evaluationState = previous with { Model = null, LastTime = null };
            }
        }

        private void OnModelTopologyChanged(object? sender, EventArgs e)
        {
            _snapshot.MarkDirty();
        }

        protected override void PrepareGeneratedResourceCleanupCore(
            bool disposing,
            GeneratedResourceCleanupContext context)
        {
            if (disposing)
            {
                _resourceCleanupContext = context;
                _evaluationState.Snapshot.ReserveResources(context.Reserve);
            }

            base.PrepareGeneratedResourceCleanupCore(disposing, context);
        }

        protected override void RollbackGeneratedResourceCleanupCore()
        {
            _resourceCleanupContext = null;
            _evaluationState.Snapshot.RollbackCleanupReservation();
            base.RollbackGeneratedResourceCleanupCore();
        }

        protected override void Dispose(bool disposing)
        {
            EvaluationState state = _evaluationState;
            GraphModel? model = state.Model;
            _evaluationState = state with { Model = null, LastTime = null };
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
                    state.Snapshot.RollbackCleanupReservation();
                    _ = state.Snapshot.DetachAndDisposeWithoutReservation();
                }
                else
                {
                    NodeGraphDisposal.Capture(
                        () => state.Snapshot.DisposeAfterResourcesReserved(context.DisposeOwned, context.Capture),
                        ref failure);
                }
            }
            NodeGraphDisposal.Capture(() => base.Dispose(disposing), ref failure);
            NodeGraphDisposal.ThrowIfFailed(failure);
        }

        internal readonly record struct EvaluationState(
            GraphSnapshot Snapshot,
            GraphModel? Model,
            TimeSpan? LastTime,
            bool PreferProxy,
            ProxyPreset PreferredProxyPreset,
            bool DisableResourceShare);
    }
}
