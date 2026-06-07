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
        // targetScale = max rule); all-vector children stay Unbounded. At(1)/Unbounded are identical at s_out == 1.
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
