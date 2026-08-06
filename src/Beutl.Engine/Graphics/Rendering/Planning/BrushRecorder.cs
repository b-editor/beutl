using System.Runtime.CompilerServices;

using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

internal static class BrushRecorder
{
    [ThreadStatic]
    private static List<object>? s_activeDrawableBrushes;

    private static readonly ConditionalWeakTable<EngineObject.Resource, DetachedResourceIdentityHolder>
        s_detachedResourceIdentities = new();
    private static long s_nextDetachedResourceIdentity;

    public static RecordedPaint RecordPaint(
        RenderNodeContext context,
        Brush.Resource? fill,
        long fillVersion,
        Pen.Resource? pen,
        long penVersion,
        Rect brushBounds)
    {
        ArgumentNullException.ThrowIfNull(context);
        var builder = new Builder(context, brushBounds);
        RecordedBrush recordedFill = builder.RecordBrush(fill, fillVersion);
        RecordedPen recordedPen = builder.RecordPen(pen, penVersion);
        return new RecordedPaint(
            recordedFill,
            recordedPen,
            builder.Dependencies,
            builder.Resources);
    }

    public static RecordedBrushPlan RecordStandaloneBrush(
        RenderNodeContext context,
        Brush.Resource brush,
        long version,
        Rect brushBounds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(brush);
        var builder = new Builder(context, brushBounds);
        RecordedBrush recorded = builder.RecordBrush(brush, version);
        return new RecordedBrushPlan(recorded, builder.Dependencies, builder.Resources);
    }

    public static OpaqueRenderBoundsContract CreateSourceBounds(
        RecordedPaint paint,
        Rect bounds,
        object structuralKey)
    {
        ArgumentNullException.ThrowIfNull(paint);
        ArgumentNullException.ThrowIfNull(structuralKey);
        return paint.Dependencies.Count == 0
            ? OpaqueRenderBoundsContract.Source(bounds)
            : OpaqueRenderBoundsContract.FullInputs(
                _ => bounds,
                new BrushSourceBoundsIdentity(structuralKey, bounds, paint.Dependencies.Count));
    }

    /// <summary>
    /// Creates an engine source that draws <paramref name="state"/> under <paramref name="paint"/>.
    /// </summary>
    /// <param name="state">The immutable value the callback draws from.</param>
    /// <param name="draw">
    /// The one authored drawing. It is invoked with the paint resolved for the running session, and both the
    /// materializing execution and the direct replay onto an existing target are built from it.
    /// </param>
    public static OpaqueRenderDescription CreatePaintedSource<TState>(
        TState state,
        Action<ImmediateCanvas, TState, ResolvedBrush, ResolvedPen> draw,
        RecordedPaint paint,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        object structuralKey,
        RenderRuntimeIdentity? runtimeIdentity,
        IEnumerable<RenderResource>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(draw);
        ArgumentNullException.ThrowIfNull(paint);
        var source = new PaintedSource<TState>(state, paint, draw);
        return OpaqueRenderDescription.CreateEngineSource(
            execute: source.Execute,
            directReplay: source.ExecuteDirect,
            bounds: bounds,
            hitTest: hitTest,
            scale: scale,
            deviceGridSensitivity: deviceGridSensitivity,
            structuralKey: structuralKey,
            runtimeIdentity: runtimeIdentity,
            resources: resources);
    }

    /// <summary>
    /// Creates an engine source that draws borrowed <paramref name="content"/> under <paramref name="paint"/>.
    /// </summary>
    /// <param name="content">
    /// The drawn content. It is resolved for the duration of the callback and must not be retained by it.
    /// </param>
    /// <param name="draw">
    /// The one authored drawing. It is invoked with the resolved content and paint, and both the materializing
    /// execution and the direct replay onto an existing target are built from it.
    /// </param>
    public static OpaqueRenderDescription CreatePaintedContentSource<TContent>(
        RenderResource<TContent> content,
        Action<ImmediateCanvas, TContent, ResolvedBrush, ResolvedPen> draw,
        RecordedPaint paint,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        object structuralKey,
        RenderRuntimeIdentity? runtimeIdentity,
        IEnumerable<RenderResource>? resources = null)
        where TContent : class
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(draw);
        ArgumentNullException.ThrowIfNull(paint);
        var source = new PaintedContentSource<TContent>(content, paint, draw);
        return OpaqueRenderDescription.CreateEngineSource(
            execute: source.Execute,
            directReplay: source.ExecuteDirect,
            bounds: bounds,
            hitTest: hitTest,
            scale: scale,
            deviceGridSensitivity: deviceGridSensitivity,
            structuralKey: structuralKey,
            runtimeIdentity: runtimeIdentity,
            resources: resources);
    }

    public static RenderFragmentHandle RecordSource(
        RenderNodeContext context,
        RecordedPaint paint,
        OpaqueRenderDescription description)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(paint);
        ArgumentNullException.ThrowIfNull(description);
        OpaqueRenderDescription materializedDescription = description.WithoutDirectReplay();
        if (paint.Dependencies.Count != 0)
            return context.OpaqueCombine(paint.Dependencies, description);

        return context.OpaqueSource(
            paint.HasRawExternalWork ? materializedDescription : description);
    }

    public static object GetResourceIdentity(EngineObject.Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        EngineObject? original = resource.GetOriginal();
        if (original is not null)
            return original.Id;

        return s_detachedResourceIdentities.GetValue(
            resource,
            static _ => new DetachedResourceIdentityHolder(
                new DetachedResourceIdentity(Interlocked.Increment(ref s_nextDetachedResourceIdentity))))
            .Identity;
    }

    private sealed class Builder(RenderNodeContext context, Rect brushBounds)
    {
        private readonly List<RenderFragmentHandle> _dependencies = [];
        private readonly List<RenderResource> _resources = [];

        public IReadOnlyList<RenderFragmentHandle> Dependencies => _dependencies;

        public IReadOnlyList<RenderResource> Resources => _resources
            .DistinctBy(static resource => resource.SlotIdentity)
            .ToArray();

        public RecordedPen RecordPen(Pen.Resource? pen, long version)
        {
            if (pen is null)
                return RecordedPen.Empty;

            RenderResource<Pen.Resource> resource = context.Borrow(
                pen,
                GetResourceIdentity(pen),
                version);
            _resources.Add(resource);
            RecordedBrush brush = RecordBrush(pen.Brush, pen.Brush?.Version ?? 0);
            return new RecordedPen(resource, brush);
        }

        public RecordedBrush RecordBrush(Brush.Resource? brush, long version)
        {
            brush = UnwrapPresenter(brush);
            if (brush is null)
                return RecordedBrush.Empty;

            RenderResource<Brush.Resource> resource = context.Borrow(
                brush,
                GetResourceIdentity(brush),
                version == 0 ? brush.Version : version);
            _resources.Add(resource);

            if (brush is DrawableBrush.Resource drawableBrush)
            {
                int dependencyIndex = RecordDrawableBrush(drawableBrush, out Rect? contentBoundsHint);
                if (dependencyIndex < 0)
                {
                    // BrushConstructor rejects a DrawableBrush resource carrying no lowered content.
                    return RecordedBrush.Empty;
                }

                return new RecordedBrush(
                    RecordedBrushKind.Drawable,
                    resource,
                    dependencyIndex,
                    contentBoundsHint);
            }

            if (brush is SolidColorBrush.Resource
                or GradientBrush.Resource
                or PerlinNoiseBrush.Resource
                or ImageBrush.Resource)
            {
                return new RecordedBrush(RecordedBrushKind.Declarative, resource, -1);
            }

            context.DisableRenderCache();
            return new RecordedBrush(RecordedBrushKind.RawExternal, resource, -1);
        }

        private int RecordDrawableBrush(DrawableBrush.Resource brush, out Rect? contentBoundsHint)
        {
            contentBoundsHint = null;
            Drawable.Resource? drawable = brush.Drawable;
            if (drawable is null)
                return -1;

            object identity = GetResourceIdentity(brush);
            using ActiveDrawableBrushScope scope = EnterDrawableBrush(identity);
            using var node = new DrawableRenderNode(drawable);
            using (var graphics = new GraphicsContext2D(node, brushBounds.Size, context.OutputScale))
            {
                drawable.GetOriginal().Render(graphics, drawable);
            }

            IReadOnlyList<RenderFragmentHandle> outputs = context.RecordSubtree(node);
            if (outputs.Count == 0)
                return -1;

            Rect contentBounds = default;
            Rect recordedBoundsHint = default;
            bool hasConcreteMetadata = true;
            foreach (RenderFragmentHandle output in outputs)
            {
                recordedBoundsHint = recordedBoundsHint.Union(
                    context.GetRecordedMetadataHint(output).Bounds);
                if (!output.TryGetMetadata(out RenderFragmentMetadata metadata))
                {
                    hasConcreteMetadata = false;
                    continue;
                }

                contentBounds = contentBounds.Union(metadata.Bounds);
            }

            if (!hasConcreteMetadata)
            {
                // Keep the enclosing brush as the conservative Layer domain, but do not let that
                // fallback replace the nested drawable's natural size in TileBrushCalculator.
                if (recordedBoundsHint.Width > 0 && recordedBoundsHint.Height > 0)
                    contentBoundsHint = recordedBoundsHint;
                contentBounds = new Rect(default, brushBounds.Size);
            }
            if (contentBounds.Width == 0 || contentBounds.Height == 0)
                return -1;

            RenderFragmentHandle dependency = context.Layer(outputs, contentBounds);
            int index = _dependencies.Count;
            _dependencies.Add(dependency);
            return index;
        }

        private static Brush.Resource? UnwrapPresenter(Brush.Resource? brush)
        {
            if (brush is null)
                return null;

            var seen = new HashSet<Brush.Resource>(ReferenceEqualityComparer.Instance);
            while (brush is BrushPresenter.Resource presenter)
            {
                if (!seen.Add(brush))
                {
                    throw new InvalidOperationException(
                        "A BrushPresenter cycle was detected while recording a render request.");
                }

                brush = presenter.Target;
                if (brush is null)
                    return null;
            }

            return brush;
        }
    }

    private static ActiveDrawableBrushScope EnterDrawableBrush(object identity)
    {
        List<object> active = s_activeDrawableBrushes ??= [];
        int cycleStart = active.IndexOf(identity);
        if (cycleStart >= 0)
        {
            IEnumerable<object> cycle = active.Skip(cycleStart).Append(identity);
            throw new InvalidOperationException(
                $"A DrawableBrush recording cycle was detected: {string.Join(" -> ", cycle)}.");
        }

        active.Add(identity);
        return new ActiveDrawableBrushScope(identity);
    }

    private readonly struct ActiveDrawableBrushScope(object identity) : IDisposable
    {
        public void Dispose()
        {
            List<object>? active = s_activeDrawableBrushes;
            int last = active?.Count - 1 ?? -1;
            if (last < 0 || !Equals(active![last], identity))
                throw new InvalidOperationException("The active DrawableBrush recording stack is corrupted.");

            active.RemoveAt(last);
        }
    }

    private sealed class PaintedSource<TState>(
        TState state,
        RecordedPaint paint,
        Action<ImmediateCanvas, TState, ResolvedBrush, ResolvedPen> draw)
    {
        public void Execute(OpaqueRenderSession session)
        {
            using OpaqueRenderOutput output = session.CreateOutput(session.RequiredRegion);
            output.Canvas.Use(canvas =>
                BrushExecutionResolver.UsePaint(
                    session,
                    paint,
                    (fill, pen) => draw(canvas, state, fill, pen)));
            session.Publish(output);
        }

        public void ExecuteDirect(EngineDirectRenderSession session)
        {
            BrushExecutionResolver.UsePaint(
                session,
                paint,
                (fill, pen) => draw(session.Canvas, state, fill, pen));
        }
    }

    private sealed class PaintedContentSource<TContent>(
        RenderResource<TContent> content,
        RecordedPaint paint,
        Action<ImmediateCanvas, TContent, ResolvedBrush, ResolvedPen> draw)
        where TContent : class
    {
        public void Execute(OpaqueRenderSession session)
        {
            using OpaqueRenderOutput output = session.CreateOutput(session.RequiredRegion);
            output.Canvas.Use(canvas =>
                session.UseResource(content, value =>
                    BrushExecutionResolver.UsePaint(
                        session,
                        paint,
                        (fill, pen) => draw(canvas, value, fill, pen))));
            session.Publish(output);
        }

        public void ExecuteDirect(EngineDirectRenderSession session)
        {
            session.UseResource(content, value =>
                BrushExecutionResolver.UsePaint(
                    session,
                    paint,
                    (fill, pen) => draw(session.Canvas, value, fill, pen)));
        }
    }

    private readonly record struct BrushSourceBoundsIdentity(
        object SourceKey,
        Rect Bounds,
        int DependencyCount);

    private readonly record struct DetachedResourceIdentity(long Value);

    private sealed class DetachedResourceIdentityHolder(DetachedResourceIdentity identity)
    {
        public DetachedResourceIdentity Identity { get; } = identity;
    }
}
