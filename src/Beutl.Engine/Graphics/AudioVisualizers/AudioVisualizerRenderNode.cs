using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.AudioVisualizers;

internal sealed class AudioVisualizerRenderNode(AudioVisualizerDrawable.Resource resource) : RenderNode
{
    public (AudioVisualizerDrawable.Resource Resource, int Version)? Visualizer { get; private set; } = resource.Capture();

    public bool Update(AudioVisualizerDrawable.Resource resource)
    {
        if (!resource.Compare(Visualizer))
        {
            Visualizer = resource.Capture();
            HasChanges = true;
            return true;
        }

        return false;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        if (!Visualizer.HasValue) return [];
        AudioVisualizerDrawable.Resource resource = Visualizer.Value.Resource;

        var bounds = new Rect(0, 0, Math.Max(1f, resource.Width), Math.Max(1f, resource.Height));

        return
        [
            RenderNodeOperation.CreateLambda(bounds, canvas => resource.RenderToCanvas(canvas, bounds))
        ];
    }

    protected override void OnDispose(bool disposing)
    {
        Visualizer = null;
    }
}
