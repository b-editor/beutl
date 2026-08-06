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
        UseBrush(
            session,
            paint.Fill,
            fill => UsePen(session, paint.Pen, pen => use(fill, pen)));
    }

    public static void UsePaint(
        EngineDirectRenderSession session,
        RecordedPaint paint,
        Action<LoweredBrush, LoweredPen> use)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(paint);
        ArgumentNullException.ThrowIfNull(use);
        UseBrush(
            session,
            paint.Fill,
            fill => UsePen(session, paint.Pen, pen => use(fill, pen)));
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
            resource => UseDependency(inputs, brush, resource, use));
    }

    private static void UsePen(
        OpaqueRenderSession session,
        RecordedPen pen,
        Action<LoweredPen> use)
    {
        if (pen.Resource is null)
        {
            use(LoweredPen.Empty);
            return;
        }

        session.UseResource(
            pen.Resource,
            resource => UseBrush(
                session,
                pen.Brush,
                brush => use(new LoweredPen(resource, brush))));
    }

    private static void UsePen(
        EngineDirectRenderSession session,
        RecordedPen pen,
        Action<LoweredPen> use)
    {
        if (pen.Resource is null)
        {
            use(LoweredPen.Empty);
            return;
        }

        session.UseResource(
            pen.Resource,
            resource => UseBrush(
                session,
                pen.Brush,
                brush => use(new LoweredPen(resource, brush))));
    }

    private static void UseBrush(
        OpaqueRenderSession session,
        RecordedBrush brush,
        Action<LoweredBrush> use)
    {
        if (brush.Resource is null)
        {
            use(LoweredBrush.Empty);
            return;
        }

        session.UseResource(
            brush.Resource,
            resource => UseDependency(session.Inputs, brush, resource, use));
    }

    private static void UseBrush(
        EngineDirectRenderSession session,
        RecordedBrush brush,
        Action<LoweredBrush> use)
    {
        if (brush.Resource is null)
        {
            use(LoweredBrush.Empty);
            return;
        }

        session.UseResource(
            brush.Resource,
            resource => UseDependency(session.Inputs, brush, resource, use));
    }

    private static void UseDependency(
        IReadOnlyList<RenderExecutionInput> inputs,
        RecordedBrush brush,
        Media.Brush.Resource resource,
        Action<LoweredBrush> use)
    {
        if (!brush.HasDependency)
        {
            use(new LoweredBrush(resource, null));
            return;
        }

        if ((uint)brush.DependencyIndex >= (uint)inputs.Count)
        {
            throw new InvalidOperationException(
                "A recorded brush dependency does not identify a materialized execution input.");
        }

        RenderExecutionInput input = inputs[brush.DependencyIndex];
        input.UseShader(shader => use(new LoweredBrush(
            resource,
            new BrushTileContent(
                shader,
                brush.ContentBoundsHint ?? input.Bounds,
                input.EffectiveScale))));
    }
}
