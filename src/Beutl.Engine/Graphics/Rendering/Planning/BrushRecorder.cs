using Beutl.Media;

namespace Beutl.Graphics.Rendering;

internal static class BrushRecorder
{
    [ThreadStatic]
    private static List<object>? s_activeDrawableBrushes;

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
    /// <param name="callbackResources">
    /// The resources the callback addresses by index. They are declared ahead of the paint's own slots so a
    /// change in the paint's shape never shifts an index the callback uses.
    /// </param>
    /// <param name="scale">
    /// The declared density contract. Replaying straight onto an existing target renders at that target's
    /// density, so only a contract that declares no density of its own — one whose output is
    /// <see cref="EffectiveScale.Unbounded"/> and therefore already means "whatever the consumer renders at" —
    /// keeps the direct path. A concrete declared density has to be materialized at, and is then resampled by
    /// its consumer like any other supply.
    /// </param>
    public static OpaqueRenderDescription CreatePaintedSource<TState>(
        TState state,
        Action<PaintedRenderSession, TState> draw,
        RecordedPaint paint,
        IReadOnlyList<RenderResource> callbackResources,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        object structuralKey,
        RenderRuntimeIdentity? runtimeIdentity,
        IEnumerable<RenderResource>? additionalResources = null)
    {
        ArgumentNullException.ThrowIfNull(draw);
        ArgumentNullException.ThrowIfNull(paint);
        ArgumentNullException.ThrowIfNull(callbackResources);
        var source = new PaintedSource<TState>(state, paint, callbackResources, draw);
        return OpaqueRenderDescription.CreateEngineSource(
            execute: source.Execute,
            directReplay: scale.DeclaresNoSupplyDensity ? source.ExecuteDirect : null,
            bounds: bounds,
            hitTest: hitTest,
            scale: scale,
            deviceGridSensitivity: deviceGridSensitivity,
            structuralKey: structuralKey,
            runtimeIdentity: runtimeIdentity,
            resources: DeclareResources(callbackResources, additionalResources, paint));
    }

    /// <remarks>
    /// The primary resource is declared ahead of <paramref name="authorResources"/> so that the description's
    /// list is exactly what a caller wrote by hand before this form existed, and it is leased by token rather
    /// than by index. It stays out of the callback's positional space for the same reason the lowered paint's
    /// slots do: it is the recorder's declaration, not the author's, so it can never shift an author's index.
    /// </remarks>
    public static OpaqueRenderDescription CreatePrimaryPaintedSource<TResource, TState>(
        RenderResource<TResource> primary,
        TState state,
        Action<PaintedRenderSession, TResource, TState> draw,
        RecordedPaint paint,
        IReadOnlyList<RenderResource> authorResources,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        object structuralKey,
        RenderRuntimeIdentity? runtimeIdentity)
        where TResource : class
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(draw);
        ArgumentNullException.ThrowIfNull(paint);
        ArgumentNullException.ThrowIfNull(authorResources);
        IReadOnlyList<RenderResource> declared = DeclareResources(primary, authorResources, paint);
        var source = new PrimaryPaintedSource<TResource, TState>(
            primary,
            state,
            paint,
            authorResources,
            declared,
            draw);
        return OpaqueRenderDescription.CreateEngineSource(
            execute: source.Execute,
            directReplay: scale.DeclaresNoSupplyDensity ? source.ExecuteDirect : null,
            bounds: bounds,
            hitTest: hitTest,
            scale: scale,
            deviceGridSensitivity: deviceGridSensitivity,
            structuralKey: structuralKey,
            runtimeIdentity: runtimeIdentity,
            resources: declared);
    }

    /// <remarks>
    /// The primary is merged in here rather than prepended by the caller so that the primary form builds the
    /// same single list the positional form does, and costs the same per recording.
    /// </remarks>
    private static IReadOnlyList<RenderResource> DeclareResources(
        RenderResource primary,
        IReadOnlyList<RenderResource> authorResources,
        RecordedPaint paint)
    {
        var declared = new List<RenderResource>(authorResources.Count + paint.Resources.Count + 1);
        AddDistinct(declared, primary);
        foreach (RenderResource resource in authorResources)
            AddDistinct(declared, resource);
        foreach (RenderResource resource in paint.Resources)
            AddDistinct(declared, resource);

        return declared;
    }

    private static IReadOnlyList<RenderResource> DeclareResources(
        IReadOnlyList<RenderResource> callbackResources,
        IEnumerable<RenderResource>? additionalResources,
        RecordedPaint paint)
    {
        if (callbackResources.Count == 0 && additionalResources is null)
            return paint.Resources;

        var declared = new List<RenderResource>(callbackResources.Count + paint.Resources.Count);
        foreach (RenderResource resource in callbackResources)
            AddDistinct(declared, resource);
        if (additionalResources is not null)
        {
            foreach (RenderResource resource in additionalResources)
                AddDistinct(declared, resource);
        }

        foreach (RenderResource resource in paint.Resources)
            AddDistinct(declared, resource);

        return declared;
    }

    private static void AddDistinct(List<RenderResource> declared, RenderResource resource)
    {
        foreach (RenderResource existing in declared)
        {
            if (ReferenceEquals(existing.SlotIdentity, resource.SlotIdentity))
                return;
        }

        declared.Add(resource);
    }

    public static RenderFragmentHandle RecordSource(
        RenderNodeContext context,
        RecordedPaint paint,
        OpaqueRenderDescription description)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(paint);
        ArgumentNullException.ThrowIfNull(description);
        // A brush the recorder could only keep executable runs BrushConstructor at execution time, which may
        // start a nested renderer; that cannot happen on someone else's target.
        OpaqueRenderDescription recorded = paint.HasRawExternalWork
            ? description.WithoutDirectReplay()
            : description;
        return paint.Dependencies.Count != 0
            ? context.OpaqueCombine(paint.Dependencies, recorded)
            : context.OpaqueSource(recorded);
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
                EngineResourceIdentity.Of(pen),
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
                EngineResourceIdentity.Of(brush),
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

            object identity = EngineResourceIdentity.Of(brush);
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
        IReadOnlyList<RenderResource> callbackResources,
        Action<PaintedRenderSession, TState> draw)
    {
        public void Execute(OpaqueRenderSession session)
        {
            using OpaqueRenderOutput output = session.CreateOutput(session.RequiredRegion);
            output.Canvas.Use(canvas =>
                BrushExecutionResolver.UsePaint(
                    session,
                    paint,
                    (fill, pen) => draw(
                        new PaintedRenderSession(session.Token, canvas, callbackResources, fill, pen),
                        state)));
            session.Publish(output);
        }

        public void ExecuteDirect(EngineDirectRenderSession session)
        {
            BrushExecutionResolver.UsePaint(
                session,
                paint,
                (fill, pen) => draw(
                    new PaintedRenderSession(session.Token, session.Canvas, callbackResources, fill, pen),
                    state));
        }
    }

    private sealed class PrimaryPaintedSource<TResource, TState>(
        RenderResource<TResource> primary,
        TState state,
        RecordedPaint paint,
        IReadOnlyList<RenderResource> authorResources,
        IReadOnlyList<RenderResource> declaredResources,
        Action<PaintedRenderSession, TResource, TState> draw)
        where TResource : class
    {
        public void Execute(OpaqueRenderSession session)
        {
            using OpaqueRenderOutput output = session.CreateOutput(session.RequiredRegion);
            output.Canvas.Use(canvas =>
                BrushExecutionResolver.UsePaint(
                    session,
                    paint,
                    (fill, pen) => Draw(
                        session.Token,
                        new PaintedRenderSession(session.Token, canvas, authorResources, fill, pen))));
            session.Publish(output);
        }

        public void ExecuteDirect(EngineDirectRenderSession session)
        {
            BrushExecutionResolver.UsePaint(
                session,
                paint,
                (fill, pen) => Draw(
                    session.Token,
                    new PaintedRenderSession(session.Token, session.Canvas, authorResources, fill, pen)));
        }

        private void Draw(RenderExecutionSessionToken token, PaintedRenderSession painted)
            => token.UseResource(primary, declaredResources, resource => draw(painted, resource, state));
    }

    private readonly record struct BrushSourceBoundsIdentity(
        object SourceKey,
        Rect Bounds,
        int DependencyCount);
}
