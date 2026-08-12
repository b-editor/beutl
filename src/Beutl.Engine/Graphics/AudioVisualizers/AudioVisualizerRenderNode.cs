using Beutl.Engine;
using Beutl.Graphics.Rendering;

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
        RenderResource<AudioVisualizerDrawable.Resource> resourceToken = context.Borrow(resource);

        RawTargetCommandDefinition<RawVisualizerCommandState> definition =
            RawTargetCommandDefinition<RawVisualizerCommandState>.Create(
                static (session, state) => session.UseResource(
                    state.Resource,
                    current => current.RenderToCanvas(session.Canvas, state.Bounds)),
                bounds,
                hitTest: RenderHitTestContract.None,
                resources: [s_visualizerSlot]);
        RenderFragmentHandle rawPainter = context.RawTargetCommand(
            definition.Call(
                new RawVisualizerCommandState(resourceToken, bounds),
                [s_visualizerSlot.Bind(resourceToken)]));

        // RenderForeground is a retained raw-ImmediateCanvas author hook. Keep that legacy boundary
        // explicit, then turn its finite painter result into the value published by this source node.
        context.Publish(context.Layer([rawPainter], bounds));
    }

    protected override void OnDispose(bool disposing)
    {
        Visualizer = null;
    }

    private readonly record struct RawVisualizerCommandState(
        RenderResource<AudioVisualizerDrawable.Resource> Resource,
        Rect Bounds);
}
