using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

[Display(Name = nameof(GraphicsStrings.RectShape), ResourceType = typeof(GraphicsStrings))]
public sealed partial class RectShape : Shape
{
    public RectShape()
    {
        ScanProperties<RectShape>();
    }

    [Display(Name = nameof(GraphicsStrings.Width), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.Height), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Height { get; } = Property.CreateAnimatable<float>(100);

    public partial class Resource
    {
        private readonly RectGeometry _geometry = new();
        private RectGeometry.Resource? _geometryResource;

        partial void PostReconcile(RectShape obj, CompositionContext context)
        {
            _geometry.Width.CurrentValue = Math.Max(Width, 0);
            _geometry.Height.CurrentValue = Math.Max(Height, 0);

            bool changed = false;
            ResourceReconciler.ReconcileResource(
                context: context,
                value: _geometry,
                field: ref _geometryResource,
                changed: ref changed);
            if (changed)
                Version++;
        }

        partial void PostDispose(bool disposing)
        {
            _geometryResource?.Dispose();
        }

        public override Geometry.Resource? GetGeometry() => _geometryResource;
    }
}
