using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Transitions;

// The scene compositor draws an active clip boundary transition through one of these in place of the
// two elements' own drawables. It is never part of a document: the compositor owns one per transition
// and hands it the evaluated content of both sides every frame.
internal sealed partial class ClipTransitionPresenter : Drawable
{
    public ClipTransitionPresenter()
    {
        ScanProperties<ClipTransitionPresenter>();
    }

    public new partial class Resource
    {
        private readonly List<Drawable.Resource> _from = [];
        private readonly List<int> _fromVersions = [];
        private readonly List<Drawable.Resource> _to = [];
        private readonly List<int> _toVersions = [];
        private ClipTransition.Resource? _transition;
        private int _transitionVersion;
        private float _progress;

        public IReadOnlyList<Drawable.Resource> From => _from;

        public IReadOnlyList<Drawable.Resource> To => _to;

        public ClipTransition.Resource? TransitionResource => _transition;

        // How far the transition has played, after its easing.
        public float Progress => _progress;

        protected override void RenderCore(GraphicsContext2D context)
        {
            if (TransitionResource is not { IsDisposed: false } transition) return;

            transition.Draw(new ClipTransitionContext(context, From, To, Progress));
        }

        protected override Size MeasureCore(Size availableSize)
        {
            return Size.Empty;
        }

        protected override void OnDraw(GraphicsContext2D context)
        {
        }

        // The drawable a click on the transition selects: the incoming element's topmost drawable, or the
        // outgoing element's when the incoming side draws nothing.
        public Drawable? GetHitTestTarget()
        {
            return GetTarget(_to.LastOrDefault()) ?? GetTarget(_from.LastOrDefault());

            // A transition can draw its own fill where neither side's geometry is hit. Its fallback
            // must still name document content, even when that content is wrapped in another presenter.
            static Drawable? GetTarget(Drawable.Resource? resource) => resource switch
            {
                TrackMattePresenter.Resource matte => GetTarget(matte.Content.Drawables.LastOrDefault()),
                Resource transition => transition.GetHitTestTarget(),
                _ => resource?.GetOriginal(),
            };
        }

        // The inputs are borrowed from the compositor, which owns and disposes them. The version moves when
        // any input or its version changes, so the previous frame's drawing is not reused.
        internal void SetInputs(
            ClipTransition.Resource transition,
            ReadOnlySpan<EngineObject.Resource> from,
            ReadOnlySpan<EngineObject.Resource> to,
            float progress)
        {
            bool changed = false;
            if (!ReferenceEquals(_transition, transition) || transition.Version != _transitionVersion)
            {
                _transition = transition;
                _transitionVersion = transition.Version;
                changed = true;
            }

            changed |= SyncDrawables(_from, _fromVersions, from);
            changed |= SyncDrawables(_to, _toVersions, to);

            if (_progress != progress)
            {
                _progress = progress;
                changed = true;
            }

            if (changed)
            {
                Version++;
            }
        }

        private static bool SyncDrawables(
            List<Drawable.Resource> drawables,
            List<int> versions,
            ReadOnlySpan<EngineObject.Resource> source)
        {
            bool changed = false;
            int count = 0;
            foreach (EngineObject.Resource item in source)
            {
                if (item is not Drawable.Resource drawable) continue;

                if (count < drawables.Count)
                {
                    if (!ReferenceEquals(drawables[count], drawable) || versions[count] != drawable.Version)
                    {
                        drawables[count] = drawable;
                        versions[count] = drawable.Version;
                        changed = true;
                    }
                }
                else
                {
                    drawables.Add(drawable);
                    versions.Add(drawable.Version);
                    changed = true;
                }

                count++;
            }

            if (count < drawables.Count)
            {
                drawables.RemoveRange(count, drawables.Count - count);
                versions.RemoveRange(count, versions.Count - count);
                changed = true;
            }

            return changed;
        }

        partial void PostDispose(bool disposing)
        {
            _from.Clear();
            _fromVersions.Clear();
            _to.Clear();
            _toVersions.Clear();
            _transition = null;
        }
    }
}
