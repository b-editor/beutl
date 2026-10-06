using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

[Display(Name = nameof(GraphicsStrings.EllipseShape), ResourceType = typeof(GraphicsStrings))]
public sealed partial class EllipseShape : Shape
{
    public EllipseShape()
    {
        ScanProperties<EllipseShape>();
    }

    [Display(Name = nameof(GraphicsStrings.Width), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.Height), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Height { get; } = Property.CreateAnimatable<float>(100);

    public partial class Resource
    {
        private readonly EllipseGeometry _geometry = new();
        private EllipseGeometry.Resource? _geometryResource;

        partial void PostUpdate(EllipseShape obj, CompositionContext context)
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
