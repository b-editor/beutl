namespace Beutl.Graphics.Rendering;

public sealed class PushRenderNode : ContainerRenderNode
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return context.Input.Select(r =>
            RenderNodeOperation.CreateDecorator(r, canvas =>
            {
                using (canvas.Push())
                {
                    r.Render(canvas);
                }
            }))
            .ToArray();
    }
}
