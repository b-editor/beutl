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

    public static RecordedBrushPlan RecordMask(
        RenderNodeContext context,
        Brush.Resource mask,
        long version,
        Rect brushBounds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mask);
        var builder = new Builder(context, brushBounds);
        RecordedBrush brush = builder.RecordBrush(mask, version);
        return new RecordedBrushPlan(brush, builder.Dependencies, builder.Resources);
    }

    public static RenderOperationBoundsContract CreateSourceBounds(
        RecordedPaint paint,
        Rect bounds,
        object structuralKey)
    {
        ArgumentNullException.ThrowIfNull(paint);
        ArgumentNullException.ThrowIfNull(structuralKey);
        return paint.Dependencies.Count == 0
            ? RenderOperationBoundsContract.Source(bounds)
            : RenderOperationBoundsContract.FullInputs(
                _ => bounds,
                new BrushSourceBoundsIdentity(structuralKey, bounds, paint.Dependencies.Count));
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
                int dependencyIndex = RecordDrawableBrush(drawableBrush);
                return new RecordedBrush(RecordedBrushKind.Drawable, resource, dependencyIndex);
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

        private int RecordDrawableBrush(DrawableBrush.Resource brush)
        {
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
            bool hasConcreteMetadata = true;
            foreach (RenderFragmentHandle output in outputs)
            {
                if (!output.TryGetMetadata(out RenderFragmentMetadata metadata))
                {
                    hasConcreteMetadata = false;
                    break;
                }

                contentBounds = contentBounds.Union(metadata.Bounds);
            }

            if (!hasConcreteMetadata)
                contentBounds = new Rect(default, brushBounds.Size);
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
