using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics;

// Versioning includes animated resource changes, so both the render-node cache and every consumer of a shared
// matte see them.
internal sealed class TrackMatteContent : BorrowedDrawables;

// Like a clip transition presenter, this is compositor-owned and never saved into the document.
internal sealed partial class TrackMattePresenter : Drawable
{
    // Keep the captured content while resolving the matte anew when a controller replays it.
    private readonly DrawableGroup _contentInput = new();
    private MatteMode _mode;

    internal required Func<CompositionContext, TrackMatteContent> ResolveMatte { get; init; }

    public TrackMattePresenter()
    {
        ScanProperties<TrackMattePresenter>();
    }

    public new partial class Resource : IContentPresenter
    {
        private int _matteVersion = -1;
        private IReadOnlyList<FlowNode> _flowInputs = [];
        private IReadOnlyList<Drawable.Resource>[]? _presentedSides;
        private DrawableGroup.Resource? _replayedContent;
        private CompositionEvaluationSession? _matteSession;

        internal override IReadOnlyList<FlowNode> FlowInputs => _flowInputs;

        public TrackMatteContent Content { get; } = new();

        public TrackMatteContent? Matte { get; private set; }

        public MatteMode Mode { get; private set; }

        protected override void RenderCore(GraphicsContext2D context)
        {
            if (Matte is not { } matte) return;

            var domain = new Rect(context.Size);
            using (context.PushNode((Mode, domain),
                       static p => new TrackMatteRenderNode(p.Mode, p.domain),
                       static (node, p) => node.Update(p.Mode, p.domain)))
            {
                foreach (Drawable.Resource drawable in Content.Drawables)
                    context.DrawDrawable(drawable);

                context.DrawNode((matte, context.Size, context.OutputScale),
                    static p => new TrackMatteSourceRenderNode(p.matte, p.Size, p.OutputScale),
                    static (node, p) => node.Update(p.matte, p.Size, p.OutputScale));
            }
        }

        protected override Size MeasureCore(Size availableSize) => Size.Empty;

        protected override void OnDraw(GraphicsContext2D context)
        {
        }

        IReadOnlyList<IReadOnlyList<Drawable.Resource>> IContentPresenter.PresentedSides
            => _presentedSides ??= [Content.Drawables];

        // The matte source may cover more than the content; where only it is under the point, a hit continues to
        // the layers beneath.
        Drawable? IContentPresenter.UnhitTarget => null;

        Drawable? IContentPresenter.RepresentativeTarget
            => IContentPresenter.RepresentativeOf(Content.Drawables.LastOrDefault());

        internal void SetInputs(ReadOnlySpan<EngineObject.Resource> content, TrackMatteContent matte, MatteMode mode)
        {
            var original = (TrackMattePresenter)RequireOriginal();
            original._mode = mode;
            bool changed = Content.Update(content);
            if (Matte != matte || _matteVersion != matte.Version || Mode != mode)
            {
                Matte = matte;
                _matteVersion = matte.Version;
                Mode = mode;
                changed = true;
            }

            FlowNode contentNode = Capture(original._contentInput, Content.Drawables, _flowInputs.FirstOrDefault());
            if (_flowInputs.Count != 1 || !ReferenceEquals(contentNode, _flowInputs[0]))
            {
                _flowInputs = [contentNode];
                changed = true;
            }

            if (changed) Version++;
        }

        private static FlowNode Capture(DrawableGroup anchor, IReadOnlyList<Drawable.Resource> drawables, FlowNode? previous)
        {
            IReadOnlyList<FlowNode> inputs = FlowNode.CaptureAll(drawables, previous?.Inputs ?? []);
            return previous != null && ReferenceEquals(inputs, previous.Inputs)
                ? previous : new FlowNode(anchor, inputs);
        }

        partial void PostReconcile(TrackMattePresenter obj, CompositionContext context)
        {
            if (context.ReplayedFlow is not { } replay || !ReferenceEquals(replay.Object, obj)) return;
            bool changed = false;
            replay.Inputs[0].Reconcile(context, ref _replayedContent, ref changed);
            // Re-select the source at the replay clock, with independent resources for each consumer.
            // Capturing the original mask's drawables would freeze relative and timed layer choices.
            _matteSession ??= context.CreateEvaluationSession();
            _matteSession.Update(context, context.Time);
            SetInputs([.. _replayedContent!.Children], obj.ResolveMatte(_matteSession.Context), obj._mode);
            context.ContainsScene3D |= _matteSession.Context.ContainsScene3D;
        }

        partial void PostDispose(bool disposing)
        {
            Content.Update([]);
            Matte = null;
            _flowInputs = [];
            _replayedContent?.Dispose();
            _matteSession?.Dispose();
        }
    }
}
