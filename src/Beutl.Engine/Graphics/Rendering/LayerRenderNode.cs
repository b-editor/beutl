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
        // feature 003: the flattened layer carries the densest concrete child's supply density (the compositor's
        // targetScale = max rule) — the layer buffer must be dense enough for its finest-detail child. All-vector
        // children stay Unbounded (they re-rasterize at the consumer's working scale).
        EffectiveScale layerScale = EffectiveScale.Unbounded;
        foreach (RenderNodeOperation op in context.Input)
        {
            EffectiveScale s = op.EffectiveScale;
            if (!s.IsUnbounded && (layerScale.IsUnbounded || s.Value > layerScale.Value))
            {
                layerScale = s;
            }
        }

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
                effectiveScale: layerScale)
        ];
    }
}
