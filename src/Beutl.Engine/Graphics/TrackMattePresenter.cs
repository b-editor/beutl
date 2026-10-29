using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics;

// Evaluated inputs are borrowed from the scene compositor. Versioning includes animated resource
// changes, so both the render-node cache and every consumer of a shared matte see them.
internal sealed class TrackMatteContent
{
    private readonly List<Drawable.Resource> _drawables = [];
    private readonly List<int> _versions = [];

    public IReadOnlyList<Drawable.Resource> Drawables => _drawables;

    public int Version { get; private set; }

    public bool Update(ReadOnlySpan<EngineObject.Resource> inputs)
    {
        bool changed = false;
        int count = 0;
        foreach (EngineObject.Resource input in inputs)
        {
            if (input is not Drawable.Resource drawable) continue;
            if (count == _drawables.Count)
            {
                _drawables.Add(drawable);
                _versions.Add(drawable.Version);
                changed = true;
            }
            else if (_drawables[count] != drawable || _versions[count] != drawable.Version)
            {
                _drawables[count] = drawable;
                _versions[count] = drawable.Version;
                changed = true;
            }

            count++;
        }

        if (count < _drawables.Count)
        {
            _drawables.RemoveRange(count, _drawables.Count - count);
            _versions.RemoveRange(count, _versions.Count - count);
            changed = true;
        }

        if (changed) Version++;
        return changed;
    }
}

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

    public new partial class Resource
    {
        private int _matteVersion = -1;
        private IReadOnlyList<FlowNode> _flowInputs = [];
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
            FlowNode[] inputs = drawables.Select(FlowNode.Capture).OfType<FlowNode>().ToArray();
            return previous != null && previous.Inputs.SequenceEqual(inputs)
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
