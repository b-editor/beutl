using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

public abstract partial class Shape : Drawable
{
    public Shape()
    {
        ScanProperties<Shape>();
        Fill.CurrentValue = new SolidColorBrush(Colors.White);
    }

    [Display(Name = nameof(GraphicsStrings.Stroke), GroupName = nameof(GraphicsStrings.Stroke), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Pen?> Pen { get; } = Property.Create<Pen?>();

    [Display(Name = nameof(GraphicsStrings.Fill), ResourceType = typeof(GraphicsStrings), GroupName = nameof(GraphicsStrings.Fill))]
    public IExpressionProperty<Brush?> Fill { get; } = Property.Create<Brush?>();

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        Geometry.Resource? geometry = r.GetGeometry();
        if (geometry == null)
        {
            return default;
        }

        Size size = geometry.Bounds.Size;
        if (r.Pen != null)
        {
            size = size.Inflate(ActualThickness(r.Pen));
        }

        return size;
    }

    private static float ActualThickness(Pen.Resource pen)
    {
        return PenHelper.GetRealThickness(pen.StrokeAlignment, pen.Thickness);
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        Geometry.Resource? geometry = r.GetGeometry();
        if (geometry == null)
            return;

        Matrix matrix = Matrix.Identity;

        if (r.Pen != null)
        {
            float thickness = ActualThickness(r.Pen);

            matrix *= Matrix.CreateTranslation(thickness, thickness);
        }

        using (context.PushTransform(matrix))
        {
            context.DrawGeometry(geometry, r.Fill, r.Pen);
        }
    }

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        // Rejects a resource that is not a shape's before drawing it the way every drawable is drawn.
        _ = (Resource)resource;
        base.Render(context, resource);
    }

    public abstract partial class Resource
    {
        private Geometry.Resource? _geometryResource;

        public abstract Geometry.Resource? GetGeometry();

        /// <summary>The resource <see cref="ReconcileGeometry"/> keeps for a shape that builds its own geometry.</summary>
        private protected Geometry.Resource? GeometryResource => _geometryResource;

        /// <summary>
        /// Reconciles <paramref name="geometry"/>, which the shape configures from its own properties, and moves
        /// <see cref="EngineObject.Resource.Version"/> when its resource changes.
        /// </summary>
        private protected void ReconcileGeometry(Geometry geometry, CompositionContext context)
        {
            bool changed = false;
            ResourceReconciler.ReconcileResource(
                context: context,
                value: geometry,
                field: ref _geometryResource,
                changed: ref changed);
            if (changed)
                Version++;
        }

        partial void PreDispose(bool disposing)
        {
            _geometryResource?.Dispose();
        }
    }
}
