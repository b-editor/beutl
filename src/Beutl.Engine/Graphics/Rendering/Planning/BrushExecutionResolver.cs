namespace Beutl.Graphics.Rendering;

internal static class BrushExecutionResolver
{
    public static void UsePaint(
        OpaqueRenderSession session,
        RecordedPaint paint,
        Action<ResolvedBrush, ResolvedPen> use)
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
        Action<ResolvedBrush, ResolvedPen> use)
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
        Action<ResolvedBrush> use)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(brush);
        ArgumentNullException.ThrowIfNull(use);

        if (brush.Resource is null)
        {
            use(ResolvedBrush.Empty);
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
        Action<ResolvedPen> use)
    {
        if (pen.Resource is null)
        {
            use(ResolvedPen.Empty);
            return;
        }

        session.UseResource(
            pen.Resource,
            resource => UseBrush(
                session,
                pen.Brush,
                brush => use(new ResolvedPen(resource, brush))));
    }

    private static void UsePen(
        EngineDirectRenderSession session,
        RecordedPen pen,
        Action<ResolvedPen> use)
    {
        if (pen.Resource is null)
        {
            use(ResolvedPen.Empty);
            return;
        }

        session.UseResource(
            pen.Resource,
            resource => UseBrush(
                session,
                pen.Brush,
                brush => use(new ResolvedPen(resource, brush))));
    }

    private static void UseBrush(
        OpaqueRenderSession session,
        RecordedBrush brush,
        Action<ResolvedBrush> use)
    {
        if (brush.Resource is null)
        {
            use(ResolvedBrush.Empty);
            return;
        }

        session.UseResource(
            brush.Resource,
            resource => UseDependency(session.Inputs, brush, resource, use));
    }

    private static void UseBrush(
        EngineDirectRenderSession session,
        RecordedBrush brush,
        Action<ResolvedBrush> use)
    {
        if (brush.Resource is null)
        {
            use(ResolvedBrush.Empty);
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
        Action<ResolvedBrush> use)
    {
        if (!brush.HasDependency)
        {
            use(new ResolvedBrush(resource, null));
            return;
        }

        if ((uint)brush.DependencyIndex >= (uint)inputs.Count)
        {
            throw new InvalidOperationException(
                "A recorded brush dependency does not identify a materialized execution input.");
        }

        RenderExecutionInput input = inputs[brush.DependencyIndex];
        input.UseShader(shader => use(new ResolvedBrush(
            resource,
            new BrushTileContent(shader, input.Bounds, input.EffectiveScale))));
    }
}
