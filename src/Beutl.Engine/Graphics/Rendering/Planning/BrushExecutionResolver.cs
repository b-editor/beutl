namespace Beutl.Graphics.Rendering;

internal static class BrushExecutionResolver
{
    public static void UsePaint(
        OpaqueRenderSession session,
        RecordedPaint paint,
        Action<LoweredBrush, LoweredPen> use)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(paint);
        ArgumentNullException.ThrowIfNull(use);
        UsePaint(session.Token, session.Inputs, paint, use);
    }

    public static void UsePaint(
        EngineDirectRenderSession session,
        RecordedPaint paint,
        Action<LoweredBrush, LoweredPen> use)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(paint);
        ArgumentNullException.ThrowIfNull(use);
        UsePaint(session.Token, session.Inputs, paint, use);
    }

    public static void UseBrush(
        RenderExecutionSessionToken token,
        IReadOnlyList<RenderResource> resources,
        IReadOnlyList<RenderExecutionInput> inputs,
        RecordedBrush brush,
        Action<LoweredBrush> use)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(brush);
        ArgumentNullException.ThrowIfNull(use);

        if (brush.Resource is null)
        {
            use(LoweredBrush.Empty);
            return;
        }

        token.UseResource(
            brush.Resource,
            resources,
            resource => UseDependency(token, inputs, brush, resource, use));
    }

    private static void UsePaint(
        RenderExecutionSessionToken token,
        IReadOnlyList<RenderExecutionInput> inputs,
        RecordedPaint paint,
        Action<LoweredBrush, LoweredPen> use)
    {
        UseBrush(
            token,
            paint.Resources,
            inputs,
            paint.Fill,
            fill => UsePen(token, inputs, paint, pen => use(fill, pen)));
    }

    private static void UsePen(
        RenderExecutionSessionToken token,
        IReadOnlyList<RenderExecutionInput> inputs,
        RecordedPaint paint,
        Action<LoweredPen> use)
    {
        RecordedPen pen = paint.Pen;
        if (pen.Resource is null)
        {
            use(LoweredPen.Empty);
            return;
        }

        token.UseResource(
            pen.Resource,
            paint.Resources,
            resource => UseBrush(
                token,
                paint.Resources,
                inputs,
                pen.Brush,
                brush => use(new LoweredPen(token, resource, brush))));
    }

    private static void UseDependency(
        RenderExecutionSessionToken token,
        IReadOnlyList<RenderExecutionInput> inputs,
        RecordedBrush brush,
        Media.Brush.Resource resource,
        Action<LoweredBrush> use)
    {
        if (!brush.HasDependency)
        {
            use(new LoweredBrush(token, resource, null));
            return;
        }

        if ((uint)brush.DependencyIndex >= (uint)inputs.Count)
        {
            throw new InvalidOperationException(
                "A recorded brush dependency does not identify a materialized execution input.");
        }

        RenderExecutionInput input = inputs[brush.DependencyIndex];
        input.UseShader(shader => use(new LoweredBrush(
            token,
            resource,
            new BrushTileContent(
                shader,
                brush.ContentBoundsHint ?? input.Bounds,
                input.EffectiveScale))));
    }
}
