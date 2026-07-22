namespace Beutl.Graphics.Rendering;

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

    public override void Process(RenderNodeContext context)
    {
        RenderFragmentHandle layer = Limit == default
            ? context.TargetLayerScope(context.Inputs, TargetRegion.Full)
            : context.Layer(context.Inputs, Limit);
        context.Publish(layer);
    }
}
