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
    public IExpressionProperty<float> Width { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.Height), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Height { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.RoundedRectShape_CornerRadius), ResourceType = typeof(GraphicsStrings))]
    [Range(typeof(CornerRadius), "0", "max")]
    public IExpressionProperty<CornerRadius> CornerRadius { get; } = Property.CreateAnimatable<CornerRadius>(new(25));

    [Range(0, 100)]
    [Display(Name = nameof(GraphicsStrings.Smoothing), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Smoothing { get; } = Property.CreateAnimatable<float>();

    public partial class Resource
    {
        private readonly RoundedRectGeometry _geometry = new();

        partial void PostReconcile(RoundedRectShape obj, CompositionContext context)
        {
            _geometry.Width.Value = Math.Max(Width, 0);
            _geometry.Height.Value = Math.Max(Height, 0);
            _geometry.CornerRadius.Value = CornerRadius;
            _geometry.Smoothing.Value = Math.Clamp(Smoothing, 0, 100);

            ReconcileGeometry(_geometry, context);
        }

        public override Geometry.Resource? GetGeometry() => GeometryResource;
    }
}
