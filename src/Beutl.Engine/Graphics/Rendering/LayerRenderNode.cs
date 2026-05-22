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
        // PushLayer materializes a save-layer that composites inputs into one output raster.
        // Per contracts/transformer-node-scale-handling.md Pattern Y, unify at ComponentWiseMax
        // of upstream CorrectionScale.
        float sx = 1f, sy = 1f;
        foreach (var op in context.Input)
        {
            if (op.CorrectionScale.ScaleX > sx) sx = op.CorrectionScale.ScaleX;
            if (op.CorrectionScale.ScaleY > sy) sy = op.CorrectionScale.ScaleY;
        }
        RenderScale unifiedScale = sx == 1f && sy == 1f ? RenderScale.Identity : new RenderScale(sx, sy);

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
                correctionScale: unifiedScale)
        ];
    }
}
