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
        // feature 003: a SaveLayer flatten allocates NO node-owned buffer — its render lambda re-renders the
        // children directly into the consumer's canvas at the consumer's CTM (PushLayer == SaveLayer), and any
        // genuinely concrete child resamples itself at its own DrawSurface/DrawRenderTarget blit (FR-017). So,
        // like the other SaveLayer wrappers (Opacity/BlendMode/OpacityMask — data-model.md), the layer reports
        // EffectiveScale.Unbounded: it re-rasterizes at any working scale and must NOT pin a parent boundary's
        // working scale to a child's density (which would wrongly drag a re-rasterizable vector sibling down).
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
                hitTest: p => context.Input.Any(n => n.HitTest(p)),
                onDispose: () =>
                {
                    foreach (RenderNodeOperation op in context.Input)
                    {
                        op.Dispose();
                    }
                },
                effectiveScale: EffectiveScale.Unbounded)
        ];
    }
}
