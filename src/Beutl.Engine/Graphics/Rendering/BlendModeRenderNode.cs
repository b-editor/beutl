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

    public override void Process(RenderNodeContext context)
    {
        BlendMode blendMode = BlendMode;
        if (blendMode != BlendMode.SrcOver)
            context.DisableRenderCache();

        foreach (RenderFragmentHandle input in context.Inputs)
        {
            context.Publish(blendMode == BlendMode.SrcOver
                ? input
                : context.Blend(input, blendMode));
        }
    }
}
