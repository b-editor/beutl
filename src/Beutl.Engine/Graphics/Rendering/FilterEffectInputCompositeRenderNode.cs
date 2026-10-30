namespace Beutl.Graphics.Rendering;

internal sealed class FilterEffectInputCompositeRenderNode(bool clearsBeforeEffects) : ContainerRenderNode
{
    private bool _clearsBeforeEffects = clearsBeforeEffects;

    public bool Update(bool value)
    {
        if (value == _clearsBeforeEffects) return false;
        _clearsBeforeEffects = value;
        MarkChanged();
        return true;
    }

    public override void Process(RenderNodeContext context)
    {
        if (!_clearsBeforeEffects && !context.HasInputTargetClear())
        {
            context.PassThrough();
            return;
        }

        // Clear belongs to the recorded input, including pass-through containers and references.
        // Keep the incoming backdrop available during sampling, then replace the destination once.
        var layer = context.TargetLayerScope(context.Inputs, TargetRegion.Full, contributesToBackdrop: false);
        context.Publish(context.Blend(layer, BlendMode.Src));
    }
}
