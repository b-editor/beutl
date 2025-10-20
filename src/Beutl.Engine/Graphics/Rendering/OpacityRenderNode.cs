namespace Beutl.Graphics.Rendering;

public sealed class OpacityRenderNode(float opacity) : ContainerRenderNode
{
    public float Opacity { get; private set; } = opacity;

    public bool Update(float opacity)
    {
        if (Opacity != opacity)
        {
            Opacity = opacity;
            HasChanges = true;
            return true;
        }

        return false;
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
