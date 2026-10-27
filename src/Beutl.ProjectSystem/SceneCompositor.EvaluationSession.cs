using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed partial class SceneCompositor
{
    private sealed class SceneEvaluationSession : CompositionEvaluationSession
    {
        private readonly SceneCompositor _compositor;
        private readonly PooledList<Element> _elements;

        public SceneEvaluationSession(Scene scene, CompositionTarget target)
            : this(new SceneCompositor(scene) { DisableResourceShare = true }, new PooledList<Element>(), target)
        {
        }

        private SceneEvaluationSession(SceneCompositor compositor, PooledList<Element> elements, CompositionTarget target)
            : base(new CompositorContext(MediaTime.Zero, compositor, [], elements, target))
        {
            _compositor = compositor;
            _elements = elements;
        }

        public override void Update(CompositionContext source, MediaTime time)
        {
            base.Update(source, time);
            _compositor.DisposeDetachedResources();
            _elements.Clear();
            var context = (CompositorContext)Context;
            context.FrameTime = null;
            context.CurrentElements = _elements;
            _compositor.SortLayers(time, _elements, context.Target);
        }

        public override void Dispose()
        {
            try { _compositor.Dispose(); }
            finally { _elements.Dispose(); }
        }
    }
}
