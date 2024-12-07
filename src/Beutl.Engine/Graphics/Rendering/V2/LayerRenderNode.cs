namespace Beutl.Graphics.Rendering.V2;

public class LayerRenderNode(Rect limit) : ContainerRenderNode
{
    public Rect Limit { get; } = limit;

    public bool Equals(Rect limit)
    {
        return Limit == limit;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return context.Input.Select(r =>
        {
            return RenderNodeOperation.CreateDecorator(r, canvas =>
            {
                using (canvas.PushLayer(Limit))
                {
                    r.Render(canvas);
                }
            });
        }).ToArray();
    }
}
