using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed partial class SceneCompositor
{
    private sealed class SceneEvaluationSession : CompositionEvaluationSession
    {
        private readonly SceneCompositor _compositor;
        private readonly PooledList<Element> _elements;

        public SceneEvaluationSession(Scene scene, CompositionTarget target, bool forceOriginalSource,
            EvaluationServices services)
            : this(new SceneCompositor(scene) { DisableResourceShare = true, ForceOriginalSource = forceOriginalSource },
                new PooledList<Element>(), target, services)
        {
        }

        private SceneEvaluationSession(SceneCompositor compositor, PooledList<Element> elements, CompositionTarget target,
            EvaluationServices services)
            : base(new CompositorContext(MediaTime.Zero, compositor, [], elements, target, services))
        {
            _compositor = compositor;
            _elements = elements;
        }

        public override void Update(CompositionContext source, MediaTime time)
        {
            base.Update(source, time);
            _compositor.BeginQueryGeneration();
            _compositor.DisposeDetachedResources();
            _elements.Clear();
            var context = (CompositorContext)Context;
            // A session reused by a later frame follows that frame's query path, not the one it was created in.
            if (source.EvaluationServices is EvaluationServices services)
                context.EvaluationServices = services.Fork(_compositor, context);
            context.FrameTime = null;
            // Sample results are independent, but in-flight elements still form the same cycle.
            context.GraphicsState = context.Target == CompositionTarget.Graphics ? new(time)
            {
                Evaluating = (source as CompositorContext)?.GraphicsState?.Evaluating ?? [],
            } : null;
            context.CurrentElements = _elements;
            _compositor.SortLayers(time, _elements, context.Target);
        }

        internal override void Reconcile(FlowNode flow, ref Drawable.Resource? resource, ref bool changed)
        {
            var context = (CompositorContext)Context;
            if (flow.Object is IFlowOperator && flow.Object.HierarchicalParent is Element element)
            {
                using var objects = new PooledList<EngineObject>();
                element.CollectObjects(context.Target, objects);
                int index = objects.IndexOf(flow.Object);
                if (index >= 0)
                {
                    // Replay the authored prefix so portals select from this sample's eligible clips,
                    // including clips that were absent when the caller captured its flow.
                    using var inputs = new PooledList<EngineObject.Resource>();
                    var previousFlow = context.Flow;
                    context.Flow = inputs;
                    try
                    {
                        for (int i = 0; i < index; i++)
                        {
                            context.CancellationToken.ThrowIfCancellationRequested();
                            inputs.Add(_compositor.GetOrCreateResource(objects[i], context));
                        }
                        ResourceReconciler.ReconcileResource(context, flow.Object, ref resource, ref changed);
                    }
                    finally
                    {
                        context.Flow = previousFlow;
                    }
                    return;
                }
            }

            base.Reconcile(flow, ref resource, ref changed);
        }

        public override void Dispose()
        {
            try { _compositor.Dispose(); }
            finally { _elements.Dispose(); }
        }
    }
}
