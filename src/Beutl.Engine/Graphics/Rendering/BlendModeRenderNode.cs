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

    internal static bool RequiresFullTargetRegion(BlendMode blendMode)
    {
        return blendMode is BlendMode.Clear
            or BlendMode.Src
            or BlendMode.SrcIn
            or BlendMode.DstIn
            or BlendMode.SrcOut
            or BlendMode.DstATop
            or BlendMode.Modulate;
    }
}
