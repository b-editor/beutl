namespace Beutl.Graphics.Rendering.V2;

public sealed class BlendModeRenderNode(BlendMode blendMode) : ContainerRenderNode
{
    public BlendMode BlendMode { get; } = blendMode;

    public bool Equals(BlendMode blendMode)
    {
        return BlendMode == blendMode;
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
