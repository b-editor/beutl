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

        context.PublishMappedInputs(
            blendMode,
            static (context, input, value) => value == BlendMode.SrcOver
                ? input
                : context.Blend(input, value));
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
