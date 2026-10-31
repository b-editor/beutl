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

    public new partial class Resource : IContentPresenter
    {
        private readonly BorrowedDrawables _from = new();
        private readonly BorrowedDrawables _to = new();
        private IReadOnlyList<Drawable.Resource>[]? _presentedSides;
        private ClipTransition.Resource? _transition;
        private int _transitionVersion;
        private float _progress;

        public IReadOnlyList<Drawable.Resource> From => _from.Drawables;

        public IReadOnlyList<Drawable.Resource> To => _to.Drawables;

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
        // outgoing element's when the incoming side draws nothing. A transition can draw its own fill where
        // neither side's geometry is hit, so this names document content even when that content is wrapped in
        // another presenter.
        public Drawable? GetHitTestTarget()
            => IContentPresenter.RepresentativeOf(To.LastOrDefault())
               ?? IContentPresenter.RepresentativeOf(From.LastOrDefault());

        // The incoming side is tried first.
        IReadOnlyList<IReadOnlyList<Drawable.Resource>> IContentPresenter.PresentedSides
            => _presentedSides ??= [To, From];

        Drawable? IContentPresenter.UnhitTarget => GetHitTestTarget();

        Drawable? IContentPresenter.RepresentativeTarget => GetHitTestTarget();

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

            changed |= _from.Update(from);
            changed |= _to.Update(to);

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

        partial void PostDispose(bool disposing)
        {
            _from.Clear();
            _to.Clear();
            _transition = null;
        }
    }
}
