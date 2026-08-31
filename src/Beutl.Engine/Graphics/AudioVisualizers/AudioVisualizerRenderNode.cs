using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Graphics.AudioVisualizers;

internal sealed class AudioVisualizerRenderNode(AudioVisualizerDrawable.Resource resource) : RenderNode
{
    private static readonly RenderResourceSlot<AudioVisualizerDrawable.Resource> s_visualizerSlot = new();

    private static readonly RenderResourceSlot[] s_slots = [s_visualizerSlot];

    public (AudioVisualizerDrawable.Resource Resource, int Version)? Visualizer { get; private set; } = resource.Capture();

    public bool Update(AudioVisualizerDrawable.Resource resource)
    {
        if (!resource.Compare(Visualizer))
        {
            Visualizer = resource.Capture();
            MarkChanged();
            return true;
        }

        return false;
    }

    public override void Process(RenderNodeContext context)
    {
        if (Visualizer is not { } snapshot)
            return;

        AudioVisualizerDrawable.Resource resource = snapshot.Resource;

        var bounds = new Rect(0, 0, Math.Max(1f, resource.Width), Math.Max(1f, resource.Height));
        RenderResource<AudioVisualizerDrawable.Resource> resourceToken = context.Borrow(resource);
        Brush.Resource? fill = resource.Fill;
        context.Publish(context.PaintedSource(
            new VisualizerPainterState(resource, bounds),
            static (canvas, _, _, state) => state.Resource.RenderToCanvas(canvas, state.Bounds),
            fill,
            null,
            bounds,
            RenderHitTestContract.None,
            RenderScaleContract.Vector,
            // A visualizer strokes bars and curves that overlap one another, so its coverage cannot be
            // composited straight into a destination-out blend.
            supportsDirectDstOut: false,
            bindings: [s_visualizerSlot.Bind(resourceToken)],
            slots: s_slots));
    }

    protected override void OnDispose(bool disposing)
    {
        Visualizer = null;
    }

    private readonly record struct VisualizerPainterState(
        AudioVisualizerDrawable.Resource Resource,
        Rect Bounds);
}
