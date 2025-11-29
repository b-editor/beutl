namespace Beutl.Graphics.Rendering;

// TODO: Limitがdefaultの場合、CalculateBoundsを使うようにする
public class LayerRenderNode(Rect limit) : ContainerRenderNode
{
    public Rect Limit { get; private set; } = limit;

    public bool Update(Rect limit)
    {
        if (Limit != limit)
        {
            Limit = limit;
            HasChanges = true;
            return true;
        }

        return false;
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
