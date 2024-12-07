namespace Beutl.Graphics.Rendering;

public class LayerRenderNode(Rect limit) : ContainerRenderNode
{
    public Rect Limit { get; } = limit;

    public bool Equals(Rect limit)
    {
        return Limit == limit;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return
        [
            RenderNodeOperation.CreateLambda(
                bounds: context.CalculateBounds(),
                render: canvas =>
                {
                    using (canvas.PushLayer(Limit))
                    {
                        foreach (RenderNodeOperation op in context.Input)
                        {
                            op.Render(canvas);
                        }
                    }
                },
                hitTest: p => context.Input.Any(n => n.HitTest(p)), onDispose: () =>
                {
                    foreach (RenderNodeOperation op in context.Input)
                    {
                        op.Dispose();
                    }
                })
        ];
    }
}
