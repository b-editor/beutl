using System.Runtime.CompilerServices;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class GeometryRenderNode(Geometry.Resource geometry, Brush.Resource? fill, Pen.Resource? pen)
    : BrushRenderNode(fill, pen)
{
    public (Geometry.Resource Resource, int Version)? Geometry { get; private set; } = geometry.Capture();

    public bool Update(Geometry.Resource geometry, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool changed = Update(fill, pen);
        if (!geometry.Compare(Geometry))
        {
            Geometry = geometry.Capture();
            changed = true;
        }

        HasChanges = changed;
        return changed;
    }

    public override void Process(RenderNodeContext context)
    {
        if (Geometry is not { } geometrySnapshot)
            return;

        (Brush.Resource Resource, int Version)? fillSnapshot = Fill;
        (Pen.Resource Resource, int Version)? penSnapshot = Pen;
        Geometry.Resource geometry = geometrySnapshot.Resource;
        Brush.Resource? fill = fillSnapshot?.Resource;
        Pen.Resource? pen = penSnapshot?.Resource;
        Rect bounds = PenHelper.CalculateBoundsWithStrokeCap(
            geometry.GetRenderBounds(pen),
            pen);
        if (bounds.Width == 0 || bounds.Height == 0)
            return;

        RenderResource<Geometry.Resource> geometryResource = context.Borrow(
            geometry,
            geometry.GetOriginal().Id,
            geometrySnapshot.Version);
        RecordedPaint paint = BrushRecorder.RecordPaint(
            context,
            fill,
            fillSnapshot?.Version ?? 0,
            pen,
            penSnapshot?.Version ?? 0,
            bounds);

        var hitTestState = new GeometryHitTestState(geometry, fill, pen);
        var hitTestIdentity = new GeometryHitTestIdentity(
            geometry.GetOriginal().Id,
            geometrySnapshot.Version,
            fill?.GetOriginal().Id,
            fillSnapshot?.Version,
            pen?.GetOriginal().Id,
            penSnapshot?.Version);
        RenderResource<GeometryHitTestState> hitTestResource = context.Borrow(
            hitTestState,
            hitTestIdentity);

        OpaqueRenderDescription description = OpaqueRenderDescription.CreateEngineSource(
            execute: session => DeferredOpaqueSource.Execute(
                session,
                geometryResource,
                paint,
                static (canvas, currentGeometry, currentFill, currentPen) =>
                    canvas.DrawGeometry(currentGeometry, currentFill, currentPen)),
            directReplay: session => DeferredOpaqueSource.ExecuteDirect(
                session,
                geometryResource,
                paint,
                static (canvas, currentGeometry, currentFill, currentPen) =>
                    canvas.DrawGeometry(currentGeometry, currentFill, currentPen)),
            bounds: BrushRecorder.CreateSourceBounds(paint, bounds, typeof(GeometryRenderNode)),
            hitTest: RenderHitTestContract.FromResource(
                hitTestResource,
                static (state, point) => state.HitTest(point),
                typeof(GeometryHitTestState)),
            scale: RenderScaleContract.Vector,
            structuralKey: typeof(GeometryRenderNode),
            runtimeIdentity: new RenderRuntimeIdentity(bounds),
            resources: DeferredOpaqueSource.Resources(
                [geometryResource, hitTestResource, .. paint.Resources]));
        context.Publish(BrushRecorder.RecordSource(context, paint, description));
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Geometry = null!;
    }

    private sealed class GeometryHitTestState(
        Geometry.Resource geometry,
        Brush.Resource? fill,
        Pen.Resource? pen)
    {
        public bool HitTest(Point point)
        {
            return (fill is not null && geometry.FillContains(point))
                   || (pen is not null && geometry.StrokeContains(pen, point));
        }
    }

    private readonly record struct GeometryHitTestIdentity(
        Guid GeometryId,
        int GeometryVersion,
        Guid? FillId,
        int? FillVersion,
        Guid? PenId,
        int? PenVersion);
}

internal static class DeferredOpaqueSource
{
    private static readonly ConditionalWeakTable<object, ResourceCacheKey> s_resourceKeys = new();

    public static object GetCacheKey(object resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return resource is EngineObject.Resource engineResource
            ? engineResource.GetOriginal().Id
            : s_resourceKeys.GetValue(resource, static _ => new ResourceCacheKey());
    }

    public static IReadOnlyList<RenderResource> Resources(params RenderResource?[] resources)
    {
        return resources
            .OfType<RenderResource>()
            .DistinctBy(static resource => resource.SlotIdentity)
            .ToArray();
    }

    public static void Execute<T>(
        OpaqueRenderSession session,
        RenderResource<T> primary,
        RenderResource<Brush.Resource>? fill,
        RenderResource<Pen.Resource>? pen,
        Action<ImmediateCanvas, T, Brush.Resource?, Pen.Resource?> draw)
        where T : class
    {
        using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
        output.Canvas.Use(canvas =>
            session.UseResource(primary, value =>
                UseBrushResources(
                    session,
                    fill,
                    pen,
                    (currentFill, currentPen) => draw(canvas, value, currentFill, currentPen))));
        session.Publish(output);
    }

    public static void Execute<T>(
        OpaqueRenderSession session,
        RenderResource<T> primary,
        RecordedPaint paint,
        Action<ImmediateCanvas, T, ResolvedBrush, ResolvedPen> draw)
        where T : class
    {
        using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
        output.Canvas.Use(canvas =>
            session.UseResource(primary, value =>
                BrushExecutionResolver.UsePaint(
                    session,
                    paint,
                    (fill, pen) => draw(canvas, value, fill, pen))));
        session.Publish(output);
    }

    public static void ExecuteDirect<T>(
        EngineDirectRenderSession session,
        RenderResource<T> primary,
        RecordedPaint paint,
        Action<ImmediateCanvas, T, ResolvedBrush, ResolvedPen> draw)
        where T : class
    {
        session.UseResource(primary, value =>
            BrushExecutionResolver.UsePaint(
                session,
                paint,
                (fill, pen) => draw(session.Canvas, value, fill, pen)));
    }

    public static void Execute(
        OpaqueRenderSession session,
        RenderResource<Brush.Resource>? fill,
        RenderResource<Pen.Resource>? pen,
        Action<ImmediateCanvas, Brush.Resource?, Pen.Resource?> draw)
    {
        using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
        output.Canvas.Use(canvas =>
            UseBrushResources(
                session,
                fill,
                pen,
                (currentFill, currentPen) => draw(canvas, currentFill, currentPen)));
        session.Publish(output);
    }

    public static void Execute(
        OpaqueRenderSession session,
        RecordedPaint paint,
        Action<ImmediateCanvas, ResolvedBrush, ResolvedPen> draw)
    {
        using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
        output.Canvas.Use(canvas =>
            BrushExecutionResolver.UsePaint(
                session,
                paint,
                (fill, pen) => draw(canvas, fill, pen)));
        session.Publish(output);
    }

    public static void ExecuteDirect(
        EngineDirectRenderSession session,
        RecordedPaint paint,
        Action<ImmediateCanvas, ResolvedBrush, ResolvedPen> draw)
    {
        BrushExecutionResolver.UsePaint(
            session,
            paint,
            (fill, pen) => draw(session.Canvas, fill, pen));
    }

    private static void UseBrushResources(
        OpaqueRenderSession session,
        RenderResource<Brush.Resource>? fill,
        RenderResource<Pen.Resource>? pen,
        Action<Brush.Resource?, Pen.Resource?> use)
    {
        if (fill is not null)
        {
            session.UseResource(fill, currentFill =>
            {
                if (pen is not null)
                    session.UseResource(pen, currentPen => use(currentFill, currentPen));
                else
                    use(currentFill, null);
            });
        }
        else if (pen is not null)
        {
            session.UseResource(pen, currentPen => use(null, currentPen));
        }
        else
        {
            use(null, null);
        }
    }

    private sealed class ResourceCacheKey
    {
    }
}
