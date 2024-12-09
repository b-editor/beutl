namespace Beutl.Graphics.Rendering;

public sealed class OpacityRenderNode(float opacity) : ContainerRenderNode
{
    public float Opacity { get; } = opacity;

    public bool Equals(float opacity)
    {
        return Opacity == opacity;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return context.Input.Select(r =>
        {
            return RenderNodeOperation.CreateDecorator(r, canvas =>
            {
                using (canvas.PushOpacity(Opacity))
                {
                    r.Render(canvas);
                }
            });
        }).ToArray();
    }
}
