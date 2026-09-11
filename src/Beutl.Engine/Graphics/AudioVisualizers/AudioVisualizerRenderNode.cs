using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Graphics.AudioVisualizers;

internal sealed class AudioVisualizerRenderNode(AudioVisualizerDrawable.Resource resource) : RenderNode
{
    private static readonly RenderResourceSlot<AudioVisualizerDrawable.Resource> s_visualizerSlot = new();

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
            bounds.Inflate(GetRasterOutset(resource)),
            RenderHitTestContract.None,
            RenderScaleContract.Vector,
            // A visualizer strokes bars and curves that overlap one another, so its coverage cannot be
            // composited straight into a destination-out blend.
            supportsDirectDstOut: false,
            bindings: [s_visualizerSlot.Bind(resourceToken)]));
    }

    private static float GetRasterOutset(AudioVisualizerDrawable.Resource resource)
        => resource is AudioWaveformDrawable.Resource waveform
            ? waveform.Shape switch
            {
                LineWaveformShape.Resource line => MathF.Max(0, line.Thickness) / 2,
                DotsWaveformShape.Resource dots => MathF.Max(0.5f, dots.DotRadius),
                _ => 0,
            }
            : 0;

    protected override void OnDispose(bool disposing)
    {
        Visualizer = null;
    }

    private readonly record struct VisualizerPainterState(
        AudioVisualizerDrawable.Resource Resource,
        Rect Bounds);
}
