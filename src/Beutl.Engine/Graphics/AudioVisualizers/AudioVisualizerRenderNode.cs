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

    public override void Process(RenderNodeContext context)
    {
        if (Visualizer is not { } snapshot)
            return;

        AudioVisualizerDrawable.Resource resource = snapshot.Resource;

        var bounds = new Rect(0, 0, Math.Max(1f, resource.Width), Math.Max(1f, resource.Height));
        RenderResource<AudioVisualizerDrawable.Resource> resourceToken = context.Borrow(
            resource,
            resource.GetOriginal().Id,
            snapshot.Version);

        RawTargetCommandDescription command = RawTargetCommandDescription.Create(
            execute: session => session.UseResource(
                resourceToken,
                current => current.RenderToCanvas(session.Canvas, bounds)),
            queryBounds: bounds,
            hitTest: RenderHitTestContract.None,
            structuralKey: typeof(AudioVisualizerRenderNode),
            resources: [resourceToken]);
        RenderFragmentHandle rawPainter = context.RawTargetCommand(command);

        // RenderForeground is a retained raw-ImmediateCanvas author hook. Keep that legacy boundary
        // explicit, then turn its finite painter result into the value published by this source node.
        context.Publish(context.Layer([rawPainter], bounds));
    }

    protected override void OnDispose(bool disposing)
    {
        Visualizer = null;
    }
}
