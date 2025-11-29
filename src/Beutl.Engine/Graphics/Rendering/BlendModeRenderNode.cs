namespace Beutl.Graphics.Rendering;

public sealed class BlendModeRenderNode(BlendMode blendMode) : ContainerRenderNode
{
    public BlendMode BlendMode { get; private set; } = blendMode;

    public bool Update(BlendMode blendMode)
    {
        if (BlendMode != blendMode)
        {
            BlendMode = blendMode;
            HasChanges = true;
            return true;
        }

        return false;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        context.IsRenderCacheEnabled = BlendMode == BlendMode.SrcOver;
        return context.Input.Select(r =>
        {
            return RenderNodeOperation.CreateDecorator(r, canvas =>
            {
                using (canvas.PushBlendMode(BlendMode))
                {
                    r.Render(canvas);
                }
            });
        }).ToArray();
    }
}
