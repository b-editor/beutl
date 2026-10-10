using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

[Display(Name = nameof(GraphicsStrings.RoundedRectShape), ResourceType = typeof(GraphicsStrings))]
public sealed partial class RoundedRectShape : Shape
{
    public RoundedRectShape()
    {
        ScanProperties<RoundedRectShape>();
    }

    [Display(Name = nameof(GraphicsStrings.Width), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.Height), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Height { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.RoundedRectShape_CornerRadius), ResourceType = typeof(GraphicsStrings))]
    [Range(typeof(CornerRadius), "0", "max")]
    public IProperty<CornerRadius> CornerRadius { get; } = Property.CreateAnimatable<CornerRadius>(new(25));

    [Range(0, 100)]
    [Display(Name = nameof(GraphicsStrings.Smoothing), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Smoothing { get; } = Property.CreateAnimatable<float>();

    public partial class Resource
    {
        private readonly RoundedRectGeometry _geometry = new();
        private RoundedRectGeometry.Resource? _geometryResource;

        partial void PostReconcile(RoundedRectShape obj, CompositionContext context)
        {
            _geometry.Width.CurrentValue = Math.Max(Width, 0);
            _geometry.Height.CurrentValue = Math.Max(Height, 0);
            _geometry.CornerRadius.CurrentValue = CornerRadius;
            _geometry.Smoothing.CurrentValue = Math.Clamp(Smoothing, 0, 100);

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
